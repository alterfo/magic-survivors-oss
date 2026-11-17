using System.Collections.Generic;
using UnityEngine;

// Таверна «Кривой череп» — дом на краю площади (стены и дверь — Levels.Town): внутри стойка с барменом, бильярд,
// доска «Ищу пати» и лидеры (GameTown.cs DrawTavern). Мебель собрана из простых форм: готовых моделей нет.
// Бармен продаёт напитки — бонус на один следующий забег (server/wallet.py DRINKS, списание как у пера).
// Идея: «таверна — здание, бармен, бар, развлечения, бильярд».
public partial class Game
{
    public class Drink { public string id, title, what, icon; public int price; public Color color; public System.Action<Player> apply; }

    // id и цены — как DRINKS в server/wallet.py. Золота напитки не дают: его считает сервер по своей формуле
    public static readonly Drink[] Drinks =
    {
        new() { id = "drink.grog", title = "Грог", what = "+15% урона на забег", icon = "might", price = 120, color = new Color(1f, 0.55f, 0.3f), apply = p => p.dmgMul += 0.15f },
        new() { id = "drink.mead", title = "Медовуха", what = "+25 здоровья и +0.3/с регенерации", icon = "vitality", price = 100, color = new Color(1f, 0.82f, 0.35f), apply = p => { p.maxHp += 25; p.hp = p.maxHp; p.regen += 0.3f; } },
        new() { id = "drink.ale", title = "Тёмный эль", what = "+8% к бегу и радиусу сбора", icon = "boots", price = 80, color = new Color(0.75f, 0.55f, 0.35f), apply = p => { p.speed *= 1.08f; p.magnet *= 1.08f; } },
    };
    const int DrinkMax = 3;

    // мебель стоит от центра дома (Town.TavernC): стойка у северной стены, бильярд в юго-восточной части
    static readonly Vector2 BarAt = new(-0.6f, 4.4f), PoolAt = new(2.6f, -2.9f);
    static Vector2 BarSpot => Town.TavernC + BarAt + new Vector2(0, -1.1f);   // где встать, чтобы позвать бармена
    static Vector2 PoolSpot => Town.TavernC + PoolAt;                          // бильярдный стол
    bool atBar, nearPool;
    string barStatus;
    readonly List<string> drunk = new();   // что выпито перед этим забегом — для подписи в начале

    // ---------- интерьер ----------

    static Material Plain(Color c, float metal = 0, float smooth = 0.25f)
    {
        var m = new Material(Resources.Load<Material>("Mats/Mage")) { name = "Tavern" };
        m.SetTexture("_BaseMap", null);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    // меши куба и сферы берём один раз: CreatePrimitive в сборке без физики каждый раз ругается на коллайдер
    static Mesh cubeMesh, sphereMesh;
    static Mesh Prim(PrimitiveType t)
    {
        ref Mesh m = ref (t == PrimitiveType.Cube ? ref cubeMesh : ref sphereMesh);
        if (m == null) { var g = GameObject.CreatePrimitive(t); m = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); }
        return m;
    }

    static GameObject Shape(PrimitiveType t, Material mat)
    {
        var go = new GameObject("Shape");
        go.AddComponent<MeshFilter>().sharedMesh = Prim(t);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = Shape(PrimitiveType.Cube, mat);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        return go;
    }

    void BuildTavern()
    {
        var root = new GameObject("TavernInside").transform;
        root.position = World.W(Town.TavernC);
        var wood = Plain(new Color(0.42f, 0.26f, 0.15f));
        var dark = Plain(new Color(0.24f, 0.15f, 0.09f));
        var cloth = Plain(new Color(0.12f, 0.42f, 0.22f), 0, 0.1f);
        // стойка вдоль северной стены и бармен за ней
        var bar = Box("Bar", root, new Vector3(BarAt.x, 0.5f, BarAt.y), new Vector3(5f, 1f, 0.75f), wood);
        Box("BarTop", root, new Vector3(BarAt.x, 1.03f, BarAt.y), new Vector3(5.2f, 0.08f, 0.9f), dark);
        World.AddBlock(bar);
        var keeper = World.Character("Skeleton_Mage", 1f, out var anim, out _);
        keeper.name = "Barkeep";
        keeper.transform.localScale *= 1.55f;
        keeper.transform.position = World.W(Town.TavernC + BarAt + new Vector2(0, 0.7f));
        keeper.transform.rotation = Quaternion.LookRotation(Vector3.back);
        anim.Play("Idle");
        // бильярд: сукно, борта, ножки и шары
        var t = new GameObject("Pool").transform;
        t.SetParent(root, false);
        t.localPosition = new Vector3(PoolAt.x, 0, PoolAt.y);
        var top = Box("Cloth", t, new Vector3(0, 0.78f, 0), new Vector3(2.5f, 0.1f, 1.35f), cloth);
        Box("RimN", t, new Vector3(0, 0.86f, 0.72f), new Vector3(2.7f, 0.14f, 0.12f), wood);
        Box("RimS", t, new Vector3(0, 0.86f, -0.72f), new Vector3(2.7f, 0.14f, 0.12f), wood);
        Box("RimE", t, new Vector3(1.32f, 0.86f, 0), new Vector3(0.12f, 0.14f, 1.55f), wood);
        Box("RimW", t, new Vector3(-1.32f, 0.86f, 0), new Vector3(0.12f, 0.14f, 1.55f), wood);
        foreach (var (lx, lz) in new[] { (1.1f, 0.55f), (-1.1f, 0.55f), (1.1f, -0.55f), (-1.1f, -0.55f) })
            Box("Leg", t, new Vector3(lx, 0.37f, lz), new Vector3(0.14f, 0.74f, 0.14f), dark);
        var balls = new[] { Color.white, new Color(0.9f, 0.15f, 0.1f), new Color(1f, 0.8f, 0.1f), new Color(0.15f, 0.3f, 0.9f), new Color(0.1f, 0.1f, 0.1f), new Color(0.2f, 0.7f, 0.25f) };
        for (int i = 0; i < balls.Length; i++)
        {
            var b = Shape(PrimitiveType.Sphere, Plain(balls[i], 0, 0.7f));
            b.transform.localScale = Vector3.one * 0.11f;
            b.name = "Ball";
            b.transform.SetParent(t, false);
            b.transform.localPosition = new Vector3(i == 0 ? -0.8f : 0.35f + (i % 3) * 0.12f, 0.89f, i == 0 ? 0 : (i - 3) * 0.12f);
        }
        World.AddBlock(top);
        // тёплый свет над стойкой и над столом
        foreach (var (lp, li) in new[] { (new Vector3(BarAt.x, 2.4f, BarAt.y - 0.7f), 4f), (new Vector3(PoolAt.x, 2.2f, PoolAt.y), 3f) })
        {
            var l = new GameObject("TavernLight").AddComponent<Light>();
            l.transform.SetParent(root, false);
            l.transform.localPosition = lp;
            l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.4f); l.range = 6; l.intensity = li; l.shadows = LightShadows.None;
        }
    }

    // ---------- бармен ----------

    void TavernTick(bool free)
    {
        atBar = free && (player.pos - BarSpot).sqrMagnitude < 1.2f * 1.2f;
        nearPool = free && (player.pos - PoolSpot).sqrMagnitude < 2.2f * 2.2f;
        if (!atBar) barStatus = null;
    }

    void DrawBar(float W, float H, bool port)
    {
        if (Town.replaced) return;   // поверх — «ты вошёл с другого устройства»
        if (nearPool && !atBar)
        {
            ChipAt(W / 2, port ? H - 260 : H - 120, "БИЛЬЯРД — СКОРО ПАРТИЯ НА ДВОИХ", new Color(0.55f, 1f, 0.65f));
            return;
        }
        if (!atBar) return;
        float pw = Mathf.Min(640, W - 32), ph = 120 + Drinks.Length * 96 + 40;
        float x = (W - pw) / 2, y = port ? H - ph - 190 : H - ph - 24;
        if (!port) x = Mathf.Max(x, 16 + Mathf.Min(560, W * 0.42f) + 16);
        var box = new Rect(x, y, pw, ph);
        UiBlock(box);
        var warm = new Color(1f, 0.66f, 0.3f);
        Plate(box, new Color(0.06f, 0.035f, 0.02f, 0.95f), new Color(warm.r, warm.g, warm.b, 0.75f), 16, 2);
        Outline(new Rect(x, y + 10, pw, 46), "Бармен", Fit("Бармен", pw - 40, Color.Lerp(warm, Bone, 0.4f)), 2);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        const string hint = "Напиток действует один следующий забег — выпьешь на старте";
        Text(new Rect(x + 20, y + 56, pw - 40, 30), hint, Shrink(Col(ds, Soft(0.7f), TextAnchor.MiddleCenter), hint, pw - 40), 2);
        float cy = y + 96;
        foreach (var d in Drinks)
        {
            var row = new Rect(x + 16, cy, pw - 32, 86);
            RR(row, new Color(1, 1, 1, 0.05f), 12);
            IconTex(new Rect(row.x + 10, row.y + 11, 64, 64), d.icon, d.color);
            int n = owned.GetValueOrDefault(d.id), price = Sale(d.price);
            string title = n > 0 ? $"{d.title} ×{n}" : d.title;
            Text(new Rect(row.x + 86, row.y + 6, row.width - 300, 38), title, Shrink(Col(small, Color.Lerp(d.color, Bone, 0.4f), TextAnchor.MiddleLeft), title, row.width - 300), 2);
            Text(new Rect(row.x + 86, row.y + 44, row.width - 300, 32), d.what, Shrink(Col(ds, Soft(0.8f), TextAnchor.MiddleLeft), d.what, row.width - 300), 2);
            var b = new Rect(row.xMax - 200, row.y + 16, 190, 54);
            bool full = n >= DrinkMax, can = !full && walletOk && !buying && Coins >= price;
            if (full) Chip(b, "ХВАТИТ", d.color);
            else
            {
                Plate(b, can ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.08f, 0.07f, 0.08f, 0.9f), can ? MetaGold : new Color(1, 1, 1, 0.12f), 8);
                Text(b, buying ? "…" : $"Налить · {price}", Shrink(Col(val, can ? MetaGold : new Color(0.55f, 0.52f, 0.5f), TextAnchor.MiddleCenter), $"Налить · {price}", b.width - 10), 2);
                if (can && GUI.Button(b, GUIContent.none, GUIStyle.none)) Buy(d.id, price);
            }
            cy += 96;
        }
        string st = buyStatus ?? barStatus;
        if (st != null) Text(new Rect(x, box.yMax - 40, pw, 32), st, Col(ds, new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleCenter), 2);
    }

    // на старте забега: всё купленное у бармена выпивается — бонус на этот забег, списание в очередь (как перо)
    void DrinkUp()
    {
        drunk.Clear();
        if (bot || LabOn || !walletOk) return;
        foreach (var d in Drinks)
        {
            if (owned.GetValueOrDefault(d.id) <= 0) continue;
            d.apply(player);
            SpendConsumable(d.id);
            drunk.Add(d.title);
        }
    }
}
