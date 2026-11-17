"""Осада в самом городе: в 18:00 МСК стена падает, орда идёт на площадь, все маги в городе
бьются вместе базовой атакой, без прокачки. Таверна — место отступления: туда мобы не заходят, внутри лечит.

Орду считает сервер (по слою города — у каждого Shard своя), клиенты только рисуют и сообщают попадания.
Клиенту не доверяем: попадание засчитывается не чаще HIT_GAP, не сильнее HIT_MAX и только по мобу рядом с магом.

Кадры 0x09 (второй байт — действие), числа little-endian:
  → [30][идёт u8][осталось секунд u16][убито u16][n u8] n × (id u16, вид u8, x i16, y i16 — ×50, здоровье % u8)
       снимок орды, раз в SNAP_EVERY тиков города; идёт=0 — осады нет (один раз после конца)
  → [31][урон u8]                    тебя ударили
  → [32][id мага u8][id моба u16]    маг попал по мобу (остальным — нарисовать заряд)
  → [33][x i16][y i16]               бомбер взорвался
  → [37]                              ты пал (сервер считает здоровье: HP_MAX, удары, лечение в таверне)
  → [34] текст                        таблица защитников: ник\\tубито\\n…, раз в 2 с и в конце
  ← [35][id моба u16][урон u16]      я попал
  ← [36][жив u8]                      я пал (0) / снова в строю (1)
Виды: 0 скелет, 1 разбойник, 2 воин, 3 бомбер, 4 громила со щитом, 5 маг-призыватель (как SiegeKinds в GameSiegeTown.cs).
"""
import math, os, random, struct, time

import siege

FORCE = os.environ.get("SIEGE_TEST") == "1"   # локальная проверка: осада идёт всегда
SNAP_EVERY = 2          # тиков города (TICK 0.15 с) между снимками — ~3,3 в секунду, клиент сглаживает
HIT_GAP, HIT_MAX, HIT_RANGE = 0.4, 60, 11.0   # клиент бьёт раз в 0,45 с; присланный урон — не больше HIT_MAX
HP_MAX, HP_REGEN, DEAD_SECS = 300, 0.2, 4.0    # здоровье мага в осаде считает сервер (клиент только показывает)
SPAWN_R, TOWN_R = 24.0, 19.5
# предел толпы: было 30 + 14 на мага до 160 — на телефоне
# столько скелетов в городе не тянется и в куче не видно, кто бьёт. Призванные тоже в пределе
CAP_BASE, CAP_PER, CAP_MAX = 24, 8, 80
TAVERN = (-18.5, -16.1, 13.0, 11.2)   # Town.TavernHouse: x, y, ширина, высота

# здоровье, скорость, урон касанием, вес в смеси, радиус удара
KINDS = [
    (40, 1.7, 5, 58, 0.9),     # скелет
    (24, 3.0, 4, 16, 0.9),     # разбойник
    (95, 1.5, 9, 12, 1.0),     # воин
    (30, 2.5, 26, 7, 1.2),     # бомбер — взрывается рядом с магом
    (650, 1.0, 18, 3, 1.5),    # громила со щитом
    (70, 1.4, 4, 4, 0.9),      # маг-призыватель — держится поодаль и поднимает скелетов
]
WEIGHTS = [k[3] for k in KINDS]


def in_tavern(x, y, pad=0.0):
    tx, ty, tw, th = TAVERN
    return tx - pad <= x <= tx + tw + pad and ty - pad <= y <= ty + th + pad


class Siege:
    def __init__(self):
        self.mobs = {}          # id → [вид, x, y, здоровье, откат удара, откат призыва]
        self.next_id = 0
        self.killed = 0
        self.ticks = 0
        self.score = {}         # ник → убито
        self.score_at = 0.0
        self.was = False
        self.seen = set()       # ники всех магов, что были в городе во время осады — для строки в журнале
        self.started = 0.0


def log(text):
    # одна строка в journalctl magicsurvival-coop: по ней видно, играют ли в осаду (решение 17.11)
    print(time.strftime("%H:%M:%S"), "siege", text, flush=True)


def finish(s, sg, why):
    """Итог осады в журнал — по концу окна или когда опустевший слой убирают (иначе его step больше не позовут)."""
    if not sg.was:
        return
    sg.was = False
    top = ", ".join(f"{n}:{k}" for n, k in sorted(sg.score.items(), key=lambda kv: -kv[1])[:5])
    log(f"{why}: слой {id(s) % 10000}, магов {len(sg.seen)}, убито {sg.killed}, "
        f"{int(time.monotonic() - sg.started)} с, лучшие {top or '—'}")
    sg.seen = set()


def active():
    return FORCE or siege.open_now()


def left():
    if FORCE:
        return 900
    now = time.time()
    day = (int(now) + siege.MSK) // 86400 * 86400 - siege.MSK
    return max(0, int(day + siege.HOUR_MSK * 3600 + siege.OPEN_MIN * 60 - now))


def spawn(sg, kind=None, at=None):
    sg.next_id = (sg.next_id + 1) & 0xFFFF
    k = random.choices(range(len(KINDS)), WEIGHTS)[0] if kind is None else kind
    if at is None:
        while True:
            a = random.uniform(0, 2 * math.pi)
            x, y = math.cos(a) * SPAWN_R, math.sin(a) * SPAWN_R
            if not in_tavern(x, y, 2.0):
                break
    else:
        x, y = at
    sg.mobs[sg.next_id] = [k, x, y, float(KINDS[k][0]), 0.0, random.uniform(3, 6)]


HITS_PER_SEC = 3   # толпа не убивает за секунду: по одному магу не больше стольких ударов в секунду


def alive(o, now=None):
    return time.monotonic() >= getattr(o, "siege_dead", 0.0) if now is None else now >= getattr(o, "siege_dead", 0.0)


def hurt(o, dmg):
    now = time.monotonic()
    if not alive(o, now):
        return False
    if now - getattr(o, "siege_win", 0.0) >= 1.0:
        o.siege_win, o.siege_n = now, 0
    if o.siege_n >= HITS_PER_SEC:
        return False
    o.siege_n += 1
    o.send(bytes([0x09, 31, min(255, dmg)]))
    # смерть решает сервер: клиент, который не слушает удары, всё равно падает и 4 секунды не бьёт
    o.siege_hp = getattr(o, "siege_hp", HP_MAX) - dmg
    if o.siege_hp <= 0:
        o.siege_dead = now + DEAD_SECS
        o.siege_hp = HP_MAX
        o.send(bytes([0x09, 37]))   # ты пал — клиент падает и через DEAD_SECS встаёт в таверне
    return True


def fighters(s):
    """Маги, за которыми идёт орда: в городе, живые, не в таверне и не в портале."""
    now = time.monotonic()
    out = []
    for o in s.members.values():
        if in_tavern(o.x, o.y):
            o.siege_hp = min(HP_MAX, getattr(o, "siege_hp", HP_MAX) + HP_MAX * HP_REGEN * 0.15)   # лечит таверна (тик 0,15 с)
        elif alive(o, now) and getattr(o, "siege_alive", True) and o.portal == 255:
            out.append(o)
    return out


def step(s, dt):
    """Один тик осады слоя s (зовётся из town.tick)."""
    sg = getattr(s, "siege", None)
    if sg is None:
        sg = s.siege = Siege()
    if not active():
        if sg.was:
            finish(s, sg, "конец")
            sg.mobs.clear()
            s.cast(bytes([0x09, 30, 0]) + struct.pack("<HH", 0, sg.killed) + bytes([0]))
            board(s, sg, final=True)
            sg.killed, sg.score = 0, {}
        return
    if not s.members:
        return
    if not sg.was:
        sg.started = time.monotonic()
        log(f"начало: слой {id(s) % 10000}, в городе {len(s.members)}")
    sg.was = True
    for o in s.members.values():
        sg.seen.add(getattr(o, "nick", "?"))
    sg.ticks += 1
    targets = fighters(s)
    cap = min(CAP_MAX, CAP_BASE + CAP_PER * len(s.members))
    for _ in range(3):   # не всех сразу: волна накатывает
        if len(sg.mobs) < cap:
            spawn(sg)
    if len(sg.mobs) > cap and sg.ticks % 3 == 0:
        # маги ушли — потолок упал, а орда осталась: двое бились бы с толпой на восьмерых (проверка 17.11).
        # Лишние уходят по одному, самые дальние от площади — на глазах никто не пропадает
        far = max(sg.mobs, key=lambda i: sg.mobs[i][1] ** 2 + sg.mobs[i][2] ** 2)
        sg.mobs.pop(far)
    booms = []
    for mid, m in list(sg.mobs.items()):
        k, x, y, hp, cd, sc = m
        hp0, speed, dmg, _, reach = KINDS[k]
        m[4] = max(0.0, cd - dt)
        # ближайший маг
        best, bd = None, 1e9
        for o in targets:
            d = (o.x - x) ** 2 + (o.y - y) ** 2
            if d < bd:
                best, bd = o, d
        if best is None:
            # некого бить — стоят у пролома и ждут
            r = math.hypot(x, y)
            if r > TOWN_R + 1:
                x -= x / r * speed * dt
                y -= y / r * speed * dt
        else:
            d = math.sqrt(bd)
            want = 6.0 if k == 5 else reach * 0.8
            if d > want:
                x += (best.x - x) / d * speed * dt
                y += (best.y - y) / d * speed * dt
            if k == 5:
                m[5] = sc - dt
                if m[5] <= 0 and len(sg.mobs) < cap:
                    m[5] = 7.0
                    for _ in range(min(2, cap - len(sg.mobs))):   # не выше потолка (ревью Codex)
                        spawn(sg, 0, (x + random.uniform(-1.5, 1.5), y + random.uniform(-1.5, 1.5)))
            if d <= reach and m[4] <= 0:
                if k == 3:
                    booms.append((mid, x, y))
                    continue
                m[4] = 1.0 if hurt(best, dmg) else 0.3
        # в таверну не заходят: выталкиваем за её стены
        if in_tavern(x, y, 0.6):
            tx, ty, tw, th = TAVERN
            cx, cy = tx + tw / 2, ty + th / 2
            dx, dy = x - cx, y - cy
            if abs(dx) / tw > abs(dy) / th:
                x = cx + math.copysign(tw / 2 + 0.7, dx)
            else:
                y = cy + math.copysign(th / 2 + 0.7, dy)
        m[1], m[2] = x, y
    for mid, x, y in booms:
        sg.mobs.pop(mid, None)
        for o in targets:
            if (o.x - x) ** 2 + (o.y - y) ** 2 < 2.4 ** 2:
                hurt(o, KINDS[3][2])
        s.cast(bytes([0x09, 33]) + struct.pack("<hh", q(x), q(y)))
    if sg.ticks % SNAP_EVERY == 0:
        snap(s, sg)
    now = time.monotonic()
    if now - sg.score_at > 2:
        sg.score_at = now
        board(s, sg)


def q(v):
    return max(-32000, min(32000, int(round(v * 50))))


def snap(s, sg):
    mobs = list(sg.mobs.items())[:255]
    out = bytearray([0x09, 30, 1]) + struct.pack("<HH", min(65535, left()), min(65535, sg.killed)) + bytes([len(mobs)])
    for mid, (k, x, y, hp, _, _) in mobs:
        out += struct.pack("<HBhhB", mid, k, q(x), q(y), max(1, min(100, int(hp / KINDS[k][0] * 100))))
    s.cast(bytes(out))


def board(s, sg, final=False):
    top = sorted(sg.score.items(), key=lambda kv: -kv[1])[:5]
    text = ("ИТОГ\n" if final else "") + "\n".join(f"{n}\t{k}" for n, k in top)
    s.cast(bytes([0x09, 34]) + text.encode())


def on_hit(p, data):
    """← [35][id моба u16][урон u16] — маг попал."""
    s = getattr(p, "town", None)
    sg = getattr(s, "siege", None) if s else None
    if sg is None or not sg.was or len(data) < 6:
        return
    now = time.monotonic()
    # павший, сидящий в таверне или в портале не бьёт — даже если клиент шлёт попадания
    if now - getattr(p, "siege_hit", 0.0) < HIT_GAP or not alive(p, now) or not getattr(p, "siege_alive", True) or in_tavern(p.x, p.y) or p.portal != 255:
        return
    p.siege_hit = now
    mid, dmg = struct.unpack("<HH", data[2:6])
    m = sg.mobs.get(mid)
    if m is None or (m[1] - p.x) ** 2 + (m[2] - p.y) ** 2 > HIT_RANGE ** 2:
        return
    m[3] -= min(dmg, HIT_MAX)
    s.cast(bytes([0x09, 32, p.tid]) + struct.pack("<H", mid), skip=p)
    if m[3] <= 0:
        sg.mobs.pop(mid, None)
        sg.killed += 1
        sg.score[p.nick] = sg.score.get(p.nick, 0) + 1


def on_alive(p, data):
    """← [36][жив u8]."""
    if len(data) >= 3:
        p.siege_alive = data[2] == 1
        if data[2] == 0:
            p.siege_dead = max(getattr(p, "siege_dead", 0.0), time.monotonic() + DEAD_SECS)
            p.siege_hp = HP_MAX   # встанет с полным, как и клиент
