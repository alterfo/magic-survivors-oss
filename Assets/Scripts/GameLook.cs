using UnityEngine;

// Окно героя → вкладка «Облик»: живая модель и полки по слотам облика (шляпа, мантия, вид посоха, свечение).
// Игроки путали одежду из магазина со снаряжением: у одной вещи было два экрана, и не было видно, что твоё,
// что надето и на что хватит золота. Здесь всё в одном месте, купленное надевается сразу и видно на модели.
// Облик — только внешний вид; силу дают вещи во вкладке «Снаряжение».
public partial class Game
{
    static readonly (string slot, string title)[] LookSlots =
    {
        ("hat", "Шляпа"), ("robe", "Мантия"), ("staff", "Вид посоха"), ("glow", "Свечение посоха"),
    };

    float lookScroll;
    bool lookAffordable;   // фильтр: только своё и то, на что хватает золота

    void DrawLook(float W, float H)
    {
        bool port = W < H;
        float top = port ? 116 : 100, bottom = H - 130;
        bool modal = confirm != null;
        if (builtHero != "mage")
        {
            Text(new Rect(24, top + 80, W - 48, 50), "Облик меняется только у мага", Col(small, Soft(0.8f), TextAnchor.MiddleCenter), 2);
            float b0 = Mathf.Min(520, W - 48);
            if (Btn(new Rect((W - b0) / 2, top + 150, b0, 80), "Выбрать мага", Gold) && !modal) heroTab = 2;
            return;
        }
        if (Event.current.type == EventType.Repaint) PortraitTick();

        // портрет: в альбоме слева во всю высоту, в портрете — полосой сверху
        Rect pr, shelves;
        if (port)
        {
            // кадр портрета 2:3 — шире не растягиваем, иначе обрезка срезает мага по пояс
            float ph = Mathf.Min(360, (bottom - top) * 0.36f), pw = Mathf.Min(W - 32, ph * 0.72f);
            pr = new Rect((W - pw) / 2, top, pw, ph);
            shelves = new Rect(16, pr.yMax + 12, W - 32, bottom - pr.yMax - 12);
        }
        else
        {
            float pw = Mathf.Min(460, W * 0.3f);
            pr = new Rect(Mathf.Max(24, W * 0.03f), top, pw, bottom - top);
            shelves = new Rect(pr.xMax + 24, top, W - pr.xMax - 24 - Mathf.Max(24, W * 0.03f), bottom - top);
        }
        Plate(pr, new Color(0.03f, 0.02f, 0.045f, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 0.4f), 14);
        if (portraitRT != null) GUI.DrawTexture(new Rect(pr.x + 2, pr.y + 2, pr.width - 4, pr.height - 4), portraitRT, ScaleMode.ScaleAndCrop, false, 0, Color.white, 0, 12);
        if (Event.current.type == EventType.Repaint && !portraitDrag) portraitYaw += Time.unscaledDeltaTime * 8;
        var ev = Event.current;
        if (!modal && ev.type == EventType.MouseDown && pr.Contains(ev.mousePosition)) { portraitDrag = true; portraitDragX = ev.mousePosition.x; }
        if (portraitDrag && ev.type == EventType.MouseDrag) { portraitYaw += (ev.mousePosition.x - portraitDragX) * 0.6f; portraitDragX = ev.mousePosition.x; }
        if (ev.type == EventType.MouseUp) portraitDrag = false;
        string gold = walletOk ? $"ЗОЛОТО  {walletGold}" : "НЕТ СВЯЗИ";
        Text(new Rect(pr.x, pr.yMax - 44, pr.width, 34), gold, Col(caps, MetaGold, TextAnchor.MiddleCenter), 2);

        // шапка полок: подсказка и фильтр
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        float fw = Mathf.Min(300, shelves.width * 0.45f);
        var fr = new Rect(shelves.xMax - fw, shelves.y, fw, 46);
        Chip(fr, lookAffordable ? "МОЁ И ПО КАРМАНУ" : "ВСЁ ПОДРЯД", Gold, lookAffordable);
        if (GUI.Button(fr, GUIContent.none, GUIStyle.none) && !modal) { lookAffordable = !lookAffordable; lookScroll = 0; }
        const string hint = "Только вид. Сила — во вкладке «Снаряжение»";
        Text(new Rect(shelves.x, shelves.y, shelves.width - fw - 12, 46), hint, Shrink(Col(ds, Soft(0.65f), TextAnchor.MiddleLeft), hint, shelves.width - fw - 12), 2);

        var view = new Rect(shelves.x, shelves.y + 56, shelves.width, shelves.height - 56);
        float gap = 12, cw = port ? (view.width - 2 * gap) / 3 : Mathf.Min(190, (view.width - 5 * gap) / 6), ch = port ? 214 : 226;
        int cols = Mathf.Max(1, (int)((view.width + gap) / (cw + gap)));
        // высота содержимого: заголовок полки + ряды карточек
        float total = 0;
        foreach (var (slot, _) in LookSlots) total += 44 + Rows(LookItems(slot).Count, cols) * (ch + gap) + 10;
        if (!modal)
        {
            if (ev.type == EventType.ScrollWheel && view.Contains(ev.mousePosition)) { lookScroll += ev.delta.y * 90; ev.Use(); }
            lookScroll = TouchScroll(view, lookScroll);
        }
        lookScroll = Mathf.Clamp(lookScroll, 0, Mathf.Max(0, total - view.height));

        GUI.BeginGroup(view);
        float y = -lookScroll;
        foreach (var (slot, title) in LookSlots)
        {
            var items = LookItems(slot);
            var on = Cosmetics.Get(Wearing(slot));
            bool armed = WornWeapon().kind >= 0;
            string head = armed && slot == "staff" ? $"{Up(title)}  <color=#8E8C99>· в руке оружие — вид посоха виден без него</color>"
                : armed && slot == "glow" ? $"{Up(title)}  <color=#8E8C99>· у оружия своё свечение — от заточки</color>"
                : $"{Up(title)}  <color=#8E8C99>· надето: {on?.title ?? "—"}</color>";
            Text(new Rect(0, y, view.width, 40), head, Shrink(Col(caps, Gold, TextAnchor.MiddleLeft), head, view.width), 2);
            y += 44;
            if (items.Count == 0) { Text(new Rect(0, y, view.width, 40), "Пока нечего — копи золото", Col(ds, Soft(0.55f), TextAnchor.MiddleLeft), 2); y += 50; continue; }
            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rect(i % cols * (cw + gap), y + i / cols * (ch + gap), cw, ch);
                if (r.yMax >= 0 && r.y <= view.height) DrawLookCard(r, items[i], modal);
            }
            y += Rows(items.Count, cols) * (ch + gap) + 10;
        }
        GUI.EndGroup();
        if (total > view.height + 1)
        {
            float bh = view.height * view.height / total;
            RR(new Rect(view.xMax + 6, view.y, 6, view.height), new Color(1, 1, 1, 0.08f), 3);
            RR(new Rect(view.xMax + 6, view.y + (view.height - bh) * lookScroll / (total - view.height), 6, bh), new Color(Gold.r, Gold.g, Gold.b, 0.8f), 3);
        }
    }

    static int Rows(int n, int cols) => (n + cols - 1) / cols;

    // полка слота: всё из каталога, а с фильтром — только своё и то, на что хватает золота
    System.Collections.Generic.List<ShopItem> LookItems(string slot)
    {
        var list = new System.Collections.Generic.List<ShopItem>();
        foreach (var it in ShopList(2))
        {
            if (it.slot != slot) continue;
            bool have = it.price == 0 || Owns(it.id);
            if (lookAffordable && !have && !(it.price > 0 && walletOk && walletGold >= it.price)) continue;
            list.Add(it);
        }
        return list;
    }

    void DrawLookCard(Rect r, ShopItem it, bool modal)
    {
        bool have = it.price == 0 || Owns(it.id), wearing = have && Wearing(it.slot) == it.cos;
        var edge = wearing ? it.color : have ? new Color(it.color.r, it.color.g, it.color.b, 0.45f) : new Color(1, 1, 1, 0.12f);
        Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.94f), edge, 12, wearing ? 2 : 1);
        float s = Mathf.Min(r.width - 40, r.height - 116);
        IconTex(new Rect(r.center.x - s / 2, r.y + 10, s, s), it.icon, have ? it.color : new Color(0.45f, 0.43f, 0.5f));
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        Text(new Rect(r.x + 6, r.yMax - 100, r.width - 12, 40), it.title, Shrink(Col(ds, Color.Lerp(it.color, Bone, 0.8f), TextAnchor.MiddleCenter), it.title, r.width - 12), 2);
        var b = new Rect(r.x + 10, r.yMax - 54, r.width - 20, 44);
        if (wearing) Chip(b, "НАДЕТО", it.color, true);
        else if (have) { if (Btn(b, "Надеть", Bone) && !modal) Wear(it); }
        else if (it.price < 0)
        {
            // трофей: за золото не купить — упокой павших магов (GameFallen.cs)
            string t = it.trophy == 1 ? "1 ПАВШИЙ" : $"{it.trophy} ПАВШИХ";
            Plate(b, new Color(0.04f, 0.1f, 0.09f, 0.9f), new Color(Forbidden.Soul.r, Forbidden.Soul.g, Forbidden.Soul.b, 0.5f), 8);
            Text(b, t, Shrink(Col(ds, Forbidden.Soul, TextAnchor.MiddleCenter), t, b.width - 8), 2);
        }
        else
        {
            bool can = walletOk && walletGold >= it.price;
            Plate(b, can ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.08f, 0.07f, 0.08f, 0.9f), can ? MetaGold : new Color(1, 1, 1, 0.12f), 8);
            string t = can ? $"{it.price}" : walletOk ? $"{it.price} · нет {it.price - walletGold}" : $"{it.price}";
            Text(b, t, Shrink(Col(val, can ? MetaGold : new Color(0.72f, 0.69f, 0.64f), TextAnchor.MiddleCenter), t, b.width - 8), 2);
            if (GUI.Button(b, GUIContent.none, GUIStyle.none) && !modal) { confirm = it; buyStatus = null; }
        }
    }
}
