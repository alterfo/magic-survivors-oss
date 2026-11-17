using System.Collections.Generic;
using UnityEngine;

// Процедурная графика в стиле Magic Survival: чернильные кляксы-монстры, силуэт ведьмы, кусты, земля с крапинками.
public static class Gen
{
    static readonly Dictionary<string, Sprite> cache = new();

    static Sprite Make(Texture2D t, float ppu, Vector2 pivot)
    {
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, ppu, 0, SpriteMeshType.FullRect);
    }

    static Texture2D Tex(int w, int h) => new(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

    // Клякса: радиус зависит от угла (гармоники + шипы), край сглажен. spikes 0 — гладкая, 1 — колючая.
    public static Sprite Blob(int seed, float spikes, int size = 128)
    {
        string key = $"blob{seed}_{spikes}_{size}";
        if (cache.TryGetValue(key, out var s)) return s;
        var rnd = new System.Random(seed);
        int n = 5;
        var amp = new float[n]; var ph = new float[n];
        for (int i = 0; i < n; i++) { amp[i] = (float)rnd.NextDouble() * 0.08f / (i + 1); ph[i] = (float)rnd.NextDouble() * 6.28f; }
        int spikeCount = 9 + rnd.Next(12);
        var spikeLen = new float[spikeCount];
        for (int i = 0; i < spikeCount; i++) spikeLen[i] = 0.4f + (float)rnd.NextDouble() * 0.6f;

        var t = Tex(size, size);
        var px = new Color32[size * size];
        float h = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f - h) / h, dy = (y + 0.5f - h) / h;
            float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
            float R = 0.55f;
            for (int i = 0; i < n; i++) R += amp[i] * Mathf.Sin(a * (i + 2) + ph[i]);
            // шип: узкий пик вокруг равномерно расставленных углов
            float sa = (a + Mathf.PI) / (2 * Mathf.PI) * spikeCount;
            int si = Mathf.FloorToInt(sa) % spikeCount;
            float f = sa - Mathf.Floor(sa);
            float peak = Mathf.Pow(1 - Mathf.Abs(f - 0.5f) * 2, 6);
            R += spikes * 0.4f * spikeLen[si] * peak;
            float alpha = Mathf.Clamp01((R - r) * h * 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
        }
        t.SetPixels32(px);
        return cache[key] = Make(t, size, new Vector2(0.5f, 0.5f));
    }

    // Мягкий светящийся круг для свечения и кристаллов.
    public static Sprite Glow()
    {
        if (cache.TryGetValue("glow", out var s)) return s;
        int size = 64; var t = Tex(size, size); float h = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float r = new Vector2(x + 0.5f - h, y + 0.5f - h).magnitude / h;
            float a = Mathf.Clamp01(1 - r);
            t.SetPixel(x, y, new Color(1, 1, 1, a * a));
        }
        return cache["glow"] = Make(t, size, new Vector2(0.5f, 0.5f));
    }

    // Силуэт ведьмы в шляпе, белый — красится через SpriteRenderer.color.
    public static Sprite Witch()
    {
        if (cache.TryGetValue("witch", out var s)) return s;
        int W = 96, H = 128; var t = Tex(W, H);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
            float d = 1;
            // плащ: трапеция снизу
            float cw = Mathf.Lerp(0.36f, 0.16f, Mathf.InverseLerp(0.02f, 0.5f, v));
            if (v > 0.02f && v < 0.5f) d = Mathf.Min(d, Mathf.Abs(u - 0.5f) - cw);
            // голова
            d = Mathf.Min(d, new Vector2((u - 0.5f) * 0.75f, v - 0.52f).magnitude - 0.09f);
            // поля шляпы
            d = Mathf.Min(d, new Vector2((u - 0.5f) / 0.42f, (v - 0.6f) / 0.035f).magnitude - 1);
            // конус шляпы с загнутым кончиком
            if (v > 0.6f && v < 0.98f)
            {
                float k = Mathf.InverseLerp(0.6f, 0.98f, v);
                float cx = 0.5f + 0.18f * k * k;
                float hw = 0.2f * (1 - k);
                d = Mathf.Min(d, Mathf.Abs(u - cx) - hw);
            }
            float a = Mathf.Clamp01(-d * 120 + 0.5f);
            px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        t.SetPixels32(px);
        return cache["witch"] = Make(t, 100, new Vector2(0.5f, 0.15f));
    }

    // Тайл земли: ровный цвет с мелкими крапинками и пятнами, бесшовный.
    public static Sprite Ground()
    {
        if (cache.TryGetValue("ground", out var s)) return s;
        int size = 256; var t = Tex(size, size);
        t.wrapMode = TextureWrapMode.Repeat;
        var rnd = new System.Random(7);
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++)
        {
            // бесшовный шум: смешиваем четыре сдвинутые копии, чтобы края тайла совпадали
            float x = i % size, y = i / size, fx = x / size, fy = y / size, k = 0.02f;
            float n = Mathf.Lerp(
                Mathf.Lerp(Mathf.PerlinNoise(x * k, y * k), Mathf.PerlinNoise((x - size) * k, y * k), fx),
                Mathf.Lerp(Mathf.PerlinNoise(x * k, (y - size) * k), Mathf.PerlinNoise((x - size) * k, (y - size) * k), fx), fy);
            float v = 0.88f + n * 0.07f;
            px[i] = new Color(v, v, v, 1);
        }
        // крапинки
        for (int k = 0; k < 900; k++)
        {
            int x = rnd.Next(size), y = rnd.Next(size);
            float v = rnd.NextDouble() < 0.7 ? 0.74f : 1f;
            px[y * size + x] = new Color(v, v, v, 1);
            if (rnd.NextDouble() < 0.3) px[y * size + (x + 1) % size] = new Color(v, v, v, 1);
        }
        t.SetPixels(px);
        return cache["ground"] = Make(t, 32, new Vector2(0.5f, 0.5f));
    }
}
