using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

// Рекорды (PlayerPrefs — в браузере это IndexedDB, переживает перезагрузку) и идеи игроков:
// «Предложить» → server/ideas.py, идея сразу в голосовании; «Голосование» — список с голосами.
public partial class Game
{
    // ---------- Рекорды ----------

    void SaveRecords(bool won)
    {
        if (bot) return;
        PlayerPrefs.SetInt("rec.runs", PlayerPrefs.GetInt("rec.runs") + 1);
        if (won) PlayerPrefs.SetInt("rec.wins", PlayerPrefs.GetInt("rec.wins") + 1);
        PlayerPrefs.SetFloat("rec.time", Mathf.Max(PlayerPrefs.GetFloat("rec.time"), time));
        PlayerPrefs.SetInt("rec.level", Mathf.Max(PlayerPrefs.GetInt("rec.level"), level));
        PlayerPrefs.SetInt("rec.kills", Mathf.Max(PlayerPrefs.GetInt("rec.kills"), kills));
        PlayerPrefs.SetInt("rec.total", PlayerPrefs.GetInt("rec.total") + kills);
        PlayerPrefs.Save();
    }

    string RecordsLine()
    {
        int runs = PlayerPrefs.GetInt("rec.runs");
        if (runs == 0) return null;
        float t = PlayerPrefs.GetFloat("rec.time");
        return $"Рекорд {(int)t / 60:00}:{(int)t % 60:00}  ·  ур. {PlayerPrefs.GetInt("rec.level")}  ·  забегов {runs}  ·  побед {PlayerPrefs.GetInt("rec.wins")}";
    }

    // ---------- Идеи и голосование ----------

    [System.Serializable] class IdeaRow { public int id; public string text; public int votes; public int mine; }
    [System.Serializable] class IdeaList { public IdeaRow[] ideas; }
    [System.Serializable] class ApiReply { public bool ok; public string error; }

    int menuTab;              // 0 — выбор уровня, 2 — идеи и голосование, 3 — прокачка, 4 — лидеры, 5 — магазин
    string ideaText = "", ideaStatus;
    bool ideaBusy;
    IdeaRow[] ideas;
    float ideasScroll;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern string BrowserPrompt(string title);
    [DllImport("__Internal")] static extern void ChatInputOpen(string hint, int max, string init, int mode);
    [DllImport("__Internal")] static extern string ChatInputPoll();
#endif

    // Ввод строки на телефоне в браузере: поле страницы поверх игры (ChatInput.jslib), ответ приходит позже
    bool phoneInput;
    System.Action<string> phoneInputDone;

    // mode: 0 — сообщение, 1 — поле с текущим значением, 2 — цифры. Системное окно prompt() на телефоне неудобно
    // — везде строка браузера вверху экрана: клавиатура её не закрывает
    void PhoneInput(string hint, int max, System.Action<string> done, string init = "", int mode = 0)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        phoneInput = true;
        phoneInputDone = done;
        WebGLInput.captureAllKeyboardInput = false;
        ChatInputOpen(hint, max, init ?? "", mode);
#endif
    }

    void PhoneInputTick()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!phoneInput) return;
        var s = ChatInputPoll();
        if (string.IsNullOrEmpty(s)) return;
        phoneInput = false;
        WebGLInput.captureAllKeyboardInput = true;
        if (s.Length > 1) phoneInputDone?.Invoke(s.Substring(1));
#endif
    }

    static string Api(string path) =>
        Application.platform == RuntimePlatform.WebGLPlayer ? "/api/" + path : "https://magicsurvival.neuraldeep.ru/api/" + path;

    // устройство голосует один раз за идею: случайный id живёт в PlayerPrefs
    static string Voter()
    {
        var v = PlayerPrefs.GetString("voter");
        if (string.IsNullOrEmpty(v)) { v = System.Guid.NewGuid().ToString("N"); PlayerPrefs.SetString("voter", v); PlayerPrefs.Save(); }
        return v;
    }

    static IEnumerator Send(string method, string path, string json, System.Action<long, string> done)
    {
        using var r = new UnityWebRequest(Api(path), method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 15 };
        if (json != null)
        {
            r.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            r.SetRequestHeader("Content-Type", "application/json");
        }
        // вошедший игрок: токен в каждом запросе — сервер засчитывает забег и павшего только по нему (account.authed)
        if (LoggedIn) r.SetRequestHeader("X-Ms-Token", AccToken);
        yield return r.SendWebRequest();
        done(r.responseCode, r.downloadHandler.text);
    }

    void LoadIdeas()
    {
        ideaStatus = "Загружаю…";
        StartCoroutine(Send("GET", "ideas?voter=" + Voter(), null, (code, body) =>
        {
            if (code != 200) { ideaStatus = "Сервер не ответил — попробуй позже"; return; }
            ideas = JsonUtility.FromJson<IdeaList>(body).ideas;
            if (ideaStatus == "Загружаю…") ideaStatus = null;
        }));
    }

    void SendIdea()
    {
        if (ideaBusy || ideaText.Trim().Length < 8) { ideaStatus = "Опиши идею хотя бы парой слов"; return; }
        ideaBusy = true;
        ideaStatus = "Отправляю…";
        var json = JsonUtility.ToJson(new IdeaPost { text = ideaText.Trim(), voter = Voter() });
        StartCoroutine(Send("POST", "ideas", json, (code, body) =>
        {
            ideaBusy = false;
            if (code == 200) { ideaText = ""; ideaStatus = "Спасибо! Идея уже в голосовании — зови друзей голосовать"; LoadIdeas(); }
            else ideaStatus = code == 0 ? "Нет связи с сервером" : JsonUtility.FromJson<ApiReply>(body)?.error ?? "Ошибка " + code;
        }));
    }

    [System.Serializable] class IdeaPost { public string text, voter; }
    [System.Serializable] class VotePost { public int id, value; public string voter; }

    void Vote(IdeaRow idea)
    {
        int value = idea.mine == 1 ? 0 : 1;
        // отвечаем сразу, сервер догонит
        idea.mine = value;
        idea.votes += value == 1 ? 1 : -1;
        StartCoroutine(Send("POST", "vote", JsonUtility.ToJson(new VotePost { id = idea.id, value = value, voter = Voter() }), (code, body) =>
        {
            if (code != 200) LoadIdeas();
        }));
    }

    // Поле ввода идеи: на ПК — обычное поле; на телефоне в браузере IMGUI не вызывает клавиатуру, поэтому там
    // открывается строка ввода страницы (ChatInput.jslib), а текст показываем в рамке
    void IdeaInput(Rect box)
    {
        RR(box, new Color(0.03f, 0.025f, 0.04f, 0.96f), 12);
        RRLine(box, new Color(Gold.r, Gold.g, Gold.b, 0.5f), 1, 12);
        if (TouchUi && Application.platform == RuntimePlatform.WebGLPlayer)
        {
            Text(new Rect(box.x + 18, box.y + 12, box.width - 36, box.height - 24), string.IsNullOrEmpty(ideaText) ? "<color=#77736C>Нажми и напиши, что добавить в игру…</color>" : ideaText,
                Col(tiny, Bone, TextAnchor.UpperLeft), 2);
            if (GUI.Button(box, GUIContent.none, GUIStyle.none)) PhoneInput("Что добавить в игру?", 600, t => ideaText = t, ideaText, 1);
            return;
        }
        var area = new GUIStyle(tiny) { normal = { textColor = Bone }, focused = { textColor = Bone }, wordWrap = true };
        ideaText = GUI.TextArea(new Rect(box.x + 14, box.y + 10, box.width - 28, box.height - 20), ideaText, 600, area);
        if (string.IsNullOrEmpty(ideaText) && GUIUtility.keyboardControl == 0)
            Text(new Rect(box.x + 18, box.y + 12, box.width - 36, box.height - 24), "<color=#77736C>Напиши, что добавить в игру…</color>", Col(tiny, Bone, TextAnchor.UpperLeft), 2);
    }

    // Один экран: сверху поле «предложить», ниже — список идей с голосами
    void DrawIdeas(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.88f));
        MenuHeader(W, 40, "ИДЕИ ИГРОКОВ", "Что добавить в игру?", Gold);
        float bw = Mathf.Min(1000, W - 48), x = (W - bw) / 2, y = 200;
        float sendW = W < H ? 150 : 200;
        IdeaInput(new Rect(x, y, bw - sendW - 14, 110));
        if (Btn(new Rect(x + bw - sendW, y, sendW, 110), ideaBusy ? "…" : "Отправить", Gold)) SendIdea();
        y += 118;
        if (ideaStatus != null) Text(new Rect(x, y, bw, 34), ideaStatus, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(1f, 0.85f, 0.55f), TextAnchor.MiddleCenter), 2);
        y += 40;
        Text(new Rect(x, y, bw, 30), Up("Голосуй за то, что сделать следующим"), Col(caps, Soft(0.8f), TextAnchor.MiddleCenter), 2);
        float top = y + 40, bottom = H - 120;

        var ev = Event.current;
        var view = new Rect(0, top, W, bottom - top);
        if (ev.type == EventType.ScrollWheel) { ideasScroll += ev.delta.y * 30; ev.Use(); }
        ideasScroll = TouchScroll(view, ideasScroll);
        float tw = bw - 200, total = 0;
        if (ideas != null) foreach (var i in ideas) total += Mathf.Max(96, TextH(i.text, tiny, tw) + 36) + 12;
        ideasScroll = Mathf.Clamp(ideasScroll, 0, Mathf.Max(0, total - view.height));
        GUI.BeginGroup(view);
        float ly = -ideasScroll;
        if (ideas != null)
            foreach (var i in ideas)
            {
                float h = Mathf.Max(96, TextH(i.text, tiny, tw) + 36);
                var r = new Rect(x, ly, bw, h);
                Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.92f), i.mine == 1 ? Gold : new Color(1, 1, 1, 0.12f), 12);
                Text(new Rect(r.x + 20, r.y + 18, tw, h - 36), i.text, Col(tiny, Bone, TextAnchor.UpperLeft), 2);
                Outline(new Rect(r.xMax - 180, r.y, 70, h), i.votes.ToString(), Col(heavy, i.mine == 1 ? Gold : Bone), 2);
                var vb = new Rect(r.xMax - 104, r.y + h / 2 - 30, 90, 60);
                bool hov = vb.Contains(Event.current.mousePosition);
                Plate(vb, i.mine == 1 ? new Color(Gold.r * 0.5f, Gold.g * 0.5f, Gold.b * 0.3f, 0.95f) : new Color(0.1f, 0.08f, 0.1f, 0.95f), hov || i.mine == 1 ? Gold : new Color(1, 1, 1, 0.2f), 10);
                Text(vb, i.mine == 1 ? "ГОЛОС" : "ЗА", Col(caps, i.mine == 1 ? Color.white : Bone, TextAnchor.MiddleCenter), 2);
                if (GUI.Button(vb, GUIContent.none, GUIStyle.none)) Vote(i);
                ly += h + 12;
            }
        else if (ideaStatus == null) Text(new Rect(x, 10, bw, 40), "Загружаю…", Col(tiny, Soft(0.7f), TextAnchor.UpperCenter), 2);
        if (ideas != null && ideas.Length == 0) Text(new Rect(x, 10, bw, 60), "Пока пусто — твоя идея будет первой", Col(small, Soft(0.75f), TextAnchor.UpperCenter), 2);
        GUI.EndGroup();
        float bb = Mathf.Min(460, W - 80);
        if (Btn(new Rect((W - bb) / 2, H - 104, bb, 84), "Назад", Bone)) { menuTab = 0; ideaStatus = null; }
    }

    // ---------- Ник и лидерборд ----------

    static readonly string[] NickA = { "Тёмный", "Ледяной", "Огненный", "Костяной", "Безумный", "Древний", "Тихий", "Грозовой", "Ночной", "Пепельный", "Звёздный", "Гнилой", "Седой", "Хитрый", "Кровавый", "Лунный" };
    static readonly string[] NickB = { "Маг", "Лич", "Колдун", "Чародей", "Некромант", "Алхимик", "Друид", "Шаман", "Странник", "Отшельник", "Жрец", "Охотник", "Призрак", "Книжник", "Ворон", "Волхв" };

    public static string Nick
    {
        get
        {
            var n = PlayerPrefs.GetString("nick");
            if (string.IsNullOrEmpty(n))
            {
                n = $"{NickA[Random.Range(0, NickA.Length)]} {NickB[Random.Range(0, NickB.Length)]} {Random.Range(10, 100)}";
                PlayerPrefs.SetString("nick", n);
                PlayerPrefs.Save();
            }
            return n;
        }
    }

    [System.Serializable] class HelloPost { public string voter, nick; }
    [System.Serializable] class NickReply { public bool ok; public string nick, error; }

    // ---------- Смена ника ----------

    bool nickEdit;
    string nickDraft = "";

    void DrawNickButton(Rect r)
    {
        if (r.width < 120) return;
        if (!nickEdit)
        {
            if (Btn(r, Nick, Bone) && confirm == null)
            {
                // на телефоне — строка ввода браузера с текущим именем, сохраняем по «Готово»
                if (TouchUi) PhoneInput("Имя мага", 24, t => { nickDraft = t; SaveNick(); }, Nick, 1);
                else { nickEdit = true; nickDraft = Nick; }
            }
            return;
        }
        float okW = 76;
        var box = new Rect(r.x, r.y, r.width - okW - 8, r.height);
        RR(box, new Color(0.03f, 0.025f, 0.04f, 0.96f), 12);
        RRLine(box, new Color(Gold.r, Gold.g, Gold.b, 0.5f), 1, 12);
        var field = new GUIStyle(tiny) { normal = { textColor = Bone }, focused = { textColor = Bone }, alignment = TextAnchor.MiddleCenter };
        nickDraft = GUI.TextField(new Rect(box.x + 8, box.y + 4, box.width - 16, box.height - 8), nickDraft, 24, field);
        if (Btn(new Rect(box.xMax + 8, r.y, okW, r.height), "OK", Gold)) SaveNick();
    }

    // ник живёт на сервере (лидеры, павшие маги, комната): меняем там и запоминаем то, что сервер принял
    void SaveNick()
    {
        nickEdit = false;
        var n = System.Text.RegularExpressions.Regex.Replace(nickDraft ?? "", @"[^\w\s\-]", "").Trim();
        if (n.Length < 2 || n == Nick) return;
        PlayerPrefs.SetString("nick", n);
        PlayerPrefs.Save();
        leaders = null;
        if (me != null) me.nick = n;
        // в город перезаходим после ответа сервера: у вошедшего ник в чате берётся из базы, она должна успеть обновиться
        StartCoroutine(Send("POST", "nick", JsonUtility.ToJson(new HelloPost { voter = Voter(), nick = n }), (code, body) =>
        {
            TownRejoin();
            if (code != 200) return;
            var r = JsonUtility.FromJson<NickReply>(body);
            if (r != null && !string.IsNullOrEmpty(r.nick)) { PlayerPrefs.SetString("nick", r.nick); PlayerPrefs.Save(); }
        }));
    }

    [System.Serializable] class RunPost { public string voter, map, rid; public int secs, level, kills, won, gold, diff, quit, mode; }
    [System.Serializable] class RunResp { public bool ok, dup; public int gold, sold, wrev, credited; public GearItem[] drops; }
    [System.Serializable] class LeaderRow { public string nick; public int secs, level, runs, gold, deaths, me; }
    [System.Serializable] class LeaderList { public LeaderRow[] best, total, rich, dead; public string nick; }
    static readonly string[] LeaderTabs = { "Лучший забег", "Всего минут", "Богачи", "Смерти" };
    static readonly string[] LeaderTitles = { "Дольше всех продержались", "Больше всего минут за все игры", "Больше всего золота сейчас", "Чаще всех погибали" };

    static bool helloSent;
    LeaderList leaders;
    int leaderTab;

    // раз за сессию: устройство регистрируется под своим ником, сервер считает визит за день
    void Hello()
    {
        if (bot) return;
        // кошелёк — после hello: сервер знает только зарегистрированные устройства
        // снаряжение — на каждую сцену: после забега в сумке могла появиться добыча
        // очередь забегов уходит только после загрузки кошелька (ApplyWallet): первая синхронизация переносит локальное
        // золото и прокачку, и забег не должен успеть создать пустой кошелёк раньше неё
        TakeClosedRun();
        if (helloSent) { if (walletOk) FlushRuns(); else LoadWallet(); LoadGear(); return; }
        helloSent = true;
        // без связи регистрация не прошла — повторим на следующей сцене
        StartCoroutine(Send("POST", "hello", JsonUtility.ToJson(new HelloPost { voter = Voter(), nick = Nick }), (code, body) =>
        {
            if (code == 200) { LoadWallet(); LoadGear(); }
            else helloSent = false;
        }));
    }

    void PostRun(bool won)
    {
        if (bot) return;
        runDrops = null;
        runSold = 0;
        var json = RunJson(won, runGold, false);
        RunBeacon("");        // забег закончен по-честному — снимок для закрытой вкладки больше не нужен
        QueueRun(json);       // до ответа сервера забег лежит в очереди на диске (GameRunSave.cs)
        SendRun(json, true);
    }

    void LoadLeaders()
    {
        leaders = null;
        StartCoroutine(Send("GET", "leaders?voter=" + Voter(), null, (code, body) =>
        {
            leaders = code == 200 ? JsonUtility.FromJson<LeaderList>(body) : new LeaderList { best = new LeaderRow[0], total = new LeaderRow[0], rich = new LeaderRow[0], dead = new LeaderRow[0] };
        }));
    }

    static string Mmss(int s) => $"{s / 60}:{s % 60:00}";

    void DrawLeaders(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.88f));
        MenuHeader(W, 40, "ТЫ — " + Up(Nick), "Лидеры", Gold);
        bool port = W < H;
        float bw = Mathf.Min(port ? W - 40 : 1300, W - 40), x0 = (W - bw) / 2, top = 200;
        // четыре топа вкладками; в портрете — два ряда по две
        int per = port ? 2 : 4;
        float tw = (bw - (per - 1) * 12) / per;
        for (int t = 0; t < LeaderTabs.Length; t++)
        {
            var tr = new Rect(x0 + t % per * (tw + 12), top + t / per * 56, tw, 48);
            Chip(tr, Up(LeaderTabs[t]), Gold, leaderTab == t);
            if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) leaderTab = t;
        }
        float y0 = top + (LeaderTabs.Length + per - 1) / per * 56 + 6;
        if (leaders == null) { Text(new Rect(0, y0 + 40, W, 50), "Загружаю…", Col(small, Soft(0.7f), TextAnchor.MiddleCenter), 2); }
        else
        {
            int k = leaderTab;
            var rows = k == 0 ? leaders.best : k == 1 ? leaders.total : k == 2 ? leaders.rich : leaders.dead;
            Text(new Rect(x0, y0, bw, 40), Up(LeaderTitles[k]), Col(caps, Gold, TextAnchor.MiddleCenter), 2);
            y0 += 48;
            if (rows == null || rows.Length == 0) Text(new Rect(x0, y0, bw, 40), "Пока никого — сыграй первым", Col(tiny, Soft(0.7f), TextAnchor.MiddleCenter), 2);
            else
            {
                // в альбоме список в две колонки: 1–10 слева, 11–20 справа
                int cols = port ? 1 : 2;
                float cw = (bw - (cols - 1) * 30) / cols;
                int fit = Mathf.Max(1, Mathf.FloorToInt((H - 140 - y0) / 52));
                int n = Mathf.Min(rows.Length, fit * cols);
                for (int i = 0; i < n; i++)
                {
                    var r = rows[i];
                    var rr = new Rect(x0 + i / fit * (cw + 30), y0 + i % fit * 52, cw, 46);
                    var fill = r.me == 1 ? new Color(0.25f, 0.18f, 0.06f, 0.9f) : new Color(0.05f, 0.04f, 0.07f, i % 2 == 0 ? 0.9f : 0.7f);
                    Plate(rr, fill, r.me == 1 ? Gold : new Color(1, 1, 1, 0.08f), 10);
                    var medal = i == 0 ? new Color(1f, 0.82f, 0.3f) : i == 1 ? new Color(0.8f, 0.82f, 0.88f) : i == 2 ? new Color(0.85f, 0.55f, 0.3f) : Bone * 0.7f;
                    Text(new Rect(rr.x + 10, rr.y, 50, 46), (i + 1).ToString(), Col(num, medal, TextAnchor.MiddleCenter), 2);
                    Text(new Rect(rr.x + 66, rr.y, cw - 300, 46), r.nick, Shrink(Col(tiny, r.me == 1 ? Gold : Bone, TextAnchor.MiddleLeft), r.nick, cw - 300), 2);
                    string v = k == 0 ? $"{Mmss(r.secs)}  <color=#8E8C99>ур. {r.level}</color>"
                             : k == 1 ? $"{r.secs / 60} мин  <color=#8E8C99>{r.runs} игр</color>"
                             : k == 2 ? $"<color=#FFD25A>{r.gold}</color>  <color=#8E8C99>золота</color>"
                             : $"{r.deaths}  <color=#8E8C99>смертей, лучший {Mmss(r.secs)}</color>";
                    Text(new Rect(rr.xMax - 280, rr.y, 266, 46), v, Shrink(Col(val, Bone, TextAnchor.MiddleRight), v, 266), 2);
                }
            }
        }
        float bb = Mathf.Min(460, W - 80);
        if (Btn(new Rect((W - bb) / 2, H - 104, bb, 84), "Назад", Bone)) menuTab = 0;
    }
}
