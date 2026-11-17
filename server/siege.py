"""Осада города — раз в день в 18:00 по Москве (клиент: Assets/Scripts/GameSiege.cs).

Портал у южных ворот открыт OPEN_MIN минут. Бой — обычный забег на уровне «siege»; добыча за него как с топей,
но только если забег начался в окне осады: иначе это был бы ещё один бесплатный уровень с хорошим лутом.
Начало берём не из присланного результата (его можно отправить позже, а пауза растягивает время), а из серверного
времени первого среза телеметрии этого забега — срез уходит на первой минуте игры (ревью Codex 16.11).
"""
HOUR_MSK, OPEN_MIN = 18, 15
MSK = 3 * 3600
SLACK = 120    # часы клиента: пару минут до открытия прощаем
FIRST = 600    # первый срез — на первой игровой минуте; пауза и выбор карт растягивают её, прощаем до 10 минут


def in_window(ts, early=SLACK, late=0):
    """ts попадает в окно сегодняшней (по Москве) осады."""
    day = (int(ts) + MSK) // 86400 * 86400 - MSK      # полночь по Москве в unix-времени
    opens = day + HOUR_MSK * 3600
    return opens - early <= ts <= opens + OPEN_MIN * 60 + late


def allowed(c, rid):
    """Забег rid начался в окне осады — по серверному времени его первого среза телеметрии.
    rid должен быть строкой и уже записан в run_ids тем же игроком, что слал телеметрию: иначе числовой rid проходил
    мимо защиты от повтора и добычу осады можно было брать снова (ревью Codex 16.11)."""
    if not isinstance(rid, str) or not rid:
        return False
    row = c.execute("""select t.created from telemetry t join run_ids r on r.rid = t.run and r.voter = t.voter
                       where t.run=?""", (rid,)).fetchone()
    return bool(row) and in_window(row[0], late=FIRST)


def open_now(now=None):
    """Окно осады идёт прямо сейчас — строго 18:00–18:15 МСК, без допуска (осада в городе, siege_town.py)."""
    import time
    return in_window(time.time() if now is None else now, early=0)
