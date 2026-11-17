using UnityEngine;
using UnityEngine.SceneManagement;

// Стартовый экран: выбор уровня и персонажа. Каталоги — простые списки: новый уровень или герой — одна строка,
// пока контента нет, карточка показывает «СКОРО».
public partial class Game
{
    // item — id покупки в server/wallet.py (null — есть у всех), price — цена в золоте
    public class LevelDef { public string id, title, desc, art, item; public int price; public Color color; public bool soon, hidden; }   // hidden — не в меню и магазине (осада)
    public class HeroDef { public string id, title, desc, icon, item; public int price; public Color color; public bool soon; }

    public static readonly LevelDef[] Levels =
    {
        new() { id = "graveyard", title = "Старое кладбище", desc = "15 минут против нежити", art = "lvl_graveyard", color = new Color(0.55f, 0.65f, 1f) },
        new() { id = "crypt", title = "Склеп", desc = "Подземелье под кладбищем", art = "lvl_crypt", item = "level.crypt", price = 1500, color = new Color(0.5f, 1f, 0.6f) },
        new() { id = "swamp", title = "Гнилые топи", desc = "Болото, где не тонут мёртвые", art = "lvl_swamp", item = "level.swamp", price = 3000, color = new Color(0.6f, 0.9f, 0.4f) },
        new() { id = "frost", title = "Ледяная пустошь", desc = "Снег, лёд и мёрзлая нежить", art = "lvl_frost", item = "level.frost", price = 5000, color = new Color(0.6f, 0.85f, 1f) },
        new() { id = "abyss", title = "Огненный разлом", desc = "Пепел, лава и огненные скелеты", art = "lvl_abyss", item = "level.abyss", price = 8000, color = new Color(1f, 0.5f, 0.3f) },
        // осада города (GameSiege.cs): портал у южных ворот открывается раз в день — только из города, в меню её нет
        new() { id = "siege", title = "Осада города", desc = "Орда прорвалась на площадь", art = "lvl_graveyard", color = new Color(1f, 0.35f, 0.25f), hidden = true },
    };
    public const int PlayLevels = 5;   // уровни меню и магазина; дальше — особые (осада)

    public static readonly HeroDef[] Heroes =
    {
        new() { id = "mage", title = "Маг", desc = "Огонь, молнии и метеоры", icon = "hero_mage", color = new Color(0.75f, 0.55f, 1f) },
        new() { id = "necro", title = "Некромант", desc = "Мёртвые и запретная магия", icon = "hero_necro", item = "hero.necro", price = 2500, color = new Color(0.5f, 1f, 0.55f) },
        new() { id = "frost", title = "Ледяная ведьма", desc = "Лёд, молнии, метеоры", icon = "hero_frost", item = "hero.frost", price = 4000, color = new Color(0.6f, 0.85f, 1f) },
    };

    // Сложность: множители к здоровью, урону, частоте спавна и скорости мобов; выше сложность — больше золота
    public class DiffDef { public string title, desc; public float hp, dmg, rate, speed, gold, elite; public Color color; }

    public static readonly DiffDef[] Diffs =
    {
        new() { title = "Обычная", desc = "Золото ×1", hp = 1, dmg = 1, rate = 1, speed = 1, gold = 1, elite = 60, color = new Color(0.6f, 0.85f, 0.65f) },
        new() { title = "Сложная", desc = "Злее · золото ×1.5", hp = 1.45f, dmg = 1.35f, rate = 1.25f, speed = 1.08f, gold = 1.5f, elite = 45, color = new Color(1f, 0.7f, 0.35f) },
        new() { title = "Кошмар", desc = "Ад · золото ×2.2", hp = 2.1f, dmg = 1.75f, rate = 1.5f, speed = 1.15f, gold = 2.2f, elite = 32, color = new Color(1f, 0.35f, 0.35f) },
    };
    public static int selDiff;
    // режим: 0 — «15 минут» (победа на 15-й минуте), 1 — «Выживание» (без конца, рекорд по времени)
    public static int selMode;
    static readonly string[] ModeTitles = { "15 минут", "Выживание" };
    static readonly string[] ModeDescs = { "Победа и награда", "Без конца · рекорд" };
    static readonly Color[] ModeColors = { new(0.95f, 0.8f, 0.45f), new(0.75f, 0.55f, 1f) };
    static byte DiffByte => (byte)(selDiff | selMode << 4);   // сложность и режим в одном байте сети
    static void SetDiffByte(int b) { selDiff = Mathf.Min(b & 15, Diffs.Length - 1); selMode = (b >> 4) & 1; }

    // ряд «15 минут / Выживание»; возвращает низ ряда
    float ModeRow(float x, float y, float w, bool locked)
    {
        float mw = (w - 12) / 2;
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        for (int i = 0; i < 2; i++)
        {
            var r = new Rect(x + i * (mw + 12), y, mw, 56);
            bool on = selMode == i;
            var c = ModeColors[i];
            Plate(r, on ? new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 0.95f) : new Color(0.05f, 0.04f, 0.07f, 0.9f), on ? c : new Color(1, 1, 1, 0.12f), 10);
            Text(new Rect(r.x, r.y + 2, r.width, 30), Up(ModeTitles[i]), Col(caps, on ? Color.Lerp(c, Color.white, 0.4f) : Soft(0.8f), TextAnchor.MiddleCenter), 2);
            Text(new Rect(r.x, r.y + 27, r.width, 26), ModeDescs[i], Shrink(Col(ds, on ? Bone : Soft(0.6f), TextAnchor.MiddleCenter), ModeDescs[i], r.width - 8), 2);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !locked) { selMode = i; PlayerPrefs.SetInt("sel.mode", i); PlayerPrefs.Save(); }
        }
        return y + 56;
    }
    static DiffDef CurDiff => Diffs[selDiff];

    // переживают перезагрузку сцены: «Начать заново» стартует сразу, «В меню» — на этот экран
    public static int selLevel, selHero;
    static bool heroLoaded;
    bool tilePicked;   // TileRow: игрок нажал доступную карточку (даже уже выбранную)

    // явный выбор героя: сразу на диск — в браузере вкладку закрывают без OnApplicationQuit
    void SaveHeroPick()
    {
        PlayerPrefs.SetInt("sel.hero", selHero);
        PlayerPrefs.Save();
    }
    static bool skipMenu;
    string builtHero;

    void Restart(bool toMenu)
    {
        bool running = state != State.Dead && state != State.Won && state != State.Menu && state != State.Town;
        if (running) SendTelemetry(toMenu ? "menu" : "restart");
        // ушёл посреди забега (в город, в меню, «Начать заново») — забег засчитан как законченный на этом месте:
        // рекорды, золото и строка в таблице лидеров те же, что при гибели
        if (running && time >= 5)
        {
            if (freeRun) FreeEnd();
            else { SaveRecords(false); AwardGold(false); }
        }
        if (coopRun && toMenu) CoopLeaveRun();
        // «Ещё раз» в комнате с другом — новый общий забег
        if (!toMenu && Coop.IsHost && Coop.peer) { CoopBegin(); return; }
        skipMenu = !toMenu;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void StartRun()
    {
        if (LoginGate) return;   // без аккаунта забег не засчитается — сперва вход (GameGate.cs)
        // в комнате с другом: хозяин запускает общий забег, гость ждёт его
        if (!coopRun && Coop.InRoom && Coop.peer)
        {
            if (Coop.IsHost) CoopBegin();
            return;
        }
        // купленное могло пропасть (другой аккаунт) — тогда стартуем на бесплатном; уровень в кооперативе — хозяйский
        if (!coopRun && (Levels[selLevel].soon || !Owns(Levels[selLevel].item))) selLevel = 0;
        if (!coopRun && Levels[selLevel].hidden && !SiegeOpen) selLevel = 0;   // «Ещё раз» после осады, когда окно уже закрыто
        if (Heroes[selHero].soon || !Owns(Heroes[selHero].item)) selHero = 0;
        // мир строится в Awake: другой уровень — перезагрузка сцены сразу в забег (заодно музыка этого уровня)
        if (Levels[selLevel].id != World.Level?.id) { skipMenu = true; SceneManager.LoadScene(SceneManager.GetActiveScene().name); return; }
        state = State.Play;
        ArmFeather();
        DrinkUp();   // напитки бармена — бонус на этот забег (GameTavern.cs)
        FallenAsk();
        survival = selMode == 1 && !bot && !LabOn;
        if (survival) { nextFreeBoss = RunLength + 90; nextFreeRing = RunLength + 45; }
        string goal = survival ? "Выживание: сколько продержишься?" : coopRun ? "Продержитесь вместе 15 минут" : "Продержись 15 минут";
        if (drunk.Count > 0) goal += " · выпито: " + string.Join(", ", drunk);
        Banner(Levels[selLevel].title, goal);
        if (coopRun) CoopRunStart();
    }

    void DrawMenu(float W, float H)
    {
        WarmTextures();
        if (!(menuTab == 7 && heroTab <= 1)) PortraitOff();   // живая модель — в «Снаряжении» и «Облике»
        // Esc — как «Назад» на любом экране меню (идея игрока №15); окно покупки и карточку вещи закрывают свои кнопки
        var esc = Event.current;
        if (menuTab != 0 && esc.type == EventType.KeyDown && esc.keyCode == KeyCode.Escape && confirm == null && gearPick == null)
        {
            esc.Use();
            if (menuTab == 7) MarkGearSeen();
            menuTab = menuTab == 5 ? shopBack : 0;
            shopBack = 0; buyStatus = null; ideaStatus = null;
            return;
        }
        if (menuTab == 0 && state == State.Town) return;
        if (menuTab == 2) { DrawIdeas(W, H); return; }
        if (menuTab == 4) { DrawLeaders(W, H); return; }
        if (menuTab == 3) { DrawMeta(W, H); return; }
        if (menuTab == 5) { DrawShop(W, H); return; }
        if (menuTab == 6) { DrawCoop(W, H); return; }
        if (menuTab == 7) { DrawTownHero(W, H); return; }
        // окно покупки поверх: в IMGUI клик достаётся контролу, нарисованному раньше, — гасим всё под окном
        GUI.enabled = confirm == null;
        // мир за меню виден: маг стоит на кладбище, сверху тёмная вуаль
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.72f));
        bool port = W < H;
        FallenNewsTick(W);
        float t = Time.unscaledTime;

        Outline(new Rect(0, port ? 70 : 40, W, 100), "Magic Survivors", Col(big, new Color(1f, 0.88f, 0.62f)), 3);
        Divider(W / 2, port ? 172 : 142, Mathf.Min(W * 0.6f, 640), Gold);
        var rec = Coop.InRoom ? null : RecordsLine();
        CoopMenuChip(W, port ? 180 : 148);
        if (rec != null) Text(new Rect(0, port ? 180 : 148, W, 30), rec, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(0.85f, 0.78f, 0.6f), TextAnchor.MiddleCenter), 2);

        float y = port ? 210 : 172;
        // телефон пониже (375×600) — карточки ужимаются, чтобы «Играть» не легла поверх героев и режима
        bool wrap = TileWrap(PlayLevels);
        float fixedH = 740 + (wrap ? 18 : 0), tilesH = (wrap ? 2 : 1) * 196 + 250;
        float k = port ? Mathf.Clamp((H - fixedH) / tilesH, 0.55f, 1f) : 1f;
        // уровень у гостя выбирает хозяин
        int keepLevel = selLevel;
        // телефон в альбомной: высоты на всё в столбик нет — слева уровень и герой, справа режим, сложность и кнопки
        bool wide = PhoneLand;
        float leftW = wide ? Mathf.Round(W * 0.6f) : W, colW = W;
        if (wide) GUI.BeginGroup(new Rect(0, 0, leftW, H));
        y = TileRow(leftW, y, "Уровень", PlayLevels, port ? 196 * k : wide ? 150 : 164, i => Levels[i].title, i => Levels[i].desc,
            i => Levels[i].color, i => Levels[i].soon, i => Owns(Levels[i].item) ? null : ShopList(0)[i], ref selLevel, (r, i, dim) =>
            {
                var art = PanelT(Levels[i].art);
                if (art != null) GUI.DrawTexture(r, art, ScaleMode.ScaleAndCrop, true, 0, dim ? new Color(0.35f, 0.35f, 0.38f) : Color.white, 0, 14);
                else RR(r, new Color(Levels[i].color.r * 0.15f, Levels[i].color.g * 0.15f, Levels[i].color.b * 0.15f, 0.95f), 14);
                // низ карточки темнее, чтобы читалось название
                for (int k = 0; k < 6; k++)
                    RR(new Rect(r.x, r.y + r.height * (0.45f + k * 0.09f), r.width, r.height * 0.1f), new Color(0, 0, 0, 0.12f + k * 0.08f), k == 5 ? 14 : 0);
            });

        if (Coop.IsGuest && Coop.peer) selLevel = keepLevel;
        tilePicked = false;
        y = TileRow(leftW, y + (wide ? 12 : 24), "Персонаж", Heroes.Length, port ? 250 * k : wide ? 210 : 200, i => Heroes[i].title, i => Heroes[i].desc,
            i => Heroes[i].color, i => Heroes[i].soon, i => Owns(Heroes[i].item) ? null : ShopList(1)[i], ref selHero, (r, i, dim) =>
            {
                RR(r, new Color(0.05f, 0.04f, 0.07f, 0.92f), 14);
                // медальон шире иконки в 1.5 раза: размер считаем так, чтобы он не наезжал на имя
                float s = Mathf.Min(r.width * 0.4f, (r.height - 118) / 1.52f);
                var ir = new Rect(r.center.x - s / 2, r.y + 38 + s * 0.26f, s, s);
                GUI.color = dim ? new Color(0.4f, 0.4f, 0.45f) : Color.white;
                IconTex(ir, Heroes[i].icon, dim ? new Color(0.35f, 0.35f, 0.4f) : Heroes[i].color);
                GUI.color = Color.white;
            });
        if (tilePicked) SaveHeroPick();   // явный выбор игрока — запоминаем

        // режим и сложность — два ряда плашек
        if (wide)
        {
            GUI.EndGroup();
            colW = W - leftW;
            GUI.BeginGroup(new Rect(leftW, 0, colW, H));
            y = (port ? 210 : 172) + 26;
        }
        float bw = Mathf.Min(520, colW - (wide ? 40 : 80));
        float dw = Mathf.Min(260, (colW - 48 - 24) / 3), dx = (colW - dw * 3 - 24) / 2;
        y = ModeRow(dx, y + 18, dw * 3 + 24, Coop.IsGuest && Coop.peer);
        float dy = y + 10;
        for (int i = 0; i < Diffs.Length; i++)
        {
            var d = Diffs[i];
            var r = new Rect(dx + i * (dw + 12), dy, dw, 58);
            bool on = selDiff == i;
            Plate(r, on ? new Color(d.color.r * 0.3f, d.color.g * 0.3f, d.color.b * 0.3f, 0.95f) : new Color(0.05f, 0.04f, 0.07f, 0.9f), on ? d.color : new Color(1, 1, 1, 0.12f), 10);
            Text(new Rect(r.x, r.y + 2, r.width, 32), Up(d.title), Col(caps, on ? Color.Lerp(d.color, Color.white, 0.4f) : Soft(0.8f), TextAnchor.MiddleCenter), 2);
            Text(new Rect(r.x, r.y + 28, r.width, 26), d.desc, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, on ? Bone : Soft(0.6f), TextAnchor.MiddleCenter), 2);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !(Coop.IsGuest && Coop.peer)) selDiff = i;
        }
        y = dy + 58;
        var br = new Rect((colW - bw) / 2, port || wide ? y + 18 : Mathf.Min(y + 18, H - 250), bw, 80);
        // кнопка дышит, чтобы было понятно, куда жать
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3);
        GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.12f + 0.1f * pulse);
        GUI.DrawTexture(Grow(br, 0.25f), Gen.Glow().texture);
        GUI.color = Color.white;
        bool guestWait = Coop.IsGuest && Coop.peer;
        string play = guestWait ? (Coop.mateInRun ? "Хозяин в забеге…" : "Ждём хозяина…") : Coop.IsHost && Coop.peer ? "Играть вместе" : "Играть";
        if (Btn(br, play, Gold) && confirm == null && !guestWait) StartRun();

        // идеи игроков: предложить своё и проголосовать за чужое
        // прокачка и «Вместе» делят одну строку
        float cw = Mathf.Round(bw * 0.36f);
        var mr = new Rect(br.x, br.yMax + 14, bw - cw - 16, 68);
        if (Btn(new Rect(mr.xMax + 16, mr.y, cw, 68), "Вместе", new Color(0.6f, 1f, 0.7f)) && confirm == null) { menuTab = 6; Coop.error = null; }
        if (Btn(mr, "", new Color(1f, 0.82f, 0.35f)) && confirm == null) { menuTab = 3; buyStatus = null; }
        string metaLabel = port || wide ? $"Прокачка  <color=#FFD45E>· {Coins}</color>" : $"Прокачка героя  <color=#FFD45E>· {Coins} золота</color>";
        Text(mr, metaLabel, Shrink(Col(small, Bone, TextAnchor.MiddleCenter), metaLabel, mr.width - 24), 2);
        float iw = (bw - 48) / 4, iy = mr.yMax + 14;
        if (Btn(new Rect(br.x, iy, iw, 66), "Город", new Color(0.6f, 1f, 0.7f)) && confirm == null && !Coop.InRoom) { if (wide) GUI.EndGroup(); MenuToTown(); return; }
        if (Btn(new Rect(br.x + iw + 16, iy, iw, 66), "Магазин", Gold) && confirm == null) { menuTab = 5; buyStatus = null; if (!walletOk) LoadWallet(); }
        if (Btn(new Rect(br.x + (iw + 16) * 2, iy, iw, 66), "Лидеры", Bone) && confirm == null) { menuTab = 4; LoadLeaders(); }
        if (Btn(new Rect(br.x + (iw + 16) * 3, iy, iw, 66), "Идеи", Bone) && confirm == null) { menuTab = 2; ideaStatus = null; ideas = null; LoadIdeas(); }
        if (wide) GUI.EndGroup();
        // мир и герой строятся в Awake: выбрали другой — пересобираем сцену, меню остаётся
        if (confirm == null && (Levels[selLevel].id != World.Level?.id || Heroes[selHero].id != builtHero)
            && Owns(Levels[selLevel].item) && Owns(Heroes[selHero].item))
        { SceneManager.LoadScene(SceneManager.GetActiveScene().name); return; }
        // вход через хаб — в правом верхнем углу, чтобы не спорить с выбором уровня
        float aw = port ? 300 : 360;
        DrawAccountButton(new Rect(W - aw - 16, port ? 12 : 16, aw, port ? 50 : 56));
        // слева вверху: звук одной кнопкой и ник — нажми, чтобы сменить
        float th = port ? 50 : 56, ty = port ? 12 : 16;
        MuteButton(new Rect(16, ty, th + 8, th));
        DrawNickButton(new Rect(16 + th + 16, ty, Mathf.Min(320, W - aw - 16 - (16 + th + 16) - 12), th));
        GUI.enabled = true;
        DrawConfirm(W, H);
    }

    // перенос во второй ряд — только на вытянутом телефоне; на планшете в портрете места по высоте нет, ряд один
    static bool TileWrap(int n) => Portrait && n > 3 && Screen.height > Screen.width * 1.5f;

    // Ряд карточек с подписью: выбранная — золотая кромка, «скоро» — притушена и не выбирается
    float TileRow(float W, float y, string label, int n, float h, System.Func<int, string> title, System.Func<int, string> desc,
        System.Func<int, Color> color, System.Func<int, bool> soon, System.Func<int, ShopItem> buy, ref int sel, System.Action<Rect, int, bool> body)
    {
        Text(new Rect(0, y, W, 36), Up(label), Col(caps, Soft(0.8f), TextAnchor.MiddleCenter), 2);
        y += 44;
        // на телефоне больше трёх карточек в ряд не помещаются — переносим: 3 + остаток по центру
        int per = TileWrap(n) ? 3 : n, rows = (n + per - 1) / per;
        float gap = 18, tw = Mathf.Min(360, (W - 48 - gap * (per - 1)) / per);
        for (int i = 0; i < n; i++)
        {
            int row = i / per, inRow = Mathf.Min(per, n - row * per);
            float x0 = (W - (tw * inRow + gap * (inRow - 1))) / 2;
            var r = new Rect(x0 + (i % per) * (tw + gap), y + row * (h + gap), tw, h);
            var shop = soon(i) ? null : buy(i);
            bool locked = soon(i) || shop != null, on = sel == i, hov = !locked && r.Contains(Event.current.mousePosition);
            var c = color(i);
            Rect(new Rect(r.x + 6, r.y + 10, r.width, r.height), new Color(0, 0, 0, 0.45f));
            body(r, i, locked);
            if (on)
            {
                GUI.color = new Color(c.r, c.g, c.b, 0.25f);
                GUI.DrawTexture(Grow(r, 0.08f), Gen.Glow().texture);
                GUI.color = Color.white;
            }
            RRLine(r, on ? Gold : hov ? new Color(Gold.r, Gold.g, Gold.b, 0.6f) : new Color(1, 1, 1, 0.12f), on ? 2 : 1, 14);
            var tc = locked ? new Color(0.72f, 0.69f, 0.64f) : Color.Lerp(c, Bone, 0.6f);   // закрытое — тусклее, но читается
            // на телефоне карточки узкие: описание идёт в две строки, название ужимается по ширине
            // низкая карточка (планшет в портрете, ужатое меню): без описания — иначе плашка цены закрывает название
            float dh = h < 170 ? 0 : Portrait ? 58 : 34;
            // узкая карточка (планшет в портрете, пять в ряд) — название мельче обычного минимума, но целиком
            var ts = tw < 170 ? Shrink(Col(titleS, tc), title(i), r.width - 16, 11) : Fit(title(i), r.width - 16, tc);
            Outline(new Rect(r.x + 8, r.yMax - 42 - dh, r.width - 16, 40), title(i), ts, 2);
            var ds = Portrait ? (tinyW ??= new GUIStyle(tiny) { fontSize = 20, wordWrap = true }) : (tinyS ??= new GUIStyle(tiny) { fontSize = 21 });
            if (dh > 0) Text(new Rect(r.x + 8, r.yMax - dh - 4, r.width - 16, dh), desc(i), Col(ds, locked ? new Color(0.62f, 0.6f, 0.57f) : new Color(0.78f, 0.75f, 0.7f), TextAnchor.UpperCenter), 2);
            if (shop != null)
            {
                ChipAt(r.center.x, r.y + 12, $"{shop.price} ЗОЛОТА", MetaGold, walletOk && walletGold >= shop.price);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && confirm == null) { confirm = shop; buyStatus = null; }
            }
            else if (locked) ChipAt(r.center.x, r.y + 12, "СКОРО", new Color(0.6f, 0.58f, 0.55f));
            else if (on) ChipAt(r.center.x, r.y + 12, "ВЫБРАНО", Gold, true);
            if (!locked && GUI.Button(r, GUIContent.none, GUIStyle.none) && confirm == null) { sel = i; tilePicked = true; }
        }
        return y + rows * h + (rows - 1) * gap;
    }
}
