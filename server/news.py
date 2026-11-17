"""Объявление всем игрокам: баннер в меню и в городе (GET /api/news, клиент — GameNews.cs).

Запуск на сервере (база та же, что у ideas.py):
  sudo -u www-data python3 server/news.py "Текст объявления" [часов]   — опубликовать (без часов — пока не снимут)
  sudo -u www-data python3 server/news.py --list                       — активные
  sudo -u www-data python3 server/news.py --off <id>                   — снять
"""
import os, sqlite3, sys, time

DB = os.environ.get("IDEAS_DB", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "ideas.db"))


def main(a):
    c = sqlite3.connect(DB, timeout=30)
    c.execute("create table if not exists news(id integer primary key, text text not null, created integer not null, until integer not null default 0)")
    now = int(time.time())
    if not a or a[0] == "--list":
        for r in c.execute("select id, text, until from news where until=0 or until>? order by id", (now,)):
            print(r[0], r[1], "до " + time.strftime("%d.%m %H:%M", time.localtime(r[2])) if r[2] else "")
        return
    if a[0] == "--off":
        with c:
            c.execute("update news set until=? where id=?", (now - 1, int(a[1])))
        print("снято", a[1])
        return
    text = a[0].strip()[:600]
    until = now + int(float(a[1]) * 3600) if len(a) > 1 else 0
    with c:
        cur = c.execute("insert into news(text, created, until) values(?,?,?)", (text, now, until))
    print("опубликовано", cur.lastrowid)


if __name__ == "__main__":
    main(sys.argv[1:])
