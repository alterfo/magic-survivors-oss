using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Меню выбора магии, суперсил и сундуков: карточки школ с панелями и иконками из neuraldeep Image API.
public partial class Game
{
    static readonly Dictionary<string, Texture2D> iconCache = new();
    static readonly HashSet<string> painted = new();
    float menuT;

    static Texture2D IconT(string name)
    {
        if (!iconCache.TryGetValue(name, out var t) || t == null)
        {
            t = Resources.Load<Texture2D>("Icons/" + name);
            if (t != null) painted.Add(name);
            else t = Art.Tex(name) ?? Art.Tex("star_06");
            iconCache[name] = t;
        }
        return t;
    }

    // Прогрев: все иконки и рамки карточек грузятся и уходят в видеопамять ещё на стартовом экране,
    // иначе первое меню прокачки подвисает на загрузке текстур
    static List<Texture> warm;

    static void WarmTextures()
    {
        if (warm != null && warm.Count == 0) return;
        if (warm == null)
        {
            warm = new List<Texture>();
            foreach (var t in Resources.LoadAll<Texture2D>("Icons")) { IconT(t.name); warm.Add(t); }
            foreach (var t in Resources.LoadAll<Texture2D>("UI")) { PanelT(t.name); warm.Add(t); }
            return;
        }
        if (Event.current.type != EventType.Repaint) return;
        // по 12 текстур за кадр, почти невидимо в углу — заставляет загрузить их на видеокарту
        GUI.color = new Color(1, 1, 1, 0.004f);
        for (int i = Mathf.Min(12, warm.Count) - 1; i >= 0; i--)
        {
            GUI.DrawTexture(new Rect(0, 0, 2, 2), warm[i]);
            warm.RemoveAt(i);
        }
        GUI.color = Color.white;
    }

    static Texture2D PanelT(string name)
    {
        if (!iconCache.TryGetValue("ui/" + name, out var t) || t == null)
            iconCache["ui/" + name] = t = Resources.Load<Texture2D>("UI/" + name);
        return t;
    }

    static string PanelFor(Skill s) => s == null ? "card_neutral" : s.fusion ? "card_fusion" : s.element switch
    {
        Element.Fire => "card_fire", Element.Electro => "card_electro", Element.Meteor => "card_meteor", Element.Ice => "card_ice", _ => "card_neutral",
    };

    static Rect Grow(Rect r, float k) => new(r.x - r.width * k, r.y - r.height * k, r.width * (1 + 2 * k), r.height * (1 + 2 * k));

    static void Icon(Rect r, Skill s) => IconTex(r, s.icon, s.color);

    // Нарисованная иконка — как есть в рамке; запасная текстура частиц — подкрашенная на тёмном круге
    static void IconTex(Rect r, string tex, Color c)
    {
        var t = IconT(tex);
        GUI.color = new Color(c.r, c.g, c.b, 0.45f);
        GUI.DrawTexture(Grow(r, 0.22f), Gen.Glow().texture);
        if (painted.Contains(tex))
        {
            // круглая иконка в золотом медальоне; без медальона — кольцо цвета школы
            GUI.color = Color.white;
            GUI.DrawTexture(r, t, ScaleMode.ScaleAndCrop, true, 0, Color.white, 0, r.width / 2);
            var m = r.width >= 90 ? PanelT("medallion") : null;
            if (m != null) GUI.DrawTexture(Grow(r, 0.26f), m);
            else RRLine(r, c, Mathf.Max(2, r.width * 0.04f), r.width / 2);
        }
        else
        {
            GUI.color = new Color(0, 0, 0, 0.9f);
            GUI.DrawTexture(r, Gen.Glow().texture);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + r.width * 0.1f, r.y + r.height * 0.1f, r.width * 0.8f, r.height * 0.8f), t);
        }
        GUI.color = Color.white;
    }

    void MenuBackdrop(float W, float H, Color tint)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.012f, 0.9f));
        tint = Color.Lerp(tint, new Color(0.4f, 0.35f, 0.3f), 0.6f);
        GUI.color = new Color(tint.r, tint.g, tint.b, 0.1f);
        GUI.DrawTexture(new Rect(W * 0.1f, -H * 0.3f, W * 0.8f, H * 0.9f), Gen.Glow().texture);
        GUI.color = Color.white;
    }

    // Шапка школ: эмблема, название, уровень школы и бонус урона
    float SchoolBar(float W, float y)
    {
        // только школы, магия которых есть у героя (у мага нет льда, у ведьмы огня)
        var hero = HeroContent.Get(builtHero);
        var shown = new List<int>();
        for (int i = 1; i <= 4; i++)
        {
            var el = (Element)i;
            if (SchoolLevel(el) > 0 || skills.Exists(k => k.spell && k.element == el && hero.Allows(k))) shown.Add(i);
        }
        // узкий экран: две строки по две школы, иначе подписи не влезают
        // у героя три школы — помещаются в одну строку и на телефоне; четыре на узком экране — по две
        int per = Mathf.Max(1, W < 1000 && shown.Count > 3 ? 2 : shown.Count);
        float cw = Mathf.Min(250, (W - 60) / per), y0 = y;
        for (int j = 0; j < shown.Count; j++)
        {
            int i = shown[j];
            var el = (Element)i;
            int lv = SchoolLevel(el);
            var c = lv > 0 ? SchoolColors[i] : new Color(0.45f, 0.45f, 0.5f);
            int inRow = Mathf.Min(per, shown.Count - j / per * per);
            float x = (W - cw * inRow) / 2 + j % per * cw;
            y = y0 + j / per * 66;
            IconTex(new Rect(x + 8, y, 56, 56), SchoolIcons[i], c);
            Text(new Rect(x + 74, y + 2, cw - 80, 26), Up(SchoolNames[i]), Col(caps, c, TextAnchor.MiddleLeft), 2);
            Text(new Rect(x + 74, y + 28, cw - 80, 30), lv > 0 ? $"ур. {lv}  <color=#FFD45E>+{lv * 3}%</color>" : "—",
                Col(tiny, new Color(0.75f, 0.75f, 0.78f), TextAnchor.MiddleLeft), 2);
        }
        return y0 + Mathf.Max(1, (shown.Count + per - 1) / per) * 66;
    }

    // ---------- Карточка ----------

    struct CardInfo
    {
        public string title, desc, tag, level, icon, panel, footer, stats;
        public Color color;
        public Skill skill;
        public bool gold, isNew;
    }

    static readonly Color Gold = new(0.86f, 0.68f, 0.32f);
    static readonly Color Bone = new(0.86f, 0.8f, 0.68f);
    int cardHover = -1;
    GUIStyle tinyS, tinyW;

    // Какая карта под курсором — считается до отрисовки, чтобы остальные можно было притушить
    void HoverCard(int n, System.Func<int, Rect> rect)
    {
        cardHover = -1;
        for (int i = 0; i < n; i++) if (rect(i).Contains(Event.current.mousePosition)) cardHover = i;
    }

    bool Card(Rect r, CardInfo c, int i, bool vertical)
    {
        float t = Time.unscaledTime - menuT;
        float k = Mathf.Clamp01((t - i * 0.07f) / 0.28f);
        float ease = 1 - (1 - k) * (1 - k);
        r.y += (1 - ease) * 90;
        bool hover = i == cardHover;
        bool dim = cardHover >= 0 && !hover;

        // тень и подложка
        Rect(new Rect(r.x + 10, r.y + 14, r.width, r.height), new Color(0, 0, 0, 0.5f));
        // вертикальная рамка-картинка для колонок, широкий баннер — для стопки в портрете
        var art = vertical ? PanelT(c.panel.Replace("card_", "cardv_")) : null;
        var bg = art ?? PanelT(c.panel);
        float rad = r.width * 0.05f;
        if (c.gold)
        {
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.18f);
            GUI.DrawTexture(Grow(r, 0.06f), Gen.Glow().texture);
            GUI.color = Color.white;
        }
        // наведённая карта как есть, остальные притухают — без рамок поверх орнамента
        var tint = dim ? new Color(0.5f, 0.5f, 0.52f) : Color.white;
        if (bg != null) GUI.DrawTexture(r, bg, ScaleMode.ScaleAndCrop, true, 0, tint, 0, rad);
        if (art != null)
            RR(new Rect(r.x + r.width * 0.08f, r.y + r.height * 0.055f, r.width * 0.84f, r.height * 0.89f), new Color(0, 0, 0, 0.55f), rad);
        else RR(r, new Color(0.02f, 0.02f, 0.03f, bg != null ? 0.55f : 0.9f), rad);
        GUI.color = new Color(c.color.r, c.color.g, c.color.b, hover ? 0.2f : 0.08f);
        GUI.DrawTexture(new Rect(r.x + r.width * 0.15f, r.y + r.height * 0.05f, r.width * 0.7f, r.height * 0.5f), Gen.Glow().texture);
        GUI.color = Color.white;
        if (art == null) RRLine(r, new Color(c.color.r * 0.5f, c.color.g * 0.5f, c.color.b * 0.5f, hover ? 1 : 0.6f), 1, rad);

        // соседняя карта под пальцем приглушает остальные, но названия и описания должны читаться (проверка 17.11)
        var titleCol = Color.Lerp(c.color, Bone, dim ? 0.7f : 0.55f) * (dim ? 0.92f : 1f); titleCol.a = 1;
        var descCol = dim ? new Color(0.66f, 0.63f, 0.58f) : new Color(0.8f, 0.77f, 0.72f);
        if (vertical)
        {
            float cx = r.center.x;
            float pad = r.width * 0.11f;
            ChipAt(cx, r.y + r.height * 0.06f, c.tag, c.gold ? Gold : c.color);
            // низкая карта (телефон в альбомной): иконка меньше и отступы плотнее — иначе на описание не остаётся места
            bool cmp = r.height < 560;
            float isz = Mathf.Min(r.width * 0.42f, r.height * (cmp ? 0.17f : c.stats != null || c.footer != null ? 0.2f : 0.25f));
            var ir = new Rect(cx - isz / 2, r.y + r.height * 0.06f + (cmp ? 34 : 52) + isz * 0.26f, isz, isz);
            IconTex(ir, c.icon, c.color);
            if (c.isNew) ChipAt(ir.xMax + isz * 0.12f, ir.y - isz * 0.1f, "НОВОЕ", Gold, true);
            if (c.level != null) ChipAt(cx, ir.yMax + isz * 0.14f, c.level, c.color);
            float ty = ir.yMax + isz * 0.14f + (cmp ? 22 : 44);
            Outline(new Rect(r.x + pad, ty, r.width - 2 * pad, 56), c.title, Fit(c.title, r.width - 2 * pad, titleCol), 2);
            float dTop = cmp ? 56 : 76;
            if (!cmp) Divider(cx, ty + 64, r.width - 2.4f * pad, c.gold ? Gold : c.color);
            float by = r.yMax - r.height * 0.07f;
            float sy = c.skill != null && !c.skill.fusion && c.skill.max <= 20 ? by - (c.footer != null ? 88 : 46) : by - 4;
            if (c.stats != null) sy = StatsBlock(new Rect(r.x + pad, 0, r.width - 2 * pad, 0), sy, c.stats, c.color, dim);
            // описание подстраивается: если в свой кегль не влезает — шрифт мельче, а не обрезка
            float dw = r.width - 2 * pad, dh = sy - ty - dTop;
            var ds = tiny;
            if (TextH(c.desc, tiny, dw) > dh) ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
            if (TextH(c.desc, ds, dw) > dh) ds = microS ??= new GUIStyle(tiny) { fontSize = 18 };   // и это не влезло — ещё мельче
            Text(new Rect(r.x + pad, ty + dTop, dw, dh), Hl(c.desc), Col(ds, descCol, TextAnchor.UpperCenter), 2);
            if (c.footer != null) Text(new Rect(r.x, by - 82, r.width, 36), c.footer, Col(caps, Gold, TextAnchor.MiddleCenter), 2);
            if (c.skill != null && !c.skill.fusion && c.skill.max <= 20)
            {
                // узкая карта (планшет в портрете): полоска уровней шире карты — вместо неё «УР. 1/8»
                if (PipsW(c.skill) <= r.width - 24) Pips(cx - PipsW(c.skill) / 2, by - 30, c.skill, true, Time.unscaledTime);
                else ChipAt(cx, by - 26, $"УР. {c.skill.level}/{c.skill.max}", c.color);
            }
        }
        else
        {
            // слева медальон с иконкой (рамка медальона выходит за иконку на 26% с каждой стороны) и уровни под ним,
            // справа столбец текста: плашки, название, описание, внизу «было → станет»
            bool pips = c.skill != null && !c.skill.fusion && c.skill.max <= 20;
            float isz = Mathf.Min(170, (r.height - (pips ? 74 : 36)) / 1.52f);
            float colW = Mathf.Max(isz * 1.52f, pips ? PipsW(c.skill) + 16 : 0);
            float icx = r.x + 22 + colW / 2;
            var ir = new Rect(icx - isz / 2, r.y + 18 + isz * 0.26f, isz, isz);
            IconTex(ir, c.icon, c.color);
            if (c.isNew) ChipAt(ir.center.x, r.y + 10, "НОВОЕ", Gold, true);
            if (pips) Pips(icx - PipsW(c.skill) / 2, r.yMax - 44, c.skill, true, Time.unscaledTime);
            float tx = r.x + 22 + colW + 22, tw = r.xMax - 24 - tx;
            float cy = r.y + 20;
            var tr = new Rect(tx, cy, CapsW(c.tag), 30);
            Chip(tr, c.tag, c.gold ? Gold : c.color);
            if (c.level != null && tr.xMax + 12 + CapsW(c.level) < tx + tw) Chip(new Rect(tr.xMax + 12, cy, CapsW(c.level), 30), c.level, c.color);
            cy += 40;
            var ts = Col(Fit(c.title, tw, titleCol), titleCol, TextAnchor.MiddleLeft);
            Outline(new Rect(tx, cy, tw, 48), c.title, ts, 2);
            cy += 52;
            float bottom = r.yMax - 16;
            if (c.footer != null) { Text(new Rect(tx, bottom - 32, tw, 32), c.footer, Col(caps, Gold, TextAnchor.MiddleLeft), 2); bottom -= 38; }
            if (c.stats != null) bottom = StatsBlock(new Rect(tx, 0, tw, 0), bottom, c.stats, c.color, dim);
            float dh = bottom - cy;
            var ds = tiny;
            if (TextH(c.desc, tiny, tw) > dh) ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
            Text(new Rect(tx, cy, tw, dh), Hl(c.desc), Col(ds, descCol, TextAnchor.UpperLeft), 2);
        }

        // первые 0.45 с после появления карты не нажимаются: на телефоне палец, который вёл мага, часто тыкал в карту случайно
        if (t < 0.45f) return false;
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    // Блок «было → станет» по общим статам мага, снизу вверх от bottom; возвращает его верх
    GUIStyle microS;
    float StatsBlock(Rect r, float bottom, string stats, Color c, bool dim)
    {
        var lines = stats.Split('\n');
        // узкая карта (планшет в портрете) — было/станет мельче, иначе блок съедает описание
        var baseSt = r.width < 220 ? (tinyS ??= new GUIStyle(tiny) { fontSize = 21 }) : tiny;
        var st = Col(baseSt, dim ? new Color(0.5f, 0.48f, 0.45f) : new Color(0.8f, 0.77f, 0.72f), TextAnchor.MiddleCenter);
        float h = 14;
        foreach (var l in lines) h += Mathf.Max(30, TextH(l, st, r.width - 12));
        float y = bottom - h;
        Plate(new Rect(r.x, y, r.width, h), new Color(0.02f, 0.02f, 0.03f, 0.7f), new Color(c.r, c.g, c.b, dim ? 0.15f : 0.35f), 8);
        float ly = y + 7;
        foreach (var l in lines)
        {
            float lh = Mathf.Max(30, TextH(l, st, r.width - 12));
            Text(new Rect(r.x + 6, ly, r.width - 12, lh), l, st, 2);
            ly += lh;
        }
        return y - 8;
    }

    // ---------- Что карта даст к общим статам ----------

    // Бонус применяется к копии мага, разница со статами текущего и есть подсказка; кэш живёт одно открытие меню
    static readonly Dictionary<object, string> deltaCache = new();
    float deltaMenuT = -1;

    string StatDelta(object key, System.Action<Player> apply)
    {
        if (deltaMenuT != menuT) { deltaCache.Clear(); deltaMenuT = menuT; }
        if (deltaCache.TryGetValue(key, out var s)) return s;
        var a = player.Clone();
        apply(a);
        return deltaCache[key] = Diff(player, a);
    }

    string ArtifactDelta(Artifact art) => StatDelta(art, p =>
    {
        var real = player;
        player = p;
        art.apply(this);
        player = real;
    });

    static string Diff(Player a, Player b)
    {
        var sb = new System.Text.StringBuilder();
        void D(string name, float x, float y, string fmt)
        {
            if (Mathf.Abs(x - y) < 1e-4f) return;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(name).Append("  <color=#8E8C99>").Append(string.Format(fmt, x))
              .Append("</color> → <color=#86FF8F>").Append(string.Format(fmt, y)).Append("</color>");
        }
        D("Урон", a.dmgMul * 100, b.dmgMul * 100, "{0:0}%");
        D("Скорость чар", 100 / a.cdMul, 100 / b.cdMul, "{0:0}%");
        D("Площадь", a.area * 100, b.area * 100, "{0:0}%");
        D("Шанс крита", a.crit * 100, b.crit * 100, "{0:0}%");
        D("Крит. урон", a.critMul, b.critMul, "×{0:0.#}");
        D("Здоровье", a.maxHp, b.maxHp, "{0:0}");
        D("Регенерация", a.regen, b.regen, "{0:0.#}/с");
        D("Вампиризм", a.vamp * 100, b.vamp * 100, "{0:0}%");
        D("Защита", Mathf.Min(0.8f, a.armor) * 100, Mathf.Min(0.8f, b.armor) * 100, "{0:0}%");   // в бою не больше 80%
        D("Шипы", a.thorns * 100, b.thorns * 100, "{0:0}%");
        D("Лечение за убийство", a.killHeal, b.killHeal, "{0:0}");
        D("Бег", a.speed / 4.2f * 100, b.speed / 4.2f * 100, "{0:0}%");
        D("Радиус сбора", a.magnet, b.magnet, "{0:0.#} м");
        D("Опыт", a.xpMul * 100, b.xpMul * 100, "{0:0}%");
        D("Находки", a.findMul, b.findMul, "×{0:0.#}");
        D("Воскрешения", a.revives, b.revives, "{0:0}");
        D("Карт на выбор", 3 + a.extraChoice, 3 + b.extraChoice, "{0:0}");
        return sb.Length > 0 ? sb.ToString() : null;
    }

    void MenuHeader(float W, float y, string over, string title, Color c)
    {
        ChipAt(W / 2, y, over, c);
        Outline(new Rect(0, y + 36, W, 100), title, Shrink(Col(big, Color.Lerp(c, Bone, 0.75f)), title, W - 60, 28), 2);
        Divider(W / 2, y + 140, Mathf.Min(W * 0.6f, 700), c);
    }

    // Раскладка: в альбомной ориентации карты в ряд, в портретной — стопкой
    void LayoutCards(int n, float W, float H, float top, out bool vertical, out System.Func<int, Rect> rect)
    {
        // карты в ряд везде, кроме вытянутого телефона: на планшете в портрете стопка сплющивала их в полоски
        vertical = W > H * 0.68f;
        if (vertical)
        {
            float gap = 30, cw = Mathf.Min(400, (W - 80 - gap * (n - 1)) / n), ch = Mathf.Min(H - top - (Portrait ? 110 : 50), 640);   // в портрете снизу «Поле боя»
            float x0 = (W - (cw * n + gap * (n - 1))) / 2;
            rect = i => new Rect(x0 + i * (cw + gap), top, cw, ch);
        }
        else
        {
            float gap = 24, cw = Mathf.Min(W - 40, 980), ch = Mathf.Min(300, (H - top - 110) / n - gap);   // на телефоне снизу кнопки «Поле боя» / «Позже»
            float x0 = (W - cw) / 2;
            rect = i => new Rect(x0, top + i * (ch + gap), cw, ch);
        }
    }

    // ---------- Экраны ----------

    void DrawLevelUp(float W, float H)
    {
        MenuBackdrop(W, H, new Color(0.5f, 0.45f, 0.6f));
        // низкий экран (телефон в альбомной): шапка плотнее, картам остаётся высота под описание
        bool tight = H < 900;
        float hy = tight ? 84 : 150;
        if (grimoireOpen) MenuHeader(W, hy, "ГРИМУАР ПАВШЕГО", "Запретная магия", Forbidden.Soul);
        else MenuHeader(W, hy, $"УРОВЕНЬ {level}  ·  ЗАКЛИНАНИЯ {SpellsTaken}/{MaxSpells}  ·  ПАССИВКИ {PassivesTaken}/{MaxPassives}", "Выбери магию", Bone);
        float sb = SchoolBar(W, tight ? 236 : 312);
        LayoutCards(choices.Count, W, H, sb + (tight ? 14 : 18), out bool vertical, out var rect);
        HoverCard(choices.Count, rect);
        for (int i = 0; i < choices.Count; i++)
        {
            var s = choices[i];
            string tag = s.fusion ? "СЛИЯНИЕ" : s.forbidden ? "ЗАПРЕТНАЯ" : s.spell ? Up(SchoolNames[(int)s.element]) : s.max > 20 ? "БЕЗ ПРЕДЕЛА" : "ПАССИВКА";
            string lvl = s.fusion || s.level == 0 ? null : s.spell ? $"УР. {s.level} → {s.level + 1}" : $"УР. {s.level + 1}";
            string desc = s.fusion ? $"{((Fusion)s).a.title} + {((Fusion)s).b.title}\n{s.Desc(1)}" : s.Desc(s.level + 1);
            string footer = s.spell && !s.fusion && s.super < 0 && s.level + 1 >= s.max - 2
                ? (s.level + 1 == s.max ? "ОТКРОЕТ СУПЕРСИЛУ" : $"СУПЕРСИЛА НА {s.max} УР.") : null;
            var info = new CardInfo
            {
                title = s.title, desc = desc, tag = tag, level = lvl, isNew = s.level == 0 && !s.fusion,
                icon = s.icon, panel = PanelFor(s), color = s.color, skill = s, gold = s.fusion, footer = footer,
                stats = s is Passive ps ? StatDelta(ps, ps.ApplyTo) : null,
            };
            if (Card(rect(i), info, i, vertical)) Pick(i);
        }
    }

    void DrawSuper(float W, float H)
    {
        var s = superSkill;
        MenuBackdrop(W, H, Gold);
        bool tight = H < 900;
        if (tight) Icon(new Rect(W / 2 - 40, 62, 80, 80), s);
        else Icon(new Rect(W / 2 - 64, 96, 128, 128), s);
        float hy = tight ? 150 : 236;
        MenuHeader(W, hy, $"{Up(s.title)} · УР. {s.max}", s.spell ? "Суперсила" : "Выбери путь", Gold);
        Text(new Rect(0, hy + 148, W, 40), "Выбери одну ветку — навсегда до конца забега", Col(tiny, new Color(0.85f, 0.8f, 0.72f), TextAnchor.MiddleCenter), 2);
        LayoutCards(3, W, H, hy + 204, out bool vertical, out var rect);
        HoverCard(3, rect);
        for (int i = 0; i < 3; i++)
        {
            var info = new CardInfo
            {
                title = s.superNames[i], desc = s.superDescs[i], tag = $"ВЕТКА {i + 1}",
                icon = s.icon, panel = PanelFor(s), color = Color.Lerp(s.color, Gold, 0.35f), gold = true,
            };
            if (Card(rect(i), info, i, vertical)) PickSuper(i);
        }
    }

    void DrawChest(float W, float H)
    {
        var col = RarityColors[chestRarity];
        MenuBackdrop(W, H, col);
        bool tight = H < 900;
        if (tight) IconTex(new Rect(W / 2 - 36, 62, 72, 72), "star_06", col);
        else IconTex(new Rect(W / 2 - 56, 96, 112, 112), "star_06", col);
        MenuHeader(W, tight ? 140 : 220, chestChoices.Count > 1 ? "ВЫБЕРИ АРТЕФАКТ" : "ВНУТРИ", RarityNames[chestRarity], col);
        LayoutCards(chestChoices.Count, W, H, tight ? 300 : 400, out bool vertical, out var rect);
        HoverCard(chestChoices.Count, rect);
        for (int i = 0; i < chestChoices.Count; i++)
        {
            var a = chestChoices[i];
            var info = new CardInfo
            {
                title = a.title, desc = a.desc, tag = a.unique ? "УНИКАЛЬНЫЙ" : "АРТЕФАКТ",
                icon = a.icon, panel = chestRarity == 2 ? "card_fusion" : "card_neutral", color = a.color, gold = chestRarity == 2,
                stats = ArtifactDelta(a),
            };
            if (Card(rect(i), info, i, vertical)) PickArtifact(i);
        }
        if (chestRarity == 2) ChipAt(W / 2, H - 70, "+ БОНУСНЫЙ УРОВЕНЬ", col, true);
    }

    // ---------- Верхняя панель и HUD ----------

    // Плитка «подпись капсом + крупное число»
    void Tile(Rect r, string label, string value, Color c)
    {
        Plate(r, new Color(0.06f, 0.05f, 0.09f, 0.82f), new Color(c.r, c.g, c.b, 0.28f), 14);
        GUI.Label(new Rect(r.x, r.y + 8, r.width, 22), label, Col(caps, new Color(c.r, c.g, c.b, 0.85f)));
        Text(new Rect(r.x, r.y + 28, r.width, r.height - 34), value, Col(heavy, Color.white), 2);
    }

    void TopBar(float W, float H)
    {
        float u = UiScale;
        bool port = Portrait;
        // Вместо сплошной полосы — мягкое затемнение сверху и отдельные плашки
        if (LiteUi) Rect(new Rect(0, 0, W, 104), new Color(0, 0, 0, 0.28f));   // одна полоса вместо шести
        else for (int i = 0; i < 6; i++) Rect(new Rect(0, i * 26, W, 26), new Color(0, 0, 0, 0.5f * (1 - i / 6f)));
        // опыт
        float xf = Mathf.Clamp01(xp / xpNext);
        Rect(new Rect(0, 0, W, 12), new Color(0.06f, 0.06f, 0.1f, 0.9f));
        Rect(new Rect(0, 0, W * xf, 12), new Color(0.35f, 0.6f, 1f));
        Rect(new Rect(0, 0, W * xf, 3), new Color(0.8f, 0.9f, 1f));

        var plate = new Color(0.06f, 0.05f, 0.09f, 0.82f);
        // в вертикальном экране плитки уже, а счётчик убийств уходит плашкой под таймер
        Tile(new Rect(24, 28, port ? 110 : 130, 86), "УРОВЕНЬ", level.ToString(), new Color(0.55f, 0.75f, 1f));
        PauseButton(new Rect(port ? 144 : 166, 28, port ? 64 : 86, 86));
        MuteButton(new Rect(port ? 214 : 262, 28, port ? 64 : 86, 86));
        if (state == State.Play) BuffBar(W);
        var tr = new Rect(W / 2 - (port ? 95 : 115), 28, port ? 190 : 230, 86);
        Plate(tr, plate, new Color(1, 1, 1, 0.12f), 14);
        Outline(tr, $"{(int)time / 60:00}:{(int)time % 60:00}", timer, 2);
        // на телефоне плитка «убито» не помещается рядом с таймером: в бою — плашка под ним, в меню выбора её нет
        if (port) { if (state == State.Play) ChipAt(W / 2, 122, $"УБИТО {kills}", new Color(1f, 0.5f, 0.45f)); }
        else Tile(new Rect(W - 356, 28, 150, 86), "УБИТО", kills.ToString(), new Color(1f, 0.5f, 0.45f));
        var sb = port ? new Rect(W - 154, 28, 130, 86) : new Rect(W - 192, 28, 168, 86);
        bool hov = sb.Contains(Event.current.mousePosition);
        Plate(sb, new Color(0.16f, 0.12f, 0.2f, 0.9f), hov ? Gold : new Color(0.75f, 0.62f, 0.4f, 0.8f), 14);
        Outline(sb, "Статы", Col(titleS, new Color(1f, 0.9f, 0.7f)), 2);
        if (GUI.Button(sb, GUIContent.none, GUIStyle.none)) ToggleStats();

        // Босс
        if (bossShown != null && bossShown.alive)
        {
            float bw = Mathf.Min(W - 80, 900), bx = (W - bw) / 2, by = port ? 30 : 0;
            Outline(new Rect(0, 134 + by, W, 48), bossShown.type.name, Col(titleS, new Color(1f, 0.55f, 0.45f)), 3);
            Frame(new Rect(bx, 186 + by, bw, 26), new Color(0.5f, 0.15f, 0.15f), new Color(0, 0, 0, 0.85f), 1);
            float f = Mathf.Clamp01(bossShown.hp / bossShown.maxHp);
            Rect(new Rect(bx + 4, 190 + by, (bw - 8) * f, 18), new Color(0.85f, 0.15f, 0.2f));
            Rect(new Rect(bx + 4, 190 + by, (bw - 8) * f, 4), new Color(1f, 0.5f, 0.5f));
        }

        // Иконки заклинаний внизу слева с уровнем в уголке
        float ix = 20;
        foreach (var s in skills)
        {
            if (s.level == 0 || !s.spell) continue;
            float isz = port ? 72 : 84;
            var r = new Rect(ix, H - isz - 28, isz, isz);
            Icon(r, s);
            var b = new Rect(r.xMax - 30, r.yMax - 26, 36, 32);
            if (s.super >= 0 && !s.fusion) Star(Grow(b, 0.25f), Gold);
            else
            {
                Frame(b, s.color, new Color(0.05f, 0.04f, 0.07f, 0.95f), 1);
                GUI.Label(b, s.fusion ? "∞" : s.level.ToString(), Col(caps, Color.white));
            }
            ix += isz + 12;
        }

        // Баннер
        if (bannerT > 0 && banner != null)
        {
            float a = Mathf.Clamp01(bannerT) * Mathf.Clamp01((bannerDur - bannerT) * 3);
            // на телефоне заголовок ужимается по ширине, пояснение переносится по строкам и лежит на подложке —
            // раньше длинная подсказка цели забега обрезалась (жалоба 15.11)
            float by = H * 0.24f;
            var ts = Shrink(Col(big, new Color(1f, 0.9f, 0.75f, a)), banner, W - 40, 30);
            if (bannerSub != null)
            {
                var ss = new GUIStyle(Col(small, new Color(0.92f, 0.88f, 0.82f, a), TextAnchor.UpperCenter)) { wordWrap = true };
                float sw = Mathf.Min(W - 64, 900), sh = ss.CalcHeight(new GUIContent(Plain(bannerSub)), sw);
                RR(new Rect((W - sw) / 2 - 16, by + 100, sw + 32, sh + 20), new Color(0.02f, 0.015f, 0.03f, 0.55f * a), 14);
                Text(new Rect((W - sw) / 2, by + 110, sw, sh), Hl(bannerSub), ss, 2);
            }
            Outline(new Rect(0, by, W, 110), banner, ts, 4);
        }

        DrawStick(u);
    }

    void DrawStick(float u)
    {
        if (stickOn && (state == State.Play || state == State.Town))
        {
            float R = Mathf.Min(Screen.width, Screen.height) * 0.12f / u;
            var o = new Vector2(stickOrigin.x / u, (Screen.height - stickOrigin.y) / u);
            var k = new Vector2(stickPos.x / u, (Screen.height - stickPos.y) / u);
            var tex = Art.Tex("circle_05");
            GUI.color = new Color(1, 1, 1, 0.25f);
            GUI.DrawTexture(new Rect(o.x - R, o.y - R, 2 * R, 2 * R), tex);
            GUI.color = new Color(1, 1, 1, 0.6f);
            GUI.DrawTexture(new Rect(k.x - R * 0.45f, k.y - R * 0.45f, R * 0.9f, R * 0.9f), tex);
            GUI.color = Color.white;
        }
    }

    // ---------- Статы ----------

    void Section(float x, float y, float w, string title, Color c)
    {
        Outline(new Rect(x, y, w, 50), title, Col(mid, c), 3);
        for (int i = 0; i < 8; i++)
            Rect(new Rect(x + i * w / 8, y + 54, w / 8, 2), new Color(c.r, c.g, c.b, 0.7f * (1 - i / 8f)));
    }

    void Row(float x, float y, float w, string label, string value, bool zebra)
    {
        if (zebra) Rect(new Rect(x - 8, y, w + 16, 38), new Color(1, 1, 1, 0.04f));
        Text(new Rect(x, y + 3, w, 34), label, Col(tiny, new Color(0.72f, 0.7f, 0.68f), TextAnchor.MiddleLeft), 2);
        Text(new Rect(x, y + 3, w, 34), value, Col(val, new Color(1f, 0.88f, 0.55f)), 2);
    }

    float statsScroll, statsH;

    static float TextH(string s, GUIStyle st, float w) => st.CalcHeight(new GUIContent(Plain(s)), w);

    void DrawStats(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.02f, 0.02f, 0.04f, 0.97f));
        Outline(new Rect(0, 30, W, 90), "Маг", Col(big, new Color(1f, 0.9f, 0.7f)), 4);
        ChipAt(W / 2, 122, Up(World.Stages[stage].title), new Color(0.75f, 0.7f, 0.6f));
        bool wide = W > 1500;
        float colW = wide ? W / 3 - 60 : W - 80;
        // всё ниже шапки прокручивается колесом/свайпом, если не влезает
        var view = new Rect(0, 170, W, H - 170);
        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel) { statsScroll += ev.delta.y * 30; ev.Use(); }
        statsScroll = TouchScroll(view, statsScroll);
        statsScroll = Mathf.Clamp(statsScroll, 0, Mathf.Max(0, statsH - view.height + 40));
        GUI.BeginGroup(view);
        float x = 40, y = 10 - statsScroll, top = y, bottom = y;

        Section(x, y, colW, "Характеристики", new Color(0.7f, 0.95f, 0.75f));
        y += 68;
        (string, string)[] rows =
        {
            ("Уровень", $"{level}  ·  {xp:0}/{xpNext:0}"),
            ("Здоровье", $"{player.hp:0} / {player.maxHp:0}"),
            ("Регенерация", $"{player.regen:0.0}/с"),
            ("Урон", $"+{(player.dmgMul - 1) * 100:0}%"),
            ("Скорость заклинаний", $"+{(1 / player.cdMul - 1) * 100:0}%"),
            ("Площадь", $"+{(player.area - 1) * 100:0}%"),
            ("Шанс крита", $"{player.crit * 100:0}%  ×2"),
            ("Броня", player.armor >= 0.8f ? "-80% (предел)" : $"-{player.armor * 100:0}%"),
            ("Шипы", $"{player.thorns * 100:0}%"),
            ("Вампиризм", $"{player.vamp * 100:0.#}%"),
            ("Скорость бега", $"{player.speed:0.0}"),
            ("Магнит", $"{player.magnet:0.0} м"),
            ("Опыт", $"×{player.xpMul:0.00}"),
            ("Воскрешения", $"{player.revives}"),
            ("Время  ·  убито", $"{(int)time / 60:00}:{(int)time % 60:00}  ·  {kills}"),
        };
        for (int i = 0; i < rows.Length; i++) { Row(x, y, colW, rows[i].Item1, rows[i].Item2, i % 2 == 0); y += 38; }
        bottom = Mathf.Max(bottom, y);

        if (wide) { x = W / 3 + 20; y = top; } else y += 24;
        Section(x, y, colW, $"Артефакты · {artifacts.Count}", new Color(1f, 0.8f, 0.4f));
        y += 68;
        if (artifacts.Count == 0) { Text(new Rect(x, y, colW, 70), "Найди сундук — маркер показывает его на краю экрана", Col(tiny, new Color(0.55f, 0.55f, 0.6f)), 2); y += 70; }
        foreach (var a in artifacts)
        {
            IconTex(new Rect(x, y, 56, 56), a.icon, a.color);
            Text(new Rect(x + 70, y - 2, colW - 70, 32), a.title, Col(tiny, a.color, TextAnchor.MiddleLeft), 2);
            float dh = TextH(a.desc, tiny, colW - 70);
            Text(new Rect(x + 70, y + 30, colW - 70, dh), Hl(a.desc), Col(tiny, new Color(0.75f, 0.74f, 0.72f), TextAnchor.UpperLeft), 2);
            y += Mathf.Max(70, 30 + dh + 14);
        }
        bottom = Mathf.Max(bottom, y);

        if (wide) { x = 2 * W / 3; y = top; } else y += 24;
        Section(x, y, colW, "Заклинания", new Color(0.6f, 0.8f, 1f));
        y += 68;
        foreach (var s in skills)
        {
            if (!s.spell || s.fusion) continue;
            bool known = s.level > 0;
            Icon(new Rect(x, y, 56, 56), s);
            Text(new Rect(x + 70, y - 4, colW - 70, 34), known ? s.title : $"{s.title}  <color=#77767F>— не изучено</color>",
                Col(tiny, known ? s.color : new Color(0.55f, 0.55f, 0.6f), TextAnchor.MiddleLeft), 2);
            Pips(x + 70, y + 32, s, false, 0);
            y += 64;
            if (s.superNames != null && known)
            {
                float bh;
                if (s.super >= 0)
                {
                    string line = $"{s.SuperName}: <color=#C9C6BE>{s.superDescs[s.super]}</color>";
                    bh = TextH(line, tiny, colW - 100);
                    Star(new Rect(x + 66, y - 2, 30, 30), Gold);
                    Text(new Rect(x + 100, y - 2, colW - 100, bh), line, Col(tiny, Gold, TextAnchor.UpperLeft), 2);
                }
                else
                {
                    string line = "ветки: " + string.Join(" · ", s.superNames);
                    bh = TextH(line, tiny, colW - 70);
                    Text(new Rect(x + 70, y - 2, colW - 70, bh), line, Col(tiny, new Color(0.5f, 0.5f, 0.56f), TextAnchor.UpperLeft), 2);
                }
                y += bh + 8;
            }
        }
        y += 12;
        Section(x, y, colW, "Слияния", Gold);
        y += 66;
        foreach (var s in skills)
        {
            if (!(s is Fusion f)) continue;
            var c = f.level > 0 ? f.color : f.Ready ? new Color(0.7f, 0.95f, 0.75f) : new Color(0.5f, 0.5f, 0.55f);
            string st = f.level > 0 ? "изучено" : f.Ready ? "доступно!" : $"{f.a.title} + {f.b.title}";
            string line = $"{f.title}  <color=#8E8C99>{st}</color>";
            float fh = TextH(line, tiny, colW);
            Text(new Rect(x, y, colW, fh), line, Col(tiny, c, TextAnchor.UpperLeft), 2);
            y += fh + 6;
        }
        bottom = Mathf.Max(bottom, y);
        GUI.EndGroup();
        statsH = bottom - top;
        if (statsH > view.height)
        {
            // полоска прокрутки справа
            float vh = view.height, th = vh * vh / statsH, ty = view.y + (vh - th) * statsScroll / Mathf.Max(1, statsH - vh + 40);
            RR(new Rect(W - 14, view.y, 6, vh), new Color(1, 1, 1, 0.06f), 3);
            RR(new Rect(W - 14, ty, 6, th), new Color(Gold.r, Gold.g, Gold.b, 0.5f), 3);
        }

        var close = new Rect(W - 236, 30, 200, 80);
        Frame(close, close.Contains(Event.current.mousePosition) ? Gold : new Color(0.6f, 0.5f, 0.35f), new Color(0.13f, 0.1f, 0.13f), 1);
        Outline(close, "Назад", Col(titleS, Color.white), 2);
        if (GUI.Button(close, GUIContent.none, GUIStyle.none)) ToggleStats();
    }

    // ---------- Конец забега ----------

    void DrawEnd(float W, float H)
    {
        bool won = state == State.Won;
        var c = won ? Gold : new Color(1f, 0.35f, 0.3f);
        Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.8f));
        GUI.color = new Color(c.r, c.g, c.b, 0.25f);
        GUI.DrawTexture(new Rect(W * 0.1f, H * 0.05f, W * 0.8f, H * 0.6f), Gen.Glow().texture);
        GUI.color = Color.white;
        string title = coopRun ? (won ? "Вы выжили!" : hostLost ? "Связь потеряна" : "Маги пали") : won ? "Ты выжил!" : "Маг пал";
        if (DrawNote(W, H)) return;   // окно отзыва поверх всего экрана конца
        if (!bot && Btn(new Rect(W - 264, 112, 240, 64), "Отзыв", Bone)) NoteOpen();   // под «Поделиться»: слева вверху — уровень
        MenuHeader(W, H * 0.24f, won ? "ПОБЕДА" : freeRun ? "СВОБОДНЫЙ ЗАБЕГ" : "КОНЕЦ ЗАБЕГА", title, c);

        float tw = Mathf.Min(260, (W - 120) / 3), gap = 24, x0 = (W - tw * 3 - gap * 2) / 2, ty = H * 0.24f + 190;
        Tile(new Rect(x0, ty, tw, 100), "ВРЕМЯ", $"{(int)time / 60:00}:{(int)time % 60:00}", new Color(0.55f, 0.75f, 1f));
        Tile(new Rect(x0 + tw + gap, ty, tw, 100), "УРОВЕНЬ", level.ToString(), new Color(0.7f, 0.95f, 0.75f));
        Tile(new Rect(x0 + 2 * (tw + gap), ty, tw, 100), "УБИТО", kills.ToString(), new Color(1f, 0.5f, 0.45f));

        if (runGold > 0 && (won || !freeRun)) ChipAt(W / 2, ty + 116, $"+{runGold} ЗОЛОТА  ·  ВСЕГО {Coins}", new Color(1f, 0.82f, 0.35f), true);
        float dropH = DrawRunDrops(W, ty + 160);
        if (!bot && Btn(new Rect(W - 264, 30, 240, 70), Time.unscaledTime - sharedAt < 2.5f ? "Скопировано" : "Поделиться", Gold)) ShareRun();

        var r = new Rect((W - 460) / 2, ty + 160 + dropH, 460, 110);
        bool hov = r.Contains(Event.current.mousePosition);
        // гость ждёт, пока хозяин запустит новый забег; «В меню» — остаться в комнате, но выйти из забега
        bool wait = Guest && !hostLost && Coop.InRoom;
        Frame(r, hov && !wait ? Gold : new Color(0.55f, 0.45f, 0.3f), new Color(0.14f, 0.08f, 0.07f, 0.95f), 1);
        Outline(r, wait ? "Ждём хозяина…" : "Ещё раз", Col(center, wait ? Soft(0.7f) : Color.white), 3);
        if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !wait)
            Restart(!(coopRun && Guest) ? false : true);
        string back = Town.home ? "В город" : "В меню";
        if (coopRun && Btn(new Rect((W - 460) / 2, r.yMax + 20, 460, 80), back, Bone)) Restart(true);
        // победа в одиночном забеге: можно остаться и играть без конца по времени
        // «Играть дальше» убрано: бесконечная игра — отдельный режим «Выживание»
        bool more = false;
        if (more && Btn(new Rect((W - 460) / 2, r.yMax + 20, 460, 80), "Играть дальше", Bone)) FreeContinue();
        if (!coopRun && !bot && Btn(new Rect((W - 460) / 2, r.yMax + (more ? 116 : 20), 460, 80), back, Bone)) Restart(true);
    }
}
