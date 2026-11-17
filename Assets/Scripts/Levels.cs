using UnityEngine;

// Уровни как данные: земля, декор, свет и главы, набор врагов и боссов. World строит мир по LevelContent,
// Game берёт отсюда врагов. Новый уровень — ещё одна ветка в Get.
public class PropDef
{
    // path — относительно Resources/Models: Props/* красится атласом Halloween, Dungeon/* — атласом Dungeon,
    // остальное (Nature/* от Kenney) остаётся со своими материалами из FBX
    public string path;
    public int count;
    public Vector3[] at;         // точные места (x, z, поворот в градусах) вместо случайных — город; count тогда не нужен
    public float minR = 4;       // случайный декор не ближе этого к центру мира
    public Rect avoid;           // и не в этом прямоугольнике (дом таверны в городе)
    public float scale = 1;
    public float sink;           // утопить в землю (м): коряги и пни в болоте
    public bool walk;            // проходимая мелочь: кости, свечи, решётки в полу
    public Color tint = Color.white; // приглушить яркие цвета Kenney под ночь (только для моделей со своими материалами)
    public Color light;
    public float lightY, lightI, lightR = 5;
}

public class LevelContent
{
    public string id;
    // земля: dirt — грязь с травой, tiles — каменные плиты, swamp — грязь с лужами; a/b/c — палитра генератора
    public string ground = "dirt";
    public Color groundA, groundB, groundC;
    public float groundSmooth = 0.05f;
    public float fogStart = 16, fogEnd = 42;
    public Quaternion sun = Quaternion.Euler(52, -35, 0);
    // слой стелющегося тумана: полупрозрачные пятна на высоте mistY, 0 — без него
    public int mistCount;
    public float mistY;
    public Color mist;
    // стоячая вода: плоские пятна неровной формы чуть над землёй, на них — кувшинки poolDecor
    public int pools;
    public Color pool;
    public Color poolEmit;   // свечение воды: лава в разломе (чёрное — без свечения)
    public float poolSmooth = 0.6f;
    public string[] poolDecor;
    // общий оттенок всех мобов уровня (умножается на цвет тира) и лёгкое свечение — «призрачность»
    public Color enemyTint = Color.white;
    public float enemyGlow;
    // Поздний нажим: с минуты lateFrom здоровье врагов дополнительно растёт как 1 + lateHp·x², урон — 1 + lateDmg·x,
    // частота появления — 1 + lateRate·x (x — минут после lateFrom). По телеметрии без него забег решается в первые
    // шесть минут: кто их пережил, доходит до победы с полным здоровьем. У боссов рост вдвое мягче (корень).
    public float lateFrom = 5, lateHp, lateDmg, lateRate;
    public PropDef[] props;
    public World.Stage[] stages;
    public EnemyType[] enemies;
    public EnemyType elite, boss1, boss2;

    public static LevelContent Get(string id) => id switch
    {
        "crypt" => Crypt(),
        "swamp" => Swamp(),
        "frost" => Frost(),
        "abyss" => Abyss(),
        "town" => Town(),
        "siege" => Siege(),
        _ => Graveyard(),
    };

    static Rect Grow(Rect r, float d) => new(r.x - d, r.y - d, r.width + 2 * d, r.height + 2 * d);

    // Город: мощёная площадь с фонарями, три портала на севере, вокруг — ограда и лес. Врагов нет.
    // Всё стоит на точных местах: площадь у всех игроков одна и та же.
    static LevelContent Town()
    {
        var lamp = new Color(1f, 0.8f, 0.45f);
        var fire = new Color(1f, 0.55f, 0.15f);
        var fence = new System.Collections.Generic.List<Vector3>();
        // ограда по кругу; на севере за порталами и на юге у входа — проёмы
        for (int i = 0; i < 44; i++)
        {
            float a = i * Mathf.PI * 2 / 44;
            var fp = new Vector2(Mathf.Sin(a) * 21.5f, Mathf.Cos(a) * 21.5f);
            if (Grow(global::Town.TavernHouse, 1f).Contains(fp)) continue;   // таверна стоит за оградой — тут её стены
            fence.Add(new Vector3(fp.x, fp.y, a * Mathf.Rad2Deg));
        }
        var posts = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < 8; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2 / 8;
            posts.Add(new Vector3(Mathf.Sin(a) * 7.5f, Mathf.Cos(a) * 7.5f - 1, 0));
        }
        // таверна — дом 13×11 (Town.TavernHouse), дверь посередине восточной стены, к площади
        var house = global::Town.TavernHouse;
        var tv = house.center;
        float hx = house.width / 2, hy = house.height / 2;
        var tavernWalls = new System.Collections.Generic.List<Vector3>();
        int nx = Mathf.RoundToInt(house.width / 1.4f), ny = Mathf.RoundToInt(house.height / 1.4f);
        for (int i = 0; i < nx; i++)
        {
            float x = tv.x - hx + house.width / nx * (i + 0.5f);
            tavernWalls.Add(new Vector3(x, tv.y - hy, 0));   // южная
            tavernWalls.Add(new Vector3(x, tv.y + hy, 0));   // северная
        }
        for (int i = 0; i < ny; i++)
        {
            float y = tv.y - hy + house.height / ny * (i + 0.5f);
            tavernWalls.Add(new Vector3(tv.x - hx, y, 90));                                   // западная
            if (i != ny / 2 - 1 && i != ny / 2) tavernWalls.Add(new Vector3(tv.x + hx, y, 90));   // восточная, в середине дверь
        }
        var avoid = Grow(house, 2.5f);   // случайные деревья и камни — не в доме и не вплотную к стенам
        var pillars = new System.Collections.Generic.List<Vector3>();
        foreach (var p in global::Town.Portals)
        {
            pillars.Add(new Vector3(p.x - 2.9f, p.y + 1.2f, 0));
            pillars.Add(new Vector3(p.x + 2.9f, p.y + 1.2f, 0));
        }
        return new LevelContent
        {
            id = "town",
            ground = "tiles",
            groundA = new Color(0.46f, 0.42f, 0.38f), groundB = new Color(0.12f, 0.11f, 0.1f), groundC = new Color(0.3f, 0.34f, 0.2f),
            groundSmooth = 0.25f,
            fogStart = 20, fogEnd = 52,
            sun = Quaternion.Euler(58, -30, 0),
            props = new PropDef[]
            {
                new() { path = "Props/fence", at = fence.ToArray() },
                new() { path = "Props/post_lantern", at = posts.ToArray(), scale = 1.2f, light = lamp, lightY = 2.3f, lightI = 5, lightR = 7 },
                new() { path = "Dungeon/pillar_decorated", at = pillars.ToArray(), scale = 1.1f },
                new() { path = "Props/shrine_candles", at = new[] { new Vector3(0, 3.5f, 180) }, scale = 1.3f, light = fire, lightY = 1.2f, lightI = 4, lightR = 7 },
                new() { path = "Dungeon/barrel_large", at = new[] { new Vector3(tv.x - 5.7f, tv.y + 4.8f, 20), new Vector3(tv.x - 5.7f, tv.y - 4.8f, 70), new Vector3(tv.x - 4.3f, tv.y - 4.9f, 10), new Vector3(tv.x + 5.7f, tv.y + 4.8f, 50), new Vector3(11.5f, -7.5f, 40) } },
                new() { path = "Dungeon/wall_half", at = tavernWalls.ToArray(), scale = 0.8f },
                new() { path = "Dungeon/candle_triple", at = new[] { new Vector3(tv.x - 5.7f, tv.y + 4.8f, 0), new Vector3(tv.x - 5.7f, tv.y - 4.8f, 0) }, sink = -1.35f, light = fire, lightY = 0.5f, lightI = 3, lightR = 6, walk = true },
                new() { path = "Props/lantern_standing", at = new[] { new Vector3(tv.x + 5.8f, tv.y - 4.9f, 0), new Vector3(tv.x - 5.8f, tv.y + 0.6f, 0) }, scale = 1.1f, light = lamp, lightY = 1.1f, lightI = 3.5f },
                new() { path = "Dungeon/chest_gold", at = new[] { new Vector3(12.6f, -6.4f, 200) } },
                new() { path = "Props/lantern_standing", at = new[] { new Vector3(-13, 2, 0), new Vector3(13, 2, 0), new Vector3(-4.6f, -13.2f, 0), new Vector3(5, -12, 0) }, scale = 1.1f, light = lamp, lightY = 1.1f, lightI = 3 },
                new() { path = "Props/pumpkin_orange_jackolantern", at = new[] { new Vector3(-3.2f, 4.2f, 150), new Vector3(3.2f, 4.2f, 210) }, scale = 1.2f, light = fire, lightY = 0.6f, lightI = 2.5f },
                new() { path = "Props/tree_pine_orange_large", count = 9, scale = 1.2f, minR = 24, avoid = avoid },
                new() { path = "Props/tree_pine_orange_medium", count = 9, scale = 1.2f, minR = 24, avoid = avoid },
                new() { path = "Props/tree_dead_medium", count = 6, scale = 1.2f, minR = 24, avoid = avoid },
                new() { path = "Nature/rock_largeA", count = 5, minR = 24, avoid = avoid, tint = new Color(0.5f, 0.5f, 0.55f) },
            },
            stages = new World.Stage[]
            {
                new() { title = "Город", moon = new Color(1f, 0.82f, 0.62f), moonI = 1.25f, ambient = new Color(0.42f, 0.38f, 0.46f), fog = new Color(0.07f, 0.05f, 0.09f), ground = new Color(1.05f, 1f, 0.95f) },
            },
            enemies = new EnemyType[0],
        };
    }

    // Осада города (GameSiege.cs): та же мостовая и фонари, что в городе, ограда проломлена, по площади — орда.
    // Мобы плотнее и злее кладбищенских, нажим с третьей минуты: это вечерний общий бой, а не прогулка
    static LevelContent Siege()
    {
        var lamp = new Color(1f, 0.8f, 0.45f);
        var fire = new Color(1f, 0.45f, 0.15f);
        return new LevelContent
        {
            id = "siege",
            lateFrom = 3, lateHp = 0.08f, lateDmg = 0.06f, lateRate = 0.08f,
            ground = "tiles",
            groundA = new Color(0.46f, 0.42f, 0.38f), groundB = new Color(0.12f, 0.11f, 0.1f), groundC = new Color(0.3f, 0.34f, 0.2f),
            groundSmooth = 0.25f,
            fogStart = 18, fogEnd = 46,
            sun = Quaternion.Euler(58, -30, 0),
            props = new PropDef[]
            {
                new() { path = "Props/post_lantern", count = 6, scale = 1.2f, light = lamp, lightY = 2.3f, lightI = 5, lightR = 7 },
                new() { path = "Props/lantern_standing", count = 4, scale = 1.1f, light = lamp, lightY = 1.1f, lightI = 3 },
                new() { path = "Props/fence", count = 4 },
                new() { path = "Props/fence_broken", count = 8 },
                new() { path = "Dungeon/barrel_large", count = 6 },
                new() { path = "Dungeon/wall_half", count = 5, scale = 0.8f },
                new() { path = "Dungeon/pillar_decorated", count = 3, scale = 1.1f },
                new() { path = "Props/shrine_candles", count = 2, light = fire, lightY = 1.2f, lightI = 4 },
                new() { path = "Props/pumpkin_orange_jackolantern", count = 4, scale = 1.2f, light = fire, lightY = 0.6f, lightI = 2.5f },
                new() { path = "Props/skull", count = 6, scale = 1.2f, walk = true },
                new() { path = "Props/bone_A", count = 8, scale = 1.2f, walk = true },
                new() { path = "Props/tree_pine_orange_medium", count = 4, scale = 1.2f, minR = 10 },
            },
            stages = new World.Stage[]
            {
                new() { title = "Осада. Орда у ворот", moon = new Color(1f, 0.6f, 0.4f), moonI = 1.25f, ambient = new Color(0.45f, 0.32f, 0.36f), fog = new Color(0.1f, 0.04f, 0.04f), ground = new Color(1.05f, 0.95f, 0.9f) },
                new() { title = "Осада. Бой на площади", moon = new Color(1f, 0.35f, 0.3f), moonI = 1.3f, ambient = new Color(0.45f, 0.22f, 0.25f), fog = new Color(0.12f, 0.02f, 0.03f), ground = new Color(1.1f, 0.85f, 0.8f) },
                new() { title = "Осада. Последний рубеж", moon = new Color(1f, 0.5f, 0.2f), moonI = 1.4f, ambient = new Color(0.5f, 0.28f, 0.18f), fog = new Color(0.14f, 0.05f, 0.02f), ground = new Color(1.15f, 0.9f, 0.75f) },
            },
            enemies = new[]
            {
                new EnemyType { name = "Скелет орды", model = "Skeleton_Minion", hp = 8, speed = 1.8f, dmg = 7, xp = 1, scale = 1.5f },
                new EnemyType { name = "Разбойник орды", model = "Skeleton_Rogue", weapon = "Skeleton_Blade", hp = 7, speed = 3.0f, dmg = 6, xp = 1, scale = 1.45f },
                new EnemyType { name = "Воин орды", model = "Skeleton_Warrior", weapon = "Skeleton_Axe", hp = 24, speed = 1.8f, dmg = 10, xp = 2, scale = 1.7f },
                new EnemyType { name = "Маг орды", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 16, speed = 2.2f, dmg = 9, xp = 2, scale = 1.6f },
            },
            elite = new EnemyType { name = "Знаменосец орды", model = "Skeleton_Warrior", weapon = "Skeleton_Axe", hp = 220, speed = 2.2f, dmg = 22, xp = 25, scale = 2.7f, elite = true },
            boss1 = new EnemyType { name = "Таран", model = "Skeleton_Warrior", weapon = "Skeleton_Axe", hp = 1500, speed = 1.6f, dmg = 32, xp = 90, scale = 4.8f, boss = true },
            boss2 = new EnemyType { name = "Полководец орды", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 6500, speed = 1.9f, dmg = 48, xp = 220, scale = 5.6f, boss = true },
        };
    }

    // Внимание: пул мобов в Game ключуется по модели, поэтому в пределах уровня у одной модели одно оружие.
    // Оружие "правая+левая": "Skeleton_Blade+Skeleton_Shield_Small_B", только щит — "+Skeleton_Shield_Small_A".

    static LevelContent Graveyard()
    {
        var fire = new Color(1f, 0.55f, 0.15f);
        var lamp = new Color(1f, 0.8f, 0.45f);
        return new LevelContent
        {
            id = "graveyard",
            lateFrom = 5, lateHp = 0.06f, lateDmg = 0.05f, lateRate = 0.03f,
            ground = "dirt",
            groundA = new Color(0.42f, 0.34f, 0.26f), groundB = new Color(0.27f, 0.36f, 0.2f),
            props = new PropDef[]
            {
                new() { path = "Props/tree_dead_large", count = 6, scale = 1.3f },
                new() { path = "Props/tree_dead_medium", count = 7, scale = 1.2f },
                new() { path = "Props/tree_dead_small", count = 6, scale = 1.2f },
                new() { path = "Props/tree_pine_orange_large", count = 3, scale = 1.1f },
                new() { path = "Props/tree_pine_orange_medium", count = 3, scale = 1.1f },
                new() { path = "Props/grave_A", count = 5 },
                new() { path = "Props/grave_B", count = 5 },
                new() { path = "Props/gravestone", count = 6 },
                new() { path = "Props/gravemarker_A", count = 5 },
                new() { path = "Props/gravemarker_B", count = 5 },
                new() { path = "Props/crypt", count = 1, light = lamp, lightY = 1.5f, lightI = 3 },
                new() { path = "Props/coffin", count = 2 },
                new() { path = "Props/shrine_candles", count = 2, light = fire, lightY = 1f, lightI = 2.5f },
                new() { path = "Props/pumpkin_orange_jackolantern", count = 4, scale = 1.2f, light = fire, lightY = 0.6f, lightI = 3 },
                new() { path = "Props/pumpkin_yellow_jackolantern", count = 3, scale = 1.2f, light = fire, lightY = 0.6f, lightI = 3 },
                new() { path = "Props/pumpkin_orange_small", count = 4, scale = 1.2f, walk = true },
                new() { path = "Props/lantern_standing", count = 3, scale = 1.1f, light = lamp, lightY = 1.1f, lightI = 3 },
                new() { path = "Props/post_lantern", count = 3, scale = 1.1f, light = lamp, lightY = 2.2f, lightI = 4 },
                new() { path = "Props/candle_triple", count = 3, scale = 1.2f, light = fire, lightY = 0.5f, lightI = 1.5f, walk = true },
                new() { path = "Props/skull", count = 5, scale = 1.2f, walk = true },
                new() { path = "Props/bone_A", count = 6, scale = 1.2f, walk = true },
                new() { path = "Props/ribcage", count = 3, scale = 1.2f, walk = true },
                new() { path = "Props/fence", count = 4 },
                new() { path = "Props/fence_broken", count = 4 },
            },
            stages = new World.Stage[]
            {
                new() { title = "Глава 1. Старое кладбище", moon = new Color(0.55f, 0.65f, 1f), moonI = 1.3f, ambient = new Color(0.32f, 0.34f, 0.5f), fog = new Color(0.04f, 0.05f, 0.1f), ground = new Color(1f, 1f, 1f) },
                new() { title = "Глава 2. Кровавая луна", moon = new Color(1f, 0.35f, 0.3f), moonI = 1.3f, ambient = new Color(0.45f, 0.22f, 0.25f), fog = new Color(0.12f, 0.02f, 0.03f), ground = new Color(1.1f, 0.85f, 0.8f) },
                new() { title = "Глава 3. Гнилые топи", moon = new Color(0.45f, 1f, 0.6f), moonI = 1.2f, ambient = new Color(0.22f, 0.4f, 0.3f), fog = new Color(0.02f, 0.08f, 0.05f), ground = new Color(0.85f, 1f, 0.8f) },
            },
            enemies = new[]
            {
                new EnemyType { name = "Скелет", model = "Skeleton_Minion", hp = 6, speed = 1.6f, dmg = 6, xp = 1, scale = 1.5f },
                new EnemyType { name = "Скелет-разбойник", model = "Skeleton_Rogue", weapon = "Skeleton_Blade", hp = 5, speed = 2.9f, dmg = 5, xp = 1, scale = 1.45f },
                new EnemyType { name = "Скелет-воин", model = "Skeleton_Warrior", weapon = "Skeleton_Axe", hp = 18, speed = 1.7f, dmg = 9, xp = 2, scale = 1.7f },
                new EnemyType { name = "Скелет-маг", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 12, speed = 2.1f, dmg = 8, xp = 2, scale = 1.6f },
            },
            elite = new EnemyType { name = "Рыцарь смерти", model = "Skeleton_Warrior", weapon = "Skeleton_Axe", hp = 160, speed = 2.1f, dmg = 20, xp = 20, scale = 2.6f, elite = true },
            boss1 = new EnemyType { name = "Костяной страж", model = "Skeleton_Warrior", weapon = "Skeleton_Axe", hp = 1100, speed = 1.7f, dmg = 30, xp = 80, scale = 4.5f, boss = true },
            boss2 = new EnemyType { name = "Король-лич", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 5000, speed = 1.9f, dmg = 45, xp = 200, scale = 5.5f, boss = true },
        };
    }

    // Склеп: плиты, колонны, саркофаги, свечи и зелёный призрачный свет; мобы в броне и со щитами — плотнее, но медленнее
    static LevelContent Crypt()
    {
        var candle = new Color(1f, 0.6f, 0.25f);
        var ghost = new Color(0.35f, 1f, 0.55f);
        return new LevelContent
        {
            id = "crypt",
            ground = "tiles",
            groundA = new Color(0.36f, 0.35f, 0.33f), groundB = new Color(0.08f, 0.08f, 0.07f), groundC = new Color(0.22f, 0.32f, 0.18f),
            groundSmooth = 0.3f,
            fogStart = 14, fogEnd = 37,
            sun = Quaternion.Euler(64, -20, 0),
            mistCount = 26, mistY = 0.35f, mist = new Color(0.45f, 0.9f, 0.6f, 0.16f),
            enemyTint = new Color(0.85f, 1f, 0.9f), enemyGlow = 0.12f,
            props = new PropDef[]
            {
                new() { path = "Dungeon/pillar", count = 8, scale = 1.1f },
                new() { path = "Dungeon/pillar_decorated", count = 5, scale = 1.1f },
                new() { path = "Dungeon/column", count = 4 },
                new() { path = "Dungeon/wall_gated", count = 3 },
                new() { path = "Dungeon/wall_broken", count = 3 },
                new() { path = "Dungeon/wall_half", count = 3 },
                new() { path = "Props/coffin", count = 4, scale = 1.1f },
                new() { path = "Props/coffin_decorated", count = 4, scale = 1.1f },
                new() { path = "Props/plaque_candles", count = 3, light = candle, lightY = 1f, lightI = 2.5f },
                new() { path = "Props/shrine_candles", count = 2, light = candle, lightY = 1f, lightI = 2.5f },
                new() { path = "Props/post_skull", count = 4, scale = 1.1f, light = ghost, lightY = 1.8f, lightI = 4, lightR = 6 },
                new() { path = "Props/skull_candle", count = 5, scale = 1.2f, light = ghost, lightY = 0.5f, lightI = 2.5f, walk = true },
                new() { path = "Dungeon/candle_lit", count = 5, light = candle, lightY = 0.6f, lightI = 1.5f, walk = true },
                new() { path = "Dungeon/candle_triple", count = 4, light = candle, lightY = 0.6f, lightI = 1.5f, walk = true },
                new() { path = "Dungeon/barrel_large", count = 2 },
                new() { path = "Dungeon/rubble_large", count = 2, scale = 0.8f },
                new() { path = "Dungeon/rubble_half", count = 4, walk = true },
                new() { path = "Dungeon/sword_shield_broken", count = 3, walk = true },
                new() { path = "Dungeon/floor_tile_big_grate", count = 4, walk = true },
                new() { path = "Dungeon/floor_tile_grate", count = 5, walk = true },
                new() { path = "Props/skull", count = 6, scale = 1.2f, walk = true },
                new() { path = "Props/bone_A", count = 5, scale = 1.2f, walk = true },
                new() { path = "Props/bone_B", count = 5, scale = 1.2f, walk = true },
                new() { path = "Props/ribcage", count = 4, scale = 1.2f, walk = true },
            },
            stages = new World.Stage[]
            {
                new() { title = "Глава 1. Склеп", moon = new Color(0.45f, 0.75f, 0.6f), moonI = 0.9f, ambient = new Color(0.26f, 0.32f, 0.3f), fog = new Color(0.02f, 0.05f, 0.04f), ground = new Color(1f, 1f, 1f) },
                new() { title = "Глава 2. Призрачные залы", moon = new Color(0.35f, 1f, 0.6f), moonI = 1f, ambient = new Color(0.2f, 0.38f, 0.3f), fog = new Color(0.01f, 0.08f, 0.05f), ground = new Color(0.85f, 1.05f, 0.9f) },
                new() { title = "Глава 3. Сердце склепа", moon = new Color(0.6f, 0.45f, 1f), moonI = 1f, ambient = new Color(0.3f, 0.22f, 0.4f), fog = new Color(0.05f, 0.02f, 0.08f), ground = new Color(0.95f, 0.85f, 1.1f) },
            },
            enemies = new[]
            {
                new EnemyType { name = "Скелет-страж", model = "Skeleton_Minion", weapon = "+Skeleton_Shield_Small_A", hp = 7, speed = 1.5f, dmg = 6, xp = 1, scale = 1.5f },
                new EnemyType { name = "Склепный вор", model = "Skeleton_Rogue", weapon = "Skeleton_Blade+Skeleton_Shield_Small_B", hp = 6, speed = 2.8f, dmg = 5, xp = 1, scale = 1.45f },
                new EnemyType { name = "Латник", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_A", hp = 22, speed = 1.55f, dmg = 10, xp = 2, scale = 1.75f },
                new EnemyType { name = "Призрак", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 11, speed = 2.3f, dmg = 8, xp = 2, scale = 1.6f },
            },
            elite = new EnemyType { name = "Рыцарь склепа", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_A", hp = 180, speed = 2f, dmg = 20, xp = 20, scale = 2.6f, elite = true },
            boss1 = new EnemyType { name = "Костяной привратник", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_A", hp = 1250, speed = 1.6f, dmg = 30, xp = 80, scale = 4.5f, boss = true },
            boss2 = new EnemyType { name = "Хранитель склепа", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 5500, speed = 1.9f, dmg = 45, xp = 200, scale = 5.5f, boss = true },
        };
    }

    static readonly Color Bark = new(0.5f, 0.45f, 0.38f), Reed = new(0.45f, 0.55f, 0.35f);

    // Топи: грязь с чёрной водой, кривые деревья, пни, коряги, камыш, кувшинки и блуждающие огни; нежить живучее
    static LevelContent Swamp()
    {
        var wisp = new Color(0.5f, 1f, 0.35f);
        var lamp = new Color(1f, 0.75f, 0.35f);
        return new LevelContent
        {
            id = "swamp",
            ground = "swamp",
            groundA = new Color(0.46f, 0.39f, 0.25f), groundB = new Color(0.06f, 0.11f, 0.1f), groundC = new Color(0.3f, 0.42f, 0.16f),
            groundSmooth = 0.55f,
            fogStart = 15, fogEnd = 40,
            mistCount = 32, mistY = 0.5f, mist = new Color(0.55f, 0.75f, 0.5f, 0.2f),
            pools = 14, pool = new Color(0.03f, 0.08f, 0.04f), poolDecor = new[] { "Nature/lily_large", "Nature/lily_small" },
            enemyTint = new Color(0.8f, 0.95f, 0.7f), enemyGlow = 0,
            props = new PropDef[]
            {
                new() { path = "Props/tree_dead_large", count = 5, scale = 1.35f },
                new() { path = "Props/tree_dead_large_decorated", count = 2, scale = 1.3f },
                new() { path = "Props/tree_dead_medium", count = 8, scale = 1.25f },
                new() { path = "Props/tree_dead_small", count = 5, scale = 1.2f },
                new() { path = "Props/tree_dead_small", count = 5, scale = 1.3f, sink = 0.9f, walk = true },
                new() { path = "Nature/stump_old", count = 5, scale = 4.5f, tint = Bark },
                new() { path = "Nature/stump_oldTall", count = 3, scale = 4f, tint = Bark },
                new() { path = "Nature/log_large", count = 4, scale = 3.5f, sink = 0.25f, tint = Bark },
                new() { path = "Nature/log", count = 4, scale = 4f, sink = 0.15f, walk = true, tint = Bark },
                new() { path = "Nature/plant_bushLarge", count = 6, scale = 5f, sink = 0.3f, tint = Reed },
                new() { path = "Nature/rock_largeA", count = 3, scale = 3f, sink = 0.2f, tint = Bark },
                new() { path = "Nature/crops_bambooStageB", count = 12, scale = 3f, walk = true, tint = Reed },
                new() { path = "Nature/plant_flatTall", count = 8, scale = 4f, walk = true, tint = Reed },
                new() { path = "Nature/grass_large", count = 12, scale = 3.5f, walk = true, tint = Reed },
                new() { path = "Nature/mushroom_redGroup", count = 5, scale = 3f, walk = true },
                new() { path = "Props/post_skull", count = 3, scale = 1.1f, light = wisp, lightY = 1.8f, lightI = 4, lightR = 6 },
                new() { path = "Props/lantern_standing", count = 2, scale = 1.1f, light = lamp, lightY = 1.1f, lightI = 3 },
                new() { path = "Props/grave_A_destroyed", count = 3 },
                new() { path = "Props/skull", count = 4, scale = 1.2f, walk = true },
                new() { path = "Props/ribcage", count = 3, scale = 1.2f, walk = true },
            },
            stages = new World.Stage[]
            {
                new() { title = "Глава 1. Гнилые топи", moon = new Color(0.7f, 0.9f, 0.7f), moonI = 1.3f, ambient = new Color(0.3f, 0.38f, 0.32f), fog = new Color(0.03f, 0.07f, 0.04f), ground = new Color(1f, 1f, 1f) },
                new() { title = "Глава 2. Ведьмин туман", moon = new Color(0.75f, 0.85f, 0.45f), moonI = 1.05f, ambient = new Color(0.33f, 0.34f, 0.2f), fog = new Color(0.08f, 0.08f, 0.03f), ground = new Color(1.05f, 1f, 0.85f) },
                new() { title = "Глава 3. Трясина лича", moon = new Color(0.4f, 1f, 0.75f), moonI = 1.2f, ambient = new Color(0.16f, 0.36f, 0.32f), fog = new Color(0.01f, 0.07f, 0.06f), ground = new Color(0.85f, 1f, 0.95f) },
            },
            enemies = new[]
            {
                new EnemyType { name = "Утопленник", model = "Skeleton_Minion", hp = 8, speed = 1.45f, dmg = 7, xp = 1, scale = 1.6f },
                new EnemyType { name = "Болотный разбойник", model = "Skeleton_Rogue", weapon = "Skeleton_Blade", hp = 6, speed = 2.9f, dmg = 6, xp = 1, scale = 1.45f },
                new EnemyType { name = "Тинный воин", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_B", hp = 22, speed = 1.6f, dmg = 10, xp = 2, scale = 1.8f },
                new EnemyType { name = "Болотный шаман", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 14, speed = 2.1f, dmg = 9, xp = 2, scale = 1.6f },
            },
            elite = new EnemyType { name = "Трясинный рыцарь", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_B", hp = 190, speed = 2f, dmg = 22, xp = 20, scale = 2.7f, elite = true },
            boss1 = new EnemyType { name = "Гниющий великан", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_B", hp = 1350, speed = 1.6f, dmg = 33, xp = 80, scale = 4.8f, boss = true },
            boss2 = new EnemyType { name = "Болотный лич", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 6000, speed = 1.85f, dmg = 48, xp = 200, scale = 5.5f, boss = true },
        };
    }

    static readonly Color Frosted = new(0.72f, 0.8f, 0.92f), Ash = new(0.22f, 0.18f, 0.17f);

    // Ледяная пустошь: снег, замёрзшие озёра, голые обледенелые деревья, холодный туман; нежить в инее
    static LevelContent Frost()
    {
        var ice = new Color(0.45f, 0.8f, 1.3f);
        return new LevelContent
        {
            id = "frost",
            ground = "dirt",
            groundA = new Color(0.78f, 0.84f, 0.92f), groundB = new Color(0.5f, 0.58f, 0.7f), groundC = new Color(0.62f, 0.7f, 0.82f),
            groundSmooth = 0.3f,
            fogStart = 14, fogEnd = 38,
            mistCount = 30, mistY = 0.4f, mist = new Color(0.85f, 0.92f, 1f, 0.22f),
            pools = 12, pool = new Color(0.55f, 0.72f, 0.9f), poolSmooth = 0.92f,
            enemyTint = new Color(0.78f, 0.9f, 1.15f), enemyGlow = 0.08f,
            lateFrom = 5, lateHp = 0.07f, lateDmg = 0.055f, lateRate = 0.035f,
            props = new PropDef[]
            {
                new() { path = "Props/tree_dead_large", count = 6, scale = 1.35f },
                new() { path = "Props/tree_dead_medium", count = 8, scale = 1.25f },
                new() { path = "Props/tree_dead_small", count = 6, scale = 1.2f },
                new() { path = "Nature/rock_largeA", count = 8, scale = 3.2f, sink = 0.2f, tint = Frosted },
                new() { path = "Nature/stump_old", count = 4, scale = 4.5f, tint = Frosted },
                new() { path = "Nature/log_large", count = 3, scale = 3.5f, sink = 0.25f, tint = Frosted },
                new() { path = "Nature/grass_large", count = 10, scale = 3.5f, walk = true, tint = Frosted },
                new() { path = "Props/gravestone", count = 4 },
                new() { path = "Props/grave_A_destroyed", count = 3 },
                new() { path = "Props/post_lantern", count = 3, scale = 1.1f, light = ice, lightY = 1.8f, lightI = 4, lightR = 6 },
                new() { path = "Props/post_skull", count = 3, scale = 1.1f, light = ice, lightY = 1.8f, lightI = 3.5f, lightR = 6 },
                new() { path = "Props/skull", count = 5, scale = 1.2f, walk = true },
                new() { path = "Props/ribcage", count = 3, scale = 1.2f, walk = true },
            },
            stages = new World.Stage[]
            {
                new() { title = "Глава 1. Ледяная пустошь", moon = new Color(0.75f, 0.85f, 1f), moonI = 1.35f, ambient = new Color(0.34f, 0.4f, 0.5f), fog = new Color(0.06f, 0.09f, 0.14f), ground = new Color(1f, 1f, 1f) },
                new() { title = "Глава 2. Метель", moon = new Color(0.6f, 0.75f, 1f), moonI = 1.1f, ambient = new Color(0.3f, 0.36f, 0.52f), fog = new Color(0.1f, 0.13f, 0.2f), ground = new Color(0.92f, 0.96f, 1.08f) },
                new() { title = "Глава 3. Трон стужи", moon = new Color(0.55f, 0.6f, 1f), moonI = 1.2f, ambient = new Color(0.24f, 0.26f, 0.48f), fog = new Color(0.04f, 0.04f, 0.12f), ground = new Color(0.85f, 0.88f, 1.1f) },
            },
            enemies = new[]
            {
                new EnemyType { name = "Мёрзлый скелет", model = "Skeleton_Minion", hp = 9, speed = 1.5f, dmg = 7, xp = 1, scale = 1.55f },
                new EnemyType { name = "Снежный разбойник", model = "Skeleton_Rogue", weapon = "Skeleton_Blade", hp = 7, speed = 3f, dmg = 6, xp = 1, scale = 1.45f },
                new EnemyType { name = "Ледяной латник", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_A", hp = 26, speed = 1.55f, dmg = 11, xp = 2, scale = 1.8f },
                new EnemyType { name = "Шаман вьюги", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 16, speed = 2.2f, dmg = 10, xp = 2, scale = 1.6f },
            },
            elite = new EnemyType { name = "Рыцарь инея", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_A", hp = 210, speed = 2.05f, dmg = 24, xp = 22, scale = 2.7f, elite = true },
            boss1 = new EnemyType { name = "Ледяной великан", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_A", hp = 1500, speed = 1.6f, dmg = 35, xp = 90, scale = 4.9f, boss = true },
            boss2 = new EnemyType { name = "Королева стужи", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 6800, speed = 1.9f, dmg = 50, xp = 220, scale = 5.6f, boss = true },
        };
    }

    // Огненный разлом: чёрный пепел, светящаяся лава, обугленные деревья, красный дым; самая злая нежить
    static LevelContent Abyss()
    {
        var fire = new Color(1f, 0.45f, 0.15f);
        return new LevelContent
        {
            id = "abyss",
            ground = "dirt",
            groundA = new Color(0.38f, 0.3f, 0.27f), groundB = new Color(0.17f, 0.13f, 0.12f), groundC = new Color(0.55f, 0.24f, 0.12f),
            groundSmooth = 0.2f,
            fogStart = 14, fogEnd = 36,
            mistCount = 26, mistY = 0.6f, mist = new Color(0.6f, 0.2f, 0.12f, 0.18f),
            pools = 24, pool = new Color(0.5f, 0.12f, 0.03f), poolEmit = new Color(4f, 1.2f, 0.2f), poolSmooth = 0.3f,
            enemyTint = new Color(1.05f, 0.86f, 0.8f), enemyGlow = 0.04f,
            lateFrom = 4.5f, lateHp = 0.08f, lateDmg = 0.06f, lateRate = 0.04f,
            props = new PropDef[]
            {
                new() { path = "Props/tree_dead_large", count = 5, scale = 1.35f },
                new() { path = "Props/tree_dead_medium", count = 7, scale = 1.25f },
                new() { path = "Props/tree_dead_small", count = 6, scale = 1.25f, sink = 0.6f },
                new() { path = "Nature/rock_largeA", count = 10, scale = 3.4f, sink = 0.2f, tint = Ash },
                new() { path = "Nature/stump_oldTall", count = 4, scale = 4f, tint = Ash },
                new() { path = "Nature/log_large", count = 3, scale = 3.5f, sink = 0.25f, tint = Ash },
                new() { path = "Props/post_skull", count = 5, scale = 1.1f, light = fire, lightY = 1.8f, lightI = 4.5f, lightR = 6 },
                new() { path = "Props/skull_candle", count = 5, scale = 1.2f, light = fire, lightY = 0.5f, lightI = 2.5f, walk = true },
                new() { path = "Props/grave_A_destroyed", count = 4 },
                new() { path = "Props/skull", count = 6, scale = 1.2f, walk = true },
                new() { path = "Props/ribcage", count = 4, scale = 1.2f, walk = true },
                new() { path = "Props/bone_A", count = 5, scale = 1.2f, walk = true },
            },
            stages = new World.Stage[]
            {
                new() { title = "Глава 1. Огненный разлом", moon = new Color(1f, 0.65f, 0.5f), moonI = 1.45f, ambient = new Color(0.5f, 0.34f, 0.3f), fog = new Color(0.1f, 0.03f, 0.02f), ground = new Color(1f, 1f, 1f) },
                new() { title = "Глава 2. Пепельная буря", moon = new Color(1f, 0.5f, 0.35f), moonI = 1.35f, ambient = new Color(0.52f, 0.3f, 0.24f), fog = new Color(0.14f, 0.04f, 0.02f), ground = new Color(1.1f, 0.95f, 0.9f) },
                new() { title = "Глава 3. Сердце пламени", moon = new Color(1f, 0.38f, 0.26f), moonI = 1.4f, ambient = new Color(0.52f, 0.22f, 0.18f), fog = new Color(0.16f, 0.02f, 0.01f), ground = new Color(1.15f, 0.88f, 0.82f) },
            },
            enemies = new[]
            {
                new EnemyType { name = "Пепельный скелет", model = "Skeleton_Minion", hp = 10, speed = 1.55f, dmg = 8, xp = 1, scale = 1.55f },
                new EnemyType { name = "Угольный разбойник", model = "Skeleton_Rogue", weapon = "Skeleton_Blade", hp = 8, speed = 3.1f, dmg = 7, xp = 1, scale = 1.45f },
                new EnemyType { name = "Обугленный воин", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_B", hp = 28, speed = 1.6f, dmg = 12, xp = 2, scale = 1.8f },
                new EnemyType { name = "Огненный маг", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 18, speed = 2.2f, dmg = 11, xp = 2, scale = 1.6f },
            },
            elite = new EnemyType { name = "Рыцарь пламени", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_B", hp = 230, speed = 2.1f, dmg = 26, xp = 24, scale = 2.7f, elite = true },
            boss1 = new EnemyType { name = "Повелитель пепла", model = "Skeleton_Warrior", weapon = "Skeleton_Axe+Skeleton_Shield_Large_B", hp = 1700, speed = 1.65f, dmg = 38, xp = 100, scale = 5f, boss = true },
            boss2 = new EnemyType { name = "Лич пламени", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 7600, speed = 1.95f, dmg = 54, xp = 240, scale = 5.7f, boss = true },
        };
    }
}
