"""Снаряжение Magic Survivors (как в L2): семь слотов, грейды от обычного до S, заточка до +16, у оружия 20 видов. Подключается из ideas.py.

Вещи выпадают только по итогам забега (/api/run → roll): что и какого грейда — решает сервер по длине забега, победе,
уровню и сложности; клиент лишь сообщает, где играл. Владелец — тот же, что у кошелька (общий профиль аккаунта).

POST /api/gear          {voter}           — список вещей
POST /api/gear/equip    {voter, id}       — надеть (вещь того же слота снимается)
POST /api/gear/unequip  {voter, id}       — снять
POST /api/gear/enchant  {voter, id}       — заточить за золото: до +3 без риска, дальше шанс падает, неудача сбрасывает на +3
POST /api/gear/salvage  {voter, id}       — разобрать в золото (надетую нельзя)

Статы считает клиент (Assets/Scripts/GameGear.cs) по тем же таблицам — их держать одинаковыми.
"""
import random, time

import presence  # скидка дня у кузнеца
import siege
import wallet

SLOTS = ("weapon", "helm", "robe", "gloves", "boots", "ring", "amulet")
GRADES = 6  # 0 обычный, 1 D, 2 C, 3 B, 4 A, 5 S
AFFIXES = ("dmg", "hp", "regen", "crit", "area", "magnet", "xp", "gold", "speed", "cd")
MAX_ENCHANT = 16   # с 2026-11-15 (было +10): свечение оружия по заточке, самое яркое — на +16
WEAPON_KINDS = 20  # вид оружия (посох, жезл, сфера, гримуар, клинок…) — таблица WeaponKinds в GameGear.cs
SAFE_ENCHANT = 3
ENCHANT_PRICE = (20, 40, 80, 150, 300, 600)   # × (текущая заточка + 1)
SALVAGE = (5, 15, 40, 100, 250, 600)            # × (1 + заточка / 2)
LEVEL_TIER = {"graveyard": 0, "crypt": 1, "swamp": 2, "frost": 2.5, "abyss": 3, "siege": 2}   # осада — только в своём окне (siege.py)
LEVEL_ITEM = {"crypt": "level.crypt", "swamp": "level.swamp", "frost": "level.frost", "abyss": "level.abyss"}
BAG = 60          # больше вещей не держим: лишняя добыча сразу разбирается в золото
DAY_DROPS = 24    # потолок добычи в сутки на владельца — от накрутки поддельными забегами
# тот же замок, что у кошелька: заточка и покупка не должны читать баланс одновременно
lock = wallet.lock


GEAR_SQL = """create table if not exists gear(id integer primary key autoincrement, owner text not null, slot text not null,
    grade integer not null, affix text not null default '', enchant integer not null default 0, equipped integer not null default 0,
    created integer not null)"""


def init(c):
    # id без переиспользования (autoincrement): разобранная вещь не отдаёт свой номер новой — иначе запрос
    # из старой вкладки разобрал бы чужую. Таблица без autoincrement пересоздаётся с переносом строк — одной
    # транзакцией (savepoint): упало посередине — всё откатывается, старая таблица остаётся как была.
    old = c.execute("select sql from sqlite_master where type='table' and name='gear'").fetchone()
    if old and "autoincrement" not in old[0].lower():
        c.execute("savepoint gear_mig")
        try:
            c.execute("alter table gear rename to gear_old")
            c.execute("drop index if exists gear_owner")
            c.execute(GEAR_SQL)
            c.execute("insert into gear select * from gear_old")
            c.execute("drop table gear_old")
            c.execute("release gear_mig")
        except Exception:
            c.execute("rollback to gear_mig")
            c.execute("release gear_mig")
            raise
    c.execute(GEAR_SQL)
    # вид оружия: у прежних вещей 0 — классический посох
    if "kind" not in [r[1] for r in c.execute("pragma table_info(gear)")]:
        c.execute("alter table gear add column kind integer not null default 0")
    c.execute("create index if not exists gear_owner on gear(owner)")
    # журнал выдач: суточный потолок считается по нему (разбор вещи не возвращает лимит)
    c.execute("create table if not exists gear_drops(owner text not null, created integer not null)")
    c.execute("create index if not exists gear_drops_owner on gear_drops(owner, created)")


def items(c, o):
    rows = c.execute("select id, slot, grade, affix, enchant, equipped, kind from gear where owner=? order by equipped desc, grade desc, enchant desc, id desc", (o,))
    return [{"id": r[0], "slot": r[1], "grade": r[2], "affix": r[3], "enchant": r[4], "equipped": r[5], "kind": r[6]} for r in rows]


def state(c, o, **extra):
    w = c.execute("select gold from wallet where owner=?", (o,)).fetchone()
    return {"ok": True, "gold": w[0] if w else 0, "wrev": wallet.rev(c, o), "items": items(c, o), **extra}


def grade_for(tier, diff, won, rnd):
    """Грейд добычи: уровень (кладбище 0, склеп 1, топи 2) + сложность (0–2) + полбалла за победу, с разбросом."""
    s = tier + diff + (0.5 if won else 0)
    g = int(s * 0.8 + rnd.uniform(-0.5, 1.2))
    g = max(0, min(4, g))
    if s >= 4 and rnd.random() < 0.03:
        g = 5  # S — только с топей на кошмаре и редко
    return g


def merge(c, src, dst):
    """Вещи анонимного профиля → профиль аккаунта (из account.merge). Надетое снимается: слоты могли быть заняты."""
    c.execute("update gear set owner=?, equipped=0 where owner=?", (dst, src))
    c.execute("update gear_drops set owner=? where owner=?", (dst, src))


def roll(c, voter, d, secs, won):
    """Добыча за забег. Зовётся из /api/run (под wallet.lock) после записи забега; возвращает выданные вещи и золото за разобранные."""
    o = wallet.owner(c, voter)
    wallet.ensure(c, o)
    rnd = random.Random()
    n = 0
    if secs >= 120 and rnd.random() < min(0.85, secs / 700):
        n += 1
    if won:
        n += 1
    if not n:
        return [], 0
    day = c.execute("select count(*) from gear_drops where owner=? and created>?", (o, int(time.time()) - 86400)).fetchone()[0]
    n = max(0, min(n, DAY_DROPS - day))
    level = str(d.get("map", ""))
    tier = LEVEL_TIER.get(level, 0)
    # уровень засчитываем, только если он куплен (иначе грейд как с кладбища)
    if level in LEVEL_ITEM and not c.execute("select 1 from owned where owner=? and item=?", (o, LEVEL_ITEM[level])).fetchone():
        tier = 0
    if level == "siege" and not siege.allowed(c, d.get("rid")):
        tier = 0
    try:
        diff = max(0, min(2, int(d.get("diff", 0))))
    except (TypeError, ValueError):
        diff = 0
    now, out, sold = int(time.time()), [], 0
    for _ in range(n):
        c.execute("insert into gear_drops(owner, created) values(?,?)", (o, now))
        g = grade_for(tier, diff, won, rnd)
        slot = rnd.choice(SLOTS)
        affix = rnd.choice(AFFIXES) if g > 0 else ""
        kind = rnd.randrange(WEAPON_KINDS) if slot == "weapon" else 0
        if c.execute("select count(*) from gear where owner=?", (o,)).fetchone()[0] >= BAG:
            sold += SALVAGE[g]
            out.append({"id": 0, "slot": slot, "grade": g, "affix": affix, "enchant": 0, "equipped": 0, "kind": kind})
            continue
        cur = c.execute("insert into gear(owner, slot, grade, affix, kind, created) values(?,?,?,?,?,?)", (o, slot, g, affix, kind, now))
        out.append({"id": cur.lastrowid, "slot": slot, "grade": g, "affix": affix, "enchant": 0, "equipped": 0, "kind": kind})
    if sold:
        c.execute("update wallet set gold=gold+?, earned=earned+? where owner=?", (sold, sold, o))
    return out, sold


def enchant_chance(e):
    """Шанс заточки с +e на +e+1: до безопасного уровня — всегда, дальше 75% и минус 7% за ступень."""
    return 1.0 if e < SAFE_ENCHANT else max(0.25, 0.75 - 0.07 * (e - SAFE_ENCHANT))


def post(h, path, d, voter, db, authed=lambda c: True):
    if not path.startswith("/api/gear"):
        return False
    if len(voter) < 8:
        h.reply(400, {"error": "bad voter"})
        return True
    with lock, db() as c:
        if not c.execute("select 1 from players where voter=?", (voter,)).fetchone():
            h.reply(400, {"error": "unknown player"})
            return True
        # кошелёк здесь не создаём: первый /api/wallet переносит локальное золото, только если кошелька ещё нет
        o = wallet.owner(c, voter)
        if path == "/api/gear":
            if not authed(c):   # сумка и надетое — только владельцу (ревью Codex 17.11)
                h.reply(401, {"error": "Войди через neuraldeep"})
                return True
            h.reply(200, state(c, o))
            return True
        # надеть, снять, заточить, разобрать — только владелец (токен входа, когда вход обязателен)
        if not authed(c):
            h.reply(401, {"error": "Войди через neuraldeep"})
            return True
        try:
            gid = int(d.get("id"))
        except (TypeError, ValueError):
            h.reply(400, {"error": "bad id"})
            return True
        row = c.execute("select slot, grade, enchant, equipped from gear where id=? and owner=?", (gid, o)).fetchone()
        if not row:
            h.reply(404, {"error": "нет такой вещи"})
            return True
        slot, grade, ench, eq = row
        if path == "/api/gear/equip":
            c.execute("update gear set equipped=0 where owner=? and slot=?", (o, slot))
            c.execute("update gear set equipped=1 where id=?", (gid,))
            h.reply(200, state(c, o))
        elif path == "/api/gear/unequip":
            c.execute("update gear set equipped=0 where id=?", (gid,))
            h.reply(200, state(c, o))
        elif path == "/api/gear/enchant":
            if ench >= MAX_ENCHANT:
                h.reply(409, {"error": "уже максимум"})
                return True
            price = presence.discounted(ENCHANT_PRICE[grade] * (ench + 1))   # кузнец — тоже торговец: скидка дня
            if isinstance(d.get("price"), int) and d["price"] >= 0 and d["price"] != price:
                h.reply(409, {"error": f"Цена изменилась: теперь {price} — заточи ещё раз", "price": price})
                return True
            # списание атомарно: прошло, только если золота хватило в момент записи
            if c.execute("update wallet set gold=gold-?, spent=spent+? where owner=? and gold>=?", (price, price, o, price)).rowcount != 1:
                h.reply(402, {"error": "не хватает золота"})
                return True
            ok = random.random() < enchant_chance(ench)
            new = ench + 1 if ok else SAFE_ENCHANT
            c.execute("update gear set enchant=? where id=?", (new, gid))
            c.execute("insert into purchases(owner, item, price, created) values(?,?,?,?)",
                      (o, f"enchant.{slot}.{grade}.{ench}.{'ok' if ok else 'fail'}", price, int(time.time())))
            h.reply(200, state(c, o, enchanted=ok, enchant=new))
        elif path == "/api/gear/salvage":
            if eq:
                h.reply(409, {"error": "сначала сними"})
                return True
            g = int(SALVAGE[grade] * (1 + ench / 2))
            c.execute("delete from gear where id=?", (gid,))
            wallet.ensure(c, o)
            c.execute("update wallet set gold=gold+?, earned=earned+? where owner=?", (g, g, o))
            h.reply(200, state(c, o, salvaged=g))
        else:
            h.reply(404, {"error": "not found"})
    return True
