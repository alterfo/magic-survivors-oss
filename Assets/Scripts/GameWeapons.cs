using UnityEngine;

// Оружие мага: 20 видов в слоте «weapon» (server/gear.py → kind). У вида своя модель из деталей KayKit и свой бонус
// к одной характеристике; бонус, как и основной урон, растёт с грейдом и заточкой. Оружие — самая ценная вещь:
// заточка до +16 и свечение по заточке видно всем в городе (синее → зелёное → жёлтое → красное на +16).
public partial class Game
{
    public class WeaponKind
    {
        public string title, desc, icon, stat;
        public string body;      // staff | wand | lich | blade | axe | book | lantern | orb
        public string mat;       // wood | bone | gold | dark | night
        public string head;      // "" | gem | crown | orb
        public Color headColor;
        public float headSize = 0.35f;
    }

    // порядок = kind на сервере, не переставлять: у игроков уже лежат вещи с этими номерами (0 — прежний посох)
    public static readonly WeaponKind[] WeaponKinds =
    {
        new() { title = "Посох", desc = "Надёжный дуб, чистый урон", icon = "w_staff", stat = "dmg", body = "staff", mat = "wood" },
        new() { title = "Жезл", desc = "Лёгкий — чары быстрее", icon = "w_wand", stat = "cd", body = "wand", mat = "wood" },
        new() { title = "Посох лича", desc = "Кость тянет жизнь из врагов", icon = "w_lich", stat = "regen", body = "lich", mat = "bone" },
        new() { title = "Кристальный посох", desc = "Кристалл расширяет заклинания", icon = "w_crystal", stat = "area", body = "staff", mat = "wood", head = "gem", headColor = new Color(0.6f, 1.6f, 2.4f), headSize = 0.45f },
        new() { title = "Сфера", desc = "Притягивает кристаллы", icon = "w_orb", stat = "magnet", body = "orb", mat = "gold", head = "orb", headColor = new Color(1.2f, 0.8f, 2.4f), headSize = 0.5f },
        new() { title = "Гримуар", desc = "Опыт идёт быстрее", icon = "w_grimoire", stat = "xp", body = "book", mat = "wood" },
        new() { title = "Коса", desc = "Бьёт метко", icon = "w_scythe", stat = "crit", body = "axe", mat = "dark" },
        new() { title = "Рунный клинок", desc = "Руны режут сильнее", icon = "w_runeblade", stat = "dmg", body = "blade", mat = "dark", head = "gem", headColor = new Color(1.4f, 0.4f, 2.6f), headSize = 0.22f },
        new() { title = "Скипетр", desc = "Золото липнет к рукам", icon = "w_scepter", stat = "gold", body = "wand", mat = "gold", head = "crown", headColor = new Color(2.5f, 0.4f, 0.5f) },
        new() { title = "Ветвь друида", desc = "Лес лечит раны", icon = "w_druid", stat = "regen", body = "staff", mat = "wood", head = "gem", headColor = new Color(0.6f, 2.4f, 0.5f) },
        new() { title = "Посох бури", desc = "Молнии не ждут", icon = "w_storm", stat = "cd", body = "staff", mat = "dark", head = "gem", headColor = new Color(2.4f, 2.1f, 0.5f) },
        new() { title = "Посох пламени", desc = "Огонь жжёт больнее", icon = "w_flame", stat = "dmg", body = "staff", mat = "wood", head = "gem", headColor = new Color(2.6f, 0.9f, 0.2f) },
        new() { title = "Ледяной жезл", desc = "Холод расходится шире", icon = "w_frostwand", stat = "area", body = "wand", mat = "night", head = "gem", headColor = new Color(0.7f, 1.5f, 2.6f), headSize = 0.25f },
        new() { title = "Костяной жезл", desc = "Крепче держит удар", icon = "w_bonewand", stat = "hp", body = "wand", mat = "bone" },
        new() { title = "Посох Бездны", desc = "Бездна находит слабое место", icon = "w_abyss", stat = "crit", body = "lich", mat = "dark", head = "gem", headColor = new Color(1.3f, 0.3f, 2.6f) },
        new() { title = "Солнечный скипетр", desc = "Сияние приносит золото", icon = "w_sun", stat = "gold", body = "staff", mat = "gold", head = "orb", headColor = new Color(2.6f, 1.9f, 0.6f), headSize = 0.4f },
        new() { title = "Звёздный посох", desc = "Звёзды учат быстрее", icon = "w_star", stat = "xp", body = "staff", mat = "night", head = "gem", headColor = new Color(2.2f, 2.2f, 2.6f) },
        new() { title = "Боевой топор", desc = "Топор держит строй", icon = "w_battleaxe", stat = "hp", body = "axe", mat = "wood" },
        new() { title = "Фонарь душ", desc = "Души несут кристаллы к тебе", icon = "w_lantern", stat = "magnet", body = "lantern", mat = "dark", head = "orb", headColor = new Color(0.5f, 2.4f, 2.0f), headSize = 0.3f },
        new() { title = "Посох дракона", desc = "Драконий жар — самый сильный удар", icon = "w_dragon", stat = "dmg", body = "lich", mat = "gold", head = "gem", headColor = new Color(2.6f, 0.4f, 0.3f), headSize = 0.55f },
    };

    static readonly string[] WeaponGradeSuffix = { "ученика", "адепта", "чародея", "архимага", "бури", "Бездны" };

    static WeaponKind KindOf(GearItem it) => WeaponKinds[Mathf.Clamp(it.kind, 0, WeaponKinds.Length - 1)];
    static bool IsWeapon(GearItem it) => it != null && it.slot == "weapon";

    // бонус вида: та же единица, что у доп. свойства, × грейд × заточка (как основной стат) — заточенное оружие ценнее всего
    static float WeaponBonus(GearItem it) =>
        IsWeapon(it) && AffixUnit.TryGetValue(KindOf(it).stat, out var u) ? u * GradeMul[it.grade] * (1 + 0.12f * it.enchant) : 0;

    static string WeaponName(GearItem it)
    {
        var k = KindOf(it);
        // у посоха бури и Бездны грейд-суффикс повторил бы слово — для них «Посох бури архимага» и т. п. звучит плохо
        string suf = WeaponGradeSuffix[it.grade];
        return k.title.EndsWith(suf) ? k.title : $"{k.title} {suf}";
    }

    static string ItemIcon(GearItem it)
    {
        if (IsWeapon(it))
        {
            // иконки видов — Resources/Icons/w_*.png; пока картинки нет, рисуем общую иконку слота
            var ic = KindOf(it).icon;
            IconT(ic);
            if (painted.Contains(ic)) return ic;
        }
        return SlotOf(it.slot).icon;
    }

    // ---------- свечение по заточке ----------
    // +0…3 нет, +4…7 синее (слабое), +8…11 зелёное, +12…15 жёлтое (выше среднего), +16 красное (вершина)

    static int GlowTier(int enchant) => enchant >= 16 ? 4 : enchant >= 12 ? 3 : enchant >= 8 ? 2 : enchant >= 4 ? 1 : 0;
    static readonly string[] GlowNames = { "нет", "синее", "зелёное", "жёлтое", "красное" };
    static readonly Color[] GlowUi =
    {
        new(0.6f, 0.6f, 0.6f), new(0.45f, 0.65f, 1f), new(0.45f, 1f, 0.5f), new(1f, 0.88f, 0.3f), new(1f, 0.3f, 0.25f),
    };
    public static readonly Color[] GlowHdr =
    {
        Color.black, new(0.5f, 0.9f, 2.6f), new(0.5f, 2.4f, 0.7f), new(2.6f, 2.1f, 0.4f), new(3.2f, 0.45f, 0.3f),
    };

    static string GlowLine(int enchant)
    {
        int t = GlowTier(enchant);
        int next = t == 0 ? 4 : t == 1 ? 8 : t == 2 ? 12 : t == 3 ? 16 : 0;
        string now = t == 0 ? "Свечения нет" : $"Свечение: {GlowNames[t]}";
        return next > 0 ? $"{now} · с +{next} — {GlowNames[t + 1]}" : $"{now} — вершина заточки";
    }

    // надетое оружие для модели: вид и заточка (−1 — оружия нет, держит посох из облика)
    (int kind, int enchant) WornWeapon()
    {
        var w = gear.Find(g => g.equipped != 0 && g.slot == "weapon");
        return w == null ? (-1, 0) : (Mathf.Clamp(w.kind, 0, WeaponKinds.Length - 1), w.enchant);
    }

    // строка в описание игрока в городе: «вид.заточка» (server/town.py проверяет формат)
    string WeaponInfo()
    {
        var (k, e) = WornWeapon();
        return k < 0 ? "" : $"{k}.{e}";
    }

    static (int kind, int enchant) ParseWeaponInfo(string s)
    {
        var p = (s ?? "").Split('.');
        if (p.Length == 2 && int.TryParse(p[0], out var k) && int.TryParse(p[1], out var e) && k >= 0 && k < WeaponKinds.Length)
            return (k, Mathf.Clamp(e, 0, MaxEnchant));
        return (-1, 0);
    }

    void ApplyMyWeapon()
    {
        if (player?.go == null || builtHero != "mage") return;
        var (k, e) = WornWeapon();
        Cosmetics.ApplyWeapon(player.go, k < 0 ? null : WeaponKinds[k], GlowTier(e));
    }
}
