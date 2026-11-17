using System.Collections.Generic;
using UnityEngine;

// Эффекты: несколько глобальных систем частиц (Emit по требованию), вспышки света, молнии, пятна на земле.
public static class Vfx
{
    public static ParticleSystem Spark, Fire, Glow, Smoke, Ring, Bone, Ember;
    static readonly List<(Light l, float t, float life, float i0)> flashes = new();
    static readonly Stack<Light> lightPool = new();
    static readonly List<(LineRenderer lr, Vector3 a, Vector3 b, float t, float life)> bolts = new();
    static readonly Stack<LineRenderer> boltPool = new();
    static readonly List<(Renderer r, float t, float life, Color c, float s0, float s1)> decals = new();
    static readonly Dictionary<Material, Stack<Renderer>> decalPools = new();
    static readonly Dictionary<string, Material> decalMats = new();
    static readonly List<(Transform tr, Vector3 at, float h, float t, float life)> spikes = new();
    static readonly Stack<Transform> spikePool = new();
    static Material addMat, alphaMat, decalAdd, decalAlpha, boltMat, rockMat, iceMat;
    static MaterialPropertyBlock mpb;
    static Transform root;

    public static void Init()
    {
        root = new GameObject("VFX").transform;
        mpb = new MaterialPropertyBlock();
        addMat = Resources.Load<Material>("Mats/Additive");
        alphaMat = Resources.Load<Material>("Mats/Alpha");
        flashes.Clear(); lightPool.Clear(); bolts.Clear(); boltPool.Clear(); decals.Clear(); decalPools.Clear(); decalMats.Clear();
        spikes.Clear(); spikePool.Clear();
        MakeShardMeshes();

        var grow = AnimationCurve.EaseInOut(0, 0.6f, 1, 1.4f);
        var shrink = AnimationCurve.Linear(0, 1, 1, 0);
        var bigGrow = AnimationCurve.EaseInOut(0, 0.1f, 1, 1f);
        Spark = Make("Spark", Art.Tex("spark_01"), true, 3.5f, 2.5f, 1.3f, shrink, ParticleSystemRenderMode.Stretch);
        Fire = Make("Fire", Art.Tex("fire_01"), true, -0.3f, 1.2f, 1.6f, grow, ParticleSystemRenderMode.Billboard);
        Glow = Make("Glow", Art.Tex("light_01"), true, 0, 0f, 1.8f, shrink, ParticleSystemRenderMode.Billboard);
        Ember = Make("Ember", Art.Tex("light_01"), true, -0.4f, 0.5f, 2.5f, shrink, ParticleSystemRenderMode.Billboard);
        Smoke = Make("Smoke", Art.Tex("smoke_01"), false, -0.15f, 1.5f, 1f, grow, ParticleSystemRenderMode.Billboard);
        Ring = Make("Ring", Art.Tex("circle_02"), true, 0, 0, 2f, bigGrow, ParticleSystemRenderMode.HorizontalBillboard);
        Bone = Make("Bone", Art.White, false, 2.5f, 1.5f, 1f, shrink, ParticleSystemRenderMode.Billboard);

        MakeHalos();

        decalAdd = new Material(addMat) { mainTexture = Art.Tex("circle_02") };
        decalAlpha = new Material(alphaMat) { mainTexture = Art.Tex("scorch_01") };
        boltMat = new Material(addMat) { mainTexture = Art.White };
        boltMat.SetColor("_Color", new Color(3f, 3f, 4f, 1));
    }

    // Ореолы кристаллов опыта и сердец: одна система частиц на все, частицы каждый кадр расставляются по кристаллам
    // (SetParticles) — один вызов отрисовки на сотни ореолов вместо света или объекта на каждый.
    static ParticleSystem haloPs;
    static readonly ParticleSystem.Particle[] haloBuf = new ParticleSystem.Particle[1200];
    static int haloN;

    static void MakeHalos()
    {
        var go = new GameObject("VFX_Halos");
        go.transform.SetParent(root, false);
        haloPs = go.AddComponent<ParticleSystem>();
        haloPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = haloPs.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = haloBuf.Length;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0; main.startLifetime = 1000;
        var em = haloPs.emission; em.enabled = false;
        var sh = haloPs.shape; sh.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        var m = new Material(addMat) { mainTexture = Art.Tex("light_01") };
        m.SetColor("_Color", new Color(1.5f, 1.5f, 1.5f, 1));
        r.material = m;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortMode = ParticleSystemSortMode.None;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        haloPs.Play();
    }

    public static void HaloBegin() => haloN = 0;

    public static void Halo(Vector3 pos, float size, Color c)
    {
        if (haloN >= haloBuf.Length) return;
        ref var p = ref haloBuf[haloN++];
        p.position = pos;
        p.startSize = size;
        p.startColor = c;
        p.startLifetime = 1000;
        p.remainingLifetime = 500;
        p.velocity = Vector3.zero;
    }

    public static void HaloEnd() { if (haloPs != null) haloPs.SetParticles(haloBuf, haloN); }

    static ParticleSystem Make(string name, Texture tex, bool additive, float gravity, float drag, float hdr, AnimationCurve size, ParticleSystemRenderMode mode)
    {
        var go = new GameObject("VFX_" + name);
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = 4000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.startSpeed = 0;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, size);
        if (drag > 0)
        {
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = drag;
        }
        var r = go.GetComponent<ParticleSystemRenderer>();
        var m = new Material(additive ? addMat : alphaMat) { mainTexture = tex };
        m.SetColor("_Color", new Color(hdr, hdr, hdr, 1));
        r.material = m;
        r.renderMode = mode;
        if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.08f; r.lengthScale = 1.5f; }
        r.sortMode = ParticleSystemSortMode.None;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // «Спокойный режим» и большие толпы (Game.Update): меньше искр в россыпях, вспышки тусклее — не рябит и легче кадру
    public static float Amount = 1, Dim = 1;

    public static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c, float rot = -1)
    {
        if (Dim < 1) { c.r *= Dim; c.g *= Dim; c.b *= Dim; }
        var p = new ParticleSystem.EmitParams
        {
            position = pos,
            velocity = vel,
            startSize = size,
            startLifetime = life,
            startColor = c,
            rotation = rot < 0 ? Random.Range(0, 360f) : rot,
            applyShapeToPosition = false,
        };
        ps.Emit(p, 1);
    }

    public static void Burst(ParticleSystem ps, Vector3 pos, int n, float speed, float size, float life, Color c, float up = 0.5f, float spread = 0.2f)
    {
        if (Amount < 1) n = Mathf.CeilToInt(n * Amount);
        for (int i = 0; i < n; i++)
        {
            var d = Random.onUnitSphere;
            d.y = Mathf.Abs(d.y) * up + (1 - up) * d.y;
            Emit(ps, pos + Random.insideUnitSphere * spread, d * speed * Random.Range(0.4f, 1f),
                size * Random.Range(0.6f, 1.3f), life * Random.Range(0.6f, 1.2f), c);
        }
    }

    public static void Flash(Vector3 pos, Color c, float intensity, float range, float life)
    {
        var l = lightPool.Count > 0 ? lightPool.Pop() : null;
        if (l == null)
        {
            l = new GameObject("Flash").AddComponent<Light>();
            l.transform.SetParent(root, false);
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
        }
        l.gameObject.SetActive(true);
        l.transform.position = pos;
        l.color = c;
        l.range = range;
        l.intensity = intensity;
        flashes.Add((l, 0, life, intensity));
    }

    public static void Bolt(Vector3 a, Vector3 b, Color c, float life = 0.18f)
    {
        var lr = boltPool.Count > 0 ? boltPool.Pop() : null;
        if (lr == null)
        {
            lr = new GameObject("Bolt").AddComponent<LineRenderer>();
            lr.transform.SetParent(root, false);
            lr.sharedMaterial = boltMat;
            lr.positionCount = 9;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        lr.gameObject.SetActive(true);
        lr.startColor = lr.endColor = c;
        Jitter(lr, a, b, 0.14f);
        bolts.Add((lr, a, b, 0, life));
    }

    static void Jitter(LineRenderer lr, Vector3 a, Vector3 b, float w)
    {
        int n = lr.positionCount;
        var side = Vector3.Cross((b - a).normalized, Vector3.up);
        for (int i = 0; i < n; i++)
        {
            float k = i / (n - 1f);
            var p = Vector3.Lerp(a, b, k);
            if (i > 0 && i < n - 1) p += side * Random.Range(-0.35f, 0.35f) + Vector3.up * Random.Range(-0.2f, 0.2f);
            lr.SetPosition(i, p);
        }
        lr.widthMultiplier = w;
    }

    // Молния с ответвлениями: основной разряд и пара коротких веток из случайных точек
    public static void Fork(Vector3 a, Vector3 b, Color c, int branches = 2, float life = 0.18f)
    {
        Bolt(a, b, c, life);
        for (int i = 0; i < branches; i++)
        {
            var from = Vector3.Lerp(a, b, Random.Range(0.25f, 0.75f));
            var dir = (b - a).normalized + Random.insideUnitSphere * 0.9f;
            Bolt(from, from + dir.normalized * Random.Range(0.8f, 1.8f), c * 0.8f, life * 0.7f);
        }
    }

    // Процедурные гранёные меши: сосулька (тонкая, сужается в острие) и рваный каменный осколок
    public static Mesh[] Icicles, Rocks;

    static void MakeShardMeshes()
    {
        var rnd = new System.Random(7);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        Icicles = new Mesh[4];
        Rocks = new Mesh[5];
        for (int v = 0; v < Icicles.Length; v++)
        {
            int n = 6;
            float tw = R(0, 1);
            Icicles[v] = Facet(n, new[] { (-0.5f, 0.5f, 0.12f), (-0.1f, 0.34f, 0.12f), (0.22f, 0.17f, 0.1f) },
                new Vector3(R(-0.04f, 0.04f), 0.5f, R(-0.04f, 0.04f)), R, tw);
        }
        for (int v = 0; v < Rocks.Length; v++)
        {
            int n = 5 + v % 2;
            Rocks[v] = Facet(n, new[] { (-0.5f, 0.55f, 0.4f), (-0.05f, 0.45f, 0.45f), (0.3f, 0.22f, 0.6f) },
                new Vector3(R(-0.18f, 0.18f), R(0.4f, 0.5f), R(-0.18f, 0.18f)), R, R(0, 1));
        }
    }

    // Кольца (высота, радиус, разброс) + вершина; плоские нормали — каждая грань отдельно, чтобы было видно сколы
    static Mesh Facet(int n, (float y, float r, float jit)[] rings, Vector3 tip, System.Func<float, float, float> R, float twist)
    {
        var pts = new Vector3[rings.Length][];
        for (int k = 0; k < rings.Length; k++)
        {
            pts[k] = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = (i + twist * k * 0.5f + R(-0.2f, 0.2f)) * Mathf.PI * 2 / n;
                float rr = rings[k].r * R(1 - rings[k].jit, 1 + rings[k].jit * 0.5f);
                pts[k][i] = new Vector3(Mathf.Cos(a) * rr, rings[k].y + R(-rings[k].jit, rings[k].jit) * 0.12f, Mathf.Sin(a) * rr);
            }
        }
        var verts = new List<Vector3>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { verts.Add(a); verts.Add(b); verts.Add(c); }
        for (int k = 0; k + 1 < rings.Length; k++)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Tri(pts[k][i], pts[k + 1][i], pts[k + 1][j]);
                Tri(pts[k][i], pts[k + 1][j], pts[k][j]);
            }
        var top = pts[rings.Length - 1];
        for (int i = 0; i < n; i++) Tri(top[i], tip, top[(i + 1) % n]);
        var c0 = new Vector3(0, rings[0].y, 0);
        for (int i = 0; i < n; i++) Tri(pts[0][i], pts[0][(i + 1) % n], c0);
        var m = new Mesh { name = "Shard" };
        m.SetVertices(verts);
        var idx = new int[verts.Count];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        // порядок вершин в Tri — по часовой снаружи (как ждёт Unity)
        m.SetTriangles(idx, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Каменный шип: вылетает из земли, держится и уходит обратно
    public static void Spike(Vector3 at, float h, Color c, float life = 0.7f, bool ice = false)
    {
        var tr = spikePool.Count > 0 ? spikePool.Pop() : null;
        if (tr == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // освещённый камень, а не плоский светящийся куб
            if (rockMat == null) rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.32f, 0.26f, 0.21f) };
            rockMat.SetFloat("_Smoothness", 0.15f);
            r.sharedMaterial = rockMat;
            tr = go.transform;
        }
        if (iceMat == null)
        {
            // лёд: гладкий, голубой, слегка светится изнутри
            iceMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.62f, 0.86f, 1f) };
            iceMat.SetFloat("_Smoothness", 0.95f);
            iceMat.EnableKeyword("_EMISSION");
            iceMat.SetColor("_EmissionColor", new Color(0.18f, 0.42f, 0.75f));
        }
        tr.GetComponent<Renderer>().sharedMaterial = ice ? iceMat : rockMat;
        tr.GetComponent<MeshFilter>().sharedMesh = ice ? Icicles[Random.Range(0, Icicles.Length)] : Rocks[Random.Range(0, Rocks.Length)];
        tr.gameObject.SetActive(true);
        tr.rotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0, 360f), Random.Range(-25f, 25f));
        tr.localScale = (ice ? new Vector3(0.3f, 1.1f, 0.3f) : new Vector3(Random.Range(0.45f, 0.65f), 1f, Random.Range(0.45f, 0.65f))) * h;
        spikes.Add((tr, at, h, 0, life));
    }

    // Плоское пятно на земле: additive — светящийся круг, иначе — выжженная земля.
    public static Renderer Decal(Vector2 p, float size, Color c, float life, bool additive, float growTo = -1, string tex = null)
    {
        var mat = additive ? decalAdd : decalAlpha;
        if (tex != null && !decalMats.TryGetValue(tex, out mat))
            decalMats[tex] = mat = new Material(addMat) { mainTexture = Art.Tex(tex) };
        if (!decalPools.TryGetValue(mat, out var pool)) decalPools[mat] = pool = new Stack<Renderer>();
        var r = pool.Count > 0 ? pool.Pop() : null;
        if (r == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        r.gameObject.SetActive(true);
        r.transform.SetPositionAndRotation(new Vector3(p.x, additive ? 0.04f : 0.02f, p.y), Quaternion.Euler(90, Random.Range(0, 360f), 0));
        r.transform.localScale = Vector3.one * size;
        decals.Add((r, 0, life, c, size, growTo < 0 ? size : growTo));
        SetColor(r, c);
        return r;
    }

    static void SetColor(Renderer r, Color c)
    {
        r.GetPropertyBlock(mpb);
        mpb.SetColor("_Color", c);
        r.SetPropertyBlock(mpb);
    }

    public static void Update(float dt)
    {
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            var f = flashes[i];
            f.t += dt;
            if (f.t >= f.life) { f.l.gameObject.SetActive(false); lightPool.Push(f.l); flashes.RemoveAt(i); continue; }
            f.l.intensity = f.i0 * (1 - f.t / f.life);
            flashes[i] = f;
        }
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            b.t += dt;
            if (b.t >= b.life) { b.lr.gameObject.SetActive(false); boltPool.Push(b.lr); bolts.RemoveAt(i); continue; }
            Jitter(b.lr, b.a, b.b, 0.14f * (1 - b.t / b.life) + 0.03f);
            bolts[i] = b;
        }
        for (int i = spikes.Count - 1; i >= 0; i--)
        {
            var sp = spikes[i];
            sp.t += dt;
            if (sp.t >= sp.life) { sp.tr.gameObject.SetActive(false); spikePool.Push(sp.tr); spikes.RemoveAt(i); continue; }
            float k = sp.t / sp.life;
            float up = k < 0.15f ? k / 0.15f : k > 0.7f ? 1 - (k - 0.7f) / 0.3f : 1;
            sp.tr.position = sp.at + Vector3.up * (up * sp.h * 0.55f - sp.h * 0.35f);
            spikes[i] = sp;
        }
        for (int i = decals.Count - 1; i >= 0; i--)
        {
            var d = decals[i];
            d.t += dt;
            if (d.t >= d.life)
            {
                d.r.gameObject.SetActive(false);
                decalPools[d.r.sharedMaterial].Push(d.r);
                decals.RemoveAt(i);
                continue;
            }
            float k = d.t / d.life;
            d.r.transform.localScale = Vector3.one * Mathf.Lerp(d.s0, d.s1, k);
            var c = d.c;
            c.a *= k < 0.7f ? 1 : 1 - (k - 0.7f) / 0.3f;
            SetColor(d.r, c);
            decals[i] = d;
        }
    }
}
