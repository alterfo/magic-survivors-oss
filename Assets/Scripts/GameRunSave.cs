using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// Забег не теряется. Раньше золото пропадало в трёх местах:
// 1) «Завершить и в город» перезагружает сцену сразу после отправки — корутина умирала вместе с Game, ответ терялся;
// 2) обрыв связи — клиент уже прибавил золото к балансу, а сервер забег не видел;
// 3) закрытая вкладка (cmd+W) посреди забега — забег не уходил вовсе.
// Теперь: у забега постоянный номер (rid = номер телеметрии), сервер засчитывает его один раз;
// законченный забег лежит в очереди на диске, пока сервер не ответит, и шлётся из объекта, который переживает сцену;
// идущий забег раз в 10 секунд оставляет браузеру снимок «вышел на этом месте» — на pagehide он уходит
// через sendBeacon, а если и это не успело — при следующем запуске из localStorage.
public class NetRunner : MonoBehaviour
{
    static NetRunner i;
    public static NetRunner I
    {
        get
        {
            if (i) return i;
            var go = new GameObject("NetRunner");
            DontDestroyOnLoad(go);
            return i = go.AddComponent<NetRunner>();
        }
    }
}

public partial class Game
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void BrowserRunSnap(string url, string json, string token);
    [DllImport("__Internal")] static extern string BrowserRunTake();
    [DllImport("__Internal")] static extern void BrowserShareText(string text);
#else
    static void BrowserShareText(string text) { GUIUtility.systemCopyBuffer = text; }
    static void BrowserRunSnap(string url, string json, string token) { }
    static string BrowserRunTake() => "";
#endif

    static readonly HashSet<string> runsInFlight = new();
    float sharedAt = -9;

    // «Картинка с итогами для шаринга» (фидбек): пока текстом — карта, время, уровень, убийства, главная магия и ссылка
    void ShareRun()
    {
        var spells = new List<string>();
        var list = new List<Skill>(skills.FindAll(s => s.spell && s.level > 0));
        list.Sort((a, b) => (b.fusion ? 100 : b.level).CompareTo(a.fusion ? 100 : a.level));
        for (int i = 0; i < list.Count && i < 4; i++) spells.Add(list[i].title);
        string res = state == State.Won ? "победа" : "продержался";
        string text = $"Magic Survivors · {Levels[selLevel].title} · {CurDiff.title}: {res} {(int)time / 60}:{(int)time % 60:00}, уровень {level}, убито {kills}." +
                      (spells.Count > 0 ? $" Магия: {string.Join(", ", spells)}." : "") + " Сыграй: https://magicsurvival.neuraldeep.ru";
        BrowserShareText(text);
        sharedAt = Time.unscaledTime;
    }
    float runSnapT;

    string RunJson(bool won, int gold, bool quit) => JsonUtility.ToJson(new RunPost
    {
        voter = Voter(), secs = (int)time, level = level, kills = kills, won = won ? 1 : 0, gold = gold,
        // уровень и сложность нужны серверу для грейда добычи (server/gear.py); уровень — тот, что построен (в кооперативе — хозяйский)
        map = World.Level?.id ?? Levels[selLevel].id, diff = selDiff, rid = runId, quit = quit ? 1 : 0, mode = survival ? 1 : 0,
    });

    // без загруженного кошелька маяк не шлём (url пустой): снимок останется в localStorage и уйдёт после загрузки кошелька
    // маяк не умеет заголовки — токен входа JS добавит полем t в тело только при отправке (в localStorage его нет)
    static void RunBeacon(string json) => BrowserRunSnap(walletOk ? Api("run") : "", json, AccToken);

    // снимок идущего забега для закрытой вкладки: как «Завершить и в город» на этом месте
    void RunSnapTick()
    {
        if (bot || LabOn || freeRun || runGold >= 0 || time < 5) return;
        if (state == State.Dead || state == State.Won || state == State.Menu || state == State.Town) return;
        runSnapT -= Time.unscaledDeltaTime;
        if (runSnapT > 0) return;
        runSnapT = 10;
        RunBeacon(RunJson(false, RunGold(false), true));
    }

    static List<string> RunQueue()
    {
        var s = PlayerPrefs.GetString("run.queue");
        return string.IsNullOrEmpty(s) ? new List<string>() : new List<string>(s.Split('\n'));
    }

    static void SaveQueue(List<string> q)
    {
        // больше пяти неотправленных — старые выкидываем, чтобы очередь не росла без конца
        while (q.Count > 5) q.RemoveAt(0);
        PlayerPrefs.SetString("run.queue", string.Join("\n", q));
        PlayerPrefs.Save();
    }

    static void QueueRun(string json) { var q = RunQueue(); if (!q.Contains(json)) q.Add(json); SaveQueue(q); }
    static void DropRun(string json) { var q = RunQueue(); if (q.Remove(json)) SaveQueue(q); }

    // снимок забега, который не закончился по-честному: вкладку закрыли, а маяк не дошёл (или дошёл — сервер узнает номер
    // и второй раз не засчитает), или сцена перезагрузилась посреди забега. Берём на каждой сцене — новый забег ещё не начат
    void TakeClosedRun()
    {
        if (bot) return;
        var s = BrowserRunTake();
        if (string.IsNullOrEmpty(s)) return;
        foreach (var json in s.Split('\n')) if (json.Contains("\"rid\"")) QueueRun(json);
    }

    static void RequeueRuns(string from, string to)
    {
        if (from == to) return;
        PlayerPrefs.SetString("merged." + from, to);   // снимки, восстановленные позже, тоже уйдут аккаунту
        var q = RunQueue();
        for (int i = 0; i < q.Count; i++)
        {
            var p = JsonUtility.FromJson<RunPost>(q[i]);
            if (p != null && p.voter == from) { p.voter = to; q[i] = JsonUtility.ToJson(p); }
        }
        SaveQueue(q);
    }

    void FlushRuns()
    {
        if (bot) return;
        foreach (var json in RunQueue()) SendRun(json, false);
    }

    // mine — забег этой сцены: его ответ показывает добычу и точное золото на экране конца забега
    void SendRun(string json, bool mine)
    {
        // пока кошелёк не загружен, забег только ждёт в очереди: первая синхронизация переносит локальное золото
        // и прокачку, а забег, пришедший раньше, создал бы пустой кошелёк. FlushRuns из ApplyWallet отправит его
        if (!walletOk) return;
        if (!runsInFlight.Add(json)) return;
        int wgen = walletGen;
        var g = this;
        var post = JsonUtility.FromJson<RunPost>(json);
        string voter = post?.voter;
        NetRunner.I.StartCoroutine(Send("POST", "run", json, (code, body) =>
        {
            runsInFlight.Remove(json);
            // 400 «unknown player»: устройство вошло в аккаунт, и прежний профиль слит в аккаунтный — забег переписываем
            // на нынешний профиль (номер тот же, второй раз не засчитается). Иначе 400 — мусор: из очереди убираем.
            // 429 и обрыв — пробуем при следующей сцене
            if (code == 400)
            {
                bool unknown = body.Contains("unknown player");
                // своё устройство ещё не зарегистрировано (регистрация не прошла без сети) — забег ждёт в очереди
                if (unknown && voter == Voter()) { helloSent = false; return; }
                DropRun(json);
                // профиль слит в аккаунт (запомнено при входе) — забег переписываем туда; чужому аккаунту не отдаём
                var mergedTo = post != null ? PlayerPrefs.GetString("merged." + voter) : "";
                if (unknown && !string.IsNullOrEmpty(mergedTo))
                {
                    post.voter = mergedTo;
                    var again = JsonUtility.ToJson(post);
                    QueueRun(again);
                    if (I != null) I.SendRun(again, false);
                }
                else if (I != null && wgen == walletGen) I.LoadWallet();
                return;
            }
            if (code != 200) return;
            DropRun(json);
            var r = JsonUtility.FromJson<RunResp>(body);
            if (r == null) return;
            // баланс из ответа — только если забег этого профиля: после выхода из аккаунта очередь могла дослать чужой
            if (voter == Voter()) SetGold(r.gold, r.wrev, wgen);
            if (!mine || g == null || r.dup) return;
            runDrops = r.drops != null ? new List<GearItem>(r.drops) : null;
            runSold = r.sold;
            // сервер урезал золото потолком — показываем засчитанное, а не обещанное
            if (r.credited >= 0 && r.credited < g.runGold) g.runGold = r.credited;
        }));
    }
}
