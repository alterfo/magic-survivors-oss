"""Павшие маги Magic Survivors: маг, погибший в забеге, приходит к другим игрокам элитным врагом. Подключается из ideas.py.

POST /api/fallen/add   {voter, nick, hero, hat, robe, staff, glow, element, spells, lvl, secs, map, diff}
                                     — слепок мага после смерти; раз в минуту, храним 5 последних на игрока
POST /api/fallen/get   {voter, map}  — чужой павший для забега (id: 0 — некого дать); выдача запоминается на 2 часа
POST /api/fallen/slain {voter, id}   — павший упокоен: победителю золото и трофеи, хозяину золото и новость
POST /api/fallen/news  {voter}       — непрочитанные новости «твоего мага упокоил X», отдаются один раз
Золото и вещи идут через wallet.py (grant_gold, grant_item) — в кошелёк устройства или аккаунта.
Трофеев нет в ценах wallet.py, купить их нельзя.
"""
import os, re, time

import wallet
import account  # authed — проверка токена входа

MIN_SECS, MAX_SECS = 180, 1500  # павший — только из забега от 3 минут; потолок как в /api/run
ADD_EVERY = 60     # не чаще одного слепка в минуту
KEEP = 5           # столько последних павших храним на игрока
FRESH = 30 * 86400  # старше месяца не выдаём
GIVEN_TTL = 7200   # slain принимаем только по выдаче не старше 2 часов
SLAIN_DAY = 10     # упокоений на игрока за сутки
SLAYER_GOLD = 20
OWNER_GOLD = 30
OWNER_DAY = 5      # платных новостей хозяину за сутки, дальше новость без золота
TROPHIES = ((1, "cos.glow_soul"), (3, "cos.robe_ghost"), (10, "cos.hat_fallen"))  # сколько упокоил → вещь
PATHS = ("/api/fallen/add", "/api/fallen/get", "/api/fallen/slain", "/api/fallen/news")
FIELDS = ("nick", "hero", "hat", "robe", "staff", "glow", "element", "spells", "lvl", "secs")
lock = wallet.lock  # пишем в кошелёк — под его же замком


def init(c):
    c.executescript("""
    create table if not exists fallen(id integer primary key, voter text not null, nick text not null, hero text not null,
        hat text not null, robe text not null, staff text not null, glow text not null, element integer not null,
        spells text not null, lvl integer not null, secs integer not null, map text not null, diff integer not null,
        slain integer not null default 0, created integer not null);
    create index if not exists fallen_voter on fallen(voter, created);
    create table if not exists fallen_given(voter text not null, fallen integer not null, created integer not null,
        primary key(voter, fallen));
    create table if not exists fallen_slain(fallen integer not null, voter text not null, created integer not null,
        primary key(fallen, voter));
    create index if not exists fallen_slain_voter on fallen_slain(voter, created);
    create table if not exists fallen_news(id integer primary key, owner text not null, nick text not null,
        gold integer not null, created integer not null, seen integer not null default 0);
    create index if not exists fallen_news_owner on fallen_news(owner, seen);
    """)


def ident(d, k):
    return re.sub(r"[^A-Za-z0-9_.\-]", "", str(d.get(k, "")))[:32]


def add(h, c, d, voter, now):
    try:
        element, lvl, secs, diff = int(d.get("element", 0)), int(d.get("lvl", 1)), int(d.get("secs")), int(d.get("diff", 0))
    except (TypeError, ValueError):
        return h.reply(400, {"error": "bad fallen"})
    # «Выживание» длится до четырёх часов: такого павшего принимаем, а силу считаем по первым 25 минутам
    if not MIN_SECS <= secs <= 14400:
        return h.reply(400, {"error": "bad fallen"})
    secs = min(secs, MAX_SECS)
    last = c.execute("select max(created) from fallen where voter=?", (voter,)).fetchone()[0] or 0
    if now - last < ADD_EVERY:
        return h.reply(429, {"error": "too fast"})
    nick = re.sub(r"[^\w\s\-]", "", str(d.get("nick", "")))[:24].strip() or "Безымянный маг"
    spells = re.sub(r"[\x00-\x1f\x7f]", "", str(d.get("spells", ""))).strip()[:160]
    c.execute("""insert into fallen(voter, nick, hero, hat, robe, staff, glow, element, spells, lvl, secs, map, diff, created)
                 values(?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
              (voter, nick, ident(d, "hero"), ident(d, "hat"), ident(d, "robe"), ident(d, "staff"), ident(d, "glow"),
               max(0, min(element, 4)), spells, max(1, min(lvl, 400)), secs, ident(d, "map"), max(0, min(diff, 2)), now))
    # старые слепки игрока убираем вместе с их выдачами; fallen_slain остаётся — по нему считаются трофеи
    c.execute("""delete from fallen where voter=? and id not in
                 (select id from fallen where voter=? order by created desc, id desc limit ?)""", (voter, voter, KEEP))
    c.execute("delete from fallen_given where fallen not in (select id from fallen)")
    h.reply(200, {"ok": True})


def get(h, c, d, voter, now):
    # чужой, свежий, ещё не упокоенный этим игроком; сначала с той же карты, потом кого реже упокаивали
    row = c.execute(f"""select id, {", ".join(FIELDS)} from fallen f where voter<>? and created>?
                        and not exists(select 1 from fallen_slain s where s.fallen=f.id and s.voter=?)
                        order by (map=?) desc, slain, random() limit 1""",
                    (voter, now - FRESH, voter, ident(d, "map"))).fetchone()
    c.execute("delete from fallen_given where created<?", (now - GIVEN_TTL,))
    if not row:
        return h.reply(200, {"ok": True, "id": 0})
    c.execute("""insert into fallen_given(voter, fallen, created) values(?,?,?)
                 on conflict(voter, fallen) do update set created=excluded.created""", (voter, row["id"], now))
    h.reply(200, {"ok": True, **dict(row)})


def slain(h, c, d, voter, now):
    try:
        fid = int(d.get("id"))
    except (TypeError, ValueError):
        return h.reply(400, {"error": "bad id"})
    f = c.execute("""select f.voter from fallen f join fallen_given g on g.fallen=f.id
                     where f.id=? and g.voter=? and g.created>?""", (fid, voter, now - GIVEN_TTL)).fetchone()
    if not f or f["voter"] == voter:
        return h.reply(400, {"error": "not given"})
    if c.execute("select 1 from fallen_slain where fallen=? and voter=?", (fid, voter)).fetchone():
        return h.reply(400, {"error": "already slain"})
    if c.execute("select count(*) from fallen_slain where voter=? and created>?", (voter, now - 86400)).fetchone()[0] >= SLAIN_DAY:
        return h.reply(429, {"error": "too many"})
    host = f["voter"]
    c.execute("insert into fallen_slain(fallen, voter, created) values(?,?,?)", (fid, voter, now))
    c.execute("update fallen set slain=slain+1 where id=?", (fid,))
    c.execute("delete from fallen_given where voter=? and fallen=?", (voter, fid))
    gold = wallet.grant_gold(c, voter, SLAYER_GOLD)
    # хозяину платим не больше OWNER_DAY раз в сутки, новость пишем всегда
    paid = c.execute("select count(*) from fallen_news where owner=? and gold>0 and created>?", (host, now - 86400)).fetchone()[0]
    pay = OWNER_GOLD if paid < OWNER_DAY else 0
    if pay:
        wallet.grant_gold(c, host, pay)
    nick = c.execute("select nick from players where voter=?", (voter,)).fetchone()[0]
    c.execute("insert into fallen_news(owner, nick, gold, created) values(?,?,?,?)", (host, nick, pay, now))
    total = c.execute("select count(*) from fallen_slain where voter=?", (voter,)).fetchone()[0]
    granted = [item for need, item in TROPHIES if total >= need and wallet.grant_item(c, voter, item)]
    h.reply(200, {"ok": True, "slain": total, "granted": granted, "gold": gold, "wrev": wallet.rev(c, wallet.owner(c, voter))})


def news(h, c, d, voter, now):
    rows = c.execute("select id, nick, gold from fallen_news where owner=? and seen=0 order by id desc limit 10", (voter,)).fetchall()
    c.executemany("update fallen_news set seen=1 where id=?", [(r["id"],) for r in rows])
    h.reply(200, {"ok": True, "news": [{"nick": r["nick"], "gold": r["gold"]} for r in rows]})


def LOGIN_REQUIRED():
    """Как ideas.LOGIN_REQUIRED: вход обязателен, если он заведён (HUB_CLIENT_SECRET)."""
    return os.environ.get("LOGIN_REQUIRED", "1") == "1" and bool(os.environ.get("HUB_CLIENT_SECRET"))


def post(h, path, d, voter, db):
    """Обработать POST, если путь наш. True — ответ отправлен."""
    if path not in PATHS:
        return False
    if len(voter) < 8:
        h.reply(400, {"error": "bad voter"})
        return True
    with lock, db() as c:
        if not c.execute("select 1 from players where voter=?", (voter,)).fetchone():
            h.reply(400, {"error": "unknown player"})
            return True
        op = path.rsplit("/", 1)[1]
        # павший, награда и новости — только владельцу профиля: с токеном, а без входа — лишь непривязанному к аккаунту
        # (раньше LOGIN_REQUIRED=0 открывал чужой прогресс, а news читал и гасил чужие новости всегда — ревью Codex 17.11)
        if not account.guard(h, c, voter, d, LOGIN_REQUIRED()):
            h.reply(401, {"error": "login required"})
            return True
        {"add": add, "get": get, "slain": slain, "news": news}[op](h, c, d, voter, int(time.time()))
    return True
