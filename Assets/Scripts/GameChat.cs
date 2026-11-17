using System.Collections.Generic;
using UnityEngine;

// Глобальный чат (server/town.py global_say): пишут все, кто подключён к серверу кооператива — город и походы.
// В городе — вкладка «Все» рядом с «Город», в походе — те же сообщения в ленте с меткой и переключатель «кому».
// Чат города сворачивается в строку с числом новых.
public partial class Game
{
    static readonly List<string> globalChat = new();
    static readonly HashSet<string> globalSeen = new();   // история приходит при каждом входе — не дублируем
    static readonly Queue<string> globalSeenOrder = new(); // набор помнит только последние 120 сообщений
    static int chatTab;                 // 0 — город, 1 — все, 2 — система (объявления)
    static int unreadTown, unreadAll, unreadSys;   // новые с прошлого взгляда на вкладку
    static readonly List<(int id, string line)> systemChat = new();   // вкладка «Система»: id объявления и строка
    static bool chatCollapsed;
    static bool runToAll;               // в походе: писать всем, а не только своим
    static readonly List<(string nick, string text)> globalRecent = new();   // последние сообщения — в ленту нового забега

    void GlobalChatIn(byte[] m)
    {
        if (m.Length < 3) return;
        var raw = System.Text.Encoding.UTF8.GetString(m, 2, m.Length - 2);
        if (!globalSeen.Add(raw)) return;
        globalSeenOrder.Enqueue(raw);
        while (globalSeenOrder.Count > 120) globalSeen.Remove(globalSeenOrder.Dequeue());
        var f = raw.Split('\t');
        if (f.Length < 2) return;
        string nick = f[0].Replace('<', '‹').Replace('>', '›'), text = f[1].Replace('<', '‹').Replace('>', '›');
        bool adm = f.Length > 2 && f[2] == "1";
        string at = f.Length > 3 && long.TryParse(f[3], out var ts) ? ChatTime(ts) + "  " : "";
        string nc = adm ? "FF8A6B" : nick == Nick ? "9FE8A8" : "8FD3FF";
        globalChat.Add($"<color=#8A857D>{at}</color><color=#{nc}>{nick}{(adm ? " · ADMIN" : "")}</color>  {text}");
        if (globalChat.Count > 40) globalChat.RemoveAt(0);
        globalRecent.Add((nick, text));
        if (globalRecent.Count > 3) globalRecent.RemoveAt(0);
        bool fresh = m[1] == 20;   // 21 — из истории: без счётчика
        // в забеге история тоже видна (последние строки): иначе до первого нового сообщения чата «нет» (фидбек 16.11)
        if (!fresh) { if (state != State.Town && state != State.Menu) RunSay("[все] " + nick, text, nick == Nick); return; }
        if (chatTab != 1 || chatCollapsed) unreadAll++;
        if (state != State.Town && state != State.Menu) RunSay("[все] " + nick, text, nick == Nick);
    }

    // Системный канал (server/town.py system_*): объявления из news — во вкладке «Система»; повтор раз в 15 минут
    // ещё и строкой в «Город»/«Все» и в ленте похода, чтобы объявление увидели, не открывая вкладку
    static readonly Color SysColor = new(1f, 0.78f, 0.3f);

    void SystemChatIn(byte[] m)
    {
        if (m.Length < 3) return;
        var f = System.Text.Encoding.UTF8.GetString(m, 2, m.Length - 2).Split('\t');
        if (f.Length < 2 || !int.TryParse(f[0], out var id)) return;
        string text = f[1].Replace('<', '‹').Replace('>', '›');
        string at = f.Length > 2 && long.TryParse(f[2], out var ts) ? ChatTime(ts) + "  " : "";
        string line = $"<color=#8A857D>{at}</color><color=#FFC64D>Система</color>  <color=#FFE2A0>{text}</color>";
        // во вкладке объявление одно на id; вытесненное вернётся, если сервер пришлёт его снова (ревью Codex)
        if (!systemChat.Exists(x => x.id == id))
        {
            systemChat.Add((id, line));
            if (systemChat.Count > 20) systemChat.RemoveAt(0);
            if (chatTab != 2 || chatCollapsed) unreadSys++;
        }
        if (m[1] != 23) return;   // 24 — из истории при входе: только во вкладку
        Town.chat.Add(line);
        if (Town.chat.Count > 40) Town.chat.RemoveRange(0, Town.chat.Count - 40);   // повторы копятся и в походе (ревью Codex)
        globalChat.Add(line);
        if (globalChat.Count > 40) globalChat.RemoveAt(0);
        if (state != State.Town && state != State.Menu) RunSay("Система", text, false);
    }

    void GlobalSay(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 160) text = text.Substring(0, 160);
        if (Coop.sock == null || !Coop.sock.Open) { Town.status = "Нет связи с сервером"; return; }
        var b = System.Text.Encoding.UTF8.GetBytes(AccToken + "\t" + text);
        var op = new byte[b.Length + 2];
        op[0] = 0x09; op[1] = 20;
        b.CopyTo(op, 2);
        Coop.Send(op);
    }

    // строка свёрнутого чата и вкладки над развёрнутым; возвращает true, если чат свёрнут (рисовать его не надо)
    bool DrawChatHead(Rect box, bool port)
    {
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        if (chatCollapsed)
        {
            int n = unreadTown + unreadAll + unreadSys;
            var pill = new Rect(16, box.yMax - 50, Mathf.Min(320, box.width), 50);
            UiBlock(pill);
            string t = n > 0 ? $"Чат · {n} новых" : "Чат";
            if (Btn(pill, t, n > 0 ? Gold : Bone)) { chatCollapsed = false; PlayerPrefs.SetInt("chat.min", 0); }
            return true;
        }
        float th = 40, ty = box.y - th - 6, tw = Mathf.Min(130, (box.width - 44 - 3 * 8) / 3);
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(16 + i * (tw + 8), ty, tw, th);
            int un = i == 0 ? unreadTown : i == 1 ? unreadAll : unreadSys;
            string label = (i == 0 ? "ГОРОД" : i == 1 ? "ВСЕ" : "СИСТЕМА") + (un > 0 && chatTab != i ? $" · {un}" : "");
            Chip(r, label, i == 1 ? new Color(0.56f, 0.83f, 1f) : i == 2 ? SysColor : Gold, chatTab == i);
            UiBlock(r);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) chatTab = i;
        }
        var mr = new Rect(box.xMax - 44, ty, 44, th);
        UiBlock(mr);
        if (Btn(mr, "—", Bone)) { chatCollapsed = true; PlayerPrefs.SetInt("chat.min", 1); }
        if (chatTab == 0) unreadTown = 0; else if (chatTab == 1) unreadAll = 0; else unreadSys = 0;
        return false;
    }

    // Одиночный забег: в походе без комнаты сокета нет, и глобальный чат не доходил.
    // Подключаемся к ретранслятору «слушателем» (0x09 [22]) — комнаты не занимаем, при обрыве тихо пробуем снова.
    float listenAt;

    void SoloChatTick()
    {
        // вернулись в меню — слушатель не нужен (город закрывает сокет сам в TownStart)
        if (Coop.listening && state == State.Menu) { Coop.Close(); return; }
        if (coopRun || bot || LabOn || Coop.InRoom || state == State.Town || state == State.Menu) return;
        if (Coop.sock != null || Time.unscaledTime < listenAt) return;
        listenAt = Time.unscaledTime + 15;
        // история уже пришла в городе (и отсеется как повтор) — в ленту нового забега кладём её сами (ревью Codex)
        if (runChat.Count == 0) foreach (var (n, t) in globalRecent) RunSay("[все] " + n, t, n == Nick);
        Coop.ListenGlobal();
    }

    // сокет любой — слушатель или пустая комната, в которой начали забег без друга (ревью Codex)
    bool SoloChat => !coopRun && Coop.sock != null && Coop.sock.Open && state != State.Town && state != State.Menu;
}
