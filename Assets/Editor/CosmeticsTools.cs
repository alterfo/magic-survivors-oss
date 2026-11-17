using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Косметика: разбор моделей, выпечка шляп/мантий в Resources/Cosmetics, рендер примерки в PNG.
// Вызов: Unity -batchmode -projectPath . -executeMethod CosmeticsTools.Bake | Preview | Dump
// Bake читает исходники KayKit из Assets/CosmeticsSrc (кладутся на время, в репо не храним — 20 МБ анимаций на модель).
public static class CosmeticsTools
{
    const string Out = "Assets/Resources/Cosmetics";
    const string Chars = "Assets/Resources/Models/Characters";

    // ---------- Выпечка ----------

    public static void Bake()
    {
        Directory.CreateDirectory(Out + "/Hats");
        Directory.CreateDirectory(Out + "/Robes");
        Directory.CreateDirectory(Out + "/Textures");

        // жёсткая шляпа висит на кости head: переводим вершины в её пространство, на маге встаёт без подгонки
        BakeHat("hat_lich", $"{Chars}/Skeleton_Mage.fbx", "Skeleton_Mage_Hat");
        BakeHat("hat_bear", "Assets/CosmeticsSrc/Barbarian.fbx", "Barbarian_Hat");
        BakeHat("hat_knight", "Assets/CosmeticsSrc/Knight.fbx", "Knight_Helmet");
        CopyTex("Assets/CosmeticsSrc/barbarian_texture.png", Out + "/Textures/barbarian_texture.png");
        CopyTex("Assets/CosmeticsSrc/knight_texture.png", Out + "/Textures/knight_texture.png");

        // мантия: клетки атласа с тканью (r1c0, r1c1 — мантия и шляпа, r1c7 — штаны, r1c2 — плащ-акцент)
        Robe("robe_crimson", 352, 1.15f, 0.95f, 40, 1.1f, 1.2f);
        Robe("robe_emerald", 148, 1.1f, 0.95f, 45, 1.1f, 1.2f);
        Robe("robe_night", 228, 0.75f, 0.5f, 205, 0.35f, 1.5f);
        Robe("robe_gold", 42, 1.25f, 1.45f, 355, 1.1f, 0.9f);
        AssetDatabase.Refresh();
        Debug.Log("CosmeticsTools.Bake ok");
    }

    static void BakeHat(string id, string fbx, string child)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var head = Find(root.transform, "head");
        var hat = Find(root.transform, child);
        var src = hat.GetComponent<MeshFilter>().sharedMesh;
        var M = head.worldToLocalMatrix * hat.localToWorldMatrix;
        var v = src.vertices; var n = src.normals;
        for (int i = 0; i < v.Length; i++) v[i] = M.MultiplyPoint3x4(v[i]);
        for (int i = 0; i < n.Length; i++) n[i] = M.MultiplyVector(n[i]).normalized;
        var m = new Mesh { name = id };
        m.vertices = v;
        m.normals = n;
        m.uv = src.uv;
        m.triangles = src.triangles;
        m.RecalculateBounds();
        var path = $"{Out}/Hats/{id}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }

    // Импорт как у mage_texture (256 px, без мипов): копируем её .meta с новым guid
    static void CopyTex(string from, string to)
    {
        File.Copy(from, to, true);
        WriteMeta(to);
    }

    static void WriteMeta(string png)
    {
        var meta = File.ReadAllLines($"{Chars}/mage_texture.png.meta");
        for (int i = 0; i < meta.Length; i++)
            if (meta[i].StartsWith("guid:")) meta[i] = "guid: " + GUID.Generate();
        if (!File.Exists(png + ".meta")) File.WriteAllLines(png + ".meta", meta);
    }

    static void Robe(string id, float hue, float sat, float val, float accHue, float accSat, float accVal)
    {
        var src = new Texture2D(2, 2);
        src.LoadImage(File.ReadAllBytes($"{Chars}/mage_texture.png"));
        // в игре атлас и так режется до 256 (настройка импорта), храним сразу 256
        const int S = 256;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = src.GetPixelBilinear((x + 0.5f) / S, (y + 0.5f) / S);
                int col = x * 8 / S, row = 3 - y * 4 / S;
                bool main = row == 1 && (col == 0 || col == 1 || col == 7);
                bool acc = row == 1 && col == 2;
                if (main || acc)
                {
                    Color.RGBToHSV(c, out _, out var s, out var vv);
                    c = Color.HSVToRGB((main ? hue : accHue) / 360f, Mathf.Clamp01(s * (main ? sat : accSat)), Mathf.Clamp01(vv * (main ? val : accVal)));
                }
                px[y * S + x] = c;
            }
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        t.SetPixels(px);
        t.Apply();
        var path = $"{Out}/Robes/{id}.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        WriteMeta(path);
    }

    // ---------- Рендер примерки ----------

    static readonly string[][] Sets =
    {
        new[] { "hat_mage", "robe_violet", "staff_oak", "glow_none" },
        new[] { "hat_lich", "robe_crimson", "staff_lich", "glow_fire" },
        new[] { "hat_bear", "robe_emerald", "staff_wand", "glow_poison" },
        new[] { "hat_knight", "robe_night", "staff_crystal", "glow_ice" },
        new[] { "hat_crown", "robe_gold", "staff_oak", "glow_gold" },
        new[] { "hat_mage", "robe_night", "staff_lich", "glow_dark" },
    };

    public static void Preview()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        World.Mage = Resources.Load<Material>("Mats/Mage");
        World.Skeleton = Resources.Load<Material>("Mats/Skeleton");
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.5f);
        var moon = new GameObject("Moon").AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.intensity = 1.3f;
        moon.color = new Color(0.55f, 0.65f, 1f);
        moon.transform.rotation = Quaternion.Euler(52, -35, 0);

        var idle = System.Array.Find(AssetDatabase.LoadAllAssetsAtPath($"{Chars}/Mage.fbx"), o => o is AnimationClip c && c.name == "Idle") as AnimationClip;
        for (int i = 0; i < Sets.Length; i++)
        {
            var go = World.Character("Mage", 1.8f, out _, out _);
            World.Attach(go, "staff", World.Mage);
            go.transform.position = new Vector3((i - (Sets.Length - 1) / 2f) * 1.6f, 0, 0);
            go.transform.rotation = Quaternion.Euler(0, 180 + 20, 0);
            if (idle != null) idle.SampleAnimation(go, 0.4f);
            var s = Sets[i];
            Cosmetics.Apply(go, s[0], s[1], s[2], s[3]);
            var warm = new GameObject("Warm").AddComponent<Light>();
            warm.type = LightType.Point; warm.color = new Color(1f, 0.78f, 0.55f); warm.range = 9; warm.intensity = 1.5f;
            warm.transform.position = go.transform.position + new Vector3(0, 2.5f, -1.5f);
        }
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(1.5f, false, true);
        Cosmetics.Tick(0);

        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.1f);
        cam.fieldOfView = 30;
        Shot(cam, new Vector3(0, 1.3f, -10.5f), new Vector3(0, 1.0f, 0), "Logs/cos_front.png", 1800, 600);
        // ближе: голова и навершие двух первых
        Shot(cam, new Vector3(-3.2f, 2.0f, -3.6f), new Vector3(-3.2f, 1.45f, 0), "Logs/cos_close1.png", 1200, 800);
        Shot(cam, new Vector3(1.6f, 2.0f, -4.6f), new Vector3(1.6f, 1.45f, 0), "Logs/cos_close2.png", 1600, 800);
        Shot(cam, new Vector3(-2.0f, 1.4f, -2.6f), new Vector3(-2.6f, 0.8f, 0), "Logs/cos_hand.png", 1200, 800);
        // ракурс игры — сверху под углом
        Shot(cam, new Vector3(0, 9f, -7.5f), new Vector3(0, 0.8f, 0), "Logs/cos_top.png", 1600, 900);
        Debug.Log("CosmeticsTools.Preview ok");
    }

    static void Shot(Camera cam, Vector3 pos, Vector3 look, string file, int w, int h)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        rt.Release();
    }

    // ---------- Разбор моделей ----------

    public static void Dump()
    {
        var sb = new StringBuilder();
        foreach (var p in new[] { $"{Chars}/Mage.fbx", $"{Chars}/staff.fbx", $"{Chars}/Skeleton_Mage.fbx", $"{Chars}/Skeleton_Staff.fbx" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            sb.AppendLine("== " + p + (go == null ? " MISSING" : ""));
            if (go != null) Tree(go.transform, 0, sb);
        }
        File.WriteAllText("Logs/cos_dump.txt", sb.ToString());
    }

    static void Tree(Transform t, int d, StringBuilder sb)
    {
        var line = new string(' ', d * 2) + t.name + $" lp={t.localPosition:F3} lr={t.localEulerAngles:F0} ls={t.localScale:F3}";
        var mf = t.GetComponent<MeshFilter>();
        var smr = t.GetComponent<SkinnedMeshRenderer>();
        var m = mf != null ? mf.sharedMesh : smr != null ? smr.sharedMesh : null;
        if (m != null)
        {
            line += $" MESH[{(smr ? "skin" : "rigid")}] v={m.vertexCount} b={m.bounds.size:F3}";
            // какие клетки атласа 8x4 использует меш — по ним выбираем, что перекрашивать в мантии
            var cells = new int[32];
            foreach (var uv in m.uv) cells[Mathf.Clamp((int)(uv.x * 8), 0, 7) + 8 * Mathf.Clamp((int)((1 - uv.y) * 4), 0, 3)]++;
            line += " cells:";
            for (int i = 0; i < 32; i++) if (cells[i] > 0) line += $" r{i / 8}c{i % 8}={cells[i]}";
        }
        sb.AppendLine(line);
        foreach (Transform c in t) Tree(c, d + 1, sb);
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
}
