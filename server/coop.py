#!/usr/bin/env python3
"""Комнаты кооператива Magic Survivors: WebSocket-ретранслятор, в комнате до ROOM_SIZE игроков. Только stdlib (asyncio).

Клиент ходит на wss://magicsurvival.neuraldeep.ru/coop (nginx → 127.0.0.1:8798). Игру считает один из игроков,
сервер только сводит их в комнату и пересылает кадры. Все кадры бинарные, первый байт — тип.

Служебные (< 0x10):
  0x01 [код]|ключ|версия  создать комнату; код — 4 цифры по желанию, иначе случайный  → 0x03 код|слот
  0x02 код|ключ|версия    войти в комнату                                    → 0x04 код|слот или 0x05 текст ошибки
  У каждого в комнате свой слот 0..ROOM_SIZE-1 (создатель — 0). Ключ — случайная строка устройства: с тем же
  ключом игрок с нового соединения возвращается на свой слот (вкладка перезагрузилась, телефон уснул).
  Версия — номер протокола клиента (PROTO): старым клиентам отвечаем 0x05 «обнови страницу».
  0x06 [слот]  в комнате есть игрок в этом слоте (всем, кто уже в комнате, и вошедшему — про каждого)
  0x07 [слот]  игрок вышел. Комната живёт, пока в ней кто-то есть; кто теперь хозяин, клиенты решают сами
               одинаково: младший занятый слот.
  0x08         keepalive, сервер не отвечает
Игровые (≥ 0x10) уходят всем остальным в комнате; вторым байтом сервер вставляет слот отправителя —
их разбирает Assets/Scripts/GameCoop.cs.
Город (0x09–0x0F: общая площадь, чат, сбор в поход) — в town.py; поход сводит двоих из города в обычную комнату.
"""
import asyncio, base64, hashlib, os, random, struct, time
import town

PORT = int(os.environ.get("COOP_PORT", "8798"))
MAX_MSG = 1 << 18
MAX_ROOMS = 2000
ROOM_SIZE = 4
PROTO = 5  # 5 — режим «Выживание» в байте сложности; версия протокола клиента: 2 — слоты и до четырёх игроков, 3 — враги в снимке дельтой, 4 — особые враги и стрелы
# снимки мира у хозяина ~4 КБ × 15 в секунду: если гость столько не принимает, его буфер растёт — рвём связь
MAX_BUFFER = 4 << 20
GUID = b"258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

rooms = {}  # код → список из ROOM_SIZE слотов (Peer или None)
stats = {"rooms": 0, "joins": 0}


class Peer:
    def __init__(self, writer, ip):
        self.w = writer
        self.ip = ip
        self.room = None
        self.key = ""
        self.ver = 1
        self.closed = False
        # город (town.py)
        self.town = None
        self.tid = 0
        self.nick = ""
        self.info = b""
        self.x = self.y = 0.0
        self.portal = 255
        self.moved = False
        self.pos_at = self.chat_at = 0.0
        self.account = None  # id аккаунта neuraldeep, если вошёл (town.enter сверяет токен с базой)
        self.admin = False
        self.lfg = False     # «ищу пати» на доске таверны
        self.party = None    # пати в городе (town.Party)
        self.invites = {}    # кого позвал в пати: сессия (Peer) → когда

    def send(self, data, op=2):
        if self.closed:
            return
        n = len(data)
        if n < 126:
            head = bytes([0x80 | op, n])
        elif n < 65536:
            head = bytes([0x80 | op, 126]) + struct.pack(">H", n)
        else:
            head = bytes([0x80 | op, 127]) + struct.pack(">Q", n)
        try:
            self.w.write(head + data)
            if self.w.transport.get_write_buffer_size() > MAX_BUFFER:
                self.close()
        except Exception:
            self.close()

    def close(self):
        if self.closed:
            return
        self.closed = True
        try:
            self.w.close()
        except Exception:
            pass


def members(code):
    r = rooms.get(code)
    return [q for q in r if q is not None] if r else []


def leave(p):
    """Игрок вышел из комнаты: остальным 0x07 [слот]. Комната живёт, пока в ней кто-то есть —
    уход создателя её не закрывает (старший из оставшихся становится хозяином у клиентов сам)."""
    code, p.room = p.room, None
    r = rooms.get(code)
    if not r or p not in r:
        return
    slot = r.index(p)
    r[slot] = None
    rest = members(code)
    if not rest:
        del rooms[code]
        log(f"room {code} closed")
        return
    for q in rest:
        q.send(bytes([0x07, slot]))
    log(f"room {code}: slot {slot} left, {len(rest)} remain")


def split_key(s):
    """код|ключ|версия → (код, ключ, версия клиента)"""
    f = s.split("|")
    code = f[0].strip()
    key = f[1].strip()[:64] if len(f) > 1 else ""
    ver = int(f[2]) if len(f) > 2 and f[2].strip().isdigit() else 1
    return code, key, ver


OLD = "Вышла новая версия — обнови страницу"


def seat(p, code, slot, created=False):
    """Посадить игрока в слот комнаты и рассказать всем, кто есть кто. 0x03 — комнату создал он, 0x04 — вошёл."""
    r = rooms[code]
    r[slot] = p
    p.room = code
    p.send((b"\x03" if created else b"\x04") + f"{code}|{slot}".encode())
    for i, q in enumerate(r):
        if q is None or q is p:
            continue
        p.send(bytes([0x06, i]))
        q.send(bytes([0x06, slot]))
    town.global_history(p)   # последние сообщения глобального чата — и в походе


def retake(p, code):
    """Игрок вернулся с нового соединения (вкладка перезагрузилась, телефон уснул): занимает свой прежний слот,
    старое соединение закрывается. True — вернули."""
    r = rooms.get(code)
    if not r or not p.key:
        return False
    for i, q in enumerate(r):
        if q is not None and q is not p and q.key == p.key:
            q.room = None
            q.close()
            r[i] = None
            leave(p)
            town.leave(p)
            seat(p, code, i, created=i == 0)
            log(f"room {code}: slot {i} retaken by {p.ip}")
            return True
    return False


def create(p, want):
    want, p.key, ver = split_key(want)
    p.ver = ver
    if ver < PROTO:
        p.send(b"\x05" + OLD.encode())
        return
    if retake(p, want):
        return
    leave(p)
    town.leave(p)
    if len(rooms) >= MAX_ROOMS:
        p.send(b"\x05" + "Сервер переполнен, попробуй позже".encode())
        return
    code = want if len(want) == 4 and want.isdigit() and want not in rooms else None
    while code is None:
        c = f"{random.randint(1000, 9999)}"
        if c not in rooms:
            code = c
    rooms[code] = [None] * ROOM_SIZE
    stats["rooms"] += 1
    seat(p, code, 0, created=True)
    log(f"room {code} created by {p.ip}")


def join(p, code):
    code, p.key, ver = split_key(code)
    p.ver = ver
    if ver < PROTO:
        p.send(b"\x05" + OLD.encode())
        return
    r = rooms.get(code)
    if not r:
        p.send(b"\x05" + "Комната не найдена — проверь код".encode())
        return
    if p in r:
        p.send(b"\x05" + "Ты уже в этой комнате".encode())
        return
    if retake(p, code):
        return
    if None not in r:
        p.send(b"\x05" + f"В комнате уже {ROOM_SIZE} игрока".encode())
        return
    leave(p)
    town.leave(p)
    stats["joins"] += 1
    seat(p, code, r.index(None))
    log(f"room {code} joined by {p.ip}")


def party(p, data):
    """Пати в городе и поход: действие — второй байт (см. town.py). Поход: лидер и все, кто принял приглашение,
    садятся в одну комнату, лидер — хозяин. Дальше всё как в обычной комнате. Действие 20 — глобальный чат."""
    if len(data) >= 2 and data[1] == 20:
        return town.global_say(p, data)   # и из города, и из комнаты похода
    if len(data) >= 2 and data[1] == 35:
        return town.siege_town.on_hit(p, data)
    if len(data) >= 2 and data[1] == 36:
        return town.siege_town.on_alive(p, data)
    if len(data) >= 2 and data[1] == 22:
        # одиночный забег слушает глобальный чат: соединение уже в town.peers, отдаём историю один раз
        if not getattr(p, "gsub", False):
            p.gsub = True
            town.global_history(p)
        return
    k = data[1] if len(data) > 1 else 0
    if k == 10 and len(data) > 2:
        town.party_invite(p, data[2])
    elif k == 11 and len(data) > 2:
        town.party_accept(p, data[2])
    elif k == 12:
        town.party_leave(p)
    elif k == 14 and len(data) > 2:
        town.set_lfg(p, data[2])
    elif k == 13:
        if len(rooms) >= MAX_ROOMS:
            return town.fail(p, "Сервер переполнен, попробуй позже")
        if p.party is not None and any(m.ver < PROTO for m in p.party.members):
            return town.fail(p, OLD)
        members = town.party_take(p)
        if members is None:
            return town.fail(p, "Поход начинает лидер пати")
        for q in members:
            leave(q)
            town.leave(q)
        code = None
        while code is None:
            c = f"{random.randint(1000, 9999)}"
            if c not in rooms:
                code = c
        rooms[code] = [None] * ROOM_SIZE
        stats["rooms"] += 1
        stats["joins"] += len(members) - 1
        town.stats["parties"] += 1
        for i, q in enumerate(members[:ROOM_SIZE]):
            seat(q, code, i, created=i == 0)
        log(f"party {code}: {len(members)} mages, leader {p.ip}")


def on_message(p, data):
    if not data:
        return
    t = data[0]
    if t >= 0x10:
        # игровой кадр — всем остальным в комнате, вторым байтом вставляем слот отправителя
        r = rooms.get(p.room)
        if r and p in r:
            out = data[:1] + bytes([r.index(p)]) + data[1:]
            for q in r:
                if q is not None and q is not p:
                    q.send(out)
    elif t == 0x09:
        party(p, data)
    elif 0x0A <= t <= 0x0F:
        town.on_message(p, t, data)
    elif t == 0x08:
        # пинг клиента раз в 10 с: отвечаем, иначе тихое соединение (стоит в городе, все молчат) nginx рвёт через 120 с
        p.send(b"\x08")
    elif t == 0x01:
        create(p, data[1:].decode(errors="ignore"))
    elif t == 0x02:
        join(p, data[1:].decode(errors="ignore").strip())


async def handshake(reader, writer):
    head = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), 10)
    lines = head.decode(errors="ignore").split("\r\n")
    hdr = {}
    for l in lines[1:]:
        k, _, v = l.partition(":")
        hdr[k.strip().lower()] = v.strip()
    key = hdr.get("sec-websocket-key")
    if not lines[0].startswith("GET ") or "websocket" not in hdr.get("upgrade", "").lower() or not key:
        writer.write(b"HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n")
        return None
    accept = base64.b64encode(hashlib.sha1(key.encode() + GUID).digest()).decode()
    writer.write(("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                  f"Sec-WebSocket-Accept: {accept}\r\n\r\n").encode())
    return hdr.get("x-real-ip") or (writer.get_extra_info("peername") or ("?",))[0]


async def frames(reader):
    """Кадры клиента: (опкод, данные) с уже собранными фрагментами."""
    buf, op0 = b"", 0
    while True:
        h = await reader.readexactly(2)
        fin, op, n = h[0] & 0x80, h[0] & 0x0F, h[1] & 0x7F
        if n == 126:
            n = struct.unpack(">H", await reader.readexactly(2))[0]
        elif n == 127:
            n = struct.unpack(">Q", await reader.readexactly(8))[0]
        if n > MAX_MSG:
            raise ValueError("too big")
        mask = await reader.readexactly(4) if h[1] & 0x80 else None
        data = await reader.readexactly(n)
        if mask and n:
            m = (mask * (n // 4 + 1))[:n]
            data = (int.from_bytes(data, "big") ^ int.from_bytes(m, "big")).to_bytes(n, "big")
        if op >= 8:
            yield op, data
            continue
        if op:
            op0, buf = op, data
        else:
            buf += data
        if len(buf) > MAX_MSG:
            raise ValueError("too big")
        if fin:
            yield op0, buf
            buf = b""


async def client(reader, writer):
    p = None
    try:
        ip = await handshake(reader, writer)
        if ip is None:
            return
        p = Peer(writer, ip)
        town.peers.add(p)   # глобальный чат шлёт всем соединениям
        async for op, data in frames(reader):
            if op == 8:
                p.send(data[:2], op=8)
                break
            if op == 9:
                p.send(data, op=10)
            elif op == 2:
                on_message(p, data)
    except (asyncio.IncompleteReadError, ConnectionError, asyncio.TimeoutError, asyncio.LimitOverrunError, ValueError):
        pass
    finally:
        if p:
            town.peers.discard(p)
            leave(p)
            town.leave(p)
            p.close()
        else:
            writer.close()


def log(s):
    print(time.strftime("%H:%M:%S"), s, f"(rooms {len(rooms)}, town {town.online()})", flush=True)


async def town_loop():
    n = 0
    while True:
        await asyncio.sleep(town.TICK)
        try:
            town.tick()
        except Exception as e:  # рассылка позиций не должна ронять комнаты
            log(f"town tick: {e!r}")
        n += 1
        if n % 4000 == 0 and town.online():
            log(f"town: {town.stats}")


async def main():
    srv = await asyncio.start_server(client, os.environ.get("COOP_HOST", "127.0.0.1"), PORT)
    log(f"coop relay on :{PORT}")
    asyncio.get_running_loop().create_task(town_loop())
    async with srv:
        await srv.serve_forever()


if __name__ == "__main__":
    asyncio.run(main())
