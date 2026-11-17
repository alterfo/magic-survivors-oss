"""Кошелёк Magic Survivors: золото и покупки живут на сервере, а не в PlayerPrefs. Подключается из ideas.py.

POST /api/wallet {voter, gold, meta:{id:lvl}} — состояние кошелька; при первом обращении переносит локальное золото
                                               и прокачку с устройства (один раз, с потолком)
POST /api/buy    {voter, item}                 — купить: level.crypt, hero.necro, cos.<id>, meta.<id> (следующий уровень), feather (+1 перо)
POST /api/feather {voter, rid}                — перо сгорело в забеге rid: −1 из запаса, повтор rid не списывает
Золото начисляется в /api/run (credit) по серверной формуле с потолком — клиентской сумме не верим.
Цены — только здесь: клиент показывает свои, но списывает сервер.
"""
import threading, time

import presence  # скидка торговцев от убитых за день — server/presence.py

# предметы разовой покупки; бесплатные (кладбище, маг, стандартная одежда) не продаются — они есть у всех
PRICES = {
    "level.crypt": 1500, "level.swamp": 3000, "level.frost": 5000, "level.abyss": 8000,
    "hero.necro": 2500, "hero.frost": 4000,
}
# постоянная прокачка: цена уровня lvl = cost × lvl × (lvl + 3) / 4, как MetaCost в GameMeta.cs
# (с 2026-11-13; раньше росла линейно — первый уровень стоит столько же, последний втрое дороже)
META = {"dmg": (40, 10), "hp": (30, 10), "regen": (50, 5), "cd": (60, 5), "area": (50, 5), "speed": (40, 5),
        "magnet": (30, 5), "xp": (50, 5), "crit": (50, 5), "gold": (60, 5), "revive": (600, 1)}

# перо феникса — расходник (с 2026-11-15): прежнее «навсегда» (meta.revive) у купивших остаётся, новым не продаётся
FEATHER_PRICE, FEATHER_MAX = 150, 9
# напитки бармена (с 2026-11-16): выпиваются при старте следующего забега, бонус на один забег (GameTavern.cs)
DRINKS = {"drink.grog": 120, "drink.mead": 100, "drink.ale": 80}
DRINK_MAX = 3
CONSUMABLE_MAX = {"feather": FEATHER_MAX, **{k: DRINK_MAX for k in DRINKS}}


def meta_price(k, lvl):
    return META[k][0] * lvl * (lvl + 3) // 4


MIGRATE_CAP = 5000  # больше локального золота при переносе не верим: столько набегается часов за 4
lock = threading.Lock()


def init(c):
    c.executescript("""
    create table if not exists wallet(owner text primary key, gold integer not null, earned integer not null default 0,
        spent integer not null default 0, created integer not null);
    create table if not exists owned(owner text not null, item text not null, level integer not null default 1,
        created integer not null, primary key(owner, item));
    create table if not exists purchases(id integer primary key, owner text not null, item text not null,
        price integer not null, created integer not null);
    """)


def owner(c, voter):
    """Чей кошелёк: устройство, а после входа через хаб — общий профиль аккаунта (таблица accounts из account.py)."""
    try:
        row = c.execute("""select a2.voter from accounts a1 join accounts a2 on a2.hub_user=a1.hub_user
                           where a1.voter=? order by a2.created, a2.voter limit 1""", (voter,)).fetchone()
        if row:
            return row[0]
    except Exception:  # аккаунтов ещё нет — кошелёк по устройству
        pass
    return voter


def rev(c, o):
    """Ревизия кошелька: заработано + потрачено. Растёт при каждом изменении золота — клиент по ней отбрасывает
    устаревшие ответы (заточка и покупка могут вернуться в любом порядке)."""
    w = c.execute("select earned + spent from wallet where owner=?", (o,)).fetchone()
    return w[0] if w else 0


def state(c, o):
    w = c.execute("select gold from wallet where owner=?", (o,)).fetchone()
    items = {r[0]: r[1] for r in c.execute("select item, level from owned where owner=?", (o,))}
    # crev — ревизия расходников: каждая покупка и каждое списание пера/напитка — новая строка purchases,
    # так что её rowid только растёт; по ней клиент отбрасывает запоздавшие снимки запаса (ревью Codex 16.11)
    cr = c.execute("select max(rowid) from purchases where owner=?", (o,)).fetchone()
    import prefs   # наряд и настройки владельца (server/prefs.py) — приходят вместе с кошельком
    return {"ok": True, "gold": w[0] if w else 0, "wrev": rev(c, o), "crev": cr[0] or 0, "items": [{"id": k, "level": v} for k, v in items.items()],
            "prefs": prefs.get(c, o)}


def ensure(c, o, gold=0, meta=None):
    if c.execute("select 1 from wallet where owner=?", (o,)).fetchone():
        return False
    now = int(time.time())
    c.execute("insert into wallet(owner, gold, created) values(?,?,?)", (o, max(0, min(int(gold), MIGRATE_CAP)), now))
    for k, lvl in (meta or {}).items():
        if k in META:
            lvl = max(0, min(int(lvl), META[k][1]))
            if lvl:
                c.execute("insert or ignore into owned(owner, item, level, created) values(?,?,?,?)", (o, "meta." + k, lvl, now))
    return True


def run_gold(d, secs, level, kills, won):
    """Потолок золота за забег: формула клиента (GameMeta.RunGold) с максимальными множителями."""
    kills = min(kills, secs * 30)  # сильная сборка на кошмаре выкашивает 10–15 в секунду; 4/с урезало золото у половины забегов
    level = min(level, 200, 5 + secs // 4)   # 35-й уровень берут к 6-й минуте; без привязки уровень давал золото за «пустой» забег
    base = kills * 0.08 + secs / 8 + level * 2
    cap = base * (1.5 if won else 1) * 2.2 * 3  # сложность ×2.2, жадность (мета, бесконечные карты, свойство «золото» у снаряжения) до ×3
    try:
        asked = int(d.get("gold", 0))
    except (TypeError, ValueError):
        asked = 0
    return max(0, min(asked, int(cap)))


def credit(c, voter, d, secs, level, kills, won):
    """Начислить золото за забег (вызывается из /api/run после записи забега). Возвращает новый баланс."""
    o = owner(c, voter)
    ensure(c, o)
    g = run_gold(d, secs, level, kills, won)
    c.execute("update wallet set gold=gold+?, earned=earned+? where owner=?", (g, g, o))
    return c.execute("select gold from wallet where owner=?", (o,)).fetchone()[0]


def grant_gold(c, voter, n):
    """Начислить n золота вне забега (награды из fallen.py). Возвращает новый баланс."""
    o = owner(c, voter)
    ensure(c, o)
    c.execute("update wallet set gold=gold+?, earned=earned+? where owner=?", (n, n, o))
    return c.execute("select gold from wallet where owner=?", (o,)).fetchone()[0]


def grant_item(c, voter, item):
    """Выдать вещь бесплатно (трофеи из fallen.py). True — выдана сейчас, False — уже была."""
    o = owner(c, voter)
    ensure(c, o)
    return c.execute("insert or ignore into owned(owner, item, level, created) values(?,?,1,?)",
                     (o, item, int(time.time()))).rowcount == 1


def post(h, path, d, voter, db, authed=lambda c: True):
    """Обработать POST, если путь наш. True — ответ отправлен."""
    if path not in ("/api/wallet", "/api/buy", "/api/feather"):
        return False
    if len(voter) < 8:
        h.reply(400, {"error": "bad voter"})
        return True
    with lock, db() as c:
        if not c.execute("select 1 from players where voter=?", (voter,)).fetchone():
            h.reply(400, {"error": "unknown player"})
            return True
        o = owner(c, voter)
        if path == "/api/wallet":
            # золото и прокачку с клиента больше не принимаем: перенос локального прогресса закончился с серверным кошельком
            # (14.10), а новый voter с gold=5000, влитый при входе в свой аккаунт, печатал золото без конца (ревью Codex 17.11)
            ensure(c, o)
            # баланс, вещи и настройки — только владельцу: по одному чужому voter раньше читали чужой кошелёк (ревью Codex 17.11)
            if not authed(c):
                h.reply(401, {"error": "Войди через neuraldeep"})
                return True
            h.reply(200, state(c, o))
            return True
        ensure(c, o)
        # тратить золото и перья может только владелец (токен входа, когда вход обязателен): иначе по чужому voter
        # покупали бы за чужие деньги и сжигали чужой запас
        if not authed(c):
            h.reply(401, {"error": "Войди через neuraldeep"})
            return True
        if path == "/api/feather":
            # перо (или напиток) потрачено в забеге rid: списываем одно; повтор того же rid второй раз не списывает
            rid = str(d.get("rid", ""))[:40]
            what = str(d.get("item") or "feather")
            if len(rid) < 6 or what not in CONSUMABLE_MAX:
                h.reply(400, {"error": "bad rid"})
                return True
            key = what + ".use." + rid
            if not c.execute("select 1 from purchases where owner=? and item=?", (o, key)).fetchone():
                if c.execute("update owned set level=level-1 where owner=? and item=? and level>0", (o, what)).rowcount == 1:
                    c.execute("insert into purchases(owner, item, price, created) values(?,?,0,?)", (o, key, int(time.time())))
            h.reply(200, state(c, o))
            return True
        item = str(d.get("item", ""))[:64]
        lvl = 1
        if item in CONSUMABLE_MAX:
            # расходники поштучно: перо спасает в одном забеге, напиток действует один забег; запас ограничен
            row = c.execute("select level from owned where owner=? and item=?", (o, item)).fetchone()
            lvl = (row[0] if row else 0) + 1
            if lvl > CONSUMABLE_MAX[item]:
                h.reply(409, {"error": f"в запасе уже {CONSUMABLE_MAX[item]}"})
                return True
            price = presence.discounted(FEATHER_PRICE if item == "feather" else DRINKS[item])
        elif item == "meta.revive" and not c.execute("select 1 from owned where owner=? and item=?", (o, item)).fetchone():
            h.reply(409, {"error": "перо теперь продаётся поштучно"})
            return True
        elif item.startswith("meta."):
            k = item[5:]
            if k not in META:
                h.reply(404, {"error": "нет такого"})
                return True
            row = c.execute("select level from owned where owner=? and item=?", (o, item)).fetchone()
            lvl = (row[0] if row else 0) + 1
            if lvl > META[k][1]:
                h.reply(409, {"error": "уже максимум"})
                return True
            price = presence.discounted(meta_price(k, lvl))
        elif item in PRICES or (item.startswith("cos.") and item in COSMETICS):
            if c.execute("select 1 from owned where owner=? and item=?", (o, item)).fetchone():
                h.reply(200, state(c, o))  # уже куплено — повтор запроса не списывает
                return True
            price = presence.discounted(PRICES.get(item) or COSMETICS[item])
        else:
            h.reply(404, {"error": "нет такого"})
            return True
        # клиент присылает цену, которую показал: скидка могла смениться (полночь, новая ступень) — не списываем другую
        if isinstance(d.get("price"), int) and d["price"] > 0 and d["price"] != price:
            h.reply(409, {"error": f"Цена изменилась: теперь {price} — проверь и купи ещё раз", "price": price})
            return True
        # списание атомарно: прошло, только если золота хватило в момент записи
        if c.execute("update wallet set gold=gold-?, spent=spent+? where owner=? and gold>=?", (price, price, o, price)).rowcount != 1:
            h.reply(402, {"error": "не хватает золота"})
            return True
        now = int(time.time())
        c.execute("""insert into owned(owner, item, level, created) values(?,?,?,?)
                     on conflict(owner, item) do update set level=excluded.level""", (o, item, lvl, now))
        c.execute("insert into purchases(owner, item, price, created) values(?,?,?,?)", (o, item, price, now))
        h.reply(200, state(c, o))
    return True


# косметика: id из Assets/Scripts/Cosmetics.cs с префиксом cos.; бесплатные сюда не пишем
# трофеи за павших магов (cos.glow_soul, cos.robe_ghost, cos.hat_fallen) тоже не здесь: их выдаёт fallen.py, купить нельзя
COSMETICS = {"cos.hat_lich": 500, "cos.hat_bear": 600, "cos.hat_knight": 700, "cos.hat_crown": 800, "cos.robe_crimson": 400, "cos.robe_emerald": 500, "cos.robe_night": 600, "cos.robe_gold": 800, "cos.staff_wand": 800, "cos.staff_lich": 1000, "cos.staff_crystal": 1000, "cos.glow_fire": 600, "cos.glow_ice": 600, "cos.glow_poison": 600, "cos.glow_dark": 600, "cos.glow_gold": 800}
