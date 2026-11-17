using System.Collections.Generic;
using UnityEngine;

// Косметика мага: шляпы, мантии, посохи, свечение навершия. Только внешний вид — силу не продаём.
// Каталог — данные (новый предмет = строка в All), Apply переодевает живую модель: в примерочной на каждый клик и в забеге.
public static class Cosmetics
{
    public class CosmeticDef
    {
        public string id, slot /* hat|robe|staff|glow */, title, icon, desc;
        public int price;   // 0 — бесплатно, -1 — не продаётся: трофей
        public int trophy;  // сколько павших магов надо упокоить (server/fallen.py выдаёт сам)
        public Color color; // цвет-подпись для плашки (образец мантии, тон свечения)
    }

    public static readonly CosmeticDef[] All =
    {
        new() { id = "hat_mage", slot = "hat", title = "Остроконечная шляпа", desc = "Шляпа ученика, в цвет мантии", icon = "cos_hat_mage", price = 0, color = new Color(0.45f, 0.4f, 0.75f) },
        new() { id = "hat_lich", slot = "hat", title = "Шляпа лича", desc = "Сорвана с костяного мага", icon = "cos_hat_lich", price = 500, color = new Color(0.35f, 0.3f, 0.4f) },
        new() { id = "hat_bear", slot = "hat", title = "Медвежья шапка", desc = "Тёплая, с ушами", icon = "cos_hat_bear", price = 600, color = new Color(0.6f, 0.45f, 0.3f) },
        new() { id = "hat_knight", slot = "hat", title = "Шлем паладина", desc = "Сталь вместо фетра", icon = "cos_hat_knight", price = 700, color = new Color(0.7f, 0.75f, 0.8f) },
        new() { id = "hat_crown", slot = "hat", title = "Корона архимага", desc = "Золото и горящий камень", icon = "cos_hat_crown", price = 800, color = new Color(1f, 0.8f, 0.3f) },

        new() { id = "robe_violet", slot = "robe", title = "Фиолетовая мантия", desc = "Мантия ученика", icon = "cos_robe_violet", price = 0, color = new Color(0.45f, 0.4f, 0.75f) },
        new() { id = "robe_crimson", slot = "robe", title = "Багровая мантия", desc = "Цвета кровавой луны", icon = "cos_robe_crimson", price = 400, color = new Color(0.75f, 0.12f, 0.18f) },
        new() { id = "robe_emerald", slot = "robe", title = "Изумрудная мантия", desc = "Сшита в гнилых топях", icon = "cos_robe_emerald", price = 500, color = new Color(0.15f, 0.65f, 0.4f) },
        new() { id = "robe_night", slot = "robe", title = "Ночная мантия", desc = "Темнее ночного неба", icon = "cos_robe_night", price = 600, color = new Color(0.15f, 0.17f, 0.3f) },
        new() { id = "robe_gold", slot = "robe", title = "Золотая мантия", desc = "Для тех, кто дожил до утра", icon = "cos_robe_gold", price = 800, color = new Color(1f, 0.78f, 0.3f) },

        new() { id = "staff_oak", slot = "staff", title = "Посох ученика", desc = "Старый дубовый посох", icon = "cos_staff_oak", price = 0, color = new Color(0.6f, 0.4f, 0.25f) },
        new() { id = "staff_wand", slot = "staff", title = "Жезл чародея", desc = "Лёгкий и быстрый", icon = "cos_staff_wand", price = 800, color = new Color(0.85f, 0.5f, 0.3f) },
        new() { id = "staff_lich", slot = "staff", title = "Посох лича", desc = "Кость и чёрное железо", icon = "cos_staff_lich", price = 1000, color = new Color(0.5f, 0.45f, 0.6f) },
        new() { id = "staff_crystal", slot = "staff", title = "Кристальный посох", desc = "Парящий кристалл на навершии", icon = "cos_staff_crystal", price = 1000, color = new Color(0.4f, 0.85f, 1f) },

        new() { id = "glow_none", slot = "glow", title = "Без свечения", desc = "Посох как посох", icon = "cos_glow_none", price = 0, color = new Color(0.5f, 0.5f, 0.5f) },
        new() { id = "glow_fire", slot = "glow", title = "Пламя", desc = "Навершие горит огнём", icon = "cos_glow_fire", price = 600, color = new Color(1f, 0.45f, 0.1f) },
        new() { id = "glow_ice", slot = "glow", title = "Иней", desc = "Холодное голубое сияние", icon = "cos_glow_ice", price = 600, color = new Color(0.45f, 0.8f, 1f) },
        new() { id = "glow_poison", slot = "glow", title = "Яд", desc = "Ядовито-зелёный дым", icon = "cos_glow_poison", price = 600, color = new Color(0.45f, 1f, 0.2f) },
        new() { id = "glow_dark", slot = "glow", title = "Тьма", desc = "Фиолетовая бездна", icon = "cos_glow_dark", price = 600, color = new Color(0.6f, 0.2f, 1f) },
        new() { id = "glow_gold", slot = "glow", title = "Золото", desc = "Сияние чемпиона", icon = "cos_glow_gold", price = 800, color = new Color(1f, 0.8f, 0.3f) },

        // трофеи: за золото не продаются, выдаются за упокоенных павших магов
        new() { id = "glow_soul", slot = "glow", title = "Свечение душ", desc = "Души упокоенных вьются у навершия", icon = "cos_glow_ice", price = -1, trophy = 1, color = new Color(0.35f, 1f, 0.8f) },
        new() { id = "robe_ghost", slot = "robe", title = "Призрачная мантия", desc = "Соткана из тумана кладбища", icon = "cos_robe_night", price = -1, trophy = 3, color = new Color(0.55f, 1f, 0.9f) },
        new() { id = "hat_fallen", slot = "hat", title = "Корона павших", desc = "Костяной венец с камнем душ", icon = "cos_hat_crown", price = -1, trophy = 10, color = new Color(0.6f, 0.95f, 0.85f) },
    };

    public static CosmeticDef Get(string id)
    {
        foreach (var d in All) if (d.id == id) return d;
        return null;
    }

    // Бесплатный предмет слота — им же заменяем неизвестный id (старое сохранение, убранный предмет)
    public static string Default(string slot)
    {
        foreach (var d in All) if (d.slot == slot && d.price == 0) return d.id;
        return null;
    }

    static string Pick(string id, string slot)
    {
        var d = Get(id);
        return d != null && d.slot == slot ? id : Default(slot);
    }

    // ---------- Стили, которых нет в каталоге: куда крепить и чем красить ----------

    // Шляпы выпечены в пространство кости head (CosmeticsTools.Bake), сдвиг/масштаб — подгонка под голову мага
    static readonly Dictionary<string, (string mesh, string tex, Vector3 pos, float scale)> hats = new()
    {
        ["hat_lich"] = ("Cosmetics/Hats/hat_lich", null, new Vector3(0, 0, 0), 1f),
        ["hat_bear"] = ("Cosmetics/Hats/hat_bear", "Cosmetics/Textures/barbarian_texture", new Vector3(0, 0, 0), 1f),
        ["hat_knight"] = ("Cosmetics/Hats/hat_knight", "Cosmetics/Textures/knight_texture", new Vector3(0, 0, 0), 1f),
    };

    struct Glow { public Color core, light; public string tex; public bool alpha; public float rise; }

    static readonly Dictionary<string, Glow> glows = new()
    {
        ["glow_fire"] = new Glow { core = new Color(2.4f, 0.9f, 0.25f), light = new Color(1f, 0.5f, 0.15f), tex = "fire_01", rise = 0.9f },
        ["glow_ice"] = new Glow { core = new Color(0.8f, 1.6f, 2.6f), light = new Color(0.45f, 0.75f, 1f), tex = "spark_04", rise = -0.1f },
        ["glow_poison"] = new Glow { core = new Color(0.8f, 2.4f, 0.4f), light = new Color(0.45f, 1f, 0.25f), tex = "smoke_01", rise = 0.35f },
        ["glow_dark"] = new Glow { core = new Color(1.3f, 0.35f, 2.6f), light = new Color(0.55f, 0.2f, 1f), tex = "magic_03", rise = 0.2f },
        ["glow_gold"] = new Glow { core = new Color(2.6f, 1.9f, 0.6f), light = new Color(1f, 0.8f, 0.35f), tex = "light_01", rise = 0.3f },
        ["glow_soul"] = new Glow { core = new Color(0.7f, 2.6f, 2.0f), light = new Color(0.35f, 1f, 0.8f), tex = "magic_03", rise = 0.6f },
    };

    // Мантия = тот же атлас KayKit с перекрашенными клетками ткани (CosmeticsTools.Bake), стандартная — исходный материал
    static readonly Dictionary<string, Material> mats = new();
    static readonly Dictionary<string, Mesh> meshes = new();

    static Material Mat(string key, System.Func<Material> make)
    {
        if (!mats.TryGetValue(key, out var m) || m == null) mats[key] = m = make();
        return m;
    }

    static Material Base => Resources.Load<Material>("Mats/Mage");

    static Material Textured(string tex) => Mat("tex:" + tex, () =>
    {
        var m = new Material(Base) { name = "Cos_" + tex };
        m.SetTexture("_BaseMap", Resources.Load<Texture2D>(tex));
        return m;
    });

    static Material RobeMat(string robe) => robe == Default("robe") ? Base : robe == "robe_ghost" ? GhostMat : Textured("Cosmetics/Robes/" + robe);

    // призрачная мантия: тот же атлас, выбеленный в бирюзу и чуть светящийся в темноте
    static Material GhostMat => Mat("ghost", () =>
    {
        var m = new Material(Base) { name = "Cos_Ghost" };
        m.SetColor("_BaseColor", new Color(0.5f, 1.15f, 1f));
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(0.03f, 0.2f, 0.16f));
        return m;
    });

    // костяной венец: матовая кость вместо золота
    static Material BoneMat => Mat("bone", () =>
    {
        var m = new Material(Base) { name = "Cos_Bone" };
        m.SetTexture("_BaseMap", null);
        m.SetColor("_BaseColor", new Color(0.82f, 0.86f, 0.78f));
        m.SetFloat("_Metallic", 0.05f);
        m.SetFloat("_Smoothness", 0.3f);
        m.SetColor("_EmissionColor", new Color(0.04f, 0.1f, 0.08f));
        return m;
    });

    static Material GoldMat => Mat("gold", () =>
    {
        var m = new Material(Base) { name = "Cos_Gold" };
        m.SetTexture("_BaseMap", null);
        m.SetColor("_BaseColor", new Color(1f, 0.72f, 0.25f));
        m.SetFloat("_Metallic", 0.75f);
        m.SetFloat("_Smoothness", 0.6f);
        m.SetColor("_EmissionColor", new Color(0.25f, 0.15f, 0.02f)); // ночью металл без отражений чернеет — чуть подсвечиваем
        return m;
    });

    static Material GemMat(Color hdr) => Mat("gem:" + hdr, () =>
    {
        var m = new Material(Resources.Load<Material>("Mats/Glow")) { name = "Cos_Gem" };
        m.SetColor("_BaseColor", hdr);
        return m;
    });

    static Material FxMat(string tex, bool alpha, Color c) => Mat($"fx:{tex}:{alpha}:{c}", () =>
    {
        var m = new Material(Resources.Load<Material>(alpha ? "Mats/Alpha" : "Mats/Additive")) { name = "Cos_Fx" };
        m.mainTexture = Resources.Load<Texture2D>("Fx/" + tex);
        m.SetColor("_Color", c);
        return m;
    });

    // ---------- Переодевание ----------

    static readonly List<(Light l, float i0, float phase)> lights = new();
    static readonly List<Transform> spinners = new();

    public static void Apply(GameObject mage, string hat, string robe, string staff, string glow)
    {
        if (mage == null) return;
        hat = Pick(hat, "hat"); robe = Pick(robe, "robe"); staff = Pick(staff, "staff"); glow = Pick(glow, "glow");
        var head = Find(mage.transform, "head");
        var hand = Find(mage.transform, "handslot.r");

        // прошлый комплект снимаем целиком: отцепляем сразу, Destroy в игре отложенный
        var old = new List<Transform>();
        foreach (var t in mage.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("Cos_")) old.Add(t);
        foreach (var t in old) { t.SetParent(null, false); Kill(t.gameObject); }

        var robeMat = RobeMat(robe);
        foreach (var r in mage.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            r.sharedMaterial = robeMat; // посох и книга на этом же атласе, их клетки не перекрашены
            if (r.name == "Mage_Hat") r.enabled = hat == "hat_mage";
        }

        if (head != null && hat != "hat_mage")
        {
            if (hat == "hat_crown" || hat == "hat_fallen") Part("Cos_Hat", head, Crown(), hat == "hat_fallen" ? BoneMat : GoldMat, new Vector3(0, 0.6f, 0), 0.85f);
            else if (hats.TryGetValue(hat, out var h))
            {
                var mesh = LoadMesh(h.mesh);
                var mat = h.tex != null ? Textured(h.tex) : Resources.Load<Material>("Mats/Skeleton");
                Part("Cos_Hat", head, mesh, mat, h.pos, h.scale);
            }
        }
        if (head != null && (hat == "hat_crown" || hat == "hat_fallen"))
        {
            var gem = Part("Cos_Gem", head, Gem(), GemMat(hat == "hat_fallen" ? new Color(0.5f, 2.6f, 2.0f) : new Color(2.5f, 0.4f, 0.5f)), new Vector3(0, 0.74f, 0.45f), 0.13f);
            gem.localRotation = Quaternion.Euler(90, 0, 0);
        }

        if (hand == null) return;
        // в Mage.fbx на руке уже висят жезл и посох, плюс World.Attach добавляет staff: прячем всё, вешаем своё
        foreach (Transform c in hand) c.gameObject.SetActive(false);

        GameObject st;
        switch (staff)
        {
            case "staff_wand":
                st = Part("Cos_Staff", hand, ChildMesh("Models/Characters/Mage", "1H_Wand"), robeMat, Vector3.zero, 1f).gameObject;
                break;
            case "staff_lich":
                st = Object.Instantiate(Resources.Load<GameObject>("Models/Characters/Skeleton_Staff"), hand, false);
                st.name = "Cos_Staff";
                foreach (var r in st.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Resources.Load<Material>("Mats/Skeleton");
                break;
            default:
                st = Object.Instantiate(Resources.Load<GameObject>("Models/Characters/staff"), hand, false);
                st.name = "Cos_Staff";
                foreach (var r in st.GetComponentsInChildren<Renderer>()) r.sharedMaterial = robeMat;
                break;
        }
        var tip = Tip(hand, st);
        if (staff == "staff_crystal")
        {
            tip += Vector3.up * 0.25f;
            var cr = Part("Cos_Crystal", hand, Gem(), GemMat(new Color(0.6f, 1.6f, 2.4f)), tip, 0.45f);
            spinners.Add(cr);
            tip += Vector3.up * 0.05f;
        }

        if (glows.TryGetValue(glow, out var g)) MakeGlow(hand, tip, g);
    }

    // Пульс света и вращение кристаллов. Дешево: пара объектов на мага.
    public static void Tick(float dt)
    {
        float t = Time.time;
        for (int i = lights.Count - 1; i >= 0; i--)
        {
            var (l, i0, ph) = lights[i];
            if (l == null) { lights.RemoveAt(i); continue; }
            l.intensity = i0 * (1f + 0.22f * Mathf.Sin(t * 4.2f + ph) + 0.08f * Mathf.Sin(t * 11.3f + ph * 2));
        }
        for (int i = spinners.Count - 1; i >= 0; i--)
        {
            if (spinners[i] == null) { spinners.RemoveAt(i); continue; }
            spinners[i].Rotate(0, 70 * dt, 0, Space.Self);
        }
    }

    // ---------- Навершие: свет + две маленькие системы частиц ----------

    static void MakeGlow(Transform hand, Vector3 tip, Glow g)
    {
        var go = new GameObject("Cos_Glow");
        go.transform.SetParent(hand, false);
        go.transform.localPosition = tip;

        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = g.light;
        l.range = 3.2f;
        l.intensity = 2.6f;
        l.shadows = LightShadows.None;
        lights.Add((l, l.intensity, Random.value * 6f));

        // ореол — в локальном пространстве, висит на навершии; искры — в мировом, тянутся шлейфом за бегущим магом
        Particles(go.transform, "Cos_Halo", FxMat("light_01", false, Color.white), g.core, false, 6, 0.7f, 0.55f, 0.75f, 0f, 0f);
        Particles(go.transform, "Cos_Sparks", FxMat(g.tex, g.alpha, Color.white), g.core * 1.4f, true, 16, 0.75f, 0.18f, 0.36f, 0.25f, -g.rise);
    }

    static void Particles(Transform at, string name, Material mat, Color c, bool world, float rate, float life, float s0, float s1, float speed, float gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(at, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = 32;
        main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape; // размер частиц в мировых единицах, рост мага не влияет
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSize = new ParticleSystem.MinMaxCurve(s0, s1);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0, speed);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.startColor = c;
        main.gravityModifier = gravity;
        var em = ps.emission;
        em.rateOverTime = rate;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = world ? 0.06f : 0.01f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                   new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) });
        col.color = gr;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, world ? 0.2f : 0.85f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
    }

    // ---------- Меши ----------

    static Transform Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, float scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return go.transform;
    }

    static Mesh LoadMesh(string path)
    {
        if (!meshes.TryGetValue(path, out var m) || m == null) meshes[path] = m = Resources.Load<Mesh>(path);
        return m;
    }

    static Mesh ChildMesh(string model, string child)
    {
        var key = model + "/" + child;
        if (!meshes.TryGetValue(key, out var m) || m == null)
        {
            var t = Find(Resources.Load<GameObject>(model).transform, child);
            meshes[key] = m = t != null ? t.GetComponent<MeshFilter>().sharedMesh : null;
        }
        return m;
    }

    // Навершие = верх габарита посоха в пространстве руки (посохи KayKit смотрят вдоль +Y руки)
    static Vector3 Tip(Transform hand, GameObject staff)
    {
        var b = new Bounds();
        bool any = false;
        foreach (var mf in staff.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var M = hand.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var p = M.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return any ? new Vector3(b.center.x, b.max.y - 0.12f, b.center.z) : Vector3.up;
    }

    // Корона: зубчатый обод с плоскими гранями, в пространстве кости head
    static Mesh Crown()
    {
        if (meshes.TryGetValue("crown", out var m) && m != null) return m;
        const int n = 10;
        const float r0 = 0.5f, r1 = 0.56f, h = 0.2f, spike = 0.42f;
        var v = new List<Vector3>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { v.Add(a); v.Add(b); v.Add(c); }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
        Vector3 P(float i, float r, float y) { float a = i * Mathf.PI * 2 / n; return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }
        for (int i = 0; i < n; i++)
        {
            // обод снаружи и изнутри (изнутри видно сверху-сзади камерой)
            Quad(P(i, r0, 0), P(i, r1, h), P(i + 1, r1, h), P(i + 1, r0, 0));
            Quad(P(i + 1, r0 - 0.04f, 0), P(i + 1, r1 - 0.04f, h), P(i, r1 - 0.04f, h), P(i, r0 - 0.04f, 0));
            // зубец: треугольник из середины сегмента, наружная и внутренняя стороны
            var a = P(i, r1, h); var b = P(i + 1, r1, h); var top = P(i + 0.5f, r1 + 0.05f, h + spike);
            var ai = P(i, r1 - 0.04f, h); var bi = P(i + 1, r1 - 0.04f, h); var ti = P(i + 0.5f, r1 + 0.01f, h + spike);
            Tri(a, top, b);
            Tri(bi, ti, ai);
            Tri(a, ai, ti); Tri(a, ti, top);
        }
        m = Flat("Crown", v);
        meshes["crown"] = m;
        return m;
    }

    // Гранёный камень-бипирамида: кристалл посоха и самоцвет короны
    static Mesh Gem()
    {
        if (meshes.TryGetValue("gem", out var m) && m != null) return m;
        const int n = 6;
        var v = new List<Vector3>();
        var top = new Vector3(0, 0.8f, 0); var bot = new Vector3(0, -0.5f, 0);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2 / n, b = (i + 1) * Mathf.PI * 2 / n;
            var p = new Vector3(Mathf.Cos(a) * 0.35f, 0, Mathf.Sin(a) * 0.35f);
            var q = new Vector3(Mathf.Cos(b) * 0.35f, 0, Mathf.Sin(b) * 0.35f);
            v.Add(p); v.Add(top); v.Add(q);
            v.Add(q); v.Add(bot); v.Add(p);
        }
        m = Flat("Gem", v);
        meshes["gem"] = m;
        return m;
    }

    static Mesh Flat(string name, List<Vector3> v)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v);
        var idx = new int[v.Count];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        m.SetTriangles(idx, 0);
        var uv = new Vector2[v.Count];
        m.uv = uv;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
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

    // ---------- Оружие из снаряжения (GameWeapons.cs) ----------
    // Надетое оружие заменяет посох облика: модель вида из деталей KayKit + навершие + свечение по заточке.
    // Части зовутся Wpn_*, Apply облика их не трогает — после каждого Apply оружие накладывается заново.

    static Material DarkMat => Mat("dark", () =>
    {
        var m = new Material(Base) { name = "Cos_Dark" };
        m.SetColor("_BaseColor", new Color(0.32f, 0.28f, 0.4f));
        m.SetColor("_EmissionColor", new Color(0.03f, 0.02f, 0.05f));
        return m;
    });

    static Material NightMat => Mat("night", () =>
    {
        var m = new Material(Base) { name = "Cos_Night" };
        m.SetColor("_BaseColor", new Color(0.45f, 0.55f, 0.95f));
        return m;
    });

    static Material WeaponMat(Game.WeaponKind k)
    {
        // скелетные модели KayKit (лич, клинок, топор) на своём атласе; золото и кость — однотонные
        bool skel = k.body == "lich" || k.body == "blade" || k.body == "axe";
        return k.mat switch
        {
            "gold" => GoldMat,
            "bone" => skel ? Resources.Load<Material>("Mats/Skeleton") : BoneMat,
            "dark" => skel ? Resources.Load<Material>("Mats/Skeleton") : DarkMat,
            "night" => NightMat,
            _ => skel ? Resources.Load<Material>("Mats/Skeleton") : Base,
        };
    }

    public static void ApplyWeapon(GameObject mage, Game.WeaponKind k, int tier)
    {
        if (mage == null) return;
        var hand = Find(mage.transform, "handslot.r");
        if (hand == null) return;
        var old = new List<Transform>();
        foreach (Transform c in hand) if (c.name.StartsWith("Wpn_")) old.Add(c);
        foreach (var t in old) { t.SetParent(null, false); Kill(t.gameObject); }
        // посох облика (и его свечение) виден, только пока оружия нет
        foreach (Transform c in hand)
            if (c.name == "Cos_Staff" || c.name == "Cos_Crystal" || c.name == "Cos_Glow") c.gameObject.SetActive(k == null);
        if (k == null) return;

        var mat = WeaponMat(k);
        GameObject body = null;
        switch (k.body)
        {
            case "staff": body = Spawn("Models/Characters/staff", hand, mat); break;
            case "lich": body = Spawn("Models/Characters/Skeleton_Staff", hand, mat); break;
            case "blade": body = Spawn("Models/Characters/Skeleton_Blade", hand, mat); break;
            case "axe": body = Spawn("Models/Characters/Skeleton_Axe", hand, mat); break;
            case "book": body = Spawn("Models/Characters/spellbook_open", hand, null); break;
            case "lantern": body = Spawn("Models/Dungeon/candle_lit", hand, null); body.transform.localScale = Vector3.one * 0.6f; break;
            case "wand": body = Part("Wpn_Body", hand, ChildMesh("Models/Characters/Mage", "1H_Wand"), mat, Vector3.zero, 1f).gameObject; break;
        }
        // сфера без древка: висит над ладонью
        var tip = body != null ? Tip(hand, body) : new Vector3(0, 0.45f, 0);
        if (!string.IsNullOrEmpty(k.head))
        {
            var at = tip + Vector3.up * (k.head == "crown" ? 0.05f : k.headSize * 0.45f);
            if (k.head == "crown") Part("Wpn_Head", hand, Crown(), GoldMat, at, 0.32f);
            else spinners.Add(Part("Wpn_Head", hand, Gem(), GemMat(k.headColor), at, k.headSize));
            if (k.head == "crown")
            {
                var gem = Part("Wpn_Gem", hand, Gem(), GemMat(k.headColor), at + Vector3.up * 0.08f, 0.1f);
                gem.localRotation = Quaternion.Euler(90, 0, 0);
            }
            tip = at;
        }
        if (tier > 0) WeaponGlow(hand, tip, tier);
    }

    static GameObject Spawn(string path, Transform hand, Material mat)
    {
        var go = Object.Instantiate(Resources.Load<GameObject>(path), hand, false);
        go.name = "Wpn_Body";
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return go;
    }

    // свечение заточки: свет и ореол цвета ступени, на вершине (+16) — ярче, крупнее и с шлейфом искр
    static void WeaponGlow(Transform hand, Vector3 tip, int tier)
    {
        var c = Game.GlowHdr[tier];
        var go = new GameObject("Wpn_Glow");
        go.transform.SetParent(hand, false);
        go.transform.localPosition = tip;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(Mathf.Min(1, c.r / 2.6f), Mathf.Min(1, c.g / 2.6f), Mathf.Min(1, c.b / 2.6f));
        // свет скромный: в портрете камера близко, и яркий источник заливал мага цветом целиком
        l.range = 1.6f + tier * 0.35f;
        l.intensity = 0.7f + tier * 0.4f;
        l.shadows = LightShadows.None;
        lights.Add((l, l.intensity, Random.value * 6f));
        Particles(go.transform, "Wpn_Halo", FxMat("light_01", false, Color.white), c, false, 4 + tier * 2, 0.7f, 0.35f + tier * 0.1f, 0.5f + tier * 0.14f, 0f, 0f);
        Particles(go.transform, "Wpn_Sparks", FxMat(tier >= 4 ? "fire_01" : "spark_04", false, Color.white), c * 1.3f, true, 6 + tier * 5, 0.7f, 0.12f, 0.26f + tier * 0.03f, 0.2f + tier * 0.05f, tier >= 4 ? -0.6f : -0.15f);
    }

    static void Kill(GameObject go)
    {
        if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
    }
}
