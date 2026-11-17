using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

// Вход через хаб neuraldeep (server/account.py). В браузере — уход на хаб и возврат с ?ms_login=<билет>,
// в desktop-сборке — браузер с кодом, код вводится здесь. У аккаунта свой voter: ник, рекорды и голоса общие на всех устройствах.
public partial class Game
{
    [System.Serializable] class LoginPost { public string ticket, voter, nick; }
    [System.Serializable] class TokenPost { public string token; }
    [System.Serializable] class AccountRec { public int runs, wins, secs, level, kills, total; }
    [System.Serializable] class AccountReply { public bool ok; public string error, token, voter, nick, name, merged; public AccountRec rec; }

    static bool accountStarted;
    bool accBusy, accCodeMode, accConfirm;
    static int accEnabled = -1;  // -1 — не спрашивали, -3 — спрашиваем, -2 — сбой (повтор), 0 — вход выключен, 1 — включён
    static float accRetryAt;
    [System.Serializable] class EnabledReply { public bool enabled; }
    string accStatus, accCode = "";

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void BrowserGo(string url);
    [DllImport("__Internal")] static extern void BrowserDropLogin();
#endif

    static string AccToken => PlayerPrefs.GetString("acc.token");
    public static string AccName => PlayerPrefs.GetString("acc.name");
    public static bool LoggedIn => AccToken.Length > 0;

    // раз за запуск: вернулись с хаба — меняем билет на токен; уже вошли — сверяем токен с сервером
    public void AccountStart()
    {
        if (accountStarted || bot) return;
        accountStarted = true;
        var url = Application.absoluteURL ?? "";
        int i = url.IndexOf("ms_login=");
        if (i >= 0)
        {
            var t = url.Substring(i + 9);
            int end = t.IndexOfAny(new[] { '&', '#' });
            if (end >= 0) t = t.Substring(0, end);
#if UNITY_WEBGL && !UNITY_EDITOR
            BrowserDropLogin(); // билет одноразовый, но в адресной строке и истории ему делать нечего
#endif
            Login(UnityWebRequest.UnEscapeURL(t));
        }
        else if (LoggedIn)
            StartCoroutine(Send("POST", "me", JsonUtility.ToJson(new TokenPost { token = AccToken }), (code, body) =>
            {
                if (code == 200) Adopt(JsonUtility.FromJson<AccountReply>(body));
                else if (code == 401) ForgetAccount(false); // токен больше не действует — профиль устройства оставляем
            }));
    }

    void Login(string ticket)
    {
        if (accBusy || string.IsNullOrWhiteSpace(ticket)) return;
        accBusy = true;
        accStatus = "Вхожу…";
        var json = JsonUtility.ToJson(new LoginPost { ticket = ticket.Trim(), voter = Voter(), nick = Nick });
        StartCoroutine(Send("POST", "login", json, (code, body) =>
        {
            accBusy = false;
            if (code == 200)
            {
                var a = JsonUtility.FromJson<AccountReply>(body);
                // сервер слил профиль устройства в аккаунт (merged) — неотправленные забеги этого профиля идут туда же.
                // Профиль другого аккаунта сервер не сливает — его забеги остаются его
                if (!string.IsNullOrEmpty(a.merged) && !string.IsNullOrEmpty(a.voter)) RequeueRuns(a.merged, a.voter);
                PlayerPrefs.SetString("acc.token", a.token);
                Adopt(a);
                accCodeMode = false;
                accCode = "";
                accStatus = "Вошёл как " + a.name;
            }
            else accStatus = code == 0 ? "Нет связи с сервером" : JsonUtility.FromJson<ApiReply>(body)?.error ?? "Ошибка " + code;
        }));
    }

    // профиль аккаунта становится профилем устройства: дальше рекорды, голоса и забеги идут под его voter
    void Adopt(AccountReply a)
    {
        if (!string.IsNullOrEmpty(a.voter)) PlayerPrefs.SetString("voter", a.voter);
        if (!string.IsNullOrEmpty(a.nick)) PlayerPrefs.SetString("nick", a.nick);
        PlayerPrefs.SetString("acc.name", a.name ?? "");
        // рекорды: лучшее из того, что наиграно на этом устройстве, и того, что уже есть в аккаунте
        if (a.rec != null)
        {
            void Best(string k, int v) { if (v > PlayerPrefs.GetInt(k)) PlayerPrefs.SetInt(k, v); }
            Best("rec.runs", a.rec.runs); Best("rec.wins", a.rec.wins); Best("rec.level", a.rec.level);
            Best("rec.kills", a.rec.kills); Best("rec.total", a.rec.total);
            if (a.rec.secs > PlayerPrefs.GetFloat("rec.time")) PlayerPrefs.SetFloat("rec.time", a.rec.secs);
        }
        PlayerPrefs.Save();
        leaders = null;
        // кошелёк теперь аккаунта — перечитать
        prefsSwitched = true;   // настройки — нового профиля (GamePrefs.cs)
        ResetWallet();
        owned.Clear();
        LoadWallet();
        ResetProfileGear();   // снаряжение тоже аккаунта: прежнее снимаем, запросы прежнего профиля отбрасываем
        RefreshGearStats();
        LoadGear();
    }

    void Logout()
    {
        var token = AccToken;
        StartCoroutine(Send("POST", "logout", JsonUtility.ToJson(new TokenPost { token = token }), (code, body) => { }));
        ForgetAccount(true);
        accStatus = "Вышел";
    }

    // newProfile: устройство получает новый анонимный voter и ник, иначе следующий игрок играл бы в чужой профиль
    void ForgetAccount(bool newProfile)
    {
        PlayerPrefs.DeleteKey("acc.token");
        PlayerPrefs.DeleteKey("acc.name");
        if (newProfile)
        {
            PlayerPrefs.DeleteKey("voter");
            PlayerPrefs.DeleteKey("nick");
            foreach (var k in new[] { "rec.runs", "rec.wins", "rec.time", "rec.level", "rec.kills", "rec.total" }) PlayerPrefs.DeleteKey(k);
            prefsSwitched = true;   // настройки — нового профиля (GamePrefs.cs)
            ResetWallet();
            owned.Clear();
            // снаряжение прошлого профиля снимаем с героя сразу, новое придёт после hello
            ResetProfileGear();
            RefreshGearStats();
            helloSent = false;
            Hello();
        }
        PlayerPrefs.Save();
        leaders = null;
    }

    void StartHubLogin()
    {
        accConfirm = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        accStatus = "Перехожу на neuraldeep…";
        BrowserGo(Api("login/start?mode=web"));
#else
        // в приложении вернуть браузер в игру нельзя: хаб покажет код, его вводят здесь
        Application.OpenURL(Api("login/start?mode=app"));
        accCodeMode = true;
        accStatus = "Войди в браузере и введи код со страницы";
#endif
    }

    // Кнопка аккаунта для меню: «Войти через neuraldeep» / имя и выход; под ней строка статуса
    public void DrawAccountButton(Rect r)
    {
        AccountStart();
        if (accEnabled == -2 && Time.unscaledTime > accRetryAt) accEnabled = -1;
        if (accEnabled == -1)
        {
            accEnabled = -3;   // спрашиваем
            // сбой связи — не «вход выключен»: спросим ещё раз через 10 с
            StartCoroutine(Send("GET", "login/enabled", null, (code, body) =>
            {
                if (code == 200) accEnabled = JsonUtility.FromJson<EnabledReply>(body).enabled ? 1 : 0;
                else { accEnabled = -2; accRetryAt = Time.unscaledTime + 10; }
            }));
        }
        // вход обязателен (сервер сказал в /api/news) — кнопка есть всегда, даже пока проверка не ответила
        if (accEnabled != 1 && !LoggedIn && !loginRequired) return;
        if (accCodeMode && !LoggedIn)
        {
            float okW = r.width * 0.26f, xW = r.height;
            var box = new Rect(r.x, r.y, r.width - okW - xW - 16, r.height);
            RR(box, new Color(0.03f, 0.025f, 0.04f, 0.96f), 12);
            RRLine(box, new Color(Gold.r, Gold.g, Gold.b, 0.5f), 1, 12);
            var field = new GUIStyle(tiny) { normal = { textColor = Bone }, focused = { textColor = Bone }, alignment = TextAnchor.MiddleCenter };
            accCode = GUI.TextField(new Rect(box.x + 10, box.y + 4, box.width - 20, box.height - 8), accCode, 12, field).ToUpperInvariant();
            if (string.IsNullOrEmpty(accCode)) Text(box, "<color=#77736C>КОД</color>", Col(tiny, Bone, TextAnchor.MiddleCenter), 2);
            if (Btn(new Rect(box.xMax + 8, r.y, okW, r.height), accBusy ? "…" : "Войти", Gold)) Login(accCode);
            if (Btn(new Rect(r.xMax - xW, r.y, xW, r.height), "×", Bone)) { accCodeMode = false; accStatus = null; }
        }
        else if (LoggedIn)
        {
            if (Btn(r, accConfirm ? "Точно выйти?" : AccName + "  ·  выйти", accConfirm ? Gold : Bone))
            {
                if (accConfirm) { accConfirm = false; Logout(); }
                else accConfirm = true;
            }
        }
        else if (Btn(r, accBusy ? "Вхожу…" : "Войти через neuraldeep", Gold) && !accBusy) StartHubLogin();
        if (accStatus != null)
            Text(new Rect(r.x - 100, r.yMax + 4, r.width + 200, 30), accStatus, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(1f, 0.85f, 0.55f), TextAnchor.MiddleCenter), 2);
    }
}
