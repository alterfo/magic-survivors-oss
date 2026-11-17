"""Генерация музыки через OpenRouter Lyria 3. python3 lyria.py <model> <out.mp3> "<prompt>" """
import base64, json, os, sys, urllib.request

key = os.environ["OPENROUTER_API_KEY"]   # ключ OpenRouter из окружения
model, out, prompt = sys.argv[1], sys.argv[2], sys.argv[3]
body = {"model": model, "modalities": ["text", "audio"], "stream": True,
        "messages": [{"role": "user", "content": prompt}]}
req = urllib.request.Request("https://openrouter.ai/api/v1/chat/completions", data=json.dumps(body).encode(),
                             headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
audio, text, fmt = [], [], None
with urllib.request.urlopen(req, timeout=600) as r:
    for line in r:
        line = line.decode().strip()
        if not line.startswith("data:") or line == "data: [DONE]":
            continue
        ev = json.loads(line[5:])
        if "error" in ev:
            print("error:", ev["error"]); sys.exit(1)
        for ch in ev.get("choices", []):
            d = ch.get("delta", {})
            if d.get("content"): text.append(d["content"])
            a = d.get("audio") or {}
            if a.get("data"): audio.append(a["data"])
            if a.get("format"): fmt = a["format"]
        if ev.get("usage"): print("usage:", ev["usage"])
raw = base64.b64decode("".join(audio)) if audio else b""
open(out, "wb").write(raw)
print("format:", fmt, "bytes:", len(raw), "text:", "".join(text)[:300])
