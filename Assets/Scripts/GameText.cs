using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// Типографика интерфейса: Philosopher — заголовки, Russo One — цифры, Fira Sans — текст; обводка, подсветка чисел, скруглённые плашки.
public partial class Game
{
    GUIStyle big, mid, small, tiny, center, timer, num, caps, heavy, titleL, titleS, val;
    static Font fDisplay, fBody, fHeavy, fNum;

    void Styles()
    {
        if (big != null) return;
        LoadOpts();
        fDisplay = Resources.Load<Font>("Fonts/Philosopher-Bold");
        fNum = Resources.Load<Font>("Fonts/RussoOne-Regular");
        fBody = Resources.Load<Font>("Fonts/FiraSans-SemiBold");
        fHeavy = Resources.Load<Font>("Fonts/FiraSans-ExtraBold");
        GUIStyle S(Font f, int size, TextAnchor a, bool wrap = false) => new(GUI.skin.label)
        {
            font = f, fontSize = size, alignment = a, wordWrap = wrap, richText = true,
            fontStyle = FontStyle.Normal, normal = { textColor = Color.white },
        };
        big = S(fDisplay, 76, TextAnchor.MiddleCenter);
        mid = S(fDisplay, 42, TextAnchor.MiddleLeft);
        titleL = S(fDisplay, 40, TextAnchor.MiddleCenter);
        titleS = S(fDisplay, 32, TextAnchor.MiddleCenter);
        center = S(fDisplay, 50, TextAnchor.MiddleCenter);
        small = S(fBody, 32, TextAnchor.UpperLeft, true);
        tiny = S(fBody, 25, TextAnchor.UpperLeft, true);
        caps = S(fHeavy, 18, TextAnchor.MiddleCenter);
        heavy = S(fNum, 38, TextAnchor.MiddleCenter);
        timer = S(fNum, 58, TextAnchor.MiddleCenter);
        num = S(fNum, 30, TextAnchor.MiddleCenter);
        val = S(fNum, 26, TextAnchor.MiddleRight);
        timer.normal.textColor = new Color(0.97f, 0.94f, 0.86f);
    }

    // Приглушённый текст: раньше Bone * 0.6 гасил и цвет, и прозрачность — на телефоне подписи пропадали
    // (проверка читаемости 17.11, «чтобы весь текст во всех окнах читался»). Теперь не темнее 80% и без прозрачности
    static Color Soft(float k) { float l = Mathf.Max(k, 0.8f); return new Color(Bone.r * l, Bone.g * l, Bone.b * l, 1); }

    // Телефон в портрете: интерфейс сжат до 760 условных точек на ширину (~0,5 CSS px), подписи капсом 18 выходили
    // в 9 px. Здесь они крупнее; кэши стилей сбрасываются при смене ориентации — копии держат старый кегль
    int stylesPortrait = -1;   // у экземпляра: после перезагрузки сцены Styles() создаёт caps заново (ревью Codex)
    // телефон в любой ориентации: условная точка меньше ~0,6 CSS px (портрет 390/760, альбом 390/820).
    // Считаем в CSS-пикселях: холст на телефоне плотнее (DPR до 1,5), и UiScale в пикселях холста завышен (ревью Codex)
    static bool SmallUi => (CssHeight > 0 ? UiScale * CssHeight / Screen.height : UiScale) < 0.62f;
    void PortraitType()
    {
        int p = SmallUi ? 1 : 0;
        if (p == stylesPortrait || caps == null) return;
        stylesPortrait = p;
        caps.fontSize = p == 1 ? 21 : 18;
        styleCache.Clear();
        shrinkCache.Clear();
    }

    static readonly Dictionary<(GUIStyle, Color, int), GUIStyle> styleCache = new();

    // Стили кэшируются: OnGUI вызывается несколько раз за кадр, новые GUIStyle каждый раз — лишний мусор
    static GUIStyle Col(GUIStyle s, Color c, TextAnchor? a = null)
    {
        var key = (s, c, a.HasValue ? (int)a.Value : -1);
        if (styleCache.TryGetValue(key, out var r)) return r;
        r = new GUIStyle(s) { normal = { textColor = c } };
        if (a.HasValue) r.alignment = a.Value;
        return styleCache[key] = r;
    }

    // Строка в одну линию по ширине: не влезает — кегль уменьшается ровно настолько, чтобы влезла (не мельче min).
    // Нужна везде, где ширина зависит от экрана: кнопки, карточки меню на телефоне, строки статуса.
    static readonly Dictionary<(GUIStyle, int), GUIStyle> shrinkCache = new();
    static readonly GUIContent shrinkTmp = new();

    static GUIStyle Shrink(GUIStyle st, string s, float w, int min = 15)
    {
        if (SmallUi) min = Mathf.Max(min, 19);   // в портрете мельче ~10 px не сжимаем — пусть лучше обрежется край
        shrinkTmp.text = Plain(s);
        bool wrap = st.wordWrap;
        st.wordWrap = false;
        float need = st.CalcSize(shrinkTmp).x;
        st.wordWrap = wrap;
        if (need <= w && !wrap) return st;
        int size = need <= w ? st.fontSize : Mathf.Max(min, Mathf.FloorToInt(st.fontSize * w / need));
        if (shrinkCache.TryGetValue((st, size), out var r)) return r;
        return shrinkCache[(st, size)] = new GUIStyle(st) { fontSize = size, wordWrap = false };
    }

    // Заголовок в ширину: если крупный кегль не влезает — берём помельче
    GUIStyle Fit(string s, float w, Color c) =>
        Shrink(Col(titleL.CalcSize(new GUIContent(s)).x <= w ? titleL : titleS, c), s, w, 18);

    static void Rect(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Art.White);
        GUI.color = old;
    }

    static void Frame(Rect r, Color border, Color fill, float w = 3) => Plate(r, fill, border, 12, w);

    // Скруглённые прямоугольники рисует сам IMGUI: borderRadius у DrawTexture, без своих текстур
    static void RR(Rect r, Color c, float rad) =>
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, 0, rad);

    static void RRLine(Rect r, Color c, float w, float rad) =>
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, w, rad);

    // Плашка: мягкая тень, заливка, блик сверху, тонкая кромка
    // Прокрутка списка пальцем или зажатой мышью. IMGUI в браузере на телефоне не присылает MouseDrag, поэтому
    // указатель читаем сами (раз в кадр). Если список тащили, отпускание не считается нажатием кнопки под пальцем.
    bool dragHeld, dragOn, dragMoved;
    float dragStartY, dragLastY;
    int dragFrame = -1;

    float TouchScroll(Rect view, float scroll)
    {
        var ev = Event.current;
        var ptr = UnityEngine.InputSystem.Pointer.current;
        if (ev.type == EventType.Repaint && ptr != null && dragFrame != Time.frameCount)
        {
            dragFrame = Time.frameCount;
            float u = UiScale;
            var sp = ptr.position.ReadValue();
            var gp = new Vector2(sp.x / u, (Screen.height - sp.y) / u);
            bool down = ptr.press.isPressed;
            if (down && !dragHeld) { dragOn = view.Contains(gp); dragMoved = false; dragStartY = dragLastY = gp.y; }
            else if (down && dragOn)
            {
                if (Mathf.Abs(gp.y - dragStartY) > 14) dragMoved = true;
                if (dragMoved) scroll -= gp.y - dragLastY;
                dragLastY = gp.y;
            }
            else if (!down) dragOn = false;
            dragHeld = down;
        }
        if (dragMoved && ev.type == EventType.MouseUp) GUIUtility.hotControl = 0;
        return scroll;
    }

    // Облегчённый интерфейс на телефонах (16.11): по телеметрии IMGUI там стоил 5,7 мс на кадр — втрое дороже всей
    // симуляции. Плашка — заливка и кромка без тени и блика, надпись — тень и текст без четырёх копий обводки:
    // рисунков вдвое меньше, на маленьком экране разница не видна. На ПК — ключ -liteui для замера.
    public static bool LiteUi;   // ставится в Game.Awake (Unity API нельзя звать из статического инициализатора)

    static void Plate(Rect r, Color fill, Color edge, float rad = 12, float w = 1)
    {
        if (LiteUi) { RR(r, fill, rad); RRLine(r, edge, w, rad); return; }
        RR(new Rect(r.x - 2, r.y + 4, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.4f * fill.a), rad + 2);
        RR(r, fill, rad);
        RR(new Rect(r.x + w, r.y + w, r.width - 2 * w, r.height * 0.45f), new Color(1, 1, 1, 0.03f * fill.a), rad - w);
        RRLine(r, edge, w, rad);
    }

    // Тень и обводка рисуются текстом без тегов, иначе <color> красит и их
    static readonly Dictionary<string, string> plainCache = new();
    static readonly Regex tagRx = new("<[^>]+>", RegexOptions.Compiled);

    static string Plain(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        if (!plainCache.TryGetValue(s, out var p)) plainCache[s] = p = tagRx.Replace(s, "");
        return p;
    }

    static void Text(Rect r, string s, GUIStyle st) => Text(r, s, st, 2);

    static void Text(Rect r, string s, GUIStyle st, float sh)
    {
        var c = st.normal.textColor;
        // тень строго вниз и короткая: длинная диагональная читается как двоение
        st.normal.textColor = new Color(0, 0, 0, 0.7f * c.a);
        GUI.Label(new Rect(r.x, r.y + sh * 0.5f + 0.5f, r.width, r.height), Plain(s), st);
        st.normal.textColor = c;
        GUI.Label(r, s, st);
    }

    // Обводка для заголовков и крупных цифр: тень вниз и четыре тёмные копии по диагоналям
    static void Outline(Rect r, string s, GUIStyle st, float w = 3)
    {
        var c = st.normal.textColor;
        var p = Plain(s);
        w *= 0.5f;
        if (LiteUi)
        {
            st.normal.textColor = new Color(0.04f, 0.02f, 0.06f, 0.85f * c.a);
            GUI.Label(new Rect(r.x + w * 0.6f, r.y + w + 1.5f, r.width, r.height), p, st);
            st.normal.textColor = c;
            GUI.Label(r, s, st);
            return;
        }
        st.normal.textColor = new Color(0, 0, 0, 0.5f * c.a);
        GUI.Label(new Rect(r.x, r.y + w + 2, r.width, r.height), p, st);
        st.normal.textColor = new Color(0.04f, 0.02f, 0.06f, c.a);
        for (int i = 0; i < 4; i++)
            GUI.Label(new Rect(r.x + (i < 2 ? -w : w), r.y + (i % 2 == 0 ? -w : w), r.width, r.height), p, st);
        st.normal.textColor = c;
        GUI.Label(r, s, st);
    }

    // Числа в описаниях подсвечены: «было → стало» серым и зелёным, остальные — золотом
    static readonly Dictionary<string, string> hlCache = new();
    static readonly Regex numRx = new(@"(\d+(?:[.,]\d+)?) → (\d+(?:[.,]\d+)?)|[+−×]?\d+(?:[.,]\d+)?%?", RegexOptions.Compiled);

    static string Hl(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (!hlCache.TryGetValue(s, out var r))
            hlCache[s] = r = numRx.Replace(s, m => m.Groups[1].Success
                ? $"<color=#8E8C99>{m.Groups[1].Value}</color> → <color=#86FF8F>{m.Groups[2].Value}</color>"
                : $"<color=#FFD45E>{m.Value}</color>");
        return r;
    }

    static readonly Dictionary<string, string> upCache = new();
    static string Up(string s)
    {
        if (!upCache.TryGetValue(s, out var r)) upCache[s] = r = s.ToUpperInvariant();
        return r;
    }

    float CapsW(string s) => caps.CalcSize(new GUIContent(s)).x + 26;

    // Плашка: тёмная заливка в цвет, светлые кромки, капс внутри
    void Chip(Rect r, string s, Color c, bool solid = false)
    {
        Plate(r, solid ? c : new Color(c.r * 0.1f, c.g * 0.1f, c.b * 0.1f, 0.92f), new Color(c.r, c.g, c.b, solid ? 1 : 0.45f), 6, 1);
        var tc = solid ? new Color(0.1f, 0.06f, 0.02f) : Color.Lerp(c, new Color(0.86f, 0.8f, 0.68f), 0.4f);
        if (solid) GUI.Label(r, s, Col(caps, tc, TextAnchor.MiddleCenter));
        else Text(r, s, Col(caps, tc, TextAnchor.MiddleCenter), 2);
    }

    Rect ChipAt(float cx, float y, string s, Color c, bool solid = false)
    {
        float w = CapsW(s);
        var r = new Rect(cx - w / 2, y, w, 30);
        Chip(r, s, c, solid);
        return r;
    }

    // Разделитель с ромбом посередине, линия гаснет к краям
    static void Divider(float cx, float y, float w, Color c)
    {
        const int n = 8;
        for (int i = 0; i < n; i++)
        {
            float seg = w / 2 / n, a = 0.5f * (1 - i / (float)n);
            var col = new Color(c.r, c.g, c.b, a);
            Rect(new Rect(cx + 12 + i * seg, y, seg, 1), col);
            Rect(new Rect(cx - 12 - (i + 1) * seg, y, seg, 1), col);
        }
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(45, new Vector2(cx, y + 1));
        Rect(new Rect(cx - 4, y - 3.5f, 8, 8), c);
        GUI.matrix = m;
    }

    static void Star(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Art.Tex("star_06"));
        GUI.color = Color.white;
    }

    static void Pips(float x, float y, Skill s, bool next, float t)
    {
        RR(new Rect(x - 8, y - 8, PipsW(s) + 16, 38), new Color(0.02f, 0.02f, 0.04f, 0.8f), 19);
        for (int i = 0; i < s.max; i++)
        {
            var r = new Rect(x + i * 30, y, 22, 22);
            bool on = i < s.level;
            bool nx = next && i == s.level;
            RR(r, new Color(1, 1, 1, 0.1f), 6);
            if (on) RR(new Rect(r.x + 2, r.y + 2, 18, 18), s.color, 5);
            else if (nx) RR(new Rect(r.x + 2, r.y + 2, 18, 18), new Color(s.color.r, s.color.g, s.color.b, 0.45f + 0.45f * Mathf.Sin(t * 8)), 5);
        }
        if (s.superNames != null)
            Star(new Rect(x + s.max * 30 + 4, y - 6, 34, 34), s.super >= 0 ? new Color(1f, 0.85f, 0.3f) : new Color(1, 1, 1, 0.25f));
    }

    static float PipsW(Skill s) => s.max * 30 - 8 + (s.superNames != null ? 42 : 0);
}
