using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SpriteBaker
{
    const int Fps = 30;
    const int Frame = 128;
    const int Cols = 32;
    const int Dirs = 6;
    const float Pitch = 55f;

    static readonly string[,] States = {
        { "Walk", "Walking_D_Skeletons" }, { "Attack", "1H_Melee_Attack_Chop" }, { "Hit", "Hit_A" }, { "Death", "Death_C_Skeletons" } };
    static readonly string[] Models = { "Skeleton_Warrior", "Skeleton_Mage", "Skeleton_Rogue", "Skeleton_Minion", "Mage" };

    public static void Bake()
    {
        Directory.CreateDirectory("Assets/Resources/Sprites");
        foreach (var name in Models) BakeModel(name);
        AssetDatabase.SaveAssets();
        Debug.Log("sprite: bake done");
    }

    static void BakeModel(string name)
    {
        var prefab = Resources.Load<GameObject>("Models/Characters/" + name);
        var ctrl = Resources.Load<RuntimeAnimatorController>("Anim/" + name);
        if (prefab == null || ctrl == null) { Debug.Log($"sprite: skip {name} prefab={prefab != null} ctrl={ctrl != null}"); return; }
        var go = Object.Instantiate(prefab);
        go.transform.position = new Vector3(2000, 0, 2000);
        var anim = go.GetComponentInChildren<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        anim.applyRootMotion = false;

        var clips = new Dictionary<string, AnimationClip>();
        foreach (var c in ctrl.animationClips)
        {
            var raw = c.name;
            clips[raw.Substring(raw.LastIndexOfAny(new[] { '|', '_' }) + 1)] = c;
            clips[raw] = c;
        }

        var rends = go.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rends) bounds.Encapsulate(r.bounds);

        var lightGo = new GameObject("spriteLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.intensity = 1.15f;
        lightGo.transform.rotation = Quaternion.Euler(50, -35, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.42f, 0.5f);

        var camGo = new GameObject("spriteCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(0.6f, bounds.size.y * 0.58f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 40f;
        cam.allowHDR = false;
        cam.allowMSAA = true;

        var pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        var rt = new RenderTexture(Frame, Frame, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;

        var counts = new List<int>();
        var jobs = new List<(int dir, int state, int frame, AnimationClip clip, float t)>();
        for (int si = 0; si < States.GetLength(0); si++)
        {
            if (!clips.TryGetValue(States[si, 1], out var clip)) { Debug.Log($"sprite: {name} no clip {States[si, 0]}"); counts.Add(0); continue; }
            int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * Fps));
            counts.Add(frames);
            for (int d = 0; d < Dirs; d++)
                for (int f = 0; f < frames; f++)
                    jobs.Add((d, si, f, clip, frames > 1 ? (float)f / (frames - 1) : 0f));
        }
        if (jobs.Count == 0) { Cleanup(go, camGo, lightGo, rt); return; }

        int rows = Mathf.CeilToInt((float)jobs.Count / Cols);
        var atlas = new Texture2D(Cols * Frame, rows * Frame, TextureFormat.RGBA32, false) { name = name + "_sprites" };
        var clear = new Color32[Cols * Frame * rows * Frame];
        atlas.SetPixels32(clear);

        int i = 0;
        int lastDir = -1;
        foreach (var j in jobs)
        {
            if (j.dir != lastDir)
            {
                lastDir = j.dir;
                float yaw = j.dir * 360f / Dirs;
                go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                float rad = Mathf.Sqrt(bounds.extents.x * bounds.extents.x + bounds.extents.z * bounds.extents.z) + bounds.size.y;
                var back = Quaternion.Euler(Pitch, 0, 0) * Vector3.forward;
                cam.transform.position = pivot - back * (rad * 2f + 1f);
                cam.transform.rotation = Quaternion.LookRotation(back);
            }
            anim.Play(States[j.state, 0], 0, j.t);
            anim.Update(0f);
            cam.Render();
            RenderTexture.active = rt;
            atlas.ReadPixels(new Rect(0, 0, Frame, Frame), (i % Cols) * Frame, (i / Cols) * Frame);
            RenderTexture.active = null;
            i++;
        }
        atlas.Apply();

        var png = atlas.EncodeToPNG();
        File.WriteAllBytes($"Assets/Resources/Sprites/{name}_sprites.png", png);
        var meta = new StringBuilder();
        meta.AppendLine($"dirs={Dirs}");
        meta.AppendLine($"cols={Cols}");
        meta.AppendLine($"frame={Frame}");
        meta.AppendLine($"rows={rows}");
        for (int s = 0; s < States.GetLength(0); s++) meta.AppendLine($"{States[s, 0]},{counts[s]}");
        File.WriteAllText($"Assets/Resources/Sprites/{name}_sprites.txt", meta.ToString());
        Debug.Log($"sprite: {name} dirs={Dirs} frames={jobs.Count} atlas={atlas.width}x{atlas.height} bytes={png.Length}");
        Cleanup(go, camGo, lightGo, rt);
        Object.DestroyImmediate(atlas);
    }

    static void Cleanup(GameObject go, GameObject camGo, GameObject lightGo, RenderTexture rt)
    {
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(lightGo);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
