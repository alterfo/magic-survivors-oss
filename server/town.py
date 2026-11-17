"""Город Magic Survivors: общая площадь, где игроки видят друг друга, пишут в чат и собираются в поход.

Живёт в том же ретрансляторе, что и комнаты (coop.py, тот же сокет /coop). Игроков делит на «слои» по
MAX_SHARD человек: новый попадает в самый заполненный слой, где ещё есть место. Сервер ничего не считает,
только хранит последнюю позицию и раз в TICK секунд рассылает пачкой тех, кто сдвинулся.

Кадры (первый байт — тип, числа little-endian):
  0x0A ник\\tгерой\\tшляпа\\tмантия\\tпосох\\tсвечение\\tверсия\\tмаска уровней\\tигровой токен   войти в город
       → 0x0A [мой id][n] n × запись, затем последние сообщения чата (0x0E)
       запись: [id][x f32][y f32][портал u8][длина u8][описание — та же строка через \\t]
  0x0B запись        остальным: пришёл новый игрок
  0x0C [id]          остальным: игрок ушёл;  от клиента (без id) — выйти из города
  0x0D [x f32][y f32]                         моя позиция (не чаще 12 в секунду)
       → 0x0D n × ([id][x f32][y f32])        пачка сдвинувшихся, раз в TICK
  0x0E текст         сказать в чат  → всем слоем 0x0E [id] ник\\tтекст\\tадмин(0/1)\\tunix-время (история новому — с id 0)

Чат привязан к аккаунту: писать может только вошедший через neuraldeep (игровой токен из server/account.py
проверяется по базе при входе в город), ник в чате и над головой у него — из аккаунта, а не присланный клиентом.
Гость без входа ходит по городу и читает чат; ник, совпадающий с ником чужого аккаунта, заменяется на «Маг».
Админы — id аккаунтов хаба в ADMINS_FILE (по строке); у них метка ADMIN восьмым полем описания и в чате.
Слова «admin»/«админ» из ников вырезаются у всех: метку даёт только сервер.
Токен дальше входа никуда не уходит: в описание игрока для остальных он не попадает.
  0x0F [портал]      встал в круг портала (255 — вышел) → остальным 0x0F [id][портал]
  0x09 — пати и поход (второй байт — действие):
     [10][id]   позвать игрока в пати            → ему 0x09 [1][id зовущего]
     [11][id]   принять приглашение от игрока    → всем в пати 0x09 [3][n] n × id (первый — лидер)
     [12]       выйти из пати                    → вышедшему 0x09 [3][0], остальным новый состав
     [13][портал][сложность]  лидер ведёт пати в поход: coop.py сажает всех в одну комнату (лидер — хозяин)
     [14][0/1]  «ищу пати» на доске таверны   → всем слоем 0x09 [4][id][0/1] (новому — по каждому, кто ищет)
     [20] токен\tтекст  сказать ВСЕМ: в городе (во всех слоях) и в комнатах походов → каждому 0x09 [20] ник\tтекст\tадмин\tвремя
                          (писать — только вошедшему через neuraldeep, ник из аккаунта; не чаще GLOBAL_GAP)
     → 0x09 [21] ...  то же, но из истории (последние GLOBAL_KEEP): при входе в город и в комнату
     → 0x09 [0] текст  не получилось;  → 0x09 [5][1] ты админ (сразу после входа)
     ← 0x09 [22]       одиночный забег подписывается на глобальный чат (история в ответ, дальше — как всем)
     [30]…[36]  осада в городе — см. siege_town.py
     → 0x09 [6] текст  ты вышел из города: этот аккаунт вошёл с другого устройства (сам не возвращайся)
  В пати до PARTY_SIZE игроков из одного слоя. Приглашение живёт INVITE_TTL секунд. Игрок в пати из двух и
  больше не может принять чужое приглашение, пока не выйдет. Ушёл из города — вышел из пати.

Инварианты: игрок состоит не больше чем в одной пати; в пати нет ушедших из города; в комнату похода попадают
только те, кто сам принял приглашение; игрок не бывает в городе и в комнате одновременно.
"""
import hashlib, math, os, re, sqlite3, struct, time

import siege_town
from collections import deque

DB = os.environ.get("IDEAS_DB", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "ideas.db"))
CHAT_ANON = os.environ.get("CHAT_ANON") == "1"  # локальная проверка без базы аккаунтов: чат открыт всем
ADMINS_FILE = os.environ.get("ADMINS_FILE", os.path.join(os.path.dirname(DB), "admins.txt"))
admins = {"at": -1e9, "set": set()}
ADMIN_WORD = re.compile(r"(?iu)\s*\b(admin|админ)\w*")


def is_admin(hub_user):
    now = time.monotonic()
    if now - admins["at"] > 60:
        admins["at"] = now
        try:
            admins["set"] = {l.strip() for l in open(ADMINS_FILE, encoding="utf-8") if l.strip() and not l.startswith("#")}
        except OSError:
            admins["set"] = set()
    return bool(hub_user) and hub_user in admins["set"]
NICKS_TTL = 60
nicks = {"at": -1e9, "set": set()}

MAX_SHARD = 24
HISTORY = 20
TICK = 0.15
CHAT_GAP = 1.2
CHAT_LEN = 160
INVITE_TTL = 40
PARTY_SIZE = 4
LIMIT = 40.0  # дальше от центра площади позиции не принимаем

shards = []
stats = {"enters": 0, "chat": 0, "parties": 0}

# грубый мат прячем звёздочками: чат общий и без модератора
BAD = re.compile(r"(?iu)\b\w*(ху[йеяёию]|пизд|еб[аулиё]|ёб|бля[дт]?|сук[аи]|мудак|мудил|пидор|пидар|гандон|залуп|шлюх|fuck|shit|bitch|nigg)\w*")
LINK = re.compile(r"(?i)(https?://|www\.)\S+")
FIELD = re.compile(r"[^\w \-]", re.U)


class Shard:
    def __init__(self):
        self.members = {}
        self.history = deque(maxlen=HISTORY)

    def cast(self, data, skip=None):
        for o in list(self.members.values()):
            if o is not skip:
                o.send(data)


def entry(p):
    return bytes([p.tid]) + struct.pack("<ff", p.x, p.y) + bytes([p.portal, len(p.info)]) + p.info


def query(sql, args=()):
    """Чтение базы игры (аккаунты ведёт ideas.py). Нет базы или она занята — как будто никто не вошёл."""
    try:
        c = sqlite3.connect(f"file:{DB}?mode=ro", uri=True, timeout=1)
        try:
            return c.execute(sql, args).fetchall()
        finally:
            c.close()
    except sqlite3.Error:
        return []


ACC_NICK = "select a.hub_user, coalesce(p.nick, a.nick) from accounts a left join players p on p.voter=a.voter"


def account(token):
    """Игровой токен → (id аккаунта, ник аккаунта) или None."""
    if len(token) < 20 or len(token) > 200:
        return None
    r = query("select t.hub_user, coalesce(p.nick, a.nick) from game_tokens t join accounts a on a.hub_user=t.hub_user "
              "left join players p on p.voter=a.voter where t.hash=?", (hashlib.sha256(token.encode()).hexdigest(),))
    return r[0] if r else None


def taken(nick):
    """Ник занят чьим-то аккаунтом (список освежается раз в минуту)."""
    now = time.monotonic()
    if now - nicks["at"] > NICKS_TTL:
        nicks["at"] = now
        nicks["set"] = {norm(n) for _, n in query(ACC_NICK)}
    return norm(nick) in nicks["set"]


def norm(nick):
    """Ник для сравнения: без значков, без слов admin/админ, без регистра и лишних пробелов."""
    return " ".join(ADMIN_WORD.sub("", FIELD.sub("", nick or "")).split()).casefold()


def clean_info(text, nick=None, admin=False):
    raw = text.split("\t")
    f = [FIELD.sub("", s).strip()[:24] for s in raw][:6]
    f += [""] * (6 - len(f))
    f[0] = ADMIN_WORD.sub("", FIELD.sub("", nick) if nick else f[0]).strip()[:24]
    if not nick and taken(f[0]):
        f[0] = "Маг"
    # седьмое поле описания — какие уровни куплены (битовая маска): пати может идти в портал, купленный у любого
    mask = raw[7].strip() if len(raw) > 7 and raw[7].strip().isdigit() else "1"
    f.append(mask[:4])
    f.append("1" if admin else "0")
    # десятое поле от клиента (после токена) — надетое оружие «вид.заточка»; в описании оно девятое
    w = raw[9].strip() if len(raw) > 9 else ""
    f.append(w if re.fullmatch(r"\d{1,2}\.\d{1,2}", w) else "")
    if len(f[0]) < 2:
        f[0] = "Маг"
    if BAD.search(f[0]):
        f[0] = "Маг"
    b = "\t".join(f).encode()
    return f[0], b[:250]


def enter(p, text):
    leave(p)
    # седьмое поле — версия протокола клиента (нужна для похода: комнаты понимают только новые клиенты)
    f = text.split("\t")
    p.ver = int(f[6]) if len(f) > 6 and f[6].strip().isdigit() else 1
    acc = account(f[8].strip()) if len(f) > 8 else None
    p.account = acc[0] if acc else None
    p.tok, p.tok_at = (f[8].strip() if len(f) > 8 else ""), time.monotonic()
    p.admin = bool(acc) and is_admin(acc[0])
    # один аккаунт — один маг в городе: вошёл с другого устройства — прежнее подключение выходит из города
    # (иначе игрок видел самого себя, фидбек 16.11). Клиент получает 0x09 [6] и сам не возвращается.
    if p.account:
        for s0 in list(shards):
            for o in list(s0.members.values()):
                if o is not p and o.account == p.account:
                    leave(o)
                    o.send(bytes([0x09, 6]) + "Ты вошёл в город с другого устройства".encode())
    p.lfg = False
    p.siege_alive = True
    p.nick, p.info = clean_info(text, acc[1] if acc else None, p.admin)
    best = None
    for s in shards:
        if len(s.members) < MAX_SHARD and (best is None or len(s.members) > len(best.members)):
            best = s
    if best is None:
        best = Shard()
        shards.append(best)
    tid = next(i for i in range(1, 255) if i not in best.members)
    p.town, p.tid, p.x, p.y, p.portal, p.moved = best, tid, 0.0, 0.0, 255, False
    out = bytearray([0x0A, tid, len(best.members)])
    for o in best.members.values():
        out += entry(o)
    p.send(bytes(out))
    for m in best.history:
        p.send(m)
    for o in best.members.values():
        if o.lfg:
            p.send(bytes([0x09, 4, o.tid, 1]))
    if p.admin:
        p.send(bytes([0x09, 5, 1]))
    global_history(p)
    best.cast(b"\x0B" + entry(p))
    best.members[tid] = p
    stats["enters"] += 1


class Party:
    def __init__(self, leader):
        self.members = [leader]


def fail(p, text):
    p.send(b"\x09\x00" + text.encode())


def roster(party):
    frame = bytes([0x09, 3, len(party.members)] + [m.tid for m in party.members])
    for m in party.members:
        m.send(frame)


def party_invite(p, tid):
    s = getattr(p, "town", None)
    o = s.members.get(tid) if s else None
    if o is None or o is p:
        return fail(p, "Игрок ушёл из города")
    if o.party is not None and len(o.party.members) > 1:
        return fail(p, f"{o.nick} уже в пати")
    if p.party is not None and len(p.party.members) >= PARTY_SIZE:
        return fail(p, "В пати уже четверо")
    if p.party is not None and p.party.members[0] is not p:
        return fail(p, "Звать в пати может только лидер")
    # приглашение — конкретной сессии, а не номеру: номер ушедшего игрока достаётся следующему вошедшему
    now = time.monotonic()
    for q in [q for q, at in p.invites.items() if now - at > INVITE_TTL or q.closed]:
        del p.invites[q]
    p.invites[o] = now
    o.send(bytes([0x09, 1, p.tid]))


def party_accept(p, tid):
    s = getattr(p, "town", None)
    o = s.members.get(tid) if s else None
    if o is None or o is p or time.monotonic() - o.invites.get(p, -1e9) > INVITE_TTL:
        return fail(p, "Приглашение уже не действует")
    if p.party is not None and len(p.party.members) > 1:
        return fail(p, "Сначала выйди из своей пати")
    party = o.party
    if party is None:
        party = o.party = Party(o)
    if party.members[0] is not o or len(party.members) >= PARTY_SIZE:
        return fail(p, "В пати уже нет места")
    o.invites.pop(p, None)
    p.party = party
    party.members.append(p)
    roster(party)
    for m in party.members:
        set_lfg(m, False)


def set_lfg(p, on):
    """«Ищу пати» на доске таверны: в пати из двух и больше — снимается само."""
    s = getattr(p, "town", None)
    if s is None:
        return
    on = bool(on) and not (p.party is not None and len(p.party.members) > 1)
    if on == p.lfg:
        return
    p.lfg = on
    s.cast(bytes([0x09, 4, p.tid, 1 if on else 0]))


def party_leave(p):
    party, p.party = p.party, None
    p.invites.clear()
    if party is None or p not in party.members:
        return
    party.members.remove(p)
    p.send(bytes([0x09, 3, 0]))
    if len(party.members) == 1:
        # пати из одного — не пати
        last = party.members.pop()
        last.party = None
        last.send(bytes([0x09, 3, 0]))
    elif party.members:
        roster(party)


def party_take(p):
    """Лидер ведёт пати в поход: вернуть состав и распустить пати (дальше игроки живут в комнате). None — нельзя."""
    party = p.party
    if party is None or len(party.members) < 2 or party.members[0] is not p:
        return None
    members = [m for m in party.members if not m.closed and m.town is p.town]
    if len(members) < 2:
        return None
    for m in party.members:
        m.party = None
        m.invites.clear()
    party.members = []
    return members


def leave(p):
    s = getattr(p, "town", None)
    if s is None:
        return
    party_leave(p)
    p.town = None
    if s.members.get(p.tid) is p:
        del s.members[p.tid]
        s.cast(bytes([0x0C, p.tid]))
    if not s.members and s in shards:
        if getattr(s, "siege", None):
            siege_town.finish(s, s.siege, "все ушли")   # опустевший слой больше не тикает — итог пишем сейчас (ревью Codex)
        shards.remove(s)


def on_message(p, t, data):
    """Кадры города, кроме похода (0x09 — в coop.py: там комнаты)."""
    if t == 0x0A:
        enter(p, data[1:].decode(errors="ignore"))
        return
    s = getattr(p, "town", None)
    if s is None:
        return
    now = time.monotonic()
    if t == 0x0C:
        leave(p)
    elif t == 0x0D and len(data) >= 9:
        if now - p.pos_at < 0.07:
            return
        p.pos_at = now
        x, y = struct.unpack("<ff", data[1:9])
        if math.isfinite(x) and math.isfinite(y) and abs(x) <= LIMIT and abs(y) <= LIMIT:
            p.x, p.y, p.moved = x, y, True
    elif t == 0x0E:
        # вышел из аккаунта (/api/logout) — писать от его имени больше нельзя: токен перепроверяем раз в минуту (ревью Codex)
        if getattr(p, "tok", "") and now - getattr(p, "tok_at", 0.0) > 60:
            p.tok_at = now
            acc = account(p.tok)
            if acc:
                # сбой базы при прошлой проверке не выкидывает навсегда; личность — целиком из проверенного аккаунта
                p.account, p.admin = acc[0], is_admin(acc[0])
                p.nick = ADMIN_WORD.sub("", FIELD.sub("", acc[1] or "")).strip()[:24] or p.nick
            else:
                p.account, p.admin = None, False
        if p.account is None and not CHAT_ANON:
            return fail(p, "Чтобы писать в чат, войди через neuraldeep")
        if now - p.chat_at < CHAT_GAP:
            return
        text = " ".join(data[1:].decode(errors="ignore").split())
        text = "".join(ch for ch in text if ch.isprintable() and ch != "\t")[:CHAT_LEN]
        if not text:
            return
        p.chat_at = now
        text = LINK.sub("[ссылка]", text)
        text = BAD.sub(lambda m: "*" * len(m.group(0)), text)
        body = f"{p.nick}\t{text}\t{1 if p.admin else 0}\t{int(time.time())}".encode()
        # в историю — без номера: номер в городе к приходу нового игрока может достаться другому
        s.history.append(bytes([0x0E, 0]) + body)
        s.cast(bytes([0x0E, p.tid]) + body)
        stats["chat"] += 1
    elif t == 0x0F and len(data) >= 2:
        p.portal = data[1]
        s.cast(bytes([0x0F, p.tid, p.portal]), skip=p)


# ---------- глобальный чат ----------
# Видят все подключённые к ретранслятору: город и комнаты походов. Список соединений даёт coop.py (town.peers).
peers = set()
GLOBAL_GAP = 3.0
GLOBAL_KEEP = 30
ghistory = deque(maxlen=GLOBAL_KEEP)
glast = {}   # hub_user → когда писал всем: пауза на аккаунт, переподключение её не сбрасывает


def global_say(p, data):
    now = time.monotonic()
    # попытка не чаще раза в секунду на соединение — до любого обращения к базе (и с выдуманным токеном тоже)
    if now - getattr(p, "gchat_try", 0.0) < 1.0:
        return
    p.gchat_try = now
    raw = data[2:].decode(errors="ignore")
    tok, _, text = raw.partition("\t")
    tok = tok.strip()[:200]
    # токен проверяем по базе один раз на соединение, дальше — из памяти соединения
    # токен из памяти соединения, но не дольше 10 с: вышедший (/api/logout) не должен писать дальше — тем более /sys
    if getattr(p, "gtok", None) != tok or now - getattr(p, "gtok_at", 0.0) > 10:
        p.gtok, p.gacc, p.gtok_at = tok, account(tok), now
    acc = p.gacc
    if not acc and not CHAT_ANON:
        return fail(p, "Чтобы писать всем, войди через neuraldeep")
    if acc and text.strip().lower().startswith("/sys"):
        acc = p.gacc = account(tok)   # объявление всем — только по живому токену админа, без кэша (ревью Codex)
        if acc and is_admin(acc[0]):
            return system_post(p, " ".join(text.split()))
        if not acc and not CHAT_ANON:
            return fail(p, "Чтобы писать всем, войди через neuraldeep")
    key = acc[0] if acc else id(p)
    if now - glast.get(key, -1e9) < GLOBAL_GAP:
        return
    if len(glast) > 5000:
        for k in [k for k, v in glast.items() if now - v > GLOBAL_GAP]:
            del glast[k]
    text = " ".join(text.split())
    text = "".join(ch for ch in text if ch.isprintable() and ch != "\t")[:CHAT_LEN]
    if not text:
        return
    glast[key] = now
    text = LINK.sub("[ссылка]", text)
    text = BAD.sub(lambda m: "*" * len(m.group(0)), text)
    nick = ADMIN_WORD.sub("", FIELD.sub("", acc[1] if acc else getattr(p, "nick", "") or "Маг")).strip()[:24] or "Маг"
    admin = bool(acc) and is_admin(acc[0])
    body = f"{nick}\t{text}\t{1 if admin else 0}\t{int(time.time())}".encode()
    ghistory.append(body)
    msg = bytes([0x09, 20]) + body
    for q in list(peers):
        q.send(msg)
    stats["global"] = stats.get("global", 0) + 1


def global_history(p):
    for body in ghistory:
        p.send(bytes([0x09, 21]) + body)
    system_send(p, False)


# ---------- системный канал ----------
# Идея: объявления — отдельной вкладкой «Система» в чате и повтором всем раз в ANNOUNCE_EVERY.
# Источник — таблица news (её же показывает баннер, server/news.py). Админ публикует прямо из чата «Все»:
#   /sys текст      — объявление на 24 часа
#   /sys off        — снять все активные
# → [23] id\tтекст\tвремя — свежее (повтор по таймеру или только что опубликовано), [24] — то же из истории, без счётчика
ANNOUNCE_EVERY = float(os.environ.get("ANNOUNCE_EVERY", 900))
ann = {"at": -1e9, "rows": [], "said": time.monotonic()}


def announcements(force=False):
    now = time.monotonic()
    if force or now - ann["at"] > 30:
        ann["at"] = now
        rows = query("select id, text from news where until=0 or until>? order by id desc limit 3", (int(time.time()),))
        # табуляция — разделитель полей кадра: в тексте её быть не должно (ревью Codex)
        ann["rows"] = [(nid, " ".join(str(text).split())) for nid, text in rows]
    return ann["rows"]


def system_send(q, fresh):
    for nid, text in reversed(announcements()):
        q.send(bytes([0x09, 23 if fresh else 24]) + f"{nid}\t{text}\t{int(time.time())}".encode())


def system_tick():
    now = time.monotonic()
    if now - ann["said"] < ANNOUNCE_EVERY:
        return
    ann["said"] = now
    if announcements():
        for q in list(peers):
            system_send(q, True)


def system_post(p, text):
    """/sys от админа: запись в news (баннер увидят и в меню) и сразу всем в канал."""
    arg = text[4:].strip()
    now = int(time.time())
    try:
        c = sqlite3.connect(DB, timeout=2)
        with c:
            c.execute("create table if not exists news(id integer primary key, text text not null, created integer not null, until integer not null default 0)")
            if arg.lower() == "off":
                c.execute("update news set until=? where until=0 or until>?", (now - 1, now))
            elif arg:
                c.execute("insert into news(text, created, until) values(?,?,?)", (arg[:600], now, now + 86400))
        c.close()
    except sqlite3.Error as e:
        return fail(p, f"Не записалось: {e}")
    rows = announcements(force=True)
    if arg.lower() == "off":
        return fail(p, "Объявления сняты")
    for q in list(peers):
        for nid, t in rows[:1]:
            q.send(bytes([0x09, 23]) + f"{nid}\t{t}\t{now}".encode())


def tick():
    system_tick()
    for s in shards:
        try:
            siege_town.step(s, TICK)   # осада в городе (siege_town.py): орду считает сервер
        except Exception as e:
            err = repr(e)[:200]
            if stats.get("siege_err") != err:   # в журнал — только новую ошибку, а не каждый тик
                print(time.strftime("%H:%M:%S"), "siege error", err, flush=True)
            stats["siege_err"] = err
        out = bytearray([0x0D])
        for o in s.members.values():
            if o.moved:
                o.moved = False
                out += bytes([o.tid]) + struct.pack("<ff", o.x, o.y)
        if len(out) > 1:
            s.cast(bytes(out))


def online():
    return sum(len(s.members) for s in shards)
