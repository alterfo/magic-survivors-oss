#!/usr/bin/env python3
"""Лаборатория баланса: гоняет автопилота по стратегиям в несколько процессов без графики и сводит итог.

  python3 tools/lab.py matrix                 # все пары заклинаний × наборы пассивок × бег/стояние
  python3 tools/lab.py file my.txt            # свой список стратегий (строка = стратегия, см. Assets/Scripts/GameLab.cs)
  python3 tools/lab.py report results.jsonl   # только сводка по готовым результатам
Параметры: --jobs 6 (процессов), --diff 0|1|2, --out каталог (по умолчанию Logs/lab).
Нужна свежая Mac-сборка: Builds/Mac/MagicSurvivors.app.
"""
import argparse, itertools, json, os, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "Builds/Mac/MagicSurvivors.app/Contents/MacOS/Magic Survivors")
SPELLS = ["fireball", "lightning", "ballorbs", "fireaura", "meteor", "icelance", "blizzard", "quake"]
PASSIVES = {
    "none": "",
    "sustain": "vampire,regen,vitality",
    "power": "might,haste,area",
    "crit": "precision,might,haste",
}


def matrix(diff, meta, after, repeat):
    out = []
    for a, b in itertools.combinations(SPELLS, 2):
        for pname, p in PASSIVES.items():
            # kite — бегает весь забег; stand — с уровня after встаёт на месте и больше не двигается
            for move in ("kite", "stand"):
                out.append(f"spells={a},{b};pass={p};move={move};after={after};diff={diff};meta={meta}")
    # точки отсчёта: бот без плана, с выживальными пассивками и стоя
    for p in PASSIVES.values():
        for move in ("kite", "stand"):
            out.append(f"spells=;pass={p};move={move};after={after};diff={diff};meta={meta}")
    return out * repeat


def run(strats, jobs, outdir):
    os.makedirs(outdir, exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    res = os.path.join(outdir, f"results-{stamp}.jsonl")
    procs = []
    for j in range(jobs):
        part = strats[j::jobs]
        if not part:
            continue
        f = os.path.join(outdir, f"strats-{stamp}-{j}.txt")
        open(f, "w").write("\n".join(part) + "\n")
        o = os.path.join(outdir, f"part-{stamp}-{j}.jsonl")
        log = os.path.join(outdir, f"player-{stamp}-{j}.log")
        procs.append((subprocess.Popen([APP, "-batchmode", "-nographics", "-logFile", log, "-labFile", f, "-labOut", o]), o, len(part)))
    t0 = time.time()
    while any(p.poll() is None for p, _, _ in procs):
        done = sum(sum(1 for _ in open(o)) if os.path.exists(o) else 0 for _, o, _ in procs)
        print(f"\r{done}/{len(strats)} забегов, {time.time() - t0:.0f} с", end="", flush=True)
        time.sleep(5)
    print()
    with open(res, "w") as w:
        for _, o, _ in procs:
            if os.path.exists(o):
                w.write(open(o).read())
    return res


def report(path):
    rows = [json.loads(l) for l in open(path) if l.strip()]
    if not rows:
        print("результатов нет"); return
    def key(r):
        d = dict(kv.split("=", 1) for kv in r["strat"].split(";") if "=" in kv)
        return d.get("spells", ""), d.get("pass", ""), d.get("move", "")
    print(f"забегов: {len(rows)}, побед: {sum(r['won'] for r in rows)}")
    # главное: сочетания, где бот встал на месте и всё равно дожил до победы или простоял дольше 3 минут
    afk = [r for r in rows if key(r)[2] != "kite" and r.get("standSecs", 0) > 0]
    if afk:
        bad = sorted([r for r in afk if r["won"] or r["standSecs"] >= 180], key=lambda r: -r["standSecs"])
        print(f"\nСТОЯЛ НА МЕСТЕ: дошли до стояния {len(afk)} забегов, из них простояли 3+ мин или победили: {len(bad)}")
        for r in bad[:40]:
            s, p, m = key(r)
            print(f"  {s:26} {p:24} стоял {r['standSecs'] / 60:4.1f} мин  {'ПОБЕДА' if r['won'] else 'пал   '} ур {r['lvl']:3}  ср.HP {r['avgHpPct']}%  реген {r['healRegen']:.0f}  вамп {r['healDrain']:.0f}  урон {r['dmgTaken']:.0f}")
    print()
    print(f"{'заклинания':28} {'пассивки':24} {'ход':6} {'итог':5} {'мин':>5} {'ур':>3} {'убито':>6} {'ср.HP':>5} {'минHP':>5} {'реген':>6} {'вамп':>6} {'урон':>7}  кто убил")
    for r in sorted(rows, key=lambda r: (-r["won"], -r["secs"], -r["avgHpPct"])):
        s, p, m = key(r)
        print(f"{s:28} {p:24} {m:6} {'ПОБЕДА' if r['won'] else 'пал':5} {r['secs'] / 60:5.1f} {r['lvl']:3} {r['kills']:6} {r['avgHpPct']:4}% {r['minHpPct']:4}% "
              f"{r['healRegen']:6.0f} {r['healDrain']:6.0f} {r['dmgTaken']:7.0f}  {r['killedBy']}")
    # срезы: что даёт стояние на месте и набор пассивок
    for title, idx in (("ход", 2), ("пассивки", 1)):
        print(f"\nпо признаку «{title}»:")
        groups = {}
        for r in rows:
            groups.setdefault(key(r)[idx], []).append(r)
        for g, rs in sorted(groups.items()):
            print(f"  {g or '—':24} забегов {len(rs):4}  побед {100 * sum(r['won'] for r in rs) / len(rs):5.1f}%  ср. время {sum(r['secs'] for r in rs) / len(rs) / 60:5.1f} мин  ср. HP {sum(r['avgHpPct'] for r in rs) / len(rs):3.0f}%")
    print("\nпо заклинанию (доля побед среди забегов, где оно в плане):")
    for sp in SPELLS:
        rs = [r for r in rows if sp in key(r)[0].split(",")]
        if rs:
            print(f"  {sp:12} забегов {len(rs):4}  побед {100 * sum(r['won'] for r in rs) / len(rs):5.1f}%  ср. время {sum(r['secs'] for r in rs) / len(rs) / 60:5.1f} мин")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["matrix", "file", "report"])
    ap.add_argument("path", nargs="?")
    ap.add_argument("--jobs", type=int, default=6)
    ap.add_argument("--diff", type=int, default=0)
    ap.add_argument("--meta", type=float, default=1.0, help="доля постоянной прокачки: 0 — свежий игрок, 1 — всё куплено")
    ap.add_argument("--after", type=int, default=30, help="с какого уровня стратегия stand перестаёт двигаться")
    ap.add_argument("--repeat", type=int, default=1)
    ap.add_argument("--out", default=os.path.join(ROOT, "Logs/lab"))
    a = ap.parse_args()
    if a.mode == "report":
        report(a.path)
    else:
        strats = matrix(a.diff, a.meta, a.after, a.repeat) if a.mode == "matrix" else [l.strip() for l in open(a.path) if l.strip() and not l.startswith("#")]
        print(f"стратегий: {len(strats)}, процессов: {a.jobs}")
        res = run(strats, a.jobs, a.out)
        print("результаты:", res)
        report(res)
