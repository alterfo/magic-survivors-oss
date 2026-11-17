using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Лаборатория баланса: автопилот проходит забеги по заданным стратегиям на ускорении и пишет итог каждого
// строкой JSON. Запуск без графики (tools/lab.py гоняет пачку в несколько процессов):
//   MagicSurvivors -batchmode -nographics -labFile strategies.txt -labOut results.jsonl
// Строка стратегии: "spells=fireball,meteor;pass=vampire,regen;move=stand;diff=1;map=0;hero=0;meta=0"
//   spells / pass — что качать в первую очередь (по имени иконки), остальное берётся, только если выбора нет;
//   move — kite (убегать и собирать, как обычный бот), stand (стоять на месте), circle (ходить по кругу);
//   after=30 — стоять/кружить начиная с 30-го уровня, до него бегать; meta=0..1 — доля постоянной прокачки
//   (0 — «свежий игрок», 1 — куплено всё); free=1 — после победы играть дальше до гибели;
//   lhp / ldmg / lrate / lfrom — поздний нажим уровня (LevelContent.late*) для подбора без пересборки.
// В лаборатории нет сети, телеметрии, рекордов и павших магов (Game.bot = true).
public partial class Game
{
    class LabStrat
    {
        public string raw, move = "kite";
        public List<string> spells = new(), pass = new();
        public int diff, map, hero, after;   // after — с какого уровня начинать стоять/кружить (до него бот бегает как обычно)
        public bool free;                    // free=1 — после победы играть дальше (свободный забег), итог пишется по гибели
        public float meta;                   // доля постоянной прокачки: 0 — свежий игрок, 1 — всё куплено
        public float lhp = -1, ldmg = -1, lrate = -1, lfrom = -1;   // поздний нажим вместо чисел уровня (−1 — как в уровне)
    }

    [System.Serializable]
    class LabResult
    {
        public string strat, move, killedBy, build;
        public int diff, map, hero, secs, won, lvl, kills, minHpPct, avgHpPct, standSecs;
        public float healRegen, healDrain, dmgTaken;
    }

    const int LabSteps = 10;         // шагов мира за кадр по 1/30 с: ×10 при 30 кадрах, без графики кадров больше
    static List<LabStrat> labQueue;
    static int labIndex;
    static string labOut;
    static LabStrat lab;
    static bool labChecked;
    // счётчики текущего забега
    float labRegen, labDrain, labTaken, labHpSum, labMinHp = 1, labStandFrom = -1;
    int labHpN;
    bool labDone;

    static bool LabOn => lab != null;

    // тестовые запуски (лаборатория, автопилот, -batchmode) молчат с первого кадра: звук глушится целиком,
    // ещё до того, как сцена успеет завести музыку
    static bool testMute;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void MuteTests()
    {
        // в браузере то же самое делает ?mute=1 в адресе — для проверок сборки без звука
        bool url = (Application.absoluteURL ?? "").Contains("mute=1");
        foreach (var a in System.Environment.GetCommandLineArgs())
            if (url || a == "-labFile" || a == "-autoplay" || a == "-batchmode" || a == "-nosound") { testMute = true; AudioListener.volume = 0; AudioListener.pause = true; return; }
        if (url) { testMute = true; AudioListener.volume = 0; AudioListener.pause = true; }
    }

    void LabArgs()
    {
        if (labChecked) return;
        labChecked = true;
        var args = System.Environment.GetCommandLineArgs();
        string file = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "-labFile") file = args[i + 1];
            if (args[i] == "-labOut") labOut = args[i + 1];
        }
        if (file == null || labOut == null) return;
        labQueue = new List<LabStrat>();
        foreach (var line in File.ReadAllLines(file))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;
            var s = new LabStrat { raw = t };
            foreach (var part in t.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim(), v = kv[1].Trim();
                if (k == "spells") s.spells.AddRange(v.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
                else if (k == "pass") s.pass.AddRange(v.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
                else if (k == "move") s.move = v;
                else if (k == "diff") int.TryParse(v, out s.diff);
                else if (k == "map") int.TryParse(v, out s.map);
                else if (k == "hero") int.TryParse(v, out s.hero);
                else if (k == "meta") float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s.meta);
                else if (k == "after") int.TryParse(v, out s.after);
                else if (k == "lhp" || k == "ldmg" || k == "lrate" || k == "lfrom")
                {
                    float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f);
                    if (k == "lhp") s.lhp = f; else if (k == "ldmg") s.ldmg = f; else if (k == "lrate") s.lrate = f; else s.lfrom = f;
                }
                else if (k == "free") s.free = v == "1";
            }
            labQueue.Add(s);
        }
        labIndex = 0;
        bot = true;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        Debug.Log($"lab: {labQueue.Count} strategies → {labOut}");
        LabNext();
    }

    // следующая стратегия: выставить уровень, героя, сложность и перезагрузить сцену прямо в забег
    void LabNext()
    {
        if (labIndex >= labQueue.Count) { Debug.Log("lab: done"); lab = null; Application.Quit(); return; }
        lab = labQueue[labIndex++];
        AudioListener.volume = 0; AudioListener.pause = true;
        Debug.Log($"lab: audio volume={AudioListener.volume} pause={AudioListener.pause} sources playing={System.Array.FindAll(FindObjectsByType<AudioSource>(FindObjectsSortMode.None), s => s.isPlaying).Length}");
        selLevel = Mathf.Clamp(lab.map, 0, Levels.Length - 1);
        selHero = Mathf.Clamp(lab.hero, 0, Heroes.Length - 1);
        selDiff = Mathf.Clamp(lab.diff, 0, Diffs.Length - 1);
        skipMenu = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    // каждый кадр: добрать шаги мира до LabSteps, собрать статистику, по концу забега — записать итог
    void LabTick()
    {
        if (!LabOn) return;
        for (int i = 1; i < LabSteps; i++)
        {
            BotTick();
            if (state == State.Play) Step(1 / 30f);
        }
        if (state == State.Play && lab.move != "kite" && labStandFrom < 0 && level >= lab.after) labStandFrom = time;
        if (state == State.Play && time > 60)
        {
            float k = Mathf.Clamp01(player.hp / player.maxHp);
            labHpSum += k; labHpN++;
            labMinHp = Mathf.Min(labMinHp, k);
        }
        if (state == State.Won && lab.free && !freeRun) FreeContinue();
        if ((state == State.Dead || state == State.Won) && !labDone)
        {
            labDone = true;
            var r = new LabResult
            {
                strat = lab.raw, move = lab.move, diff = selDiff, map = selLevel, hero = selHero,
                secs = (int)time, won = state == State.Won || freeRun ? 1 : 0, lvl = level, kills = kills,
                killedBy = state == State.Won ? "" : lastHitBy ?? "", build = SpellSummary().Trim(),
                minHpPct = Mathf.RoundToInt(labMinHp * 100), avgHpPct = labHpN > 0 ? Mathf.RoundToInt(labHpSum / labHpN * 100) : 100,
                standSecs = labStandFrom >= 0 ? (int)(time - labStandFrom) : 0,
                healRegen = Mathf.Round(labRegen), healDrain = Mathf.Round(labDrain), dmgTaken = Mathf.Round(labTaken),
            };
            File.AppendAllText(labOut, JsonUtility.ToJson(r) + "\n");
            Debug.Log($"lab: {labIndex}/{labQueue.Count} {(r.won == 1 ? "WON" : "DEAD")} t={r.secs} lv={r.lvl} {lab.raw}");
            LabNext();
        }
    }

    // выбор карты по стратегии: сначала слияния и заданные заклинания/пассивки, чужое — только когда больше нечего
    int LabPick()
    {
        int pick = 0, best = int.MinValue;
        for (int i = 0; i < choices.Count; i++)
        {
            var s = choices[i];
            int sc;
            int si = lab.spells.IndexOf(s.icon), pi = lab.pass.IndexOf(s.icon);
            if (s.fusion) sc = 900;
            else if (si >= 0) sc = 800 - si * 10 + s.level;
            else if (pi >= 0) sc = 600 - pi * 10 + s.level;
            else if (s.spell) sc = s.level > 0 ? 100 + s.level : (lab.spells.Count == 0 ? 60 : 5);
            else sc = lab.pass.Count == 0 ? 40 - s.level : 20 - s.level;
            if (sc > best) { best = sc; pick = i; }
        }
        return pick;
    }

    Vector2 LabMove()
    {
        if (level < lab.after) return BotMove();
        if (lab.move == "stand") return Vector2.zero;
        if (lab.move == "circle") return new Vector2(Mathf.Cos(time * 0.6f), Mathf.Sin(time * 0.6f));
        return BotMove();
    }
}
