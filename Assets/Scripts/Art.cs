using System.Collections.Generic;
using UnityEngine;

// Загрузка спрайтов из Resources: Tiles/tile_NNNN (Kenney Tiny Dungeon) и Fx/<name> (Kenney Particle Pack).
public static class Art
{
    static readonly Dictionary<string, Sprite> cache = new();
    static Texture2D white;
    static Sprite pixel;

    static readonly Dictionary<string, Texture2D> texCache = new();

    // Текстура из Kenney Particle Pack для частиц.
    public static Texture2D Tex(string name)
    {
        if (!texCache.TryGetValue(name, out var t) || t == null)
            texCache[name] = t = Resources.Load<Texture2D>("Fx/" + name);
        return t;
    }

    public static Sprite Tile(int i) => Load($"Tiles/tile_{i:0000}");
    public static Sprite Fx(string name) => Load("Fx/" + name);

    static Sprite Load(string path)
    {
        if (!cache.TryGetValue(path, out var s))
        {
            s = Resources.Load<Sprite>(path);
            if (s == null) Debug.LogError("Нет спрайта " + path);
            cache[path] = s;
        }
        return s;
    }

    public static Texture2D White
    {
        get
        {
            if (white == null)
            {
                white = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            return white;
        }
    }

    // Квадрат 1x1 юнит, из него рисуются линии молний.
    public static Sprite Pixel => pixel != null ? pixel : (pixel = Sprite.Create(White, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1));

    static Material additive;
    public static Material DefaultMat;

    // Аддитивный материал для свечения заклинаний. Лежит в Resources, чтобы шейдер попал в билд.
    public static Material Additive
    {
        get
        {
            if (additive == null)
            {
                additive = Resources.Load<Material>("Additive");
                if (additive == null) additive = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
            }
            return additive;
        }
    }

    public static SpriteRenderer Glow(SpriteRenderer sr)
    {
        sr.sharedMaterial = Additive;
        return sr;
    }

    public static SpriteRenderer Make(string name, Sprite s, int order, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingOrder = order;
        return sr;
    }
}
