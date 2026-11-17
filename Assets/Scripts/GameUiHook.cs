using UnityEngine;

// Проверка вёрстки: ?ui=<экран> открывает экран сам после загрузки — снимки всех экранов на телефоне, планшете
// и в альбомной без ручных кликов (Playwright). Работает только на localhost — на сайте игнорируется.
// Экраны: town menu meta shop0 shop1 shop2 shop3 leaders ideas coop hero0 hero1 hero2 portal tavern bar pool
//         levelup pause settings stats chest super dead
//         show<карта><герой> (show42) — витрина для скриншотов: заклинания героя на максимуме с суперсилами, 7:10 перед
//         боссом, маг бессмертен и бегает автопилотом
public partial class Game
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern string BrowserUiNext();
#else
    static string BrowserUiNext() => "";
#endif
    static readonly string[] RunScreens = { "levelup", "pause", "settings", "stats", "chest", "super", "dead" };
    static bool uiPoll;   // страница с ?ui= — следующий экран можно задать из JS: window.__msUi = "meta"
    static string uiHook;
    static int uiStage;   // 0 — ждём загрузку, 1 — забег запущен, 2 — готово (переживает перезагрузку сцены)
    static float uiAt;
    static bool showcase;

    void UiHookTick()
    {
        if (uiHook == null)
        {
            // только локально: на сайте ?ui=chest выдал бы бесплатный сундук
            var url = Application.absoluteURL ?? "";
            bool local = url.Contains("://127.0.0.1") || url.Contains("://localhost");
            var m = System.Text.RegularExpressions.Regex.Match(local ? url : "", "[?&]ui=(\\w+)");
            uiHook = m.Success ? m.Groups[1].Value : "";
            uiPoll = m.Success;
            if (uiHook == "") uiStage = 2;
        }
        if (uiPoll)
        {
            string next = BrowserUiNext();
            if (!string.IsNullOrEmpty(next))
            {
                uiHook = next;
                bool inRun = state != State.Town && state != State.Menu;
                if (System.Array.IndexOf(RunScreens, next) >= 0 && inRun) { state = State.Play; pauseTab = 0; uiStage = 1; uiAt = 0; }
                else { menuTab = 0; uiStage = 0; }
            }
        }
        if (uiStage >= 2) return;
        float t = Time.unscaledTime;
        if (uiStage == 0)
        {
            if (t < 6 || (state != State.Town && state != State.Menu)) return;
            bool town = state == State.Town;
            switch (uiHook)
            {
                case "town": if (!town) MenuToTown(); break;
                case "menu": if (town) TownToMenu(); break;
                case "meta": menuTab = 3; break;
                case "shop0": case "shop1": case "shop2": case "shop3": menuTab = 5; shopTab = uiHook[4] - '0'; if (!walletOk) LoadWallet(); if (shopTab == 3) LoadMarket(); break;
                case "leaders": menuTab = 4; LoadLeaders(); break;
                case "ideas": menuTab = 2; ideas = null; LoadIdeas(); break;
                case "coop": menuTab = 6; break;
                case "hero0": case "hero1": case "hero2": menuTab = 7; heroTab = uiHook[4] - '0'; break;
                case "portal": if (town) player.pos = Town.Portals[1]; break;
                case "tavern": if (town) player.pos = Town.Tavern; break;
                case "bar": if (town) player.pos = BarSpot; break;
                case "siege": siegeForce = true; if (town) player.pos = SiegeGate; break;
                case "pool": if (town) player.pos = PoolSpot + new Vector2(-1.8f, 0); break;
                case "replaced": if (town) { Coop.Close(); TownClear(); Town.replaced = true; Town.retryAt = float.MaxValue; } break;
                default:
                    if (uiHook.StartsWith("show") && uiHook.Length >= 6)
                    {
                        if (town) { TownToMenu(); return; }   // из города забег не стартует — сперва в меню
                        showcase = true;
                        selLevel = Mathf.Clamp(uiHook[4] - '0', 0, Levels.Length - 1);
                        selHero = Mathf.Clamp(uiHook[5] - '0', 0, Heroes.Length - 1);
                        // StartRun может перезагрузить сцену под другую карту и после неё не стартовать — пробуем, пока не пошёл забег
                        StartRun();
                        if (state == State.Play) { uiStage = 1; uiAt = t + 3; }
                        return;
                    }
                    StartRun(); uiStage = 1; uiAt = t + 3; return;
            }
            uiStage = 2;
            return;
        }
        if (state != State.Play || t < uiAt) return;
        switch (uiHook)
        {
            case "levelup": OpenLevelUp(); break;
            case "pause": state = State.Pause; pauseTab = 0; break;
            case "settings": state = State.Pause; pauseTab = 1; break;
            case "stats": ToggleStats(); break;
            case "chest": ChestChoices(2); break;
            case "super":
                superSkill = skills.Find(s => s.superNames != null && s.level > 0) ?? skills.Find(s => s.superNames != null);
                if (superSkill != null) { state = State.Super; menuT = t; }
                break;
            case "dead": player.hp = -1; player.revives = 0; break;
            default:
                if (!showcase) break;
                var hero = HeroContent.Get(builtHero);
                int n = 0;
                foreach (var s in skills)
                {
                    if (!s.spell || s.fusion || s.forbidden || !hero.Allows(s) || n >= 5) continue;
                    while (s.level < s.max) LevelSkill(s);
                    if (s.superNames != null) { s.super = Random.Range(0, 3); s.OnSuper(this); }
                    n++;
                }
                player.maxHp = player.hp = 1e6f;
                time = 430;
                bot = true;   // бегает и выбирает карты сам — кадр без окон выбора
                if (uiHook.Length > 6 && uiHook[6] == 'h') { optHigh = true; ApplyOpts(); }   // show40h — высокая графика
                break;
        }
        uiStage = 2;
    }
}
