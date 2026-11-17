using System.Collections.Generic;
using UnityEngine;

// Золото между забегами и постоянная прокачка героя (PlayerPrefs — в браузере переживает перезагрузку),
// плюс бесконечные карты: когда всё прокачано, уровни продолжают давать по чуть-чуть статов.
public partial class Game
{
    // ---------- Постоянная прокачка ----------

    public class MetaDef
    {
        public string id, title, icon, per;
        public int max, cost;
        public Color color;
        public System.Action<Player, int> apply;
    }

    static readonly Color MetaGold = new(1f, 0.82f, 0.35f);

    public static readonly MetaDef[] Metas =
    {
        new() { id = "dmg", title = "Сила", icon = "might", per = "+5% урона", max = 10, cost = 40, color = MetaGold, apply = (p, n) => p.dmgMul += 0.05f * n },
        new() { id = "hp", title = "Здоровье", icon = "vitality", per = "+10 здоровья", max = 10, cost = 30, color = new Color(1f, 0.45f, 0.5f), apply = (p, n) => { p.maxHp += 10 * n; p.hp = p.maxHp; } },
        new() { id = "regen", title = "Регенерация", icon = "regen", per = "+0.2 здоровья/с", max = 5, cost = 50, color = new Color(0.5f, 1f, 0.5f), apply = (p, n) => p.regen += 0.2f * n },
        new() { id = "cd", title = "Спешка", icon = "haste", per = "чары на 3% быстрее", max = 5, cost = 60, color = MetaGold, apply = (p, n) => p.cdMul *= Mathf.Pow(0.97f, n) },
        new() { id = "area", title = "Размах", icon = "area", per = "+4% площади", max = 5, cost = 50, color = MetaGold, apply = (p, n) => p.area += 0.04f * n },
        new() { id = "speed", title = "Сапоги", icon = "boots", per = "+3% к бегу", max = 5, cost = 40, color = MetaGold, apply = (p, n) => p.speed *= 1 + 0.03f * n },
        new() { id = "magnet", title = "Магнит", icon = "magnet", per = "+10% радиуса сбора", max = 5, cost = 30, color = MetaGold, apply = (p, n) => p.magnet *= 1 + 0.1f * n },
        new() { id = "xp", title = "Мудрость", icon = "a_book", per = "+5% опыта", max = 5, cost = 50, color = MetaGold, apply = (p, n) => p.xpMul += 0.05f * n },
        new() { id = "crit", title = "Меткость", icon = "precision", per = "+2% шанс крита", max = 5, cost = 50, color = MetaGold, apply = (p, n) => p.crit += 0.02f * n },
        new() { id = "gold", title = "Жадность", icon = "a_clover", per = "+10% золота за забег", max = 5, cost = 60, color = MetaGold, apply = (p, n) => p.goldMul += 0.1f * n },
        new() { id = "revive", title = "Перо феникса", icon = "a_phoenix", per = "одно воскрешение в каждом забеге", max = 1, cost = 600, color = new Color(1f, 0.6f, 0.25f), apply = (p, n) => p.revives += n },
    };

    // баланс с сервера (GameShop.cs); пока его нет — локальный, он перенесётся при первой связи
    static int Coins => walletOk ? walletGold : PlayerPrefs.GetInt("gold");
    static int MetaLevel(MetaDef m) => PlayerPrefs.GetInt("meta." + m.id);
    // как meta_price в server/wallet.py: первый уровень — cost, дальше дороже с каждым (последний — втрое против линейной цены)
    static int MetaCost(MetaDef m) { int l = MetaLevel(m) + 1; return Sale(m.cost * l * (l + 3) / 4); }

    void ApplyMeta()
    {
        foreach (var m in Metas)
        {
            // лаборатория баланса задаёт прокачку сама: доля от максимума, а не то, что куплено на этом устройстве
            int n = LabOn ? Mathf.RoundToInt(m.max * lab.meta) : MetaLevel(m);
            if (n > 0) m.apply(player, n);
        }
    }

    int runGold = -1;

    // золото за забег: убийства, время, уровень; победа — ×1.5
    int RunGold(bool won) => Mathf.RoundToInt((kills * 0.08f + time / 8f + level * 2f) * (won ? 1.5f : 1f) * player.goldMul * CurDiff.gold);

    void AwardGold(bool won)
    {
        if (bot || runGold >= 0) return;
        runGold = RunGold(won);
        // сразу показываем новый баланс, сервер ответит точным (с потолком)
        // без кошелька локальное золото не трогаем: забег лежит в очереди (GameRunSave.cs) и сервер начислит его сам —
        // иначе первая синхронизация перенесла бы награду и с локальным золотом, и с забегом (дважды)
        if (walletOk) walletGold += runGold;
        PostRun(won);
        SendTelemetry(won ? "won" : "dead");
    }

    float metaScroll;

    void DrawMeta(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.88f));
        MenuHeader(W, 50, SalePct > 0 ? $"СКИДКА СЕГОДНЯ −{SalePct}% — ЗА УБИТЫХ ВСЕМИ" : "ЗОЛОТО КОПИТСЯ ЗА ЗАБЕГИ", "Прокачка героя", Gold);
        ChipAt(W / 2, 210, walletOk ? $"ЗОЛОТО  {Coins}" : "НЕТ СВЯЗИ — ПОКУПКИ НЕДОСТУПНЫ", MetaGold, true);

        bool port = W < H;
        int cols = port ? 2 : 3;
        float gap = 16, top = 260, bottom = H - 130;
        float cw = Mathf.Min(420, (W - 48 - gap * (cols - 1)) / cols), ch = 150, x0 = (W - (cw * cols + gap * (cols - 1))) / 2;
        int rows = (Metas.Length + cols - 1) / cols;
        var view = new Rect(0, top, W, bottom - top);
        var ev = Event.current;
        if (ev.type == EventType.ScrollWheel) { metaScroll += ev.delta.y * 110; ev.Use(); }
        metaScroll = TouchScroll(view, metaScroll);
        metaScroll = Mathf.Clamp(metaScroll, 0, Mathf.Max(0, rows * (ch + gap) - view.height));
        GUI.BeginGroup(view);
        for (int i = 0; i < Metas.Length; i++)
        {
            var m = Metas[i];
            var r0 = new Rect(x0 + i % cols * (cw + gap), i / cols * (ch + gap) - metaScroll, cw, ch);
            // перо феникса — расходник: штучная покупка в запас, а не уровень прокачки (у купивших «навсегда» — оно)
            if (m.id == "revive" && MetaLevel(m) == 0) { DrawFeatherCard(r0, m); continue; }
            int lv = MetaLevel(m), cost = MetaCost(m);
            bool maxed = lv >= m.max, can = !maxed && walletOk && !buying && Coins >= cost;
            var r = r0;
            Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.94f), lv > 0 ? new Color(m.color.r, m.color.g, m.color.b, 0.5f) : new Color(1, 1, 1, 0.12f), 12);
            IconTex(new Rect(r.x + 30, r.y + 36, 64, 64), m.icon, m.color);
            float tx = r.x + 120, tw = r.width - 132;
            Text(new Rect(tx, r.y + 12, tw, 36), m.title, Shrink(Col(titleS, Color.Lerp(m.color, Bone, 0.5f), TextAnchor.MiddleLeft), m.title, tw), 2);
            Text(new Rect(tx, r.y + 48, tw, 30), m.per, Shrink(Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(0.78f, 0.75f, 0.7f), TextAnchor.MiddleLeft), m.per, tw), 2);
            // уровни: точки; на узкой карточке шаг сжимается, чтобы ряд не уходил под кнопку цены
            float step = Mathf.Min(22, (r.xMax - 140 - tx) / m.max), dot = Mathf.Max(6, step - 6);
            for (int k = 0; k < m.max; k++)
                RR(new Rect(tx + k * step, r.y + 86, dot, 16), k < lv ? m.color : new Color(1, 1, 1, 0.12f), 4);
            var b = new Rect(r.xMax - 132, r.yMax - 50, 120, 40);
            if (maxed) Chip(b, m.id == "revive" ? "НАВСЕГДА" : "МАКС", m.color);
            else
            {
                bool hov = can && b.Contains(Event.current.mousePosition);
                Plate(b, can ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.08f, 0.07f, 0.08f, 0.9f), can ? (hov ? Color.white : MetaGold) : new Color(1, 1, 1, 0.12f), 8);
                Text(b, $"{cost}", Col(val, can ? MetaGold : new Color(0.72f, 0.69f, 0.64f), TextAnchor.MiddleCenter), 2);
                if (can && GUI.Button(b, GUIContent.none, GUIStyle.none))
                    Buy("meta." + m.id, cost);
            }
        }
        GUI.EndGroup();
        float bw = Mathf.Min(460, W - 80);
        if (buyStatus != null) Text(new Rect(0, H - 150, W, 36), buyStatus, Col(small, new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleCenter), 2);
        if (Btn(new Rect((W - bw) / 2, H - 112, bw, 86), "Назад", Bone)) menuTab = 0;
    }

    void DrawFeatherCard(Rect r, MetaDef m)
    {
        int n = Feathers;
        int fp = Sale(FeatherPrice);
        bool full = n >= FeatherMax, can = !full && walletOk && !buying && Coins >= fp;
        Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.94f), n > 0 ? new Color(m.color.r, m.color.g, m.color.b, 0.5f) : new Color(1, 1, 1, 0.12f), 12);
        IconTex(new Rect(r.x + 30, r.y + 36, 64, 64), m.icon, m.color);
        float tx = r.x + 120, tw = r.width - 132;
        string title = n > 0 ? $"{m.title} ×{n}" : m.title;
        Text(new Rect(tx, r.y + 12, tw, 36), title, Shrink(Col(titleS, Color.Lerp(m.color, Bone, 0.5f), TextAnchor.MiddleLeft), title, tw), 2);
        const string per = "спасёт в одном забеге, сгорит";
        Text(new Rect(tx, r.y + 48, tw, 30), per, Shrink(Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(0.78f, 0.75f, 0.7f), TextAnchor.MiddleLeft), per, tw), 2);
        string sub = n > 0 ? "возьмёшь в следующий забег" : "в запасе нет";
        Text(new Rect(tx, r.y + 80, r.xMax - 140 - tx, 28), sub, Shrink(Col(tinyS, n > 0 ? m.color : Soft(0.55f), TextAnchor.MiddleLeft), sub, r.xMax - 140 - tx), 2);
        var b = new Rect(r.xMax - 132, r.yMax - 50, 120, 40);
        if (full) { Chip(b, "ПОЛНО", m.color); return; }
        bool hov = can && b.Contains(Event.current.mousePosition);
        Plate(b, can ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.08f, 0.07f, 0.08f, 0.9f), can ? (hov ? Color.white : MetaGold) : new Color(1, 1, 1, 0.12f), 8);
        Text(b, $"+1 · {fp}", Col(val, can ? MetaGold : new Color(0.72f, 0.69f, 0.64f), TextAnchor.MiddleCenter), 2);
        if (can && GUI.Button(b, GUIContent.none, GUIStyle.none)) Buy("feather", fp);
    }

    // ---------- Бесконечные карты после полной прокачки ----------

    List<Skill> endless;

    List<Skill> Endless()
    {
        if (endless != null) return endless;
        var g = MetaGold;
        endless = new List<Skill>
        {
            new Passive("Мощь", "+5% урона всем заклинаниям", "might", p => p.dmgMul += 0.05f, g) { max = 9999 },
            new Passive("Ускорение", "Заклинания на 3% быстрее", "haste", p => p.cdMul *= 0.97f, g) { max = 9999 },
            new Passive("Закалка", "+15 к здоровью и лечение", "vitality", p => { p.maxHp += 15; p.hp = p.maxHp; }, new Color(1f, 0.4f, 0.45f)) { max = 9999 },
            new Passive("Охват", "+5% к площади заклинаний", "area", p => p.area += 0.05f, g) { max = 9999 },
            new Passive("Удача", "+2% шанс критического удара", "precision", p => p.crit += 0.02f, g) { max = 9999 },
            new Passive("Жадность", "+10% золота в конце забега", "a_clover", p => p.goldMul += 0.1f, g) { max = 9999 },
        };
        return endless;
    }
}
