#!/usr/bin/env python3
"""Идеи игроков и голосование для Magic Survivors.

POST /api/ideas {text, voter}      — новая идея → строка в SQLite → issue в GitHub (метка idea)
GET  /api/ideas?voter=…            — идеи, одобренные к голосованию (на issue висит метка voting), с голосами
POST /api/vote  {id, voter, value} — голос «за» (1) или снять (0); один голос на устройство
POST /api/ping  {voter, state}     — игра открыта (menu | play), раз в 30 с
POST /api/fallen/add|get|slain|news — павшие маги (server/fallen.py)
POST /api/gear[/equip|unequip|enchant|salvage] — снаряжение (server/gear.py); добыча выдаётся в /api/run
GET  /api/admin?key=ADMIN_TOKEN    — админка: сколько людей играет сейчас (данные — /api/admin/online)

Сейчас (IDEAS_AUTO_APPROVE=1, по умолчанию) идеи сразу попадают в голосование, GitHub не нужен.
С токеном и IDEAS_AUTO_APPROVE=0 одобряет владелец меткой voting на GitHub — фоновая синхронизация раз в 2 минуты подтягивает метки
и пишет в issue текущее число голосов. Без GITHUB_TOKEN идеи копятся и уйдут в GitHub, когда токен появится.
Только stdlib: http.server + sqlite3 + urllib.
"""
import hashlib, json, os, re, sqlite3, threading, time, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

import presence  # онлайн и админка — server/presence.py
import wallet  # золото и покупки — server/wallet.py
import account  # вход через hub.neuraldeep.ru — server/account.py
import fallen  # павшие маги — server/fallen.py
import gear  # снаряжение — server/gear.py
import town  # список админов (admins.txt) — server/town.py
import market  # рынок вещей между игроками — server/market.py
import prefs  # наряд, герой и настройки игрока — server/prefs.py

DB = os.environ.get("IDEAS_DB", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "ideas.db"))
PORT = int(os.environ.get("IDEAS_PORT", "8797"))
REPO = os.environ.get("IDEAS_REPO", "")
TOKEN = os.environ.get("GITHUB_TOKEN", "")
SALT = os.environ.get("IDEAS_SALT", "change-me")
# без модерации: идея сразу в голосовании
AUTO = os.environ.get("IDEAS_AUTO_APPROVE", "1") == "1"
# вход через neuraldeep обязателен: забег и награда за павшего засчитываются только аккаунту.
# Работает, только когда вход заведён (HUB_CLIENT_SECRET), иначе играть было бы нельзя вовсе
LOGIN_REQUIRED = os.environ.get("LOGIN_REQUIRED", "1") == "1" and bool(account.SECRET)
TELEMETRY_AUTH_FROM = int(os.environ.get("TELEMETRY_AUTH_FROM", "1794992400"))   # 2026-11-18 12:00 МСК
lock = threading.Lock()


def db():
    c = sqlite3.connect(DB, timeout=10)
    c.row_factory = sqlite3.Row
    return c


def init():
    os.makedirs(os.path.dirname(DB), exist_ok=True)
    with db() as c:
        c.executescript("""
        create table if not exists ideas(id integer primary key, text text not null, created integer not null,
            ip text not null, issue integer, approved integer not null default 0, closed integer not null default 0,
            synced_votes integer not null default -1);
        create table if not exists votes(idea integer not null, voter text not null, primary key(idea, voter));
        create table if not exists players(voter text primary key, nick text not null, created integer not null);
        create table if not exists runs(id integer primary key, voter text not null, secs integer not null,
            level integer not null, kills integer not null, won integer not null, created integer not null);
        create table if not exists visits(day text not null, voter text not null, primary key(day, voter));
        create table if not exists telemetry(run text primary key, voter text not null, created integer not null, updated integer not null,
            secs integer not null, level integer not null, won integer not null, end_reason text not null, data text not null);
        create table if not exists run_ids(rid text primary key, voter text not null, created integer not null,
            run integer not null default 0, secs integer not null default 0, gold integer not null default 0, final integer not null default 0);
        """)
        # объявления на всех (server/news.py): баннер в меню и в городе, until — до какого времени показывать (0 — всегда)
        c.execute("create table if not exists news(id integer primary key, text text not null, created integer not null, until integer not null default 0)")
        if "mode" not in [r[1] for r in c.execute("pragma table_info(runs)")]:
            c.execute("alter table runs add column mode integer not null default 0")   # 1 — «Выживание»
        wallet.init(c)
        fallen.init(c)
        gear.init(c)
    account.init(db)


def gh(method, path, body=None):
    r = urllib.request.Request("https://api.github.com" + path, method=method,
                               data=json.dumps(body).encode() if body is not None else None,
                               headers={"Authorization": f"Bearer {TOKEN}", "Accept": "application/vnd.github+json",
                                        "User-Agent": "magicsurvival-ideas"})
    with urllib.request.urlopen(r, timeout=20) as f:
        return json.loads(f.read() or b"null")


def issue_body(text, votes):
    return (f"{text}\n\n---\nИдея от игрока из игры. **Голосов: {votes}**\n\n"
            "Повесь метку `voting`, чтобы идея появилась в голосовании в игре; закрой issue — уберётся.")


def sync():
    """Идеи без issue → создать; метки и закрытие → в базу; число голосов → в текст issue."""
    if not TOKEN:
        return
    with db() as c:
        for row in c.execute("select id, text from ideas where issue is null").fetchall():
            title = re.sub(r"\s+", " ", row["text"])[:70]
            res = gh("POST", f"/repos/{REPO}/issues", {"title": f"Идея: {title}", "body": issue_body(row["text"], 0), "labels": ["idea"]})
            c.execute("update ideas set issue=?, synced_votes=0 where id=?", (res["number"], row["id"]))
        page = 1
        while True:
            items = gh("GET", f"/repos/{REPO}/issues?labels=idea&state=all&per_page=100&page={page}")
            for it in items:
                labels = {l["name"] for l in it.get("labels", [])}
                c.execute("update ideas set approved=?, closed=? where issue=?",
                          (1 if "voting" in labels else 0, 1 if it["state"] == "closed" else 0, it["number"]))
            if len(items) < 100:
                break
            page += 1
        for row in c.execute("""select i.id, i.text, i.issue, i.synced_votes, count(v.voter) n from ideas i
                                left join votes v on v.idea=i.id where i.issue is not null group by i.id""").fetchall():
            if row["n"] != row["synced_votes"]:
                gh("PATCH", f"/repos/{REPO}/issues/{row['issue']}", {"body": issue_body(row["text"], row["n"])})
                c.execute("update ideas set synced_votes=? where id=?", (row["n"], row["id"]))


def sync_safe():
    try:
        with lock:
            sync()
    except Exception as e:  # сеть или лимиты GitHub — попробуем в следующий раз
        print("sync:", type(e).__name__, e, flush=True)


def sync_loop():
    while True:
        sync_safe()
        time.sleep(120)



def long_cap(c, rid, voter):
    """Сколько секунд забега подтверждают часы сервера. Окно забега — от первого до последнего среза его телеметрии
    (срез раз в минуту, rid = номер телеметрии) ±60 с: от времени доставки итога не зависит, поэтому поздняя очередь
    честных забегов не режется. Из окна вычитаются окна других засчитанных забегов того же кошелька — одно и то же
    время не оплачивается дважды, сколько ни заводи параллельных забегов и снимков. Без телеметрии — две минуты
    (первый срез уходит на 60-й секунде)."""
    t = c.execute("select created, updated, voter from telemetry where run=?", (rid,)).fetchone() if rid else None
    o = wallet.owner(c, voter)
    if not t or wallet.owner(c, t[2]) != o:
        return 120
    a, b = t[0] - 60, t[1] + 60
    spans = []
    for s0, s1, v in c.execute("""select t.created - 60, t.updated + 60, r.voter from run_ids r join telemetry t on t.run = r.rid
                                  where r.rid <> ? and t.created - 60 < ? and t.updated + 60 > ?""", (rid, b, a)):
        if wallet.owner(c, v) == o:
            spans.append((max(a, s0), min(b, s1)))
    covered, cur = 0, None
    for s0, s1 in sorted(spans):
        if cur and s0 <= cur[1]:
            cur[1] = max(cur[1], s1)
        else:
            covered += cur[1] - cur[0] if cur else 0
            cur = [s0, s1]
    covered += cur[1] - cur[0] if cur else 0
    return b - a - covered + 60

class H(BaseHTTPRequestHandler):
    def reply(self, code, obj):
        b = json.dumps(obj, ensure_ascii=False).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def ip(self):
        raw = self.headers.get("X-Real-IP") or self.client_address[0]
        return hashlib.sha256((SALT + raw).encode()).hexdigest()[:16]

    def body(self):
        n = min(int(self.headers.get("Content-Length") or 0), 131072)
        try:
            return json.loads(self.rfile.read(n) or b"{}")
        except ValueError:
            return {}

    def do_GET(self):
        u = urlparse(self.path)
        if account.get(self, u):
            return
        if presence.get(self, u):
            return
        if u.path == "/api/leaders":
            return self.reply(200, leaders((parse_qs(u.query).get("voter") or [""])[0][:64]))
        if u.path == "/api/stats":
            return self.reply(200, stats())
        if u.path == "/api/market":
            mv = (parse_qs(u.query).get("voter") or [""])[0][:64]
            return self.reply(200, market.get(mv, db, lambda c: account.guard(self, c, mv, {}, LOGIN_REQUIRED)))
        if u.path == "/api/news":
            with db() as c:
                rows = c.execute("select id, text from news where until=0 or until>? order by id desc limit 3", (int(time.time()),)).fetchall()
            return self.reply(200, {"items": [dict(r) for r in rows], "login": LOGIN_REQUIRED})
        if u.path != "/api/ideas":
            return self.reply(404, {"error": "not found"})
        voter = (parse_qs(u.query).get("voter") or [""])[0][:64]
        with db() as c:
            if not account.guard(self, c, voter, {}, LOGIN_REQUIRED):
                voter = ""   # свои голоса видит только владелец, остальным — общий список (ревью Codex 17.11)
            rows = c.execute("""select i.id, i.text, count(v.voter) votes,
                                max(case when v.voter=? then 1 else 0 end) mine
                                from ideas i left join votes v on v.idea=i.id
                                where i.approved=1 and i.closed=0 group by i.id order by votes desc, i.id desc limit 50""",
                             (voter,)).fetchall()
        self.reply(200, {"ideas": [dict(r) for r in rows]})

    def do_POST(self):
        path, d = urlparse(self.path).path, self.body()
        if account.post(self, path, d):
            return
        voter = str(d.get("voter", ""))[:64]
        if path == "/api/ideas":
            text = re.sub(r"[\x00-\x08\x0b-\x1f]", "", str(d.get("text", ""))).strip()[:600]
            if len(text) < 8:
                return self.reply(400, {"error": "Слишком коротко — опиши идею чуть подробнее"})
            ip, now = self.ip(), int(time.time())
            with db() as c:
                hour = c.execute("select count(*) from ideas where ip=? and created>?", (ip, now - 3600)).fetchone()[0]
                day = c.execute("select count(*) from ideas where ip=? and created>?", (ip, now - 86400)).fetchone()[0]
                if hour >= 3 or day >= 10:
                    return self.reply(429, {"error": "Идей пока хватит — попробуй позже"})
                c.execute("insert into ideas(text, created, ip, approved) values(?,?,?,?)", (text, now, ip, 1 if AUTO else 0))
            threading.Thread(target=sync_safe, daemon=True).start()
            return self.reply(200, {"ok": True})
        if presence.post(self, path, d, voter):
            return
        if prefs.post(self, path, d, voter, db, lambda c: account.guard(self, c, voter, d, LOGIN_REQUIRED)):
            return
        if wallet.post(self, path, d, voter, db, lambda c: account.guard(self, c, voter, d, LOGIN_REQUIRED)):
            return
        if fallen.post(self, path, d, voter, db):
            return
        if gear.post(self, path, d, voter, db, lambda c: account.guard(self, c, voter, d, LOGIN_REQUIRED)):
            return
        if market.post(self, path, d, voter, db, lambda c: account.guard(self, c, voter, d, LOGIN_REQUIRED)):
            return
        if path == "/api/telemetry":
            # срез забега: перезаписываем по id забега, последний срез — самый полный
            run = str(d.get("run", ""))
            if not re.fullmatch(r"[0-9a-f]{32}", run) or len(voter) < 8:
                return self.reply(400, {"error": "bad run"})
            try:
                secs, level, won = int(d.get("secs", 0)), int(d.get("lvl", 0)), int(d.get("won", 0))
            except (TypeError, ValueError):
                return self.reply(400, {"error": "bad run"})
            end = str(d.get("end", "live"))[:16]
            now = int(time.time())
            with db() as c:
                # срез пишет только владелец профиля (ревью Codex 17.11). Клиент шлёт токен с этого дня; до TELEMETRY_AUTH_FROM
                # старые вкладки без токена ещё принимаем, иначе их забеги потеряли бы окно телеметрии и золото
                if now >= TELEMETRY_AUTH_FROM and not account.guard(self, c, voter, d, LOGIN_REQUIRED):
                    return self.reply(401, {"error": "login required"})
                old = c.execute("select end_reason, voter from telemetry where run=?", (run,)).fetchone()
                if old and wallet.owner(c, old[1]) != wallet.owner(c, voter):
                    return self.reply(403, {"error": "not your run"})   # чужой забег по известному run не трогаем
                if old and old[0] != "live" and end == "live":
                    return self.reply(200, {"ok": True})  # финал уже записан, поздний срез не затирает
                c.execute("""insert into telemetry(run, voter, created, updated, secs, level, won, end_reason, data) values(?,?,?,?,?,?,?,?,?)
                             on conflict(run) do update set updated=excluded.updated, secs=excluded.secs, level=excluded.level,
                             won=excluded.won, end_reason=excluded.end_reason, data=excluded.data""",
                          (run, voter, now, now, secs, level, won, end, json.dumps(d, ensure_ascii=False)))
            return self.reply(200, {"ok": True})
        if path == "/api/hello":
            # запуск игры: регистрируем устройство под случайным ником и отмечаем визит за день
            if len(voter) < 8:
                return self.reply(400, {"error": "bad voter"})
            nick = re.sub(r"[^\w\s\-]", "", str(d.get("nick", "")))[:24].strip() or "Безымянный маг"
            with db() as c:
                c.execute("insert or ignore into players(voter, nick, created) values(?,?,?)", (voter, nick, int(time.time())))
                c.execute("insert or ignore into visits(day, voter) values(?,?)", (time.strftime("%Y-%m-%d"), voter))
                nick = c.execute("select nick from players where voter=?", (voter,)).fetchone()[0]
            return self.reply(200, {"ok": True, "nick": nick})
        if path == "/api/nick":
            # смена ника: тот же фильтр, что при регистрации; пустой или из одного символа не принимаем
            nick = re.sub(r"[^\w\s\-]", "", str(d.get("nick", "")))[:24].strip()
            if len(voter) < 8 or len(nick) < 2:
                return self.reply(400, {"error": "bad nick"})
            with db() as c:
                if not c.execute("select 1 from players where voter=?", (voter,)).fetchone():
                    return self.reply(400, {"error": "unknown player"})
                # ник аккаунта меняет только его владелец: раньше по чужому voter переименовывали кого угодно (ревью Codex)
                if not account.guard(self, c, voter, d, LOGIN_REQUIRED):
                    return self.reply(401, {"error": "login required"})
                c.execute("update players set nick=? where voter=?", (nick, voter))
                if c.execute("select 1 from sqlite_master where type='table' and name='accounts'").fetchone():
                    c.execute("update accounts set nick=? where voter=?", (nick, voter))
            return self.reply(200, {"ok": True, "nick": nick})
        if path == "/api/run":
            try:
                secs, level, kills, won = int(d.get("secs")), int(d.get("level")), int(d.get("kills")), int(bool(d.get("won")))
            except (TypeError, ValueError):
                return self.reply(400, {"error": "bad run"})
            # грубая защита от мусора: забег не длиннее 25 минут, уровень и убийства в разумных пределах
            # «Выживание» (mode=1) длится сколько продержишься — до четырёх часов
            mode = 1 if d.get("mode") == 1 else 0
            if len(voter) < 8 or not (0 <= secs <= (14400 if mode else 1500) and 1 <= level <= 999 and 0 <= kills <= 1000000):
                return self.reply(400, {"error": "bad run"})
            now = int(time.time())
            # номер забега: клиент шлёт забег повторно (обрыв связи, закрытая вкладка, перезагрузка сцены) — засчитываем один раз
            rid = d.get("rid") if isinstance(d.get("rid"), str) and 8 <= len(d.get("rid")) <= 40 else None
            # замок кошелька — до первой записи: золото и добыча, а порядок «замок, потом SQLite» везде один
            with wallet.lock, db() as c:
                if not c.execute("select 1 from players where voter=?", (voter,)).fetchone():
                    return self.reply(400, {"error": "unknown player"})
                # без аккаунта не засчитываем: 401 клиент оставляет забег в очереди, после входа очередь уйдёт в аккаунт
                if not account.guard(self, c, voter, d, LOGIN_REQUIRED):
                    return self.reply(401, {"error": "login required"})
                seen = c.execute("select voter, run, secs, gold, final from run_ids where rid=?", (rid,)).fetchone() if rid else None
                final = 0 if d.get("quit") else 1
                # любой забег сверяем с часами сервера (long_cap): не больше его окна телеметрии за вычетом окон других
                # засчитанных забегов того же кошелька. Так поддельные забеги не складываются и упираются в реальное время.
                # Не отказ, а обрезка. По проду 15.11: телеметрия есть у 45 из 46 забегов, честные влезают в окно +180 с
                if secs > 120:
                    secs = max(120, min(secs, long_cap(c, rid, voter)))
                # без телеметрии (забег до минуты, старый клиент) — тоже не быстрее часов: с прошлого забега
                if not seen and not (rid and c.execute("select 1 from telemetry where run=?", (rid,)).fetchone()):
                    prev = c.execute("select max(created) from runs where voter=?", (voter,)).fetchone()[0] or 0
                    born = c.execute("select created from players where voter=?", (voter,)).fetchone()[0]
                    secs = max(0, min(secs, now - max(prev, born)))
                if seen:
                    o = wallet.owner(c, voter)
                    # забег уже засчитан снимком «вышел на этом месте» (маяк, вторая вкладка), а это его продолжение:
                    # доначисляем разницу и обновляем строку забега. Добычу второй раз не катаем, кроме вещи за победу
                    credited, drops, sold = -1, [], 0
                    # продолжение — дольше снимка или настоящий итог в ту же секунду, пока засчитан только снимок
                    later = secs > seen[2] or (secs == seen[2] and final and not seen[4])
                    if later and wallet.owner(c, seen[0]) == o:
                        g = wallet.run_gold(d, secs, level, kills, won)
                        delta = max(0, g - seen[3])
                        c.execute("update run_ids set secs=?, gold=?, final=? where rid=?", (secs, max(g, seen[3]), final, rid))
                        was = c.execute("select won from runs where id=?", (seen[1],)).fetchone()
                        c.execute("update runs set secs=?, level=?, kills=?, won=? where id=?", (secs, level, kills, won, seen[1]))
                        if delta:
                            wallet.ensure(c, o)
                            c.execute("update wallet set gold=gold+?, earned=earned+? where owner=?", (delta, delta, o))
                        credited = max(g, seen[3])
                        # снимок был без победы, итог — победа: вещь за победу (дневной лимит считает gear.roll)
                        if won and was and not was[0]:
                            drops, sold = gear.roll(c, voter, {**d, "secs": 0}, 0, won)
                    row = c.execute("select gold from wallet where owner=?", (o,)).fetchone()
                    return self.reply(200, {"ok": True, "dup": True, "gold": row[0] if row else 0, "wrev": wallet.rev(c, o),
                                            "credited": credited, "drops": drops, "sold": sold})
                last = c.execute("select max(created) from runs where voter=?", (voter,)).fetchone()[0] or 0
                if now - last < 20:
                    return self.reply(429, {"error": "too fast"})
                cur = c.execute("insert into runs(voter, secs, level, kills, won, created, mode) values(?,?,?,?,?,?,?)", (voter, secs, level, kills, won, now, mode))
                credited = wallet.run_gold(d, secs, level, kills, won)
                if rid:
                    c.execute("insert into run_ids(rid, voter, created, run, secs, gold, final) values(?,?,?,?,?,?,?)",
                              (rid, voter, now, cur.lastrowid, secs, credited, 0 if d.get("quit") else 1))
                gold = wallet.credit(c, voter, d, secs, level, kills, won)
                drops, sold = gear.roll(c, voter, d, secs, won)
                gold += sold
                wrev = wallet.rev(c, wallet.owner(c, voter))
            return self.reply(200, {"ok": True, "gold": gold, "wrev": wrev, "credited": credited, "drops": drops, "sold": sold})
        if path == "/api/vote":
            try:
                idea, value = int(d.get("id")), int(d.get("value", 1))
            except (TypeError, ValueError):
                return self.reply(400, {"error": "bad id"})
            if len(voter) < 8:
                return self.reply(400, {"error": "bad voter"})
            with db() as c:
                if not account.guard(self, c, voter, d, LOGIN_REQUIRED):   # голос аккаунта — только его владельцу
                    return self.reply(401, {"error": "login required"})
                ok = c.execute("select 1 from ideas where id=? and approved=1 and closed=0", (idea,)).fetchone()
                if not ok:
                    return self.reply(404, {"error": "нет такой идеи"})
                if value:
                    c.execute("insert or ignore into votes(idea, voter) values(?,?)", (idea, voter))
                else:
                    c.execute("delete from votes where idea=? and voter=?", (idea, voter))
            return self.reply(200, {"ok": True})
        self.reply(404, {"error": "not found"})

    def log_message(self, *a):
        pass


def leaders(voter):
    """Четыре топа: лучший забег, сумма минут, богачи (золото сейчас) и смерти; плюс ник игрока."""
    with db() as c:
        best = c.execute("""select p.nick, max(r.secs) secs, max(r.level) level, p.voter=? me from runs r join players p on p.voter=r.voter
                            group by r.voter order by secs desc limit 20""", (voter,)).fetchall()
        total = c.execute("""select p.nick, sum(r.secs) secs, count(*) runs, p.voter=? me from runs r join players p on p.voter=r.voter
                             group by r.voter order by secs desc limit 20""", (voter,)).fetchall()
        # богачи — золото кошелька сейчас; кошелёк общий на аккаунт, ник берём у владельца. Админы не в счёт:
        # их золото выдано руками, а не заработано
        me_o = wallet.owner(c, voter)
        rich = [r for r in c.execute("""select p.nick, w.gold, w.owner=? me, (select hub_user from accounts a where a.voter=w.owner) hub
                             from wallet w join players p on p.voter=w.owner order by w.gold desc limit 40""", (me_o,))
                if not town.is_admin(r["hub"])][:20]
        # смерти — забеги, закончившиеся гибелью мага (телеметрия end_reason='dead'); выход в меню смертью не считается
        dead = c.execute("""select p.nick, count(*) deaths, max(t.secs) secs, p.voter=? me from telemetry t join players p on p.voter=t.voter
                            where t.end_reason='dead' group by t.voter order by deaths desc limit 20""", (voter,)).fetchall()
        me = c.execute("select nick from players where voter=?", (voter,)).fetchone()
    return {"best": [dict(r) for r in best], "total": [dict(r) for r in total],
            "rich": [{"nick": r["nick"], "gold": r["gold"], "me": r["me"]} for r in rich],
            "dead": [dict(r) for r in dead], "nick": me[0] if me else None}


def stats():
    """Сводка для разработчика: устройства, визиты по дням, забеги и минуты."""
    with db() as c:
        days = c.execute("select day, count(*) n from visits group by day order by day desc limit 14").fetchall()
        runs = c.execute("select count(*) n, coalesce(sum(secs),0) secs, coalesce(sum(won),0) wins from runs").fetchone()
        players = c.execute("select count(*) from players").fetchone()[0]
        ideas = c.execute("select count(*) from ideas").fetchone()[0]
        t = c.execute("select count(*) n, sum(secs>=900) full15, sum(won) wins, coalesce(avg(secs),0) avg from telemetry").fetchone()
    return {"players": players, "runs": runs["n"], "minutes": round(runs["secs"] / 60), "wins": runs["wins"],
            "ideas": ideas, "tracked_runs": t["n"], "runs_15min": t["full15"] or 0, "avg_minutes": round(t["avg"] / 60, 1),
            "daily": [dict(r) for r in days]}


def report(min_secs=0):
    """Сводка по забегам для баланса: длительность, причины смерти, урон по школам, пик-рейт карт, FPS."""
    with db() as c:
        rows = [json.loads(r[0]) for r in c.execute("select data from telemetry where secs>=?", (min_secs,))]
    if not rows:
        print("забегов нет"); return
    n = len(rows)
    print(f"забегов: {n}  (от {min_secs // 60} мин)  побед: {sum(r.get('won', 0) for r in rows)}")
    mins = sorted(r["secs"] // 60 for r in rows)
    print("минута конца забега:", " ".join(f"{m}:{mins.count(m)}" for m in sorted(set(mins))))
    ends = {}
    for r in rows: ends[r.get("end")] = ends.get(r.get("end"), 0) + 1
    print("как закончились:", ends)
    killers = {}
    for r in rows:
        if r.get("killedBy"): killers[r["killedBy"]] = killers.get(r["killedBy"], 0) + 1
    print("кто убил:", dict(sorted(killers.items(), key=lambda kv: -kv[1])))
    names = rows[0].get("schools") or []
    tot = [0] * 8
    for r in rows:
        for i, v in enumerate(r.get("dmg") or []): tot[i] += v
    s = sum(tot) or 1
    print("урон по школам:", {(names[i] if i < len(names) and names[i] else "без школы"): f"{100 * v / s:.0f}%" for i, v in enumerate(tot) if v})
    offered, picked = {}, {}
    for r in rows:
        for p in r.get("picks") or []:
            if p.get("kind") != "lvl": continue
            for o in p.get("offered") or []: offered[o] = offered.get(o, 0) + 1
            picked[p["pick"]] = picked.get(p["pick"], 0) + 1
    print("пик-рейт карт (взяли/предложили):")
    for k in sorted(offered, key=lambda k: -picked.get(k, 0) / offered[k]):
        print(f"  {picked.get(k, 0) / offered[k]:.0%}  {picked.get(k, 0)}/{offered[k]}  {k}")
    fps = [r.get("fpsAvg", 0) for r in rows if r.get("fpsAvg")]
    if fps: print(f"FPS средний по забегам: {sum(fps) / len(fps):.0f}, худший минимум: {min(r.get('fpsMin', 99) or 99 for r in rows)}")
    plat = {}
    for r in rows: plat[r.get("platform")] = plat.get(r.get("platform"), 0) + 1
    print("платформы:", plat)


def admin(args):
    """python3 ideas.py list | delete <id> | report [мин] — идеи и сводка по забегам."""
    init()
    if args[0] == "report":
        return report(int(args[1]) * 60 if len(args) > 1 else 0)
    with db() as c:
        if args[0] == "list":
            for r in c.execute("select i.id, i.text, i.approved, count(v.voter) n from ideas i left join votes v on v.idea=i.id group by i.id order by i.id"):
                print(f"#{r['id']:<4} {r['n']:>3} голосов  {'вкл ' if r['approved'] else 'скрыта'}  {r['text']}")
        elif args[0] == "delete":
            c.execute("delete from votes where idea=?", (int(args[1]),))
            c.execute("delete from ideas where id=?", (int(args[1]),))
            print("удалено", args[1])


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1:
        admin(sys.argv[1:])
        raise SystemExit
    init()
    threading.Thread(target=sync_loop, daemon=True).start()
    presence.start(db)
    ThreadingHTTPServer(("127.0.0.1", PORT), H).serve_forever()
