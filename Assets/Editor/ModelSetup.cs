using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

// Настройка 3D: URP-ассет, материалы в Resources/Mats, импорт KayKit и аниматоры в Resources/Anim.
// Вызов: unity command eval 'return ModelSetup.All();'
public static class ModelSetup
{
    const string Chars = "Assets/Resources/Models/Characters";

    public static string All() => Pipeline() + " | " + Materials() + " | " + Characters();

    public static string Pipeline()
    {
        var rp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        GraphicsSettings.defaultRenderPipeline = rp;
        int cur = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = rp;
        }
        QualitySettings.SetQualityLevel(cur, false);
        AssetDatabase.SaveAssets();
        return "rp=" + (rp ? rp.name : "null");
    }

    static Material Save(Material m, string name)
    {
        Directory.CreateDirectory("Assets/Resources/Mats");
        string p = $"Assets/Resources/Mats/{name}.mat";
        AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(m, p);
        return m;
    }

    static Material Lit(string name, string tex, bool emission)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (tex != null) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        m.SetFloat("_Smoothness", 0.15f);
        if (emission)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return Save(m, name);
    }

    public static string Materials()
    {
        Save(new Material(Shader.Find("MS/Additive")), "Additive");
        Save(new Material(Shader.Find("MS/Alpha")), "Alpha");
        Save(new Material(Shader.Find("Universal Render Pipeline/Unlit")), "Glow");
        Lit("Mage", $"{Chars}/mage_texture.png", true);
        Lit("Skeleton", $"{Chars}/skeleton_texture.png", true);
        Lit("Halloween", "Assets/Resources/Models/Props/halloweenbits_texture.png", false);
        Lit("Dungeon", "Assets/Resources/Models/Dungeon/dungeon_texture.png", true);
        var g = Lit("Ground", null, false);
        g.SetFloat("_Smoothness", 0.05f);
        AssetDatabase.SaveAssets();
        return "mats ok";
    }

    // Склейка мешей в World.Combine читает вершины на рантайме — нужен Read/Write
    public static string Readable()
    {
        int n = 0;
        foreach (var path in Directory.GetFiles(Chars, "*.fbx"))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp.isReadable) continue;
            imp.isReadable = true;
            imp.SaveAndReimport();
            n++;
        }
        return "readable " + n;
    }

    public static string Characters()
    {
        var sb = new StringBuilder();
        Directory.CreateDirectory("Assets/Resources/Anim");
        foreach (var path in Directory.GetFiles(Chars, "*.fbx"))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var defs = imp.defaultClipAnimations;
            if (defs.Length < 5) continue; // оружие и аксессуары без анимаций
            imp.animationType = ModelImporterAnimationType.Generic;
            foreach (var c in defs)
            {
                var n = c.name.ToLower();
                c.loopTime = n.Contains("idle") || n.Contains("running") || n.Contains("walking") || n == "spellcasting";
            }
            imp.clipAnimations = defs;
            imp.SaveAndReimport();

            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            var name = Path.GetFileNameWithoutExtension(path);
            var ctrlPath = $"Assets/Resources/Anim/{name}.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;

            AnimatorState Add(string state, params string[] keys)
            {
                foreach (var k in keys)
                {
                    var clip = clips.FirstOrDefault(x => x.name == k);
                    if (clip == null) continue;
                    var s = sm.AddState(state);
                    s.motion = clip;
                    sb.Append($"{name}.{state}={clip.name} ");
                    return s;
                }
                return null;
            }

            var run = Add("Run", "Running_A");
            var walk = Add("Walk", "Walking_D_Skeletons", "Walking_A");
            var idle = Add("Idle", "Idle");
            Add("Death", "Death_C_Skeletons", "Death_A");
            var spawn = Add("Spawn", "Spawn_Ground_Skeletons");
            var cast = Add("Cast", "Spellcast_Shoot");
            var attack = Add("Attack", "1H_Melee_Attack_Chop");
            var hit = Add("Hit", "Hit_A");
            sm.defaultState = name.StartsWith("Skeleton") ? walk : idle;

            void Back(AnimatorState from, AnimatorState to)
            {
                if (from == null || to == null) return;
                var t = from.AddTransition(to);
                t.hasExitTime = true;
                t.exitTime = 0.85f;
                t.duration = 0.15f;
            }
            Back(spawn, walk);
            Back(cast, idle);
            Back(attack, walk);
            Back(hit, idle);
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
