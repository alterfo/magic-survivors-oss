using System.Collections.Generic;
using UnityEngine;

// Кошелёк и магазин. Золото и покупки живут на сервере (server/wallet.py): PlayerPrefs чистится вместе с браузером
// и легко правится руками. Без связи играть можно, а покупать — нет; локальное золото переносится при первом входе.
public partial class Game
{
    [System.Serializable] class WItem { public string id; public int level; }
    [System.Serializable] class WalletResp { public bool ok; public int gold, wrev; public long crev; public WItem[] items; public string error, prefs; }
    [System.Serializable] class WalletPost { public string voter; public int gold; public MetaLevels meta; }
    [System.Serializable] class BuyPost { public string voter, item; public int price; }   // price — показанная цена
    // JsonUtility не умеет словари — поля по id прокачки (GameMeta.Metas)
    [System.Serializable] class MetaLevels { public int dmg, hp, regen, cd, area, speed, magnet, xp, crit, gold, revive; }

    // переживают перезагрузку сцены — кошелёк грузится один раз за сессию
    static bool walletOk;
    static int walletGold;
    static int walletRev = -1;   // ревизия кошелька с сервера (заработано + потрачено): ответ старше уже принятого не применяем

    // поколение профиля: выход/вход/перезагрузка кошелька его сдвигают — ответы, отправленные раньше, не применяются
    static int walletGen;

    static void ResetWallet()
    {
        walletOk = false;
        walletRev = -1;
        walletGen++;
        prefsSent = null;   // настройки нового профиля — с его кошельком
        prefsBusy = false;
    }

    // баланс из ответа сервера — только если ответ этого профиля и не старее уже показанного
    static void SetGold(int gold, int rev, int gen)
    {
        if (gen != walletGen || !walletOk || rev < walletRev) return;
        walletRev = rev;
        walletGold = gold;
    }
    static readonly Dictionary<string, int> owned = new();

    static bool Owns(string item) => showcase || string.IsNullOrEmpty(item) || owned.ContainsKey(item);   // showcase — витрина ?ui=show (только localhost)

    void ApplyWallet(string body, int gen)
    {
        if (gen != walletGen) return;   // ответ прежнего профиля
        var w = JsonUtility.FromJson<WalletResp>(body);
        if (w == null || !w.ok) return;
        // купленное на сервере только прибавляется: снимок вливаем по максимуму уровня, даже если по золоту он устарел
        // (покупка и заточка могут вернуться не по порядку — иначе оплаченная прокачка пропала бы до перезагрузки)
        if (!walletOk) owned.Clear();
        // расходники (перья, напитки) берём только из снимка не старше последнего применённого (crev растёт с каждой
        // покупкой и списанием) и за вычетом своих ещё не подтверждённых списаний: иначе запоздавший ответ прятал бы
        // оплаченный напиток или возвращал выпитый
        bool consFresh = !walletOk || w.crev >= consRev;
        if (consFresh)
        {
            consRev = w.crev;
            foreach (var id in new List<string>(owned.Keys)) if (IsConsumable(id)) owned.Remove(id);
        }
        foreach (var it in w.items ?? new WItem[0])
        {
            if (!IsConsumable(it.id)) owned[it.id] = Mathf.Max(owned.GetValueOrDefault(it.id), it.level);   // купленное только прибавляется
            else if (consFresh) owned[it.id] = Mathf.Max(0, it.level - PendingSpends(it.id));
        }
        if (!walletOk || w.wrev >= walletRev)
        {
            walletRev = w.wrev;
            walletGold = w.gold;
        }
        bool firstLoad = !walletOk;
        walletOk = true;
        if (firstLoad) ApplyServerPrefs(w.prefs);   // наряд и настройки с сервера (GamePrefs.cs)
        // зеркало в PlayerPrefs: без связи прокачка всё равно применится
        foreach (var m in Metas) PlayerPrefs.SetInt("meta." + m.id, owned.TryGetValue("meta." + m.id, out var n) ? n : 0);
        PlayerPrefs.SetInt("gold", walletGold);
        PlayerPrefs.SetInt("wallet.done", 1);
        PlayerPrefs.Save();
        FlushRuns();
        FlushFeathers();
    }

    void LoadWallet()
    {
        if (bot) return;
        // локальное золото переносится один раз за жизнь браузера: дальше PlayerPrefs — лишь зеркало сервера,
        // и после «Выйти» (новый voter) оно не должно второй раз превратиться в золото
        bool first = PlayerPrefs.GetInt("wallet.done") == 0;
        var ml = new MetaLevels();
        if (first) foreach (var f in typeof(MetaLevels).GetFields()) f.SetValue(ml, PlayerPrefs.GetInt("meta." + f.Name));
        var post = new WalletPost { voter = Voter(), gold = first ? PlayerPrefs.GetInt("gold") : 0, meta = ml };
        int gen = walletGen;
        StartCoroutine(Send("POST", "wallet", JsonUtility.ToJson(post), (code, body) => { if (code == 200) ApplyWallet(body, gen); }));
    }

    string buyStatus;
    bool buying;

    void Buy(string item, int price)
    {
        if (buying) return;
        if (!walletOk) { buyStatus = "Нет связи с сервером — покупки недоступны"; LoadWallet(); return; }
        buying = true;
        buyStatus = null;
        int gen = walletGen;
        StartCoroutine(Send("POST", "buy", JsonUtility.ToJson(new BuyPost { voter = Voter(), item = item, price = price }), (code, body) =>
        {
            buying = false;
            if (code == 200) ApplyWallet(body, gen);
            else buyStatus = code == 402 ? "Не хватает золота" : code == 0 ? "Нет связи с сервером" : JsonUtility.FromJson<WalletResp>(body)?.error ?? "Ошибка";
            if (code == 409) { worldAt = 0; confirm = null; }   // цена сменилась — освежим скидку, окно покупки закроем
        }));
    }

    // ---------- Перо феникса ----------
    // Расходник (с 15.11): одно перо из запаса берётся в каждый забег и сгорает, только когда воскресило.
    // Бесплатные воскрешения (артефакт, старое «навсегда») тратятся раньше: перо — последнее в счётчике revives.

    public const int FeatherPrice = 150, FeatherMax = 9;   // как FEATHER_PRICE, FEATHER_MAX в server/wallet.py
    static int Feathers => owned.GetValueOrDefault("feather");
    static long consRev;   // ревизия расходников последнего применённого снимка (crev сервера)
    static bool IsConsumable(string id) => id == "feather" || id.StartsWith("drink.");   // перья и напитки бармена
    bool featherArmed;

    [System.Serializable] class FeatherPost { public string voter, rid, item; }

    void ArmFeather()
    {
        featherArmed = false;
        if (bot || LabOn || !walletOk || Feathers <= 0) return;
        featherArmed = true;
        player.revives++;
    }

    // каждый кадр забега (и у гостя кооператива): счётчик воскрешений дошёл до нуля — значит, сгорело и перо из запаса.
    // Списание ложится в очередь на диске и уходит до подтверждения: без связи или при смене сцены перо не «воскресает»
    void WatchFeather()
    {
        if (!featherArmed || player.revives > 0) return;
        featherArmed = false;
        SpendConsumable("feather");
    }

    // потратить расходник в этом забеге: сразу минус в запасе, списание — в очередь на диске до подтверждения
    void SpendConsumable(string item)
    {
        owned[item] = Mathf.Max(0, owned.GetValueOrDefault(item) - 1);
        var q = FeatherQueue();
        q.Add(Voter() + ":" + runId + ":" + item);
        PlayerPrefs.SetString("feather.q", string.Join(",", q));
        PlayerPrefs.Save();
        FlushFeathers();
    }

    // списания этого профиля, ещё не подтверждённые сервером
    static int PendingSpends(string item)
    {
        int n = 0;
        string me = Voter() + ":";
        foreach (var e in FeatherQueue())
        {
            if (!e.StartsWith(me)) continue;
            var p = e.Split(':');
            if ((p.Length > 2 ? p[2] : "feather") == item) n++;
        }
        return n;
    }

    static List<string> FeatherQueue()
    {
        var s = PlayerPrefs.GetString("feather.q");
        return string.IsNullOrEmpty(s) ? new List<string>() : new List<string>(s.Split(','));
    }

    static readonly HashSet<string> feathersInFlight = new();

    // из ApplyWallet и после воскрешения; повтор того же забега сервер второй раз не списывает
    void FlushFeathers()
    {
        if (bot || !walletOk) return;
        foreach (var e in FeatherQueue())
        {
            var p = e.Split(':');   // voter:rid[:item] — старые записи без item — перья
            if (p.Length < 2 || !feathersInFlight.Add(e)) continue;
            int gen = walletGen;
            var post = new FeatherPost { voter = p[0], rid = p[1], item = p.Length > 2 ? p[2] : "feather" };
            NetRunner.I.StartCoroutine(Send("POST", "feather", JsonUtility.ToJson(post), (code, body) =>
            {
                feathersInFlight.Remove(e);
                // 200 — списано (или уже было); 400/404 — профиль исчез, повторять бессмысленно; 401 и сеть — ждём
                if (code != 200 && code != 400 && code != 404) return;
                var q = FeatherQueue();
                q.Remove(e);
                PlayerPrefs.SetString("feather.q", string.Join(",", q));
                PlayerPrefs.Save();
                if (code == 200 && p[0] == Voter()) ApplyWallet(body, gen);
            }));
        }
    }

    // ---------- Магазин ----------

    class ShopItem { public string id, title, desc, icon, art, cos, slot; public int price, trophy; public Color color; public bool soon; }

    // ---------- Одежда ----------

    // надетое хранится на устройстве; некупленное (другой аккаунт, откат) молча заменяется бесплатным
    static string Wearing(string slot)
    {
        var id = PlayerPrefs.GetString("cos." + slot);
        var d = Cosmetics.Get(id);
        return d != null && d.slot == slot && (d.price == 0 || Owns("cos." + id)) ? id : Cosmetics.Default(slot);
    }

    void Wear(ShopItem it)
    {
        PlayerPrefs.SetString("cos." + it.slot, it.cos);
        PlayerPrefs.Save();
        // в городе остальные видят одежду из описания при входе — перезаходим, чтобы обновилась и у них
        if (state == State.Town) TownRejoin();
        if (Heroes[selHero].id != "mage") return;
        Cosmetics.Apply(player.go, Wearing("hat"), Wearing("robe"), Wearing("staff"), Wearing("glow"));
        builtLook = LookKey();
        ApplyMyWeapon();
        SetXRay(false);
        playerRends = PlayerRends();
    }

    // частицы свечения не трогаем: XRay-материал на них ляжет мусором
    Renderer[] PlayerRends() => System.Array.FindAll(player.go.GetComponentsInChildren<Renderer>(), r => !(r is ParticleSystemRenderer));

    static readonly string[] ShopTabs = { "Уровни", "Герои", "Облик", "Рынок" };   // рынок — GameMarket.cs
    int shopTab, shopBack;   // shopBack — куда вернуться по «Назад» (гардероб открывают с экрана героя)
    float shopScroll;
    bool shopAffordable;   // фильтр «могу купить»: только некупленное, на что хватает золота
    ShopItem confirm;  // окно «Купить за N?»

    List<ShopItem> ShopList(int tab)
    {
        var list = new List<ShopItem>();
        if (tab == 0)
        {
            foreach (var l in Levels)
                if (!l.hidden) list.Add(new ShopItem { id = l.item, title = l.title, desc = l.desc, art = l.art, price = Sale(l.price), color = l.color, soon = l.soon });
        }
        else if (tab == 1)
            foreach (var h in Heroes)
                list.Add(new ShopItem { id = h.item, title = h.title, desc = h.desc, icon = h.icon, price = Sale(h.price), color = h.color, soon = h.soon });
        else
            foreach (var c in Cosmetics.All)
                list.Add(new ShopItem { id = "cos." + c.id, cos = c.id, slot = c.slot, title = c.title, desc = c.desc, icon = c.icon, price = Sale(c.price), trophy = c.trophy, color = c.color });
        return list;
    }

    void DrawShop(float W, float H)
    {
        GUI.enabled = confirm == null;  // см. DrawMenu: под окном покупки ничего не кликается
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.9f));
        MenuHeader(W, 50, SalePct > 0 ? $"СКИДКА ТОРГОВЦЕВ СЕГОДНЯ −{SalePct}% — ЗА УБИТЫХ ВСЕМИ" : "ЗОЛОТО ЗАРАБАТЫВАЕТСЯ В ЗАБЕГАХ", "Магазин", Gold);
        ChipAt(W / 2, 200, walletOk ? $"ЗОЛОТО  {walletGold}" : "НЕТ СВЯЗИ", MetaGold, true);

        // вкладки
        int nt = ShopTabs.Length;
        float tw = Mathf.Min(220, (W - 48 - 12 * (nt - 1)) / nt), tx = (W - tw * nt - 12 * (nt - 1)) / 2;
        for (int i = 0; i < ShopTabs.Length; i++)
        {
            var r = new Rect(tx + i * (tw + 12), 250, tw, 54);
            bool on = shopTab == i;
            Plate(r, on ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.05f, 0.04f, 0.07f, 0.9f), on ? Gold : new Color(1, 1, 1, 0.12f), 10);
            Text(r, Up(ShopTabs[i]), Col(caps, on ? Gold : Soft(0.8f), TextAnchor.MiddleCenter), 2);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && confirm == null) { shopTab = i; shopScroll = 0; if (i == 3) { marketStatus = null; LoadMarket(); } }
        }
        if (shopTab == 3)
        {
            DrawMarket(W, H, 370, H - 160);
            if (marketStatus != null) Text(new Rect(0, H - 150, W, 36), marketStatus, Col(small, marketStatus.StartsWith("Куплено") || marketStatus.StartsWith("Лот снят") ? new Color(0.53f, 1f, 0.56f) : new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleCenter), 2);
            float mb = Mathf.Min(460, W - 80);
            if (Btn(new Rect((W - mb) / 2, H - 112, mb, 86), "Назад", Bone) && confirm == null) { menuTab = shopBack; shopBack = 0; marketStatus = null; }
            GUI.enabled = true;
            return;
        }

        var items = ShopList(shopTab);
        if (shopAffordable) items.RemoveAll(it => it.soon || it.price <= 0 || Owns(it.id) || !walletOk || walletGold < it.price);
        // под вкладками: фильтр и (у облика) переход к примерке на живой модели
        {
            float fy = 312, fw2 = Mathf.Min(300, (W - 60) / 2);
            bool look = shopTab == 2 && state == State.Town;
            float fx = look ? W / 2 - fw2 - 6 : (W - fw2) / 2;
            var fr = new Rect(fx, fy, fw2, 44);
            Chip(fr, shopAffordable ? "МОГУ КУПИТЬ" : "ПОКАЗАТЬ ВСЁ", MetaGold, shopAffordable);
            if (GUI.Button(fr, GUIContent.none, GUIStyle.none) && confirm == null) { shopAffordable = !shopAffordable; shopScroll = 0; }
            if (look && Btn(new Rect(W / 2 + 6, fy, fw2, 44), "Примерить на маге", Bone) && confirm == null)
            { menuTab = 7; heroTab = 1; lookScroll = 0; shopBack = 0; buyStatus = null; }
        }
        bool port = W < H;
        int cols = port ? 2 : 3;
        float gap = 16, top = 370, bottom = H - (buyStatus != null ? 160 : 130);
        float cw = Mathf.Min(380, (W - 48 - gap * (cols - 1)) / cols), ch = port ? 300 : 320, x0 = (W - (cw * cols + gap * (cols - 1))) / 2;
        var view = new Rect(0, top, W, bottom - top);
        int rows = (items.Count + cols - 1) / cols;
        var ev = Event.current;
        if (confirm == null)
        {
            if (ev.type == EventType.ScrollWheel) { shopScroll += ev.delta.y * 110; ev.Use(); }
            shopScroll = TouchScroll(view, shopScroll);
        }
        shopScroll = Mathf.Clamp(shopScroll, 0, Mathf.Max(0, 14 + rows * (ch + gap) - view.height));
        if (items.Count == 0)
            Text(new Rect(0, top + 60, W, 40), shopAffordable ? "Сейчас ничего не по карману — копи золото в забегах" : "Пусто", Col(small, Soft(0.7f), TextAnchor.MiddleCenter), 2);
        GUI.BeginGroup(view);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var r = new Rect(x0 + i % cols * (cw + gap), 14 + i / cols * (ch + gap) - shopScroll, cw, ch); // 14 — запас под медальон верхнего ряда
            bool have = Owns(it.id) || it.price == 0;
            Plate(r, new Color(0.05f, 0.04f, 0.07f, 0.94f), have ? new Color(it.color.r, it.color.g, it.color.b, 0.5f) : new Color(1, 1, 1, 0.12f), 14);
            var pic = new Rect(r.x + 10, r.y + 10, r.width - 20, ch - 150);
            var art = it.art != null ? PanelT(it.art) : null;
            if (art != null) GUI.DrawTexture(pic, art, ScaleMode.ScaleAndCrop, true, 0, it.soon ? new Color(0.4f, 0.4f, 0.42f) : Color.white, 0, 10);
            else if (it.icon != null)
            {
                float s = Mathf.Min(pic.width, pic.height) * 0.7f;
                IconTex(new Rect(pic.center.x - s / 2, pic.center.y - s / 2, s, s), it.icon, it.soon ? new Color(0.35f, 0.35f, 0.4f) : it.color);
            }
            Outline(new Rect(r.x + 10, r.yMax - 136, r.width - 20, 40), it.title, Fit(it.title, r.width - 20, Color.Lerp(it.color, Bone, 0.6f)), 2);
            Text(new Rect(r.x + 12, r.yMax - 98, r.width - 24, 34), it.desc, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(0.78f, 0.75f, 0.7f), TextAnchor.UpperCenter), 2);
            var b = new Rect(r.x + 20, r.yMax - 60, r.width - 40, 46);
            if (it.soon) Chip(b, "СКОРО", new Color(0.6f, 0.58f, 0.55f));
            else if (!have && it.price < 0) Chip(b, it.trophy == 1 ? "ТРОФЕЙ · УПОКОЙ ПАВШЕГО" : $"ТРОФЕЙ · УПОКОЙ {it.trophy} ПАВШИХ", Forbidden.Soul);
            else if (have && it.cos != null)
            {
                bool on = Wearing(it.slot) == it.cos;
                if (on) Chip(b, "НАДЕТО", it.color, true);
                else if (Btn(b, "Надеть", Bone) && confirm == null) Wear(it);
            }
            else if (have) Chip(b, "ЕСТЬ", it.color);
            else
            {
                bool can = walletOk && walletGold >= it.price;
                Plate(b, can ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.08f, 0.07f, 0.08f, 0.9f), can ? MetaGold : new Color(1, 1, 1, 0.12f), 8);
                string pt = can || !walletOk ? $"{it.price} ЗОЛОТА" : $"{it.price} · НЕ ХВАТАЕТ {it.price - walletGold}";
                Text(b, pt, Shrink(Col(val, can ? MetaGold : new Color(0.72f, 0.69f, 0.64f), TextAnchor.MiddleCenter), pt, b.width - 10), 2);
                if (GUI.Button(b, GUIContent.none, GUIStyle.none) && confirm == null) { confirm = it; buyStatus = null; }
            }
        }
        GUI.EndGroup();
        // полоса прокрутки справа: видно, что список длиннее экрана, и где ты в нём
        float total = 14 + rows * (ch + gap);
        if (total > view.height + 1)
        {
            float bx = Mathf.Min(W - 14, x0 + cw * cols + gap * (cols - 1) + 10), bh = view.height * view.height / total;
            RR(new Rect(bx, view.y, 6, view.height), new Color(1, 1, 1, 0.08f), 3);
            RR(new Rect(bx, view.y + (view.height - bh) * shopScroll / (total - view.height), 6, bh), new Color(Gold.r, Gold.g, Gold.b, 0.8f), 3);
        }
        if (buyStatus != null) Text(new Rect(0, H - 150, W, 36), buyStatus, Col(small, new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleCenter), 2);
        float bw = Mathf.Min(460, W - 80);
        if (Btn(new Rect((W - bw) / 2, H - 112, bw, 86), "Назад", Bone) && confirm == null) { menuTab = shopBack; shopBack = 0; buyStatus = null; }
        GUI.enabled = true;
        DrawConfirm(W, H);
    }

    // Подтверждение покупки — общее для магазина и замков на карточках меню
    void DrawConfirm(float W, float H)
    {
        if (confirm == null) return;
        Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
        float w = Mathf.Min(620, W - 48), h = 300;
        var r = new Rect((W - w) / 2, (H - h) / 2, w, h);
        Plate(r, new Color(0.06f, 0.05f, 0.08f, 0.98f), MetaGold, 16, 2);
        Outline(new Rect(r.x, r.y + 24, w, 50), confirm.title, Fit(confirm.title, w - 40, Color.Lerp(confirm.color, Bone, 0.6f)), 2);
        bool can = walletOk && walletGold >= confirm.price;
        string line = !walletOk ? "Нет связи с сервером" : can ? $"Купить за {confirm.price} золота?" : $"Нужно {confirm.price} золота, у тебя {walletGold}";
        Text(new Rect(r.x, r.y + 90, w, 40), line, Col(small, can ? Bone : new Color(1f, 0.6f, 0.5f), TextAnchor.MiddleCenter), 2);
        if (buyStatus != null) Text(new Rect(r.x, r.y + 132, w, 36), buyStatus, Col(small, new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleCenter), 2);
        float bw = (w - 60) / 2;
        if (can && !Owns(confirm.id))
        {
            if (Btn(new Rect(r.x + 20, r.yMax - 100, bw, 80), buying ? "…" : "Купить", Gold)) Buy(confirm.id, confirm.price);
        }
        else if (Owns(confirm.id)) { if (confirm.cos != null) Wear(confirm); confirm = null; return; }  // куплено — надеваем, окно закрывается само
        if (Btn(new Rect(r.xMax - 20 - bw, r.yMax - 100, bw, 80), "Отмена", Bone)) { confirm = null; buyStatus = null; }
    }
}
