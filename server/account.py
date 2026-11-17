"""Вход в Magic Survivors через хаб neuraldeep (hub.neuraldeep.ru как OAuth2-провайдер, code + PKCE S256).

GET  /api/login/start?mode=web|app — PKCE и state, cookie с state, 302 на hub /oauth/authorize
GET  /api/login/callback?code&state — сверка state с cookie, обмен кода на сервере (секрет клиента),
                                       профиль с /oauth/userinfo → одноразовый билет:
                                       web — 302 на /?ms_login=<билет>, app — страница с кодом для ввода в игре
POST /api/login  {ticket, voter, nick} — билет → игровой токен; устройство привязывается к аккаунту
POST /api/me     {token}               — кто я: имя на хабе, voter и ник профиля
POST /api/logout {token}               — забыть токен

Аккаунт = id пользователя хаба (sub) + свой voter. Первый вход: voter устройства становится voter аккаунта.
Прогресс устройства переезжает в аккаунт: золото, покупки и прокачка — слиянием кошельков (merge_wallet),
рекорды — из забегов (records), клиент берёт максимум из локальных и серверных.
Вход с другого устройства: его анонимные забеги, голоса и визиты переезжают в профиль аккаунта,
клиент дальше играет под voter аккаунта. Клиенту на слово не верим: id пользователя знает только сервер,
он получил его от хаба по секрету клиента. Секрет — HUB_CLIENT_SECRET в ideas.env, без него вход выключен.
"""
import base64, hashlib, json, os, re, secrets, time, urllib.parse, urllib.request, uuid

HUB = os.environ.get("HUB_URL", "https://hub.neuraldeep.ru")
CLIENT = os.environ.get("HUB_CLIENT_ID", "magicsurvival")
SECRET = os.environ.get("HUB_CLIENT_SECRET", "")
GAME = os.environ.get("GAME_URL", "https://magicsurvival.neuraldeep.ru")
CALLBACK = GAME + "/api/login/callback"
COOKIE = "ms_login"
PENDING_TTL = 600  # от кнопки до возврата с хаба
TICKET_TTL = 300   # от возврата до обмена билета в игре
CODE_ABC = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"  # код для ввода руками: без 0/O и 1/I

db = None  # sqlite-соединение из ideas.py (через init)


def init(db_func):
    global db
    db = db_func
    with db() as c:
        c.executescript("""
        create table if not exists accounts(hub_user text primary key, voter text not null unique, nick text not null,
            name text not null default '', created integer not null);
        create table if not exists login_pending(state text primary key, verifier text not null, mode text not null, created integer not null);
        create table if not exists login_tickets(ticket text primary key, hub_user text not null, name text not null, created integer not null);
        create table if not exists game_tokens(hash text primary key, hub_user text not null, created integer not null);
        """)


def h(s):
    return hashlib.sha256(s.encode()).hexdigest()


def hub_post(path, form):
    r = urllib.request.Request(HUB + path, data=urllib.parse.urlencode(form).encode(), method="POST",
                               headers={"Content-Type": "application/x-www-form-urlencoded", "User-Agent": "magicsurvival"})
    with urllib.request.urlopen(r, timeout=15) as f:
        return json.loads(f.read())


def hub_get(path, bearer):
    r = urllib.request.Request(HUB + path, headers={"Authorization": "Bearer " + bearer, "User-Agent": "magicsurvival"})
    with urllib.request.urlopen(r, timeout=15) as f:
        return json.loads(f.read())


def redirect(hd, url, cookie=None):
    hd.send_response(302)
    hd.send_header("Location", url)
    hd.send_header("Cache-Control", "no-store")
    if cookie is not None:
        hd.send_header("Set-Cookie", cookie)
    hd.send_header("Content-Length", "0")
    hd.end_headers()


PAGE = """<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Magic Survivors — вход</title><meta name="robots" content="noindex">
<body style="margin:0;background:#0d0b10;color:#dbcdae;font:18px system-ui,sans-serif;display:flex;min-height:100vh;align-items:center;justify-content:center;text-align:center">
<div style="padding:24px">{body}</div></body>"""


def page(hd, code, body):
    b = PAGE.replace("{body}", body).encode()
    hd.send_response(code)
    hd.send_header("Content-Type", "text/html; charset=utf-8")
    hd.send_header("Cache-Control", "no-store")
    hd.send_header("Content-Length", str(len(b)))
    hd.end_headers()
    hd.wfile.write(b)


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def get(hd, u):
    """GET-ручки входа; True — запрос обработан."""
    if u.path == "/api/login/enabled":
        # клиент прячет кнопку, пока игра не заведена клиентом на хабе (нет HUB_CLIENT_SECRET)
        hd.reply(200, {"enabled": bool(SECRET)})
        return True
    if u.path == "/api/login/start":
        if not SECRET:
            page(hd, 503, "Вход через neuraldeep пока выключен")
            return True
        mode = "app" if (urllib.parse.parse_qs(u.query).get("mode") or [""])[0] == "app" else "web"
        state, verifier = secrets.token_urlsafe(24), secrets.token_urlsafe(64)
        challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip("=")
        now = int(time.time())
        with db() as c:
            c.execute("delete from login_pending where created<?", (now - PENDING_TTL,))
            c.execute("insert into login_pending(state, verifier, mode, created) values(?,?,?,?)", (state, verifier, mode, now))
        q = urllib.parse.urlencode({"response_type": "code", "client_id": CLIENT, "redirect_uri": CALLBACK, "scope": "profile",
                                    "state": state, "code_challenge": challenge, "code_challenge_method": "S256"})
        # state ещё и в cookie этого браузера: чужой code, подсунутый ссылкой, на callback не пройдёт
        redirect(hd, f"{HUB}/oauth/authorize?{q}",
                 f"{COOKIE}={state}; Path=/api/login; Max-Age={PENDING_TTL}; HttpOnly; Secure; SameSite=Lax")
        return True
    if u.path == "/api/login/callback":
        callback(hd, urllib.parse.parse_qs(u.query))
        return True
    return False


def callback(hd, q):
    state, code = (q.get("state") or [""])[0], (q.get("code") or [""])[0]
    cookies = dict(p.strip().split("=", 1) for p in (hd.headers.get("Cookie") or "").split(";") if "=" in p)
    clear = f"{COOKIE}=; Path=/api/login; Max-Age=0; HttpOnly; Secure; SameSite=Lax"
    if q.get("error"):
        return page(hd, 400, "Вход отменён. Вернись в игру и попробуй ещё раз.")
    if not state or not code or not secrets.compare_digest(cookies.get(COOKIE, ""), state):
        return page(hd, 400, "Ссылка входа устарела или открыта в другом браузере. Нажми «Войти» в игре ещё раз.")
    with db() as c:
        row = c.execute("select verifier, mode, created from login_pending where state=?", (state,)).fetchone()
        c.execute("delete from login_pending where state=?", (state,))
    if not row or row["created"] < time.time() - PENDING_TTL:
        return page(hd, 400, "Ссылка входа устарела. Нажми «Войти» в игре ещё раз.")
    try:
        tok = hub_post("/oauth/token", {"grant_type": "authorization_code", "code": code, "redirect_uri": CALLBACK,
                                        "client_id": CLIENT, "client_secret": SECRET, "code_verifier": row["verifier"]})
        me = hub_get("/oauth/userinfo", tok["access_token"])
    except Exception as e:  # тело ответа и токены не печатаем
        print("login: hub", type(e).__name__, getattr(e, "code", ""), flush=True)
        return page(hd, 502, "Хаб не ответил. Попробуй ещё раз через минуту.")
    sub = str(me.get("sub") or "")
    if not re.fullmatch(r"[0-9a-fA-F-]{8,64}", sub):
        return page(hd, 502, "Хаб вернул странный профиль. Попробуй ещё раз.")
    name = re.sub(r"[\x00-\x1f<>]", "", str(me.get("display_name") or me.get("preferred_username") or ""))[:40]
    web = row["mode"] == "web"
    ticket = secrets.token_urlsafe(24) if web else "".join(secrets.choice(CODE_ABC) for _ in range(8))
    now = int(time.time())
    with db() as c:
        c.execute("delete from login_tickets where created<?", (now - TICKET_TTL,))
        c.execute("insert into login_tickets(ticket, hub_user, name, created) values(?,?,?,?)", (ticket, sub, name, now))
    if web:
        return redirect(hd, f"{GAME}/?ms_login={ticket}", clear)
    # desktop-сборка: код вводят руками в игре — так чужая ссылка на /start не уведёт аккаунт на чужой компьютер
    page(hd, 200, f"<p>Ты вошёл как <b>{esc(name or 'игрок')}</b>.</p><p>Введи в игре код:</p>"
                  f"<p style=\"font:700 44px monospace;letter-spacing:6px;color:#dcae52\">{ticket[:4]}-{ticket[4:]}</p>"
                  f"<p style=\"opacity:.6\">Код действует 5 минут. Никому его не показывай.</p>")


def merge(c, src, dst):
    """Анонимный профиль устройства src вливается в профиль аккаунта dst."""
    c.execute("update runs set voter=? where voter=?", (dst, src))
    c.execute("update telemetry set voter=? where voter=?", (dst, src))
    c.execute("insert or ignore into votes(idea, voter) select idea, ? from votes where voter=?", (dst, src))
    c.execute("delete from votes where voter=?", (src,))
    c.execute("insert or ignore into visits(day, voter) select day, ? from visits where voter=?", (dst, src))
    c.execute("delete from visits where voter=?", (src,))
    c.execute("delete from players where voter=?", (src,))
    if c.execute("select 1 from sqlite_master where type='table' and name='run_ids'").fetchone():
        c.execute("update run_ids set voter=? where voter=?", (dst, src))   # номера забегов: доплата за продолжение — уже аккаунту
    merge_wallet(c, src, dst)
    merge_fallen(c, src, dst)
    if c.execute("select 1 from sqlite_master where type='table' and name='gear'").fetchone():
        import gear  # снаряжение — server/gear.py
        gear.merge(c, src, dst)
    if c.execute("select 1 from sqlite_master where type='table' and name='market'").fetchone():
        import market  # лоты на рынке — server/market.py
        market.merge(c, src, dst)


def merge_fallen(c, src, dst):
    """Павшие маги устройства (server/fallen.py) → профиль аккаунта: слепки, новости, упокоения, выдачи. Без fallen.py — пропуск."""
    if not c.execute("select 1 from sqlite_master where type='table' and name='fallen'").fetchone():
        return
    c.execute("update fallen set voter=? where voter=?", (dst, src))
    c.execute("update fallen_news set owner=? where owner=?", (dst, src))
    c.execute("insert or ignore into fallen_slain(fallen, voter, created) select fallen, ?, created from fallen_slain where voter=?", (dst, src))
    c.execute("delete from fallen_slain where voter=?", (src,))
    c.execute("insert or ignore into fallen_given(voter, fallen, created) select ?, fallen, created from fallen_given where voter=?", (dst, src))
    c.execute("delete from fallen_given where voter=?", (src,))


def merge_wallet(c, src, dst):
    """Кошелёк устройства (server/wallet.py) → кошелёк аккаунта: золото складываем, вещи — max(level). Без wallet.py — пропуск."""
    if not c.execute("select 1 from sqlite_master where type='table' and name='wallet'").fetchone():
        return
    # наряд и настройки (server/prefs.py): у аккаунта ещё нет своих — забирает с устройства, иначе аккаунт главнее
    if c.execute("select 1 from sqlite_master where type='table' and name='prefs'").fetchone():
        c.execute("insert or ignore into prefs(owner, data, updated) select ?, data, updated from prefs where owner=?", (dst, src))
        c.execute("delete from prefs where owner=?", (src,))
    w = c.execute("select gold, earned, spent, created from wallet where owner=?", (src,)).fetchone()
    if w:
        c.execute("""insert into wallet(owner, gold, earned, spent, created) values(?,?,?,?,?)
                     on conflict(owner) do update set gold=gold+excluded.gold, earned=earned+excluded.earned, spent=spent+excluded.spent""",
                  (dst, w["gold"], w["earned"], w["spent"], w["created"]))
        c.execute("delete from wallet where owner=?", (src,))
    # перья феникса и напитки — расходники: оплаченные штуки складываем; остальное (прокачка, покупки) — max(level)
    c.execute("""insert into owned(owner, item, level, created) select ?, item, level, created from owned where owner=? and true
                 on conflict(owner, item) do update set level=case when excluded.item='feather' or excluded.item like 'drink.%' then level+excluded.level
                                                                    else max(level, excluded.level) end""", (dst, src))
    c.execute("delete from owned where owner=?", (src,))
    c.execute("update purchases set owner=? where owner=?", (dst, src))


def bind(c, sub, name, voter, nick):
    """Аккаунт хаба ↔ игровой профиль. Возвращает (voter, nick) профиля аккаунта."""
    acc = c.execute("select voter from accounts where hub_user=?", (sub,)).fetchone()
    # устройство, которое уже чей-то аккаунт (вышли из одного и входят в другой), не сливаем
    free = len(voter) >= 8 and not c.execute("select 1 from accounts where voter=?", (voter,)).fetchone()
    if acc:
        dst = acc["voter"]
        if free and voter != dst:
            merge(c, voter, dst)
    else:
        dst = voter if free else uuid.uuid4().hex
        c.execute("insert into accounts(hub_user, voter, nick, name, created) values(?,?,?,?,?)", (sub, dst, nick, name, int(time.time())))
    c.execute("insert or ignore into players(voter, nick, created) values(?,?,?)", (dst, nick, int(time.time())))
    c.execute("update accounts set name=?, nick=(select nick from players where voter=?) where hub_user=?", (name, dst, sub))
    return dst, c.execute("select nick from players where voter=?", (dst,)).fetchone()[0]


def records(c, voter):
    """Рекорды профиля по его забегам на сервере: клиент после входа берёт максимум из своих локальных и этих."""
    r = c.execute("""select count(*), coalesce(sum(won), 0), coalesce(max(secs), 0), coalesce(max(level), 0),
                            coalesce(max(kills), 0), coalesce(sum(kills), 0) from runs where voter=?""", (voter,)).fetchone()
    return {"runs": r[0], "wins": r[1], "secs": r[2], "level": r[3], "kills": r[4], "total": r[5]}


def who(c, token):
    if len(token) < 20:
        return None
    return c.execute("""select a.hub_user, a.voter, a.name, coalesce(p.nick, a.nick) nick from game_tokens t
                        join accounts a on a.hub_user=t.hub_user left join players p on p.voter=a.voter where t.hash=?""",
                     (h(token),)).fetchone()


def authed(hd, c, voter, d):
    """Запрос от вошедшего игрока: токен из заголовка X-Ms-Token (или поле t в теле — маяк закрытой вкладки
    не умеет заголовки) живой и того же кошелька, что voter. Один voter аккаунта без токена ничего не доказывает."""
    import wallet
    tok = hd.headers.get("X-Ms-Token") or str(d.get("t") or "")
    me = who(c, tok[:128])
    return bool(me) and wallet.owner(c, me["voter"]) == wallet.owner(c, voter)


def guard(hd, c, voter, d, required):
    """Можно ли действовать от имени voter. С токеном его владельца — всегда. Без токена — только когда вход не обязателен
    И профиль не привязан к аккаунту: аварийный LOGIN_REQUIRED=0 (и свой сервер без хаба) не должен открывать чужие
    аккаунты по одному voter (ревью Codex 17.11)."""
    if authed(hd, c, voter, d):
        return True
    if required:
        return False
    import wallet
    try:
        return not c.execute("select 1 from accounts where voter=?", (wallet.owner(c, voter),)).fetchone()
    except Exception:
        return True   # таблицы аккаунтов нет — аккаунтов нет


def post(hd, path, d):
    """POST-ручки аккаунта; True — запрос обработан."""
    if path == "/api/login":
        ticket = str(d.get("ticket", "")).strip()[:64]
        short = re.sub(r"[\s-]", "", ticket).upper()  # код из desktop-флоу вводят как угодно: abcd-efgh
        if len(short) == 8:
            ticket = short
        voter = str(d.get("voter", ""))[:64]
        nick = re.sub(r"[^\w\s\-]", "", str(d.get("nick", "")))[:24].strip() or "Безымянный маг"
        now = int(time.time())
        import wallet  # слияние профилей трогает золото и вещи — под замком кошелька, взятым до первой записи
        with wallet.lock, db() as c:
            row = c.execute("select hub_user, name, created from login_tickets where ticket=?", (ticket,)).fetchone()
            c.execute("delete from login_tickets where ticket=?", (ticket,))
            if not ticket or not row or row["created"] < now - TICKET_TTL:
                hd.reply(400, {"error": "Код не подошёл или устарел — войди ещё раз"})
                return True
            device = voter
            voter, nick = bind(c, row["hub_user"], row["name"], voter, nick)
            # профиль устройства слит в аккаунт (merge) — клиент переносит туда свою очередь неотправленных забегов
            merged = device != voter and not c.execute("select 1 from players where voter=?", (device,)).fetchone()
            token = secrets.token_urlsafe(32)
            c.execute("insert into game_tokens(hash, hub_user, created) values(?,?,?)", (h(token), row["hub_user"], now))
            rec = records(c, voter)
        hd.reply(200, {"ok": True, "token": token, "voter": voter, "nick": nick, "name": row["name"], "rec": rec,
                       "merged": device if merged else ""})
        return True
    if path == "/api/me":
        with db() as c:
            me = who(c, str(d.get("token", "")))
            rec = records(c, me["voter"]) if me else None
        if not me:
            hd.reply(401, {"error": "not logged in"})
        else:
            hd.reply(200, {"ok": True, "voter": me["voter"], "nick": me["nick"], "name": me["name"], "rec": rec})
        return True
    if path == "/api/logout":
        with db() as c:
            c.execute("delete from game_tokens where hash=?", (h(str(d.get("token", ""))),))
        hd.reply(200, {"ok": True})
        return True
    return False
