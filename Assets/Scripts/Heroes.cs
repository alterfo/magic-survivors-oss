using System.Collections.Generic;
using UnityEngine;

// Герои: модель, оружие, перекраска, базовые статы, стартовое заклинание и черта. Новый герой — одна запись в All.
// Все герои на одном риге KayKit и с одним аниматором мага (Anim/Mage): состояния Idle/Run/Cast/Death одинаковые.
public class HeroContent
{
    public string id, model = "Mage", weapon = "staff", texture;
    public float height = 1.8f;
    // emission плоский и в линейном пространстве: 0.05 уже заметно красит всю модель
    public Color tint = Color.white, emission = Color.black, light = new(1f, 0.78f, 0.55f), gem;
    public float hp = 100, speed = 4.2f, magnet = 2.4f, crit = 0.05f;
    public string startSpell = "Fireball", trait;
    public string[] hide; // части модели, которые прячем (шляпа ведьмы уступает место короне)
    public bool crown;
    public System.Action<Game> traitApply;
    // свой набор заклинаний героя: только они выпадают на уровнях (пассивки — у всех). Слияние — если обе части из набора.
    // null — без ограничений. Запретные заклинания вне набора открывает гримуар павшего, как и раньше
    public string[] spells;
    public string school;   // подпись набора в меню героя

    public bool Allows(Skill s)
    {
        if (spells == null || !s.spell) return true;
        if (s is Fusion f) return Allows(f.a) && Allows(f.b);
        return System.Array.IndexOf(spells, s.GetType().Name) >= 0;
    }

    public static readonly HeroContent[] All =
    {
        new()
        {
            id = "mage", trait = "Стихийный резонанс: огонь, молнии и метеоры бьют на 12% сильнее", school = "Огонь · Электро · Метеоры",
            traitApply = g => g.heroElemMul = 1.12f,
            spells = new[] { "Fireball", "Lightning", "Orbs", "FireAura", "Meteor", "Quake" },
        },
        new()
        {
            id = "necro", model = "RogueHooded", weapon = "Skeleton_Staff", texture = "necro_texture", height = 1.75f,
            light = new Color(0.55f, 1f, 0.6f), gem = new Color(1.2f, 4f, 1.4f),
            hide = new[] { "1H_Crossbow", "2H_Crossbow", "Knife", "Knife_Offhand", "Throwable" },
            hp = 110, speed = 4f, startSpell = "RaiseDead", trait = "Каждое убийство даёт временный щит", school = "Смерть: мёртвые и запретная магия",
            spells = new[] { "RaiseDead", "SoulReap", "BonePrison", "BlackSun", "Orbs", "Quake" },
            traitApply = g => g.heroKillShield = true,
        },
        new()
        {
            id = "frost", texture = "frost_texture", height = 1.7f, hide = new[] { "Mage_Hat", "1H_Wand", "2H_Staff" }, crown = true,
            emission = new Color(0.008f, 0.016f, 0.03f), light = new Color(0.6f, 0.85f, 1f), gem = new Color(1.5f, 3.5f, 6f),
            hp = 90, speed = 4.4f, crit = 0.08f, startSpell = "IceLance", trait = "Криты замораживают на 1 с, школа Льда +10% урона", school = "Лёд · Электро · Метеоры",
            spells = new[] { "IceLance", "Blizzard", "Lightning", "Orbs", "Meteor", "Quake" },
            traitApply = g => { g.heroFrostCrit = true; g.heroIceMul = 1.1f; },
        },
    };

    public static HeroContent Get(string id)
    {
        foreach (var h in All) if (h.id == id) return h;
        return All[0];
    }

    // кэш перекрашенных материалов: экземпляр на героя, иначе SRP Batcher получает новый материал на каждый рестарт
    static readonly Dictionary<string, Material> mats = new();

    Material Mat()
    {
        if (texture == null) return World.Mage;
        if (mats.TryGetValue(id, out var m) && m != null) return m;
        m = new Material(World.Mage) { name = "Hero_" + id };
        var tex = Resources.Load<Texture2D>("Models/Characters/" + texture);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", tint);
        m.SetColor("_EmissionColor", emission);
        return mats[id] = m;
    }

    public GameObject Spawn(out Animator anim)
    {
        var go = World.Character(model, height, out anim, out var rends);
        // у RogueHooded своего контроллера нет — риг тот же, что у мага, клипы мага садятся по именам костей
        if (anim.runtimeAnimatorController == null) anim.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Anim/Mage");
        var mat = Mat();
        foreach (var r in rends)
        {
            r.sharedMaterial = mat;
            if (hide != null && System.Array.IndexOf(hide, r.name) >= 0) r.gameObject.SetActive(false);
        }
        // оружие скелета (посох-череп) нарисовано в текстуре скелета
        World.Attach(go, weapon, weapon.StartsWith("Skeleton") ? World.Skeleton : mat);
        if (gem.maxColorComponent > 0) Gem(go);
        if (crown) Crown(go);
        return go;
    }

    // Светящийся камень на конце посоха: конец — дальний от рукояти край меша вдоль длинной оси
    void Gem(GameObject go)
    {
        var slot = Find(go.transform, "handslot.r");
        if (slot == null || slot.childCount == 0) return;
        var w = slot.GetChild(slot.childCount - 1);
        var mf = w.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        var b = mf.sharedMesh.bounds;
        int k = b.size.x > b.size.y ? (b.size.x > b.size.z ? 0 : 2) : (b.size.y > b.size.z ? 1 : 2);
        var tip = b.center;
        tip[k] = Mathf.Abs(b.max[k]) > Mathf.Abs(b.min[k]) ? b.max[k] : b.min[k];
        var g = World.Primitive(PrimitiveType.Sphere, World.GlowMat(gem), 1);
        g.name = "StaffGem";
        g.transform.SetParent(mf.transform, false);
        g.transform.localPosition = tip;
        // у оружия скелета масштаб fbx в сантиметрах: размер камня задаём в метрах мира
        // без своего света: цветной свет камня перекрашивал мантию, свечение даёт bloom
        g.transform.localScale = Vector3.one * 0.14f * height / 1.8f / Mathf.Max(1e-4f, mf.transform.lossyScale.x);
    }

    // Ледяная корона: кольцо светящихся сосулек на голове вместо шляпы
    void Crown(GameObject go)
    {
        var head = Find(go.transform, "head");
        if (head == null) return;
        var root = new GameObject("IceCrown").transform;
        root.SetParent(head, false);
        // размеры в метрах мира: делим на масштаб кости, чтобы корона не зависела от единиц fbx
        float s = 1 / Mathf.Max(1e-4f, head.lossyScale.y);
        root.localPosition = new Vector3(0, 0.4f * s, 0);
        var mat = World.GlowMat(gem * 0.4f);
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.PI * 2 / 9;
            var spike = World.Primitive(PrimitiveType.Cube, mat, 1).transform;
            spike.SetParent(root, false);
            float h = (i % 2 == 0 ? 0.22f : 0.14f) * s;
            spike.localPosition = new Vector3(Mathf.Cos(a) * 0.2f * s, h * 0.5f, Mathf.Sin(a) * 0.2f * s);
            spike.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0) * Quaternion.Euler(0, 0, 8) * Quaternion.Euler(0, 45, 0);
            spike.localScale = new Vector3(0.04f * s, h, 0.04f * s);
        }
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var f = Find(c, name);
            if (f != null) return f;
        }
        return null;
    }

    // Статы ставятся до ApplyMeta: постоянная прокачка добавляется поверх базы героя
    public void ApplyTrait(Game g)
    {
        var p = g.player;
        p.maxHp = p.hp = hp;
        p.speed = speed;
        p.magnet = magnet;
        p.crit = crit;
        if (p.light != null) p.light.color = light;
        traitApply?.Invoke(g);
    }

    // Стартовое заклинание по имени класса; если его нет в общем списке (Поднять мёртвых) — создаём и регистрируем
    public Skill StartSkill(List<Skill> skills)
    {
        var s = skills.Find(x => x.GetType().Name == startSpell);
        if (s != null) return s;
        var t = System.Type.GetType(startSpell);
        if (t == null || !typeof(Skill).IsAssignableFrom(t)) return skills[0];
        s = (Skill)System.Activator.CreateInstance(t);
        skills.Add(s);
        return s;
    }
}
