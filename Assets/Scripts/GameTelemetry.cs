using System.Collections.Generic;
using UnityEngine;

// Телеметрия забега для баланса: что предлагали и что выбрали, урон по школам, срезы раз в минуту
// (уровень, здоровье, мобы, FPS), кто добил, устройство. Срез уходит на сервер каждую минуту — брошенные
// вкладки тоже попадают в статистику; разбор — `python3 ideas.py report` на сервере игр.
public partial class Game
{
    [System.Serializable] class TPick { public int t, lvl; public string kind, pick; public string[] offered; }
    // rs — масштаб рендера, %; hit — сколько кадров за минуту длились дольше 120 мс (фризы), maxMs — самый долгий кадр;
    // gap — самый долгий перерыв между снимками хозяина у гостя, мс (рывки сети); coop — 0 один, 1 хозяин, 2 гость
    [System.Serializable] class TSnap { public int t, lvl, hp, maxHp, mobs, kills, fps, rs, sim, gui, ovl, hud, hit, maxMs, gap, coop, batches, press; }
    [System.Serializable]
    class TRun
    {
        public string run, voter, end, hero, map, diff, platform, quality, killedBy;
        // gpu — чем рисует браузер («SwiftShader», «Basic Render» — программный рендер без видеокарты); os — система
        public string gpu, os;
        public int secs, lvl, kills, won, screenW, screenH, fpsAvg, fpsMin;
        public TPick[] picks;
        public TSnap[] snaps;
        public string[] schools, spells, artifacts;
        public int[] dmg;
    }

    readonly string runId = System.Guid.NewGuid().ToString("N");
    readonly List<TPick> tPicks = new();
    readonly List<TSnap> tSnaps = new();
    readonly float[] tDmg = new float[8];
    string lastHitBy;
    float tNext = 60, tFrames, tFrameTime, tFpsMin = 999, tAllFrames, tAllTime;
    bool tSent;
    int tHitches;
    // сколько отдельных вызовов отрисовки в кадре: в браузере каждый стоит процессорного времени
    Unity.Profiling.ProfilerRecorder tBatches;
    long tBatchSum; int tBatchN;
    float tMaxFrame, tGap;

    void OnDestroy() { if (tBatches.Valid) tBatches.Dispose(); }

    void TrackDamage(Element el, float d)
    {
        int i = (int)el;
        if (i >= 0 && i < tDmg.Length) tDmg[i] += d;
    }

    void TrackPick(string kind, string pick, List<string> offered) =>
        tPicks.Add(new TPick { t = (int)time, lvl = level, kind = kind, pick = pick, offered = offered.ToArray() });

    float perfLogAt = 10;

    void TelemetryTick(float dt)
    {
        if (auto && time >= perfLogAt)
        {
            perfLogAt += 10;
            int alive = 0;
            foreach (var e in enemies) if (e.alive) alive++;
            int n = Mathf.Max(1, perfFrames);
            Debug.Log($"perf: gpu={SystemInfo.graphicsDeviceName} t={time:0} fps={tFrames / Mathf.Max(0.001f, tFrameTime):0} sim={perfSim / n * 1000:0.00}ms gui={perfGui / n * 1000:0.00}ms ovl={perfOvl / n * 1000:0.00}ms hud={perfHud / n * 1000:0.00}ms mobs={alive} numbers={numbers.Count} scale={(rp == null ? 1 : rp.renderScale):0.00} screen={Screen.width}x{Screen.height}");
            if (bot) { perfSim = perfGui = perfOvl = perfHud = 0; perfFrames = 0; }
        }
        if (bot) return;
        float u = Time.unscaledDeltaTime;
        tFrames++; tFrameTime += u; tAllFrames++; tAllTime += u;
        if (u > 0.12f) tHitches++;
        if (!tBatches.Valid) tBatches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
        else if (tBatches.LastValue > 0) { tBatchSum += tBatches.LastValue; tBatchN++; }
        tMaxFrame = Mathf.Max(tMaxFrame, u);
        if (time < tNext) return;
        tNext += 60;
        int fps = Mathf.RoundToInt(tFrames / Mathf.Max(0.001f, tFrameTime));
        tFpsMin = Mathf.Min(tFpsMin, fps);
        tFrames = tFrameTime = 0;
        int pf = Mathf.Max(1, perfFrames);
        int simMs = Mathf.RoundToInt((float)(perfSim / pf * 10000)), guiMs = Mathf.RoundToInt((float)(perfGui / pf * 10000));
        int ovlMs = Mathf.RoundToInt((float)(perfOvl / pf * 10000)), hudMs = Mathf.RoundToInt((float)(perfHud / pf * 10000));
        perfSim = perfGui = perfOvl = perfHud = 0; perfFrames = 0;
        int mobs = 0;
        foreach (var e in enemies) if (e.alive) mobs++;
        tSnaps.Add(new TSnap { t = (int)time, lvl = level, hp = (int)player.hp, maxHp = (int)player.maxHp, mobs = mobs, kills = kills, fps = fps, rs = rp == null ? 100 : Mathf.RoundToInt(rp.renderScale * 100), sim = simMs, gui = guiMs, ovl = ovlMs, hud = hudMs,
            hit = tHitches, maxMs = Mathf.RoundToInt(tMaxFrame * 1000), gap = Mathf.RoundToInt(tGap * 1000), coop = !coopRun ? 0 : Guest ? 2 : 1, batches = tBatchN > 0 ? (int)(tBatchSum / tBatchN) : 0, press = Mathf.RoundToInt(press * 100) });
        tBatchSum = 0; tBatchN = 0;
        tHitches = 0; tMaxFrame = 0; tGap = 0;
        SendTelemetry("live");
    }

    void SendTelemetry(string end)
    {
        if (bot || tSent || time < 5) return;
        if (end != "live") tSent = true;
        var spells = new List<string>();
        foreach (var s in skills)
            if (s.level > 0) spells.Add($"{s.title} {s.level}" + (s.super >= 0 ? $" ({s.SuperName})" : ""));
        var arts = new List<string>();
        foreach (var a in artifacts) arts.Add(a.title);
        var dmg = new int[tDmg.Length];
        for (int i = 0; i < dmg.Length; i++) dmg[i] = Mathf.RoundToInt(tDmg[i]);
        var r = new TRun
        {
            run = runId, voter = Voter(), end = end, hero = Heroes[selHero].title, map = Levels[selLevel].title, diff = CurDiff.title + (survival ? " · выживание" : ""),
            platform = Application.platform + (Application.isMobilePlatform ? " mobile" : ""), quality = optHigh ? "high" : "low",
            killedBy = state == State.Dead ? lastHitBy : null,
            gpu = SystemInfo.graphicsDeviceName, os = SystemInfo.operatingSystem,
            secs = (int)time, lvl = level, kills = kills, won = state == State.Won || freeRun ? 1 : 0,
            screenW = Screen.width, screenH = Screen.height,
            fpsAvg = Mathf.RoundToInt(tAllFrames / Mathf.Max(0.001f, tAllTime)), fpsMin = tFpsMin > 900 ? 0 : (int)tFpsMin,
            picks = tPicks.ToArray(), snaps = tSnaps.ToArray(),
            schools = SchoolNames, spells = spells.ToArray(), artifacts = arts.ToArray(), dmg = dmg,
        };
        // без корутины: запрос доживёт, даже если сцену сейчас перезагрузят («Начать заново», «В меню»)
        var req = new UnityEngine.Networking.UnityWebRequest(Api("telemetry"), "POST")
        {
            uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(r))),
            downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer(),
            timeout = 15,
        };
        req.SetRequestHeader("Content-Type", "application/json");
        // срез пишет только владелец профиля (ревью Codex 17.11: по чужому voter рисовали смерть в рейтинге)
        if (LoggedIn) req.SetRequestHeader("X-Ms-Token", AccToken);
        req.SendWebRequest().completed += _ => req.Dispose();
    }
}
