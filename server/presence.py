"""Кто сейчас играет в Magic Survivors — для админки. Подключается из ideas.py (роутинг + start).

POST /api/ping  {voter, state}     — игра открыта (menu | play), клиент пингует раз в 30 с (GamePresence.cs)
GET  /api/world                    — для города: онлайн, убито за сегодня всеми, цель дня, скидка торговцев (sale, next), орда у стен (horde)
GET  /api/admin?key=ADMIN_TOKEN    — страница админки (admin.html рядом), обновляется раз в 10 с
GET  /api/admin/online?key=…       — данные: онлайн сейчас, живые забеги, график за сутки, визиты

Онлайн = пинги ∪ забеги со свежим срезом телеметрии, поэтому забеги видно и в сборках без пинга.
Раз в минуту число пишется в таблицу online (7 дней) — для графика.
"""
import hmac, json, os, threading, time
from urllib.parse import parse_qs

ADMIN = os.environ.get("ADMIN_TOKEN", "")  # без токена админка выключена
LIVE = 150  # забег «идёт», если срез телеметрии был не раньше 150 с назад (срез раз в игровую минуту)
PING = 75   # игрок «в игре», если пинг был не раньше 75 с назад
HERE = os.path.dirname(os.path.abspath(__file__))
lock = threading.Lock()
pings = {}  # voter -> (время, menu|play), живёт в памяти
db = None   # фабрика соединений из ideas.py


def start(db_factory):
    global db
    db = db_factory
    with db() as c:
        c.execute("create table if not exists online(ts integer primary key, total integer not null, play integer not null)")
        c.execute("create index if not exists runs_created on runs(created)")   # счёт дня — по свежим забегам
    try:
        refresh_sale()
    except Exception as e:
        print("sale:", type(e).__name__, e, flush=True)
    threading.Thread(target=sample_loop, daemon=True).start()


KILL_GOAL = 1_000_000  # столько монстров за день (по Москве) должны убить все игроки вместе — открывается супер-данж
# скидка торговцев от убитых за день всеми: (сколько убить, скидка %). Остаток до цели — «орда у стен» города
SALE_TIERS = ((100_000, 5), (250_000, 10), (500_000, 15), (750_000, 20), (1_000_000, 25))


def sale_for(kills):
    """Скидка (%) и сколько всего надо убить до следующей ступени (0 — вершина)."""
    pct, nxt = 0, SALE_TIERS[0][0]
    for need, p in SALE_TIERS:
        if kills >= need:
            pct, nxt = p, 0
        else:
            nxt = need
            break
    return pct, nxt


sale_now = [0]   # снимок скидки: обновляется в фоне (sample_loop) и при /api/world, покупки читают без базы


def day_kills(c):
    """Убитые за московский день по законченным забегам. Клиентскому числу не верим: не больше 25 в секунду
    серверного времени забега (runs.secs уже урезан по телеметрии) — иначе один поддельный забег включал бы скидку."""
    now = time.time()
    day = int((now + 10800) // 86400 * 86400 - 10800)
    return int(c.execute("select coalesce(sum(min(kills, secs * 25)),0) from runs where created>=?", (day,)).fetchone()[0]), day


def refresh_sale():
    with db() as c:
        kills, _ = day_kills(c)
    sale_now[0] = sale_for(kills)[0]
    return kills


def discounted(price):
    """Цена у торговцев с сегодняшней скидкой — её берут wallet.py и gear.py при списании (без запроса к базе)."""
    return price - price * sale_now[0] // 100
world_cache = [0.0, None]


def world():
    """Общее для всех: сколько игроков онлайн и сколько монстров убито сегодня. Показывается в городе.
    Убитые считаются по законченным забегам (таблица runs), день — московский. Ответ кешируется на 10 с."""
    now = time.time()
    if world_cache[1] is not None and now - world_cache[0] < 10:
        return world_cache[1]
    state, _ = now_online()
    with db() as c:
        kills, day = day_kills(c)
    sale, nxt = sale_for(kills)
    sale_now[0] = sale
    world_cache[0], world_cache[1] = now, {"online": len(state), "kills": int(kills), "goal": KILL_GOAL,
                                           "open": kills >= KILL_GOAL, "left": int(day + 86400 - now),
                                           "sale": sale, "next": nxt, "horde": max(0, KILL_GOAL - int(kills))}
    return world_cache[1]


def get(h, u):
    """Обработать GET, если путь наш. True — ответ отправлен."""
    if u.path == "/api/world":
        h.reply(200, world())
        return True
    if u.path not in ("/api/admin", "/api/admin/online"):
        return False
    key = (parse_qs(u.query).get("key") or [""])[0]
    if not ADMIN or not hmac.compare_digest(key, ADMIN):
        h.reply(403, {"error": "forbidden"})
    elif u.path == "/api/admin/online":
        h.reply(200, online())
    else:
        with open(os.path.join(HERE, "admin.html"), "rb") as f:
            b = f.read()
        h.send_response(200)
        h.send_header("Content-Type", "text/html; charset=utf-8")
        h.send_header("Cache-Control", "no-store")
        h.send_header("X-Robots-Tag", "noindex")
        h.send_header("Content-Length", str(len(b)))
        h.end_headers()
        h.wfile.write(b)
    return True


def post(h, path, d, voter):
    """Обработать POST, если путь наш. True — ответ отправлен."""
    if path != "/api/ping":
        return False
    if len(voter) < 8:
        h.reply(400, {"error": "bad voter"})
        return True
    with lock:
        pings[voter] = (time.time(), "play" if d.get("state") == "play" else "menu")
    h.reply(200, {"ok": True})
    return True


def now_online():
    """Кто сейчас в игре: пинги клиента + забеги со свежим срезом телеметрии (старые сборки без пинга)."""
    now = time.time()
    with lock:
        for v in [v for v, (t, _) in pings.items() if now - t > PING]:
            del pings[v]
        state = {v: st for v, (t, st) in pings.items()}
    with db() as c:
        live = c.execute("""select t.voter, t.secs, t.level, t.data, t.updated, coalesce(p.nick, '?') nick from telemetry t
                            left join players p on p.voter=t.voter where t.end_reason='live' and t.updated>? order by t.secs desc""",
                         (int(now) - LIVE,)).fetchall()
    for r in live:
        state[r["voter"]] = "play"
    return state, live


def sample_loop():
    """Раз в минуту пишем число игроков онлайн — для графика в админке. Храним 7 дней."""
    while True:
        try:
            state, _ = now_online()
            now = int(time.time())
            with db() as c:
                c.execute("insert or replace into online(ts, total, play) values(?,?,?)",
                          (now - now % 60, len(state), sum(1 for v in state.values() if v == "play")))
                c.execute("delete from online where ts<?", (now - 7 * 86400,))
            refresh_sale()
        except Exception as e:
            print("sample:", type(e).__name__, e, flush=True)
        time.sleep(60 - time.time() % 60)


def online():
    """Сводка для админки: онлайн сейчас, живые забеги, график за сутки, визиты."""
    state, live = now_online()
    now = int(time.time())
    runs = []
    for r in live:
        d = json.loads(r["data"])
        runs.append({"nick": r["nick"], "secs": r["secs"], "level": r["level"], "ago": now - r["updated"],
                     "map": d.get("map"), "hero": d.get("hero"), "diff": d.get("diff"), "platform": d.get("platform")})
    with db() as c:
        series = [list(r) for r in c.execute("select ts, total, play from online where ts>? order by ts", (now - 86400,))]
        peak = c.execute("select coalesce(max(total),0) from online where ts>?", (now - 86400,)).fetchone()[0]
        today = c.execute("select count(*) from visits where day=?", (time.strftime("%Y-%m-%d"),)).fetchone()[0]
        runs_today = c.execute("select count(*) from telemetry where created>=?",
                               (int(time.mktime(time.strptime(time.strftime("%Y-%m-%d"), "%Y-%m-%d"))),)).fetchone()[0]
        players = c.execute("select count(*) from players").fetchone()[0]
    return {"now": now, "online": len(state), "play": sum(1 for v in state.values() if v == "play"),
            "menu": sum(1 for v in state.values() if v == "menu"), "peak24": max(peak, len(state)),
            "today": today, "runs_today": runs_today, "players": players, "runs": runs, "series": series}
