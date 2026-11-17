"""Наряд, выбранный герой и настройки игрока — на сервере, у владельца кошелька (аккаунта, если вошёл).

Фидбек: «чтобы игра запоминала, в каком наряде я вышел, какие настройки стояли — всё пускай хранится».
Раньше всё лежало только в браузере (PlayerPrefs): другое устройство — другой наряд и настройки.

Клиент шлёт только изменённые ключи, сервер сливает их с сохранёнными. Строка «ключ=значение» по строке; принимаем только ключи из KEYS, значения короткие. Отдаётся в ответе кошелька
(wallet.state → "prefs"), пишется POST /api/prefs {voter, data, token} — только владельцем, как и кошелёк.
"""
import re, time

KEYS = {"cos.hat", "cos.robe", "cos.staff", "cos.glow", "sel.hero", "sel.mode", "music", "sfx", "chat.min",
        "opt.numbers", "opt.shake", "opt.bloom", "opt.fps", "opt.calm", "opt.high", "opt.mute"}
VALUE = re.compile(r"^[\w.\-]{0,40}$")


def init(c):
    c.execute("create table if not exists prefs(owner text primary key, data text not null, updated integer not null)")


def clean(text):
    out = []
    for line in str(text or "")[:4000].split("\n"):
        k, _, v = line.partition("=")
        if k in KEYS and VALUE.match(v):
            out.append(f"{k}={v}")
    return "\n".join(sorted(set(out)))


def get(c, o):
    init(c)
    row = c.execute("select data from prefs where owner=?", (o,)).fetchone()
    return row[0] if row else ""


def post(h, path, d, voter, db, authed):
    if path != "/api/prefs":
        return False
    import wallet
    if len(voter) < 8:
        h.reply(400, {"error": "bad voter"})
        return True
    with wallet.lock, db() as c:
        init(c)
        if not c.execute("select 1 from players where voter=?", (voter,)).fetchone():
            h.reply(400, {"error": "unknown player"})
            return True
        if not authed(c):
            h.reply(401, {"error": "Войди через neuraldeep"})
            return True
        o = wallet.owner(c, voter)
        # клиент шлёт только изменённые ключи: сливаем с сохранённым, иначе второе устройство со старым снимком
        # затирало бы то, что поменяли на первом (ревью Codex)
        cur = dict(l.split("=", 1) for l in get(c, o).split("\n") if "=" in l)
        cur.update(dict(l.split("=", 1) for l in clean(d.get("data")).split("\n") if "=" in l))
        data = clean("\n".join(f"{k}={v}" for k, v in cur.items()))
        c.execute("insert into prefs(owner, data, updated) values(?,?,?) on conflict(owner) do update set data=excluded.data, updated=excluded.updated",
                  (o, data, int(time.time())))
        h.reply(200, {"ok": True})
    return True
