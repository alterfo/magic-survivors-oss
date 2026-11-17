"""Отбор звуков Kenney (CC0) в Assets/Resources/Sfx: пик каждого файла выравнивается до -1 dBFS,
относительная громкость задаётся таблицей микса в GameSfx.cs. Плюс демо всего набора подряд.
python3 tools/sfx_import.py <папка с распакованными паками kenney_*> [demo.mp3]"""
import glob, os, re, subprocess, sys

SRC = sys.argv[1]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Sfx")
PICK = {  # id в игре -> шаблон файлов (все совпадения — варианты, игра берёт случайный)
    "kill": "impactWood_light_00*",
    "gem": "glass_00*",
    "hurt": "impactPunch_medium_00*",
    "cast": "forceField_00*",
    "boom": "explosionCrunch_00*",
    "bossIn": "impactBell_heavy_00*",
    "bossDead": "lowFrequency_explosion_00*",
    "levelUp": "confirmation_001",
    "super": "jingles_PIZZI03",
    "chest": "jingles_PIZZI01",
    "dead": "jingles_PIZZI12",
    "won": "jingles_PIZZI14",
    "pick": "select_00[12]",
    "open": "open_001",
    "close": "close_001",
}


def peak(f):
    err = subprocess.run(["ffmpeg", "-hide_banner", "-i", f, "-af", "volumedetect", "-f", "null", "-"],
                         capture_output=True, text=True).stderr
    m = re.search(r"max_volume: (-?[\d.]+) dB", err)
    return float(m.group(1)) if m else 0.0


os.makedirs(OUT, exist_ok=True)
demo = []
for sid, pat in PICK.items():
    files = sorted(glob.glob(os.path.join(SRC, "**", pat + ".ogg"), recursive=True))[:5]
    if not files:
        print("нет файлов:", sid, pat); continue
    for i, f in enumerate(files):
        dst = os.path.join(OUT, f"{sid}_{i}.wav")
        gain = -1.0 - peak(f)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", f, "-af", f"volume={gain:.2f}dB",
                        "-c:a", "pcm_s16le", dst], check=True)
        if i == 0: demo.append(dst)
    print(f"{sid:9} {len(files)} шт  ← {os.path.basename(files[0])}")

if len(sys.argv) > 2:
    # демо: каждый звук по разу с паузой 0.6 с
    args, parts = [], []
    for k, f in enumerate(demo):
        args += ["-i", f]
        parts.append(f"[{k}:a]aresample=44100,apad=pad_dur=0.6[a{k}]")
    filt = ";".join(parts) + ";" + "".join(f"[a{k}]" for k in range(len(demo))) + f"concat=n={len(demo)}:v=0:a=1[o]"
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", *args, "-filter_complex", filt, "-map", "[o]", sys.argv[2]], check=True)
    print("демо:", sys.argv[2], "порядок:", ", ".join(PICK))
