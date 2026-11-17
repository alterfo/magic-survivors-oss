using System.Collections.Generic;
using UnityEngine;

// Снаряжение (как в L2): семь слотов, грейды от обычного до S, заточка до +16, у оружия 20 видов (GameWeapons.cs). Вещи живут на сервере (server/gear.py),
// выпадают по итогам забега; статы считаются здесь по тем же таблицам и ложатся на героя поверх постоянной прокачки.
// Окно персонажа — экран «Герой» в городе: живая модель мага (отдельная камера в RenderTexture), слоты вокруг,
// статы, сумка; по вещи — окно с «Надеть», «Заточить», «Разобрать».
public partial class Game
{
    [System.Serializable] public class GearItem { public int id; public string slot, affix; public int grade, enchant, equipped, kind; }
    [System.Serializable] class GearResp { public bool ok; public int gold, wrev; public GearItem[] items; public string error; public bool enchanted; public int enchant, salvaged; }
    [System.Serializable] class GearPost { public string voter; public int id, price = -1; }   // price — показанная цена заточки

    public class GearSlot { public string id, title, stat, icon; public float main; public string[] names; }

    // main — основной стат на грейде D; дальше × GradeMul и × (1 + 12% за ступень заточки)
    public static readonly GearSlot[] GearSlots =
    {
        new() { id = "weapon", title = "Посох", stat = "dmg", main = 6, icon = "gear_weapon",
            names = new[] { "Посох ученика", "Посох адепта", "Посох чародея", "Посох архимага", "Посох бури", "Посох Бездны" } },
        new() { id = "helm", title = "Шлем", stat = "hp", main = 15, icon = "gear_helm",
            names = new[] { "Кожаный капюшон", "Капюшон адепта", "Шлем чародея", "Венец архимага", "Шлем бури", "Корона Бездны" } },
        new() { id = "robe", title = "Мантия", stat = "armor", main = 2.5f, icon = "gear_robe",
            names = new[] { "Холщовая мантия", "Мантия адепта", "Мантия чародея", "Мантия архимага", "Мантия бури", "Мантия Бездны" } },
        new() { id = "gloves", title = "Перчатки", stat = "cd", main = 3, icon = "gear_gloves",
            names = new[] { "Тряпичные перчатки", "Перчатки адепта", "Перчатки чародея", "Перчатки архимага", "Перчатки бури", "Длани Бездны" } },
        new() { id = "boots", title = "Сапоги", stat = "speed", main = 3, icon = "gear_boots",
            names = new[] { "Стоптанные сапоги", "Сапоги адепта", "Сапоги чародея", "Сапоги архимага", "Сапоги бури", "Поступь Бездны" } },
        new() { id = "ring", title = "Кольцо", stat = "crit", main = 1.5f, icon = "gear_ring",
            names = new[] { "Медное кольцо", "Кольцо адепта", "Кольцо чародея", "Кольцо архимага", "Кольцо бури", "Перстень Бездны" } },
        new() { id = "amulet", title = "Амулет", stat = "area", main = 4, icon = "gear_amulet",
            names = new[] { "Деревянный амулет", "Амулет адепта", "Амулет чародея", "Амулет архимага", "Амулет бури", "Око Бездны" } },
    };

    static readonly string[] GradeNames = { "Обычный", "D", "C", "B", "A", "S" };
    static readonly float[] GradeMul = { 0.6f, 1f, 1.6f, 2.3f, 3.2f, 4.5f };
    static readonly Color[] GradeColors =
    {
        new(0.72f, 0.7f, 0.68f), new(0.45f, 0.9f, 0.45f), new(0.4f, 0.65f, 1f),
        new(0.78f, 0.48f, 1f), new(1f, 0.62f, 0.22f), new(1f, 0.32f, 0.3f),
    };
    // как в server/gear.py
    static readonly int[] EnchantPrice = { 20, 40, 80, 150, 300, 600 };
    static readonly int[] SalvageGold = { 5, 15, 40, 100, 250, 600 };
    const int MaxEnchant = 16, SafeEnchant = 3;   // +16 с 15.11 (было +10)

    // доп. свойство (у вещей от грейда D): значение на грейде D, дальше × GradeMul
    static readonly Dictionary<string, float> AffixUnit = new()
    {
        ["dmg"] = 3, ["hp"] = 8, ["regen"] = 0.15f, ["crit"] = 1, ["area"] = 2, ["magnet"] = 6, ["xp"] = 3, ["gold"] = 4, ["speed"] = 1.5f, ["cd"] = 1.5f,
    };
    static readonly Dictionary<string, string> StatTitle = new()
    {
        ["dmg"] = "урон", ["hp"] = "здоровье", ["armor"] = "броня", ["cd"] = "скорость чар", ["speed"] = "бег", ["crit"] = "шанс крита",
        ["area"] = "площадь", ["regen"] = "регенерация", ["magnet"] = "радиус сбора", ["xp"] = "опыт", ["gold"] = "золото",
    };

    static GearSlot SlotOf(string id) => System.Array.Find(GearSlots, s => s.id == id);
    static float EnchantChance(int e) => e < SafeEnchant ? 1f : Mathf.Max(0.25f, 0.75f - 0.07f * (e - SafeEnchant));
    static float MainValue(GearItem it) => SlotOf(it.slot).main * GradeMul[it.grade] * (1 + 0.12f * it.enchant);
    static float AffixValue(GearItem it) => string.IsNullOrEmpty(it.affix) || !AffixUnit.ContainsKey(it.affix) ? 0 : AffixUnit[it.affix] * GradeMul[it.grade];
    static string GearName(GearItem it) => (it.enchant > 0 ? $"+{it.enchant} " : "") + (IsWeapon(it) ? WeaponName(it) : SlotOf(it.slot).names[it.grade]);

    static string StatLine(string stat, float v) => stat switch
    {
        "hp" => $"+{v:0} {StatTitle[stat]}",
        "regen" => $"+{v:0.##} {StatTitle[stat]}/с",
        _ => $"+{v:0.#}% {StatTitle[stat]}",
    };

    // ---------- состояние ----------

    static readonly List<GearItem> gear = new();
    static bool gearOk;
    static List<GearItem> runDrops;   // добыча последнего забега — для экрана конца забега
    static int runSold;

    static bool GearValid(GearItem it) => it != null && SlotOf(it.slot) != null && it.grade >= 0 && it.grade < GradeNames.Length;

    // поколение профиля: выход и вход его меняют, ответы, отправленные от прежнего профиля, отбрасываются
    static int gearGen;
    // ревизия: каждое действие с вещами её сдвигает, и загрузка, начатая до действия, приходит устаревшей — её отбрасываем
    static int gearRev;
    bool gearLoadLater;   // поля сцены: запросы сцены умирают вместе с ней

    void LoadGear()
    {
        if (bot) return;
        // пока идёт действие с вещью — откладываем до его ответа (там и загрузим: ответ прежнего профиля может быть отброшен)
        if (gearBusy) { gearLoadLater = true; return; }
        int gen = gearGen, rev = gearRev, wgen = walletGen;
        StartCoroutine(Send("POST", "gear", JsonUtility.ToJson(new GearPost { voter = Voter() }), (code, body) =>
        {
            // баланс берём только из ответов действий: при первом переносе золота /gear мог прочитать ещё пустой кошелёк
            if (code == 200 && rev == gearRev) ApplyGearResp(body, gen, wgen, false);
        }));
    }

    // смена профиля из сцены: занятость действием прежнего профиля снимается — его ответ будет отброшен
    void ResetProfileGear()
    {
        ResetGear();
        gearBusy = false;
        gearLoadLater = false;
    }

    static void ResetGear()
    {
        gearGen++;
        gear.Clear();
        gearOk = false;
    }

    GearResp ApplyGearResp(string body, int gen, int wgen, bool gold = true)
    {
        if (gen != gearGen) return null;
        var r = JsonUtility.FromJson<GearResp>(body);
        if (r == null || !r.ok) return r;
        var was = WeaponInfo();   // до очистки: иначе снятое оружие не замечалось бы
        gear.Clear();
        foreach (var it in r.items ?? new GearItem[0]) if (GearValid(it)) gear.Add(it);
        gearOk = true;
        // оружие сменилось или заточилось — новая модель и свечение; в городе перезаходим, чтобы увидели и другие
        if (WeaponInfo() != was) { ApplyMyWeapon(); if (state == State.Town) TownRejoin(); }
        if (gold) SetGold(r.gold, r.wrev, wgen);
        RefreshGearStats();
        return r;
    }

    // ---------- статы ----------

    // что сейчас наложено на героя: снимаем ровно это перед тем, как наложить новое
    readonly Dictionary<string, float> gearOn = new();

    static Dictionary<string, float> GearTotals()
    {
        var t = new Dictionary<string, float>();
        foreach (var it in gear)
        {
            if (it.equipped == 0) continue;
            var s = SlotOf(it.slot);
            t[s.stat] = t.GetValueOrDefault(s.stat) + MainValue(it);
            float a = AffixValue(it);
            if (a > 0) t[it.affix] = t.GetValueOrDefault(it.affix) + a;
            float wb = WeaponBonus(it);
            if (wb > 0) t[KindOf(it).stat] = t.GetValueOrDefault(KindOf(it).stat) + wb;
        }
        return t;
    }

    static void ApplyGearTotals(Player p, Dictionary<string, float> t, int sign)
    {
        foreach (var kv in t)
        {
            float v = kv.Value * sign, k = kv.Value / 100f;
            switch (kv.Key)
            {
                case "dmg": p.dmgMul += v / 100f; break;
                case "hp": p.maxHp += v; p.hp = Mathf.Min(p.hp + Mathf.Max(0, v), p.maxHp); break;
                case "armor": p.armor += v / 100f; break;
                case "cd": float m = 1 - Mathf.Min(0.5f, k); p.cdMul = sign > 0 ? p.cdMul * m : p.cdMul / m; break;
                case "speed": p.speed = sign > 0 ? p.speed * (1 + k) : p.speed / (1 + k); break;
                case "magnet": p.magnet = sign > 0 ? p.magnet * (1 + k) : p.magnet / (1 + k); break;
                case "crit": p.crit += v / 100f; break;
                case "area": p.area += v / 100f; break;
                case "regen": p.regen += v; break;
                case "xp": p.xpMul += v / 100f; break;
                case "gold": p.goldMul += v / 100f; break;
            }
        }
    }

    // Снаряжение ложится на героя вне забега: при постройке сцены (initial, из Awake) и в городе/меню
    // (сцена строится заново перед каждым забегом, так что в забег герой входит уже одетым).
    // Посреди забега пришедший ответ статы не меняет — ни первый, ни повторный.
    void RefreshGearStats(bool initial = false)
    {
        if (player == null || bot || LabOn) return;
        if (!initial && state != State.Town && state != State.Menu) return;
        ApplyGearTotals(player, gearOn, -1);
        gearOn.Clear();
        foreach (var kv in GearTotals()) gearOn[kv.Key] = kv.Value;
        ApplyGearTotals(player, gearOn, 1);
        player.hp = player.maxHp;
    }

    // сила снаряжения: грейд и заточка надетого — для подписи в окне персонажа
    static int GearScore()
    {
        int s = 0;
        foreach (var it in gear) if (it.equipped != 0) s += (it.grade + 1) * 10 + it.enchant * (it.grade + 2);
        return s;
    }

    static bool NewGear() => gearOk && gear.Exists(it => it.id > PlayerPrefs.GetInt("gear.seen"));

    static void MarkGearSeen()
    {
        int max = PlayerPrefs.GetInt("gear.seen");
        foreach (var it in gear) max = Mathf.Max(max, it.id);
        if (max == PlayerPrefs.GetInt("gear.seen")) return;
        PlayerPrefs.SetInt("gear.seen", max);
        PlayerPrefs.Save();
    }

    // ---------- действия ----------

    bool gearBusy;
    string gearStatus;
    GearItem gearPick;   // открытое окно вещи

    void GearAction(string what, GearItem it)
    {
        if (gearBusy || it == null) return;
        // баланс ответа действия принимается только поверх загруженного кошелька — без него ждём загрузку
        if (!walletOk) { gearStatus = "Загружаю кошелёк — попробуй ещё раз"; LoadWallet(); return; }
        gearBusy = true;
        gearStatus = null;
        gearRev++;
        int id = it.id, gen = gearGen, wgen = walletGen;
        StartCoroutine(Send("POST", "gear/" + what, JsonUtility.ToJson(new GearPost { voter = Voter(), id = id, price = what == "enchant" ? Sale(EnchantPrice[it.grade] * (it.enchant + 1)) : -1 }), (code, body) =>
        {
            if (gen != gearGen) return;   // профиль сменился: ResetProfileGear уже снял занятость, ответ чужой
            gearBusy = false;
            gearRev++;   // загрузки, начатые до ответа действия, устарели
            if (gearLoadLater) { gearLoadLater = false; LoadGear(); }
            if (code != 200)
            {
                gearStatus = code == 402 ? "Не хватает золота" : code == 0 ? "Нет связи с сервером" : JsonUtility.FromJson<GearResp>(body)?.error ?? "Ошибка";
                if (code == 409) worldAt = 0;   // цена заточки сменилась (скидка дня) — освежим её
                return;
            }
            var r = ApplyGearResp(body, gen, wgen);
            if (r == null) return;
            gearPick = gear.Find(g => g.id == id);
            if (what == "enchant") gearStatus = r.enchanted ? $"Заточка удалась: +{r.enchant}" : $"Заточка сорвалась — вещь откатилась до +{r.enchant}";
            else if (what == "salvage") gearStatus = $"Разобрано: +{r.salvaged} золота";
        }));
    }

    // ---------- портрет: живая модель в RenderTexture ----------

    const int PortraitLayer = 30;
    Camera portraitCam;
    RenderTexture portraitRT;
    float portraitYaw, portraitBase;
    bool portraitOn;

    void PortraitTick()
    {
        if (player?.go == null) return;
        if (!portraitOn)
        {
            portraitOn = true;
            portraitYaw = 0;
            portraitBase = player.tr.eulerAngles.y;
        }
        if (portraitRT == null) portraitRT = new RenderTexture(512, 768, 24) { antiAliasing = 2, name = "Portrait" };
        if (portraitCam == null)
        {
            portraitCam = new GameObject("PortraitCam").AddComponent<Camera>();
            portraitCam.clearFlags = CameraClearFlags.SolidColor;
            portraitCam.backgroundColor = new Color(0.035f, 0.025f, 0.05f, 1);
            portraitCam.cullingMask = 1 << PortraitLayer;
            portraitCam.fieldOfView = 26;
            portraitCam.nearClipPlane = 0.1f;
            portraitCam.farClipPlane = 30;
            portraitCam.targetTexture = portraitRT;
        }
        portraitCam.enabled = true;
        // маг на своём слое — его видит только эта камера; новые части (переодевание) тоже переводим
        foreach (var t in player.go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PortraitLayer;
        var c = player.tr.position + Vector3.up * 0.85f;
        var dir = Quaternion.Euler(0, portraitBase + portraitYaw, 0) * Vector3.forward;
        portraitCam.transform.position = c + dir * 7.2f + Vector3.up * 0.6f;
        portraitCam.transform.LookAt(c);
    }

    void PortraitOff()
    {
        if (!portraitOn) return;
        portraitOn = false;
        if (portraitCam != null) portraitCam.enabled = false;
        if (player?.go != null) foreach (var t in player.go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
    }

    // ---------- окно персонажа ----------

    int heroTab;          // 0 — снаряжение, 1 — облик (GameLook.cs), 2 — выбор героя
    float bagScroll;
    bool portraitDrag;
    float portraitDragX;

    void DrawCharacter(float W, float H)
    {
        bool port = W < H;
        float top = port ? 116 : 100, bottom = H - 130;
        if (Event.current.type == EventType.Repaint) PortraitTick();
        bool modal = gearPick != null || confirm != null || marketSell != null;
        GUI.enabled = !modal;

        float slot = port ? 86 : Mathf.Min(104, (bottom - top - 3 * 14) / 4);
        float dollW = port ? W - 32 : Mathf.Min(640, W * 0.38f), dollH = port ? slot * 4 + 3 * 12 : bottom - top;
        float dx = port ? 16 : Mathf.Max(24, W * 0.04f);
        var doll = new Rect(dx, top, dollW, dollH);
        // портрет между колонками слотов
        var pr = new Rect(doll.x + slot + 14, doll.y, doll.width - 2 * (slot + 14), doll.height);
        Plate(pr, new Color(0.03f, 0.02f, 0.045f, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 0.4f), 14);
        if (portraitRT != null) GUI.DrawTexture(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), portraitRT, ScaleMode.ScaleAndCrop, false, 0, Color.white, 0, 12);
        string gs = $"СИЛА СНАРЯЖЕНИЯ  {GearScore()}";
        Text(new Rect(pr.x, pr.yMax - 44, pr.width, 34), gs, Shrink(Col(caps, MetaGold, TextAnchor.MiddleCenter), gs, pr.width - 10), 2);
        // имя на тёмной полосе — иначе на телефоне оно ложится прямо на шляпу
        RR(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, 44), new Color(0.02f, 0.015f, 0.03f, 0.7f), 12);
        Text(new Rect(pr.x, pr.y + 6, pr.width, 34), Nick, Shrink(Col(small, Bone, TextAnchor.MiddleCenter), Nick, pr.width - 10), 2);
        // крутить модель — тянуть по портрету
        var ev = Event.current;
        if (!modal && ev.type == EventType.MouseDown && pr.Contains(ev.mousePosition)) { portraitDrag = true; portraitDragX = ev.mousePosition.x; }
        if (portraitDrag && ev.type == EventType.MouseDrag) { portraitYaw += (ev.mousePosition.x - portraitDragX) * 0.6f; portraitDragX = ev.mousePosition.x; }
        if (ev.type == EventType.MouseUp) portraitDrag = false;
        if (!portraitDrag && Event.current.type == EventType.Repaint) portraitYaw += Time.unscaledDeltaTime * 8;

        for (int i = 0; i < GearSlots.Length; i++)
        {
            bool left = i < 4;
            int row = left ? i : i - 4;
            float sx = left ? doll.x : doll.xMax - slot, sy = doll.y + row * (slot + (port ? 12 : 14));
            DrawGearSlot(new Rect(sx, sy, slot, slot), GearSlots[i], gear.Find(g => g.equipped != 0 && g.slot == GearSlots[i].id));
        }

        // статы
        Rect statsR, bagR;
        if (port)
        {
            statsR = new Rect(16, doll.yMax + 14, W - 32, 6 * 34 + 20);   // 11 строк в два столбца — шесть рядов
            bagR = new Rect(16, statsR.yMax + 14, W - 32, bottom - statsR.yMax - 14);
        }
        else
        {
            float sw = Mathf.Min(400, W * 0.24f);
            statsR = new Rect(doll.xMax + 24, top, sw, bottom - top);
            bagR = new Rect(statsR.xMax + 24, top, W - statsR.xMax - 24 - dx, bottom - top);
        }
        DrawHeroStats(statsR, port);
        DrawBag(bagR);

        float bw = Mathf.Min(560, W - 48);
        if (gearStatus != null && gearPick == null) Text(new Rect(0, H - 150, W, 36), gearStatus, Col(small, new Color(1f, 0.8f, 0.5f), TextAnchor.MiddleCenter), 2);
        if (Btn(new Rect((W - bw) / 2, H - 112, bw, 86), "Назад в город", Bone) && !modal) { menuTab = 0; MarkGearSeen(); }
        GUI.enabled = true;
        DrawGearPick(W, H);
        DrawSell(W, H);
    }

    void DrawGearSlot(Rect r, GearSlot s, GearItem it)
    {
        var c = it != null ? GradeColors[it.grade] : new Color(1, 1, 1, 0.14f);
        Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.94f), it != null ? c : new Color(1, 1, 1, 0.14f), 12, it != null ? 2 : 1);
        var ir = new Rect(r.x + r.width * 0.12f, r.y + r.height * 0.08f, r.width * 0.76f, r.width * 0.76f);
        GUI.DrawTexture(ir, IconT(it != null ? ItemIcon(it) : s.icon), ScaleMode.ScaleToFit, true, 0, it != null ? Color.white : new Color(0.35f, 0.33f, 0.38f, 0.8f), 0, 10);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        if (it == null) Text(new Rect(r.x, r.yMax - 28, r.width, 26), s.title, Shrink(Col(ds, Soft(0.8f), TextAnchor.MiddleCenter), s.title, r.width - 6), 2);
        else
        {
            string g = it.enchant > 0 ? $"{GradeNames[it.grade]} +{it.enchant}" : GradeNames[it.grade];
            Text(new Rect(r.x, r.yMax - 28, r.width, 26), g, Shrink(Col(ds, c, TextAnchor.MiddleCenter), g, r.width - 6), 2);
        }
        if (it != null && GUI.Button(r, GUIContent.none, GUIStyle.none)) { gearPick = it; gearStatus = null; }
    }

    void DrawHeroStats(Rect r, bool port)
    {
        Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.9f), new Color(1, 1, 1, 0.1f), 12);
        var p = player;
        var rows = new (string, string)[]
        {
            ("Здоровье", $"{p.maxHp:0}"), ("Урон", $"{p.dmgMul * 100:0}%"), ("Скорость чар", $"+{(1 / p.cdMul - 1) * 100:0}%"),
            ("Шанс крита", $"{p.crit * 100:0}%"), ("Броня", $"{Mathf.Min(0.8f, p.armor) * 100:0}%"), ("Бег", $"{p.speed:0.0}"),
            ("Регенерация", $"{p.regen:0.##}/с"), ("Площадь", $"{p.area * 100:0}%"), ("Радиус сбора", $"{p.magnet:0.0}"),
            ("Опыт", $"{p.xpMul * 100:0}%"), ("Золото", $"{p.goldMul * 100:0}%"),
        };
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        int cols = port ? 2 : 1;
        float lh = port ? 34 : Mathf.Min(48, (r.height - 70) / rows.Length);
        float cw = (r.width - 32) / cols;
        if (!port) Text(new Rect(r.x, r.y + 12, r.width, 36), "ХАРАКТЕРИСТИКИ", Col(caps, Gold, TextAnchor.MiddleCenter), 2);
        float y0 = r.y + (port ? 10 : 60);
        int perCol = (rows.Length + cols - 1) / cols;
        for (int i = 0; i < rows.Length; i++)
        {
            int col = i / perCol, row = i % perCol;
            var lr = new Rect(r.x + 16 + col * cw, y0 + row * lh, cw - 12, lh);
            Text(lr, rows[i].Item1, Shrink(Col(ds, Soft(0.75f), TextAnchor.MiddleLeft), rows[i].Item1, cw * 0.6f), 2);
            Text(lr, rows[i].Item2, Col(ds, MetaGold, TextAnchor.MiddleRight), 2);
        }
    }

    void DrawBag(Rect r)
    {
        Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.9f), new Color(1, 1, 1, 0.1f), 12);
        var bag = gear.FindAll(g => g.equipped == 0);
        string head = gearOk ? $"СУМКА  {bag.Count}/60" : "СУМКА · ЗАГРУЖАЮ…";
        Text(new Rect(r.x, r.y + 8, r.width, 36), head, Col(caps, Gold, TextAnchor.MiddleCenter), 2);
        var view = new Rect(r.x + 10, r.y + 50, r.width - 20, r.height - 60);
        if (gearOk && bag.Count == 0)
        {
            string s = "Пусто. Вещи выпадают в конце забега: чем дольше продержался, тем вернее; победа — ещё одна. Дальние уровни и сложность дают грейд выше.";
            var st = new GUIStyle(Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, Soft(0.65f), TextAnchor.UpperCenter)) { wordWrap = true };
            Text(new Rect(view.x + 16, view.y + 20, view.width - 32, view.height - 20), s, st, 2);
            return;
        }
        float cell = Mathf.Clamp(view.width / 5 - 10, 78, 104);
        int cols = Mathf.Max(1, (int)((view.width + 10) / (cell + 10)));
        float x0 = view.x + (view.width - (cols * (cell + 10) - 10)) / 2;
        int rows = (bag.Count + cols - 1) / cols;
        var ev = Event.current;
        if (gearPick == null && confirm == null)
        {
            if (ev.type == EventType.ScrollWheel && view.Contains(ev.mousePosition)) { bagScroll += ev.delta.y * 60; ev.Use(); }
            bagScroll = TouchScroll(view, bagScroll);
        }
        bagScroll = Mathf.Clamp(bagScroll, 0, Mathf.Max(0, rows * (cell + 10) - view.height));
        int seen = PlayerPrefs.GetInt("gear.seen");
        GUI.BeginGroup(view);
        for (int i = 0; i < bag.Count; i++)
        {
            var it = bag[i];
            var cr = new Rect(x0 - view.x + i % cols * (cell + 10), i / cols * (cell + 10) - bagScroll, cell, cell);
            if (cr.yMax < 0 || cr.y > view.height) continue;
            var c = GradeColors[it.grade];
            Plate(cr, new Color(0.06f, 0.05f, 0.08f, 0.95f), c, 10, 2);
            GUI.DrawTexture(new Rect(cr.x + 8, cr.y + 6, cell - 16, cell - 16), IconT(ItemIcon(it)), ScaleMode.ScaleToFit, true, 0, Color.white, 0, 8);
            string g = it.enchant > 0 ? $"{GradeNames[it.grade]} +{it.enchant}" : GradeNames[it.grade];
            Text(new Rect(cr.x, cr.yMax - 26, cr.width, 24), g, Shrink(Col(tinyS, c, TextAnchor.MiddleCenter), g, cell - 6), 2);
            if (it.id > seen) RR(new Rect(cr.xMax - 18, cr.y + 6, 12, 12), new Color(0.53f, 1f, 0.56f), 6);
            if (GUI.Button(cr, GUIContent.none, GUIStyle.none)) { gearPick = it; gearStatus = null; }
        }
        GUI.EndGroup();
    }

    void DrawGearPick(float W, float H)
    {
        if (gearPick == null) return;
        var it = gearPick;
        var s = SlotOf(it.slot);
        var c = GradeColors[it.grade];
        Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
        float w = Mathf.Min(680, W - 32), h = (IsWeapon(it) ? 690 : 620) + (it.equipped == 0 ? 80 : 0);
        var r = new Rect((W - w) / 2, (H - h) / 2, w, h);
        Plate(r, new Color(0.06f, 0.05f, 0.08f, 0.98f), c, 16, 2);
        GUI.DrawTexture(new Rect(r.x + 24, r.y + 24, 110, 110), IconT(ItemIcon(it)), ScaleMode.ScaleToFit, true, 0, Color.white, 0, 12);
        RRLine(new Rect(r.x + 24, r.y + 24, 110, 110), c, 2, 12);
        string name = GearName(it);
        float tx = r.x + 156, tw = w - 180;
        Text(new Rect(tx, r.y + 26, tw, 44), name, Shrink(Col(titleS, Color.Lerp(c, Bone, 0.3f), TextAnchor.MiddleLeft), name, tw), 2);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        string sub = $"{(IsWeapon(it) ? "Оружие" : s.title)} · грейд {GradeNames[it.grade]}" + (it.equipped != 0 ? " · надето" : "");
        Text(new Rect(tx, r.y + 72, tw, 30), sub, Col(ds, c, TextAnchor.MiddleLeft), 2);
        Text(new Rect(tx, r.y + 108, tw, 34), Hl(StatLine(s.stat, MainValue(it))), Col(small, Bone, TextAnchor.MiddleLeft), 2);
        float a = AffixValue(it);
        float y = r.y + 150;
        if (IsWeapon(it))
        {
            var k = KindOf(it);
            string wl = $"{k.desc}: " + Hl(StatLine(k.stat, WeaponBonus(it)));
            Text(new Rect(r.x + 24, y, w - 48, 32), wl, Shrink(Col(small, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleLeft), wl, w - 48), 2); y += 36;
            string gl = GlowLine(it.enchant);
            Text(new Rect(r.x + 24, y, w - 48, 30), gl, Shrink(Col(ds, GlowUi[GlowTier(it.enchant)], TextAnchor.MiddleLeft), gl, w - 48), 2); y += 34;
        }
        if (a > 0) { Text(new Rect(r.x + 24, y, w - 48, 32), "Свойство: " + Hl(StatLine(it.affix, a)), Col(small, new Color(0.75f, 0.9f, 1f), TextAnchor.MiddleLeft), 2); y += 38; }
        // сравнение с надетым в том же слоте
        var on = gear.Find(g => g.equipped != 0 && g.slot == it.slot);
        if (on != null && on != it)
        {
            float d = MainValue(it) - MainValue(on);
            string cmp = $"Против надетого «{GearName(on)}»: {(d >= 0 ? "+" : "−")}{Mathf.Abs(d):0.#} {StatTitle[s.stat]}";
            Text(new Rect(r.x + 24, y, w - 48, 30), cmp, Shrink(Col(ds, d >= 0 ? new Color(0.53f, 1f, 0.56f) : new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleLeft), cmp, w - 48), 2);
            y += 36;
        }
        // заточка
        y = Mathf.Max(y, r.y + (IsWeapon(it) ? 300 : 240));
        bool maxed = it.enchant >= MaxEnchant;
        int price = Sale(EnchantPrice[it.grade] * (it.enchant + 1));
        float ch = EnchantChance(it.enchant);
        string el = maxed ? $"Заточка на максимуме (+{MaxEnchant})"
            : it.enchant < SafeEnchant ? $"Заточка до +{it.enchant + 1}: без риска · {price} золота · основной стат +12%"
            : $"Заточка до +{it.enchant + 1}: шанс {ch * 100:0}% · {price} золота · неудача откатит до +{SafeEnchant}";
        var es = new GUIStyle(Col(ds, Soft(0.8f), TextAnchor.UpperLeft)) { wordWrap = true };
        Text(new Rect(r.x + 24, y, w - 48, 60), el, es, 2);
        if (gearStatus != null) Text(new Rect(r.x + 24, y + 62, w - 48, 32), gearStatus, Col(small, gearStatus.StartsWith("Заточка удалась") || gearStatus.StartsWith("Разобрано") ? new Color(0.53f, 1f, 0.56f) : new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleLeft), 2);

        float bw = (w - 24 * 2 - 16 * 2) / 3, by = r.yMax - 190;
        string busy = gearBusy ? "…" : null;
        if (Btn(new Rect(r.x + 24, by, bw, 78), busy ?? (it.equipped != 0 ? "Снять" : "Надеть"), it.equipped != 0 ? Bone : new Color(0.53f, 1f, 0.56f)))
            GearAction(it.equipped != 0 ? "unequip" : "equip", it);
        bool canEnch = !maxed && walletOk && walletGold >= price;
        if (Btn(new Rect(r.x + 24 + bw + 16, by, bw, 78), busy ?? (maxed ? "Максимум" : $"Заточить · {price}"), canEnch ? MetaGold : Bone * 0.5f) && canEnch)
            GearAction("enchant", it);
        int sg = (int)(SalvageGold[it.grade] * (1 + it.enchant / 2f));
        bool canSalv = it.equipped == 0;
        if (Btn(new Rect(r.x + 24 + 2 * (bw + 16), by, bw, 78), busy ?? (canSalv ? $"Разобрать · +{sg}" : "Сначала сними"), canSalv ? new Color(1f, 0.6f, 0.45f) : Bone * 0.5f) && canSalv)
            GearAction("salvage", it);
        string gold = walletOk ? $"ЗОЛОТО  {walletGold}" : "НЕТ СВЯЗИ";
        Text(new Rect(r.x, by - 44, w, 34), gold, Col(caps, MetaGold, TextAnchor.MiddleCenter), 2);
        // продать другому игроку — дороже, чем разобрать (GameMarket.cs)
        if (it.equipped == 0 && Btn(new Rect(r.x + 24, by - 118, w - 48, 64), "На рынок — продать игрокам", MetaGold)) OpenSell(it);
        if (Btn(new Rect(r.x + (w - 300) / 2, r.yMax - 100, 300, 80), "Закрыть", Bone) || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
        {
            gearPick = null;
            gearStatus = null;
        }
        // вещь исчезла (разобрали) — окно закрывается
        if (gearPick != null && !gear.Contains(gearPick)) gearPick = null;
    }

    // ---------- добыча на экране конца забега ----------

    float DrawRunDrops(float W, float y)
    {
        if (runDrops == null || runDrops.Count == 0) return 0;
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        Text(new Rect(0, y, W, 30), "ДОБЫЧА", Col(caps, Gold, TextAnchor.MiddleCenter), 2);
        float cw = Mathf.Min(300, (W - 48) / Mathf.Max(1, runDrops.Count) - 12), x = (W - (cw + 12) * runDrops.Count + 12) / 2;
        for (int i = 0; i < runDrops.Count; i++)
        {
            var it = runDrops[i];
            if (!GearValid(it)) continue;
            var c = GradeColors[it.grade];
            var r = new Rect(x + i * (cw + 12), y + 34, cw, 56);
            Plate(r, new Color(0.06f, 0.05f, 0.08f, 0.95f), c, 10, 2);
            GUI.DrawTexture(new Rect(r.x + 6, r.y + 4, 48, 48), IconT(ItemIcon(it)), ScaleMode.ScaleToFit, true, 0, Color.white, 0, 6);
            string n = $"{(IsWeapon(it) ? WeaponName(it) : SlotOf(it.slot).names[it.grade])} ({GradeNames[it.grade]})";
            Text(new Rect(r.x + 60, r.y, r.width - 66, 56), n, Shrink(Col(ds, Color.Lerp(c, Bone, 0.3f), TextAnchor.MiddleLeft), n, r.width - 66), 2);
        }
        if (runSold > 0) Text(new Rect(0, y + 94, W, 28), $"Сумка полна — лишнее разобрано за {runSold} золота", Col(ds, Soft(0.7f), TextAnchor.MiddleCenter), 2);
        return runSold > 0 ? 126 : 100;
    }
}
