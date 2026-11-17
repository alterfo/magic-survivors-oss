using System.Collections.Generic;
using UnityEngine;

// Рынок (server/market.py): игроки продают друг другу вещи за золото. Магазин → вкладка «Рынок» — список лотов
// с фильтром; карточка вещи в сумке → «На рынок» — окно цены. Самое ценное здесь — заточенное оружие.
public partial class Game
{
    [System.Serializable] class MarketLot { public int lot, price, mine, created; public GearItem item; public string nick; }
    [System.Serializable] class MarketResp { public bool ok; public int gold, wrev, bought; public float fee; public GearItem[] items; public MarketLot[] lots; public string error; }
    [System.Serializable] class MarketPost { public string voter; public int id, price, lot; }

    MarketLot[] marketLots;
    float marketFee = 0.05f;
    bool marketBusy;
    string marketStatus;
    int marketFilter;           // 0 — всё, 1 — оружие, 2 — мои лоты
    float marketScroll;
    GearItem marketSell;        // окно «Выставить на рынок»
    int marketPrice;

    static readonly string[] MarketFilters = { "Всё", "Оружие", "Мои лоты" };

    void LoadMarket()
    {
        int gen = gearGen, rev = gearRev;
        StartCoroutine(Send("GET", "market?voter=" + Voter(), null, (code, body) =>
        {
            if (code != 200 || gen != gearGen) { if (code != 200) marketStatus = "Нет связи с рынком"; return; }
            // пока ждали, было действие с вещами — снимок сумки устарел: берём только лоты
            if (rev != gearRev || gearBusy || marketBusy)
            {
                var r = JsonUtility.FromJson<MarketResp>(body);
                if (r != null && r.ok) marketLots = r.lots ?? new MarketLot[0];
                return;
            }
            ApplyMarket(body, gen, walletGen, false);
        }));
    }

    void ApplyMarket(string body, int gen, int wgen, bool gold)
    {
        var r = JsonUtility.FromJson<MarketResp>(body);
        if (r == null || !r.ok) return;
        marketLots = r.lots ?? new MarketLot[0];
        if (r.fee > 0) marketFee = r.fee;
        ApplyGearResp(body, gen, wgen, gold);   // сумка и баланс приходят тем же ответом
    }

    void MarketAction(string what, MarketPost p)
    {
        if (marketBusy) return;
        if (!walletOk) { marketStatus = "Загружаю кошелёк — попробуй ещё раз"; LoadWallet(); return; }
        marketBusy = true;
        marketStatus = null;
        gearRev++;
        p.voter = Voter();
        int gen = gearGen, wgen = walletGen;
        StartCoroutine(Send("POST", "market/" + what, JsonUtility.ToJson(p), (code, body) =>
        {
            marketBusy = false;
            if (code != 200)
            {
                marketStatus = code == 0 ? "Нет связи с сервером" : JsonUtility.FromJson<MarketResp>(body)?.error ?? "Ошибка";
                if (code == 404) LoadMarket();   // лот ушёл — обновим список
                return;
            }
            ApplyMarket(body, gen, wgen, true);
            marketStatus = what == "buy" ? "Куплено — вещь в сумке" : what == "sell" ? "Выставлено на рынок" : "Лот снят — вещь в сумке";
            if (what == "sell") { marketSell = null; gearPick = null; }
        }));
    }

    // ---------- список лотов (вкладка магазина) ----------

    void DrawMarket(float W, float H, float top, float bottom)
    {
        bool modal = confirm != null || marketSell != null;
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        float fw = Mathf.Min(220, (W - 48 - 24) / 3), fx = (W - fw * 3 - 24) / 2;
        for (int i = 0; i < MarketFilters.Length; i++)
        {
            var fr = new Rect(fx + i * (fw + 12), top - 58, fw, 44);
            Chip(fr, Up(MarketFilters[i]), MetaGold, marketFilter == i);
            if (GUI.Button(fr, GUIContent.none, GUIStyle.none) && !modal) { marketFilter = i; marketScroll = 0; }
        }
        if (marketLots == null) { Text(new Rect(0, top + 40, W, 40), "Загружаю рынок…", Col(small, Soft(0.7f), TextAnchor.MiddleCenter), 2); return; }
        var list = new List<MarketLot>();
        foreach (var l in marketLots)
            if (l.item != null && GearValid(l.item) && (marketFilter == 0 || (marketFilter == 1 && IsWeapon(l.item)) || (marketFilter == 2 && l.mine == 1))) list.Add(l);
        if (list.Count == 0)
        {
            string e = marketFilter == 2 ? "У тебя нет лотов. Выставить вещь — «Герой» → сумка → вещь → «На рынок»" : "Пока пусто. Выставь свою вещь первым: «Герой» → сумка → вещь → «На рынок»";
            var st = new GUIStyle(Col(small, Soft(0.7f), TextAnchor.UpperCenter)) { wordWrap = true };
            Text(new Rect(40, top + 40, W - 80, 120), e, st, 2);
            return;
        }
        float bw = Mathf.Min(1100, W - 32), x0 = (W - bw) / 2, rh = 96, gap = 10;
        var view = new Rect(x0, top, bw, bottom - top);
        var ev = Event.current;
        if (!modal)
        {
            if (ev.type == EventType.ScrollWheel && view.Contains(ev.mousePosition)) { marketScroll += ev.delta.y * 90; ev.Use(); }
            marketScroll = TouchScroll(view, marketScroll);
        }
        marketScroll = Mathf.Clamp(marketScroll, 0, Mathf.Max(0, list.Count * (rh + gap) - view.height));
        GUI.BeginGroup(view);
        for (int i = 0; i < list.Count; i++)
        {
            var l = list[i];
            var r = new Rect(0, i * (rh + gap) - marketScroll, bw, rh);
            if (r.yMax < 0 || r.y > view.height) continue;
            var it = l.item;
            var c = GradeColors[it.grade];
            Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.94f), l.mine == 1 ? Gold : new Color(c.r, c.g, c.b, 0.5f), 12, l.mine == 1 ? 2 : 1);
            var ir = new Rect(r.x + 10, r.y + 8, rh - 16, rh - 16);
            GUI.DrawTexture(ir, IconT(ItemIcon(it)), ScaleMode.ScaleToFit, true, 0, Color.white, 0, 8);
            RRLine(ir, c, 2, 8);
            float bx = r.xMax - Mathf.Min(260, bw * 0.34f) - 10, tx = ir.xMax + 14, tw = bx - tx - 10;
            string name = GearName(it);
            Text(new Rect(tx, r.y + 6, tw, 34), name, Shrink(Col(small, Color.Lerp(c, Bone, 0.25f), TextAnchor.MiddleLeft), name, tw), 2);
            var s = SlotOf(it.slot);
            string st = StatLine(s.stat, MainValue(it));
            if (IsWeapon(it)) st += " · " + StatLine(KindOf(it).stat, WeaponBonus(it));
            float a = AffixValue(it);
            if (a > 0) st += " · " + StatLine(it.affix, a);
            Text(new Rect(tx, r.y + 38, tw, 28), st, Shrink(Col(ds, Soft(0.8f), TextAnchor.MiddleLeft), st, tw), 2);
            string who = (l.mine == 1 ? "твой лот" : "продаёт " + l.nick) + (IsWeapon(it) && GlowTier(it.enchant) > 0 ? $" · свечение {GlowNames[GlowTier(it.enchant)]}" : "");
            Text(new Rect(tx, r.y + 64, tw, 26), who, Shrink(Col(ds, IsWeapon(it) && GlowTier(it.enchant) > 0 ? GlowUi[GlowTier(it.enchant)] : Soft(0.55f), TextAnchor.MiddleLeft), who, tw), 2);
            var b = new Rect(bx, r.y + 18, r.xMax - 10 - bx, rh - 36);
            if (l.mine == 1)
            {
                if (Btn(b, marketBusy ? "…" : $"Снять · {l.price}", Bone) && !modal) MarketAction("cancel", new MarketPost { lot = l.lot });
            }
            else
            {
                bool can = walletOk && walletGold >= l.price;
                Plate(b, can ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.08f, 0.07f, 0.08f, 0.9f), can ? MetaGold : new Color(1, 1, 1, 0.12f), 8);
                string pt = can || !walletOk ? $"Купить · {l.price}" : $"{l.price} · нет {l.price - walletGold}";
                Text(b, marketBusy ? "…" : pt, Shrink(Col(val, can ? MetaGold : new Color(0.72f, 0.69f, 0.64f), TextAnchor.MiddleCenter), pt, b.width - 10), 2);
                if (can && GUI.Button(b, GUIContent.none, GUIStyle.none) && !modal) MarketAction("buy", new MarketPost { lot = l.lot });
            }
        }
        GUI.EndGroup();
    }

    // ---------- окно «Выставить на рынок» ----------

    // цена по умолчанию — втрое дороже разбора: так продавать выгоднее, чем разбирать
    void OpenSell(GearItem it)
    {
        marketSell = it;
        marketStatus = null;
        gearPick = null;   // карточка вещи закрывается: под окном цены её кнопки не должны ловить касания
        marketPrice = Mathf.Max(10, (int)(SalvageGold[it.grade] * (1 + it.enchant / 2f)) * 3);
    }

    void DrawSell(float W, float H)
    {
        if (marketSell == null) return;
        var it = marketSell;
        var c = GradeColors[it.grade];
        Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.65f));
        float w = Mathf.Min(640, W - 32), h = 470;
        var r = new Rect((W - w) / 2, (H - h) / 2, w, h);
        Plate(r, new Color(0.06f, 0.05f, 0.08f, 0.98f), MetaGold, 16, 2);
        string name = GearName(it);
        Outline(new Rect(r.x + 20, r.y + 18, w - 40, 46), name, Fit(name, w - 40, Color.Lerp(c, Bone, 0.3f)), 2);
        Text(new Rect(r.x, r.y + 70, w, 30), "ЦЕНА НА РЫНКЕ", Col(caps, Gold, TextAnchor.MiddleCenter), 2);
        Text(new Rect(r.x, r.y + 102, w, 60), $"{marketPrice}", Col(num, MetaGold, TextAnchor.MiddleCenter), 2);
        int fee = (int)(marketPrice * marketFee);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        Text(new Rect(r.x, r.y + 162, w, 30), $"Получишь {marketPrice - fee} золота, рынок берёт {Mathf.RoundToInt(marketFee * 100)}%", Col(ds, Soft(0.75f), TextAnchor.MiddleCenter), 2);
        // цена кнопками: ввод цифр есть только в браузере, а шаги понятны и на телефоне
        string[] steps = { "÷2", "−10%", "+10%", "×2" };
        float sw = (w - 40 - 3 * 10) / 4;
        for (int i = 0; i < steps.Length; i++)
        {
            if (!Btn(new Rect(r.x + 20 + i * (sw + 10), r.y + 202, sw, 64), steps[i], Bone)) continue;
            marketPrice = i switch
            {
                0 => marketPrice / 2,
                1 => Mathf.RoundToInt(marketPrice * 0.9f),
                2 => Mathf.Max(marketPrice + 1, Mathf.RoundToInt(marketPrice * 1.1f)),
                _ => marketPrice * 2,
            };
            marketPrice = Mathf.Clamp(marketPrice, 10, 500000);
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        if (Btn(new Rect(r.x + 20, r.y + 276, w - 40, 56), "Ввести цену цифрами", Bone * 0.85f))
            PhoneInput("Цена в золоте", 6, s => { if (int.TryParse(s, out var p)) marketPrice = Mathf.Clamp(p, 10, 500000); }, marketPrice.ToString(), 2);
#endif
        if (marketStatus != null) Text(new Rect(r.x, r.y + 336, w, 30), marketStatus, Col(ds, new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleCenter), 2);
        float bw = (w - 60) / 2;
        if (Btn(new Rect(r.x + 20, r.yMax - 96, bw, 78), marketBusy ? "…" : "Выставить", Gold))
            MarketAction("sell", new MarketPost { id = it.id, price = marketPrice });
        if (Btn(new Rect(r.xMax - 20 - bw, r.yMax - 96, bw, 78), "Отмена", Bone)) { marketSell = null; marketStatus = null; }
    }
}
