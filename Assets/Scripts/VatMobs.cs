using System.Collections.Generic;
using UnityEngine;

public static class VatMobs
{
    public class Info
    {
        public Texture2D tex;
        public int verts;
        public Dictionary<string, int> offset = new();
        public Dictionary<string, int> frames = new();
    }

    static readonly Dictionary<string, Info> cache = new();

    public static Info Get(string model)
    {
        if (cache.TryGetValue(model, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>("Vat/" + model + "_VAT");
        var meta = Resources.Load<TextAsset>("Vat/" + model + "_VAT");
        if (tex == null || meta == null) { cache[model] = null; return null; }
        var info = new Info { tex = tex };
        foreach (var line in meta.text.Split('\n'))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var off) || !int.TryParse(parts[2], out var fr)) continue;
            info.offset[parts[0]] = off;
            info.frames[parts[0]] = fr;
        }
        cache[model] = info;
        return info;
    }
}

public class VatActor : MonoBehaviour
{
    public Renderer rend;
    VatMobs.Info info;
    MaterialPropertyBlock block;
    int offset, frames;
    Color color = Color.white;

    public void Setup(VatMobs.Info i, Renderer r)
    {
        info = i; rend = r;
        block = new MaterialPropertyBlock();
        State("Idle");
    }

    public void State(string state)
    {
        if (info == null) return;
        if (info.offset.TryGetValue(state, out var o))
        {
            offset = o;
            frames = info.frames.TryGetValue(state, out var f) ? Mathf.Max(1, f) : 32;
        }
        Push();
    }

    public void Tint(Color c) { color = c; Push(); }

    void Push()
    {
        if (rend == null || block == null) return;
        block.SetColor("_Color", color);
        block.SetFloat("_VatStateOffset", offset);
        block.SetFloat("_VatStateFrames", frames);
        rend.SetPropertyBlock(block);
    }
}
