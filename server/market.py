"""Рынок Magic Survivors: игроки продают друг другу вещи снаряжения за золото. Подключается из ideas.py.

Вещь на рынке не принадлежит никому (gear.owner = 'market'): продавец не может её надеть, заточить или разобрать,
пока не снимет лот. Покупка — одна транзакция под замком кошелька: золото покупателя → продавцу минус комиссия,
вещь → в сумку покупателя. Лот живёт LOT_TTL, потом вещь возвращается продавцу (при любом чтении рынка).

GET  /api/market?voter=…                    — открытые лоты (LIST свежих) плюс все мои, сколько бы им ни было
POST /api/market/sell    {voter, id, price}  — выставить вещь из сумки
POST /api/market/buy     {voter, lot}        — купить
POST /api/market/cancel  {voter, lot}        — снять свой лот, вещь вернётся в сумку
Продавать и покупать — только вошедшим через neuraldeep, когда вход обязателен (как забеги).
"""
import time

import gear
import wallet

FEE = 0.05            # комиссия рынка: золото уходит из игры, иначе оно только копится
MIN_PRICE, MAX_PRICE = 10, 500000
MAX_LOTS = 8          # открытых лотов на продавца
LOT_TTL = 7 * 86400
LIST = 120            # сколько лотов отдаём за раз
lock = wallet.lock


def init(c):
    c.execute("""create table if not exists market(id integer primary key autoincrement, seller text not null,
        gear_id integer not null, price integer not null, created integer not null, buyer text, closed integer not null default 0)""")
    c.execute("create index if not exists market_open on market(closed, created)")


def expire(c):
    """Просроченные лоты: вещь обратно продавцу, лот закрыт."""
    old = c.execute("select id, seller, gear_id from market where closed=0 and created<?", (int(time.time()) - LOT_TTL,)).fetchall()
    for lot, seller, gid in old:
        c.execute("update gear set owner=?, equipped=0 where id=? and owner='market'", (seller, gid))
        c.execute("update market set closed=? where id=?", (int(time.time()), lot))


def lots(c, o):
    rows = c.execute("""select m.id, m.price, m.seller, m.created, g.slot, g.grade, g.affix, g.enchant, g.kind,
                               coalesce(p.nick, 'Маг') nick
                        from market m join gear g on g.id=m.gear_id left join players p on p.voter=m.seller
                        where m.closed=0 and (m.seller=? or m.id in (select id from market where closed=0 order by created desc limit ?))
                        order by m.created desc""", (o, LIST)).fetchall()
    return [{"lot": r[0], "price": r[1], "mine": 1 if r[2] == o else 0, "created": r[3],
             "item": {"id": 0, "slot": r[4], "grade": r[5], "affix": r[6], "enchant": r[7], "kind": r[8], "equipped": 0},
             "nick": r[9]} for r in rows]


def state(c, o, **extra):
    s = gear.state(c, o, **extra)
    s["lots"] = lots(c, o)
    s["fee"] = FEE
    return s


def get(voter, db, check=None):
    """check(c) — владелец ли спрашивает; чужому — только общий список лотов, без своей сумки (ревью Codex 17.11)."""
    with lock, db() as c:
        init(c)
        expire(c)
        o = wallet.owner(c, voter)
        if check is not None and not check(c):
            o = ""
        return state(c, o)


def post(h, path, d, voter, db, authed):
    """authed(c) — проверка входа (account.authed), когда вход обязателен; без неё рынок открыт всем."""
    if not path.startswith("/api/market/"):
        return False
    if len(voter) < 8:
        h.reply(400, {"error": "bad voter"})
        return True
    with lock, db() as c:
        init(c)
        if not c.execute("select 1 from players where voter=?", (voter,)).fetchone():
            h.reply(400, {"error": "unknown player"})
            return True
        if not authed(c):
            h.reply(401, {"error": "Войди через neuraldeep, чтобы торговать"})
            return True
        expire(c)
        o = wallet.owner(c, voter)
        wallet.ensure(c, o)
        now = int(time.time())
        if path == "/api/market/sell":
            try:
                gid, price = int(d.get("id")), int(d.get("price"))
            except (TypeError, ValueError):
                h.reply(400, {"error": "bad id"})
                return True
            if not MIN_PRICE <= price <= MAX_PRICE:
                h.reply(400, {"error": f"цена от {MIN_PRICE} до {MAX_PRICE}"})
                return True
            row = c.execute("select equipped from gear where id=? and owner=?", (gid, o)).fetchone()
            if not row:
                h.reply(404, {"error": "нет такой вещи"})
                return True
            if row[0]:
                h.reply(409, {"error": "сначала сними"})
                return True
            if c.execute("select count(*) from market where seller=? and closed=0", (o,)).fetchone()[0] >= MAX_LOTS:
                h.reply(409, {"error": f"не больше {MAX_LOTS} лотов сразу"})
                return True
            c.execute("update gear set owner='market', equipped=0 where id=?", (gid,))
            c.execute("insert into market(seller, gear_id, price, created) values(?,?,?,?)", (o, gid, price, now))
            h.reply(200, state(c, o))
            return True
        try:
            lot = int(d.get("lot"))
        except (TypeError, ValueError):
            h.reply(400, {"error": "bad lot"})
            return True
        m = c.execute("select seller, gear_id, price from market where id=? and closed=0", (lot,)).fetchone()
        if not m:
            h.reply(404, {"error": "лот уже продан или снят"})
            return True
        seller, gid, price = m
        if path == "/api/market/cancel":
            if seller != o:
                h.reply(403, {"error": "не твой лот"})
                return True
            c.execute("update gear set owner=?, equipped=0 where id=? and owner='market'", (o, gid))
            c.execute("update market set closed=? where id=?", (now, lot))
            h.reply(200, state(c, o))
        elif path == "/api/market/buy":
            if seller == o:
                h.reply(409, {"error": "это твой лот — сними его"})
                return True
            if c.execute("select count(*) from gear where owner=?", (o,)).fetchone()[0] >= gear.BAG:
                h.reply(409, {"error": f"сумка полна ({gear.BAG})"})
                return True
            # списание атомарно: прошло, только если золота хватило в момент записи
            if c.execute("update wallet set gold=gold-?, spent=spent+? where owner=? and gold>=?", (price, price, o, price)).rowcount != 1:
                h.reply(402, {"error": "не хватает золота"})
                return True
            pay = price - int(price * FEE)
            wallet.ensure(c, seller)
            c.execute("update wallet set gold=gold+?, earned=earned+? where owner=?", (pay, pay, seller))
            c.execute("update gear set owner=?, equipped=0 where id=? and owner='market'", (o, gid))
            c.execute("update market set closed=?, buyer=? where id=?", (now, o, lot))
            c.execute("insert into purchases(owner, item, price, created) values(?,?,?,?)", (o, f"market.{lot}", price, now))
            h.reply(200, state(c, o, bought=lot))
        else:
            h.reply(404, {"error": "not found"})
    return True


def merge(c, src, dst):
    """Лоты анонимного профиля → профиль аккаунта (из account.merge)."""
    init(c)
    c.execute("update market set seller=? where seller=?", (dst, src))
