using UnityEngine;

// Обязательный вход через neuraldeep и объявления для всех (server/ideas.py: GET /api/news, server/news.py).
// Вход обязателен, когда сервер так говорит (news.login) и вход на нём заведён: забег и награды без аккаунта
// не засчитываются (401), поэтому меню и город закрыты экраном входа. Прогресс устройства при входе сливается в аккаунт.
public partial class Game
{
    [System.Serializable] class NewsItem { public int id; public string text; }
    [System.Serializable] class NewsReply { public NewsItem[] items; public bool login; }
    static NewsItem[] newsItems;
    static bool loginRequired;
    static float newsAt = -999;

    // без аккаунта играть нельзя: только экран входа (бот и лаборатория — без входа)
    bool LoginGate => loginRequired && !LoggedIn && !bot;

    // объявления и флаг входа: при запуске и раз в две минуты, пока игрок в меню или в городе
    void NewsTick()
    {
        if (bot || Time.unscaledTime < newsAt) return;
        newsAt = Time.unscaledTime + 120;
        StartCoroutine(Send("GET", "news", null, (code, body) =>
        {
            if (code != 200) return;
            var r = JsonUtility.FromJson<NewsReply>(body);
            newsItems = r?.items;
            loginRequired = r != null && r.login;
        }));
    }

    // true — экран входа нарисован, остальное меню не рисуем
    bool DrawGate(float W, float H)
    {
        NewsTick();
        if (!LoginGate) return false;
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.93f));
        bool port = W < H;
        float pw = Mathf.Min(760, W - 32), x = (W - pw) / 2;
        Outline(new Rect(0, port ? H * 0.12f : H * 0.1f, W, 100), "Magic Survivors", Col(big, new Color(1f, 0.88f, 0.62f)), 3);
        float y = port ? H * 0.12f + 130 : H * 0.1f + 120;
        var st = new GUIStyle(Col(small, Bone, TextAnchor.UpperCenter)) { wordWrap = true };
        const string why = "Теперь играем через аккаунт neuraldeep: золото, вещи и рекорды сохраняются на всех устройствах, а накрутка не проходит.";
        const string keep = "Уже играл без входа? Войди — всё наигранное на этом устройстве перейдёт в аккаунт.";
        float h1 = st.CalcHeight(new GUIContent(why), pw);
        Text(new Rect(x, y, pw, h1), why, st, 2);
        y += h1 + 24;
        var st2 = new GUIStyle(Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, MetaGold, TextAnchor.UpperCenter)) { wordWrap = true };
        float h2 = st2.CalcHeight(new GUIContent(keep), pw);
        Text(new Rect(x, y, pw, h2), keep, st2, 2);
        y += h2 + 40;
        // та же кнопка, что в углу меню: в браузере — уход на хаб, в приложении — поле для кода; под ней статус
        float bw = Mathf.Min(560, W - 48);
        DrawAccountButton(new Rect((W - bw) / 2, y, bw, 86));
        return true;
    }

    NewsItem PendingNews => newsItems != null && newsItems.Length > 0 && newsItems[0] != null
        && newsItems[0].id > PlayerPrefs.GetInt("news.seen", 0) ? newsItems[0] : null;

    // окно объявления: рамка и кнопка «Понятно»
    void NewsLayout(float W, float H, NewsItem n, out Rect box, out Rect ok, out float th, out GUIStyle st)
    {
        float pw = Mathf.Min(820, W - 32), x = (W - pw) / 2;
        st = new GUIStyle(Col(small, Bone, TextAnchor.UpperCenter)) { wordWrap = true };
        th = st.CalcHeight(new GUIContent(n.text), pw - 60);
        float ph = 70 + th + 30 + 76 + 24;
        box = new Rect(x, (H - ph) / 2, pw, ph);
        ok = new Rect(x + (pw - 300) / 2, box.y + 70 + th + 30, 300, 76);
    }

    // в IMGUI клик достаётся контролу, нарисованному раньше, а окно рисуется последним (поверх меню) —
    // поэтому мышь разбираем в начале кадра и гасим, чтобы меню под окном её не получило
    void NewsInput(float W, float H)
    {
        var n = PendingNews;
        var e = Event.current;
        if (n == null) return;
        // клавиатура тоже не идёт под окно (Enter отправил бы черновик чата); Enter и Esc — «Понятно»
        if (e.isKey)
        {
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
            { PlayerPrefs.SetInt("news.seen", n.id); PlayerPrefs.Save(); }
            e.Use();
            return;
        }
        if (!e.isMouse) return;
        NewsLayout(W, H, n, out _, out var ok, out _, out _);
        if (e.type == EventType.MouseUp && ok.Contains(e.mousePosition)) { PlayerPrefs.SetInt("news.seen", n.id); PlayerPrefs.Save(); }
        e.Use();
    }

    // свежее объявление — окно поверх меню и города, «Понятно» больше его не показывает
    void DrawNews(float W, float H)
    {
        var n = PendingNews;
        if (n == null || Event.current.type != EventType.Repaint) return;
        NewsLayout(W, H, n, out var box, out var ok, out float th, out var st);
        Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.45f));
        Plate(box, new Color(0.06f, 0.045f, 0.02f, 0.97f), Gold, 16, 2);
        Outline(new Rect(box.x, box.y + 12, box.width, 50), "Объявление", Col(caps, Gold, TextAnchor.MiddleCenter), 2);
        Text(new Rect(box.x + 30, box.y + 70, box.width - 60, th), n.text, st, 2);
        Btn(ok, "Понятно", Gold);
    }
}
