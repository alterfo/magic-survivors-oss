#!/usr/bin/env python3
"""Локальная раздача Web-сборки для проверки в браузере: python3 tools/devserver.py [порт, по умолчанию 8088].

Отдаёт Builds/Web с теми же заголовками, что nginx на проде (файлы .unityweb уже сжаты gzip).
/api/* отвечает 503 — игра работает как без сети, прод не трогаем. С DEV_API=http://127.0.0.1:8796 запросы /api/*
уходят в локальный server/ideas.py (IDEAS_DB=<тестовая база> IDEAS_PORT=8796) — кошелёк, снаряжение, забеги.
Город и комнаты — свой ретранслятор:
  COOP_PORT=8799 python3 server/coop.py
  http://127.0.0.1:8088/?ws=ws://127.0.0.1:8799/coop
"""
import http.server, os, sys, urllib.request, urllib.error

DEV_API = os.environ.get("DEV_API", "")
ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Builds/Web")


class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=ROOT, **k)

    def do_GET(self):
        if DEV_API and self.path.startswith("/api/"):
            return self.proxy(None)
        if self.path.startswith("/api/world"):
            b = b'{"online":7,"kills":98765,"goal":150000,"open":false,"left":3600}'
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(b)))
            self.end_headers()
            self.wfile.write(b)
            return
        if self.path.startswith("/api/"):
            return self.fail()
        super().do_GET()

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length") or 0))
        if DEV_API and self.path.startswith("/api/"):
            return self.proxy(body)
        self.fail()

    def proxy(self, body):
        hd = {"Content-Type": "application/json"}
        if self.headers.get("X-Ms-Token"):
            hd["X-Ms-Token"] = self.headers["X-Ms-Token"]   # токен входа (account.authed)
        req = urllib.request.Request(DEV_API + self.path, data=body, headers=hd)
        try:
            r = urllib.request.build_opener(urllib.request.ProxyHandler({})).open(req, timeout=10)
            code, data = r.status, r.read()
        except urllib.error.HTTPError as e:
            code, data = e.code, e.read()
        except OSError:
            return self.fail()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def fail(self):
        self.send_response(503)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", "2")
        self.end_headers()
        self.wfile.write(b"{}")

    def guess_type(self, path):
        if path.endswith(".wasm.unityweb"):
            return "application/wasm"
        if path.endswith(".js.unityweb"):
            return "application/javascript"
        return super().guess_type(path)

    def end_headers(self):
        if self.path.split("?")[0].endswith(".unityweb"):
            self.send_header("Content-Encoding", "gzip")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, *a):
        pass


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8088
    print(f"http://127.0.0.1:{port}/  ←  {ROOT}")
    http.server.ThreadingHTTPServer(("127.0.0.1", port), H).serve_forever()
