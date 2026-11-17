using System.Collections.Generic;
using UnityEngine;

// Павшие маги: погибший маг игрока сохраняется на сервере (server/fallen.py) и поднимается нежитью в чужих забегах —
// с его ником и стихией его сборки. Упокоивший получает гримуар (запретное заклинание на выбор, SkillsForbidden.cs),
// золото и трофейную одежду за счёт упокоенных; хозяин павшего — золото и весть в меню.
// Пока только в одиночных забегах: зоны ударов павшего считаются локально и по сети не синхронизируются.
public partial class Game
{
    [System.Serializable] class FallenAdd { public string voter, nick, hero, hat, robe, staff, glow, spells, map; public int element, lvl, secs, diff; }
    [System.Serializable] class FallenAskPost { public string voter, map; }
    [System.Serializable] class FallenDef { public bool ok; public int id, element, lvl, secs; public string nick, hero, hat, robe, staff, glow, spells; }
    [System.Serializable] class FallenSlainPost { public string voter; public int id; }
    [System.Serializable] class FallenSlainReply { public bool ok; public int slain, gold, wrev; public string[] granted; }
    [System.Serializable] class FallenNewsItem { public string nick; public int gold; }
    [System.Serializable] class FallenNews { public bool ok; public FallenNewsItem[] news; }

    // зона удара павшего: сначала метка на земле, через delay — взрыв по магу внутри
    class Hazard { public Vector2 pos; public float r, t, dmg; public Color color; public bool bolt; }

    static readonly Color[] FallenColors =
    {
        new(0.4f, 1f, 0.8f), new(1f, 0.5f, 0.15f), new(0.6f, 0.75f, 1f), new(1f, 0.7f, 0.35f), new(0.55f, 0.85f, 1f),
    };

    // когда сервер никого не дал (мало игроков, нет связи) — встают легенды кладбища; за них нет трофеев и вестей
    static readonly FallenDef[] Legends =
    {
        new() { nick = "Архимаг Орвин", element = 1, lvl = 34, spells = "Огненный шар, Огненная аура" },
        new() { nick = "Громовержец Тарн", element = 2, lvl = 31, spells = "Цепная молния, Шаровые молнии" },
        new() { nick = "Звездочёт Мерр", element = 3, lvl = 38, spells = "Метеор, Землетрясение" },
        new() { nick = "Ведьма Ильза", element = 4, lvl = 29, spells = "Ледяные копья, Буран" },
    };

    FallenDef fallenDef;
    EnemyType fallenType;
    Enemy fallen;
    Light fallenLight;
    bool fallenAsked, fallenSpawned, grimoirePending, grimoireOpen;
    float fallenAt, fallenCast, hazardSafeUntil;
    readonly List<Hazard> hazards = new();
    static string fallenNews;     // строка для меню: «Твоего мага упокоил …»
    static float fallenNewsUntil;
    static bool fallenNewsAsked;
    static float fallenAtArg;     // -fallen 30 — для проверки: павший встаёт на 30-й секунде

    // ---------- Запрос павшего на забег ----------

    void FallenAsk()
    {
        if (coopRun || bot || fallenAsked) return;
        fallenAsked = true;
        fallenAt = fallenAtArg > 0 ? fallenAtArg : Random.Range(200f, 260f);
        fallenDef = Legends[Random.Range(0, Legends.Length)];
        var json = JsonUtility.ToJson(new FallenAskPost { voter = Voter(), map = Levels[selLevel].id });
        StartCoroutine(Send("POST", "fallen/get", json, (code, body) =>
        {
            if (code != 200 || fallenSpawned) return;
            var d = JsonUtility.FromJson<FallenDef>(body);
            if (d != null && d.id > 0 && !string.IsNullOrEmpty(d.nick)) fallenDef = d;
        }));
    }

    // ---------- Каждый кадр забега ----------

    void FallenTick(float dt)
    {
        if (coopRun) return;
        if (!fallenSpawned && fallenDef != null && time >= fallenAt && !AnyBoss()) FallenRise();
        UpdateHazards(dt);
        if (fallen == null) return;
        if (!fallen.alive) { fallen = null; if (fallenLight != null) fallenLight.enabled = false; return; }
        var col = FallenColors[Mathf.Clamp(fallenDef.element, 0, 4)];
        fallenLight.transform.position = World.W(fallen.pos, 2.2f);
        fallenLight.intensity = 5 + Mathf.Sin(time * 5);
        if (Random.value < 0.5f) Vfx.Emit(Vfx.Ember, World.W(fallen.pos + Random.insideUnitCircle * 0.6f, Random.Range(0.4f, 2.4f)), Vector3.up * 1.2f, 0.16f, 0.9f, col);
        if (fallen.spawnLock > 0 || fallen.stun > 0 || fallen.frozen > 0) return;
        fallenCast -= dt;
        if (fallenCast > 0 || (fallen.pos - player.pos).sqrMagnitude > 15 * 15) return;
        fallenCast = 3.4f;
        FallenCast(col);
    }

    void FallenRise()
    {
        fallenSpawned = true;
        var d = fallenDef;
        fallenType = new EnemyType
        {
            name = "Павший маг " + d.nick, model = "Skeleton_Mage", weapon = "Skeleton_Staff",
            hp = boss1.hp * 0.5f * (fallenAtArg > 0 ? 0.08f : 1f), speed = 2.0f, dmg = 14, xp = 40, scale = 2.7f, elite = true, fallen = true,
        };
        var at = World.Free(player.pos + Random.insideUnitCircle.normalized * 12f, 1.5f);
        fallen = SpawnEnemy(fallenType, at, 0, true);
        bossShown = fallen;
        fallenCast = 2.5f;
        if (fallenLight == null)
        {
            fallenLight = new GameObject("FallenLight").AddComponent<Light>();
            fallenLight.range = 8; fallenLight.shadows = LightShadows.None;
        }
        fallenLight.enabled = true;
        fallenLight.color = FallenColors[Mathf.Clamp(d.element, 0, 4)];
        string sub = d.lvl > 0 ? $"Пал на {d.lvl}-м уровне" + (string.IsNullOrEmpty(d.spells) ? "" : " · " + d.spells) : d.spells;
        Banner("Павший маг " + d.nick, sub);
        Debug.Log($"fallen: rise {d.nick} id={d.id} element={d.element} t={time:0}");
        screenFlash = 0.5f;
        Shake(0.3f);
        Vfx.Emit(Vfx.Ring, World.W(at, 0.15f), Vector3.zero, 9, 0.7f, fallenLight.color);
    }

    // рисунок ударов — по стихии сборки павшего
    void FallenCast(Color col)
    {
        float dmg = 16 * (1 + time / 60f * 0.12f) * CurDiff.dmg * (fallenAtArg > 0 ? 0.25f : 1f);
        var p = player.pos;
        var from = fallen.pos;
        fallen.anim.CrossFade("Attack", 0.05f);
        Vfx.Flash(World.W(from, 2.4f), col, 7, 7, 0.3f);
        switch (fallenDef.element)
        {
            case 1: // огонь: дорожка взрывов от павшего к магу и дальше
            {
                var dir = (p - from).sqrMagnitude > 0.01f ? (p - from).normalized : Vector2.up;
                for (int i = 0; i < 4; i++) AddHazard(p + dir * (i - 1.5f) * 2.4f, 1.7f, 0.85f + i * 0.22f, dmg, col);
                break;
            }
            case 2: // электро: россыпь быстрых разрядов вокруг мага
                for (int i = 0; i < 6; i++) AddHazard(p + Random.insideUnitCircle * 3.6f, 1.15f, 0.65f + i * 0.12f, dmg * 0.8f, col, true);
                break;
            case 3: // метеоры: один большой и медленный
                AddHazard(p, 3.3f, 1.5f, dmg * 1.8f, col);
                break;
            case 4: // лёд: кольцо вокруг мага, потом удар в центр
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3;
                    AddHazard(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.2f, 1.45f, 1.0f, dmg, col);
                }
                AddHazard(p, 1.7f, 1.75f, dmg, col);
                break;
            default:
                AddHazard(p, 2.1f, 1.0f, dmg, col);
                break;
        }
    }

    void AddHazard(Vector2 at, float r, float delay, float dmg, Color c, bool bolt = false)
    {
        hazards.Add(new Hazard { pos = at, r = r, t = delay, dmg = dmg, color = c, bolt = bolt });
        Vfx.Decal(at, r * 2, new Color(c.r, c.g, c.b, 0.45f), delay + 0.1f, false);
        Vfx.Emit(Vfx.Ring, World.W(at, 0.12f), Vector3.zero, r * 2, Mathf.Min(delay, 0.5f), c);
    }

    void UpdateHazards(float dt)
    {
        for (int i = hazards.Count - 1; i >= 0; i--)
        {
            var h = hazards[i];
            h.t -= dt;
            if (h.t > 0)
            {
                // кромка зоны искрит, пока удар не случился: видно, откуда уходить
                float a = Random.value * Mathf.PI * 2;
                Vfx.Emit(Vfx.Ember, World.W(h.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h.r, 0.15f), Vector3.up * 1.5f, 0.14f, 0.35f, h.color);
                continue;
            }
            hazards.RemoveAt(i);
            if (h.bolt)
            {
                var ground = World.W(h.pos, 0.1f);
                Vfx.Bolt(ground + new Vector3(Random.Range(-1f, 1f), 14, Random.Range(-1f, 1f)), ground, h.color, 0.25f);
                Vfx.Burst(Vfx.Spark, ground, 16, 8, 0.3f, 0.4f, h.color, 0.9f);
                Vfx.Emit(Vfx.Ring, World.W(h.pos, 0.15f), Vector3.zero, h.r * 2.5f, 0.3f, h.color);
                Shake(0.12f);
            }
            else Boom(h.pos, h.r, h.color, false);
            // прыжок, щит-реликвия и подарок после выбора карты спасают, как и от скелетов
            // один залп бьёт мага не чаще раза в 0.6 с: шесть разрядов разом не складываются в верную смерть
            if (jumpT > 0 || time < shieldUntil || time < graceUntil || time < hazardSafeUntil || me.down) continue;
            if ((player.pos - h.pos).sqrMagnitude > (h.r + Player.Radius * 0.5f) * (h.r + Player.Radius * 0.5f)) continue;
            float taken = Mathf.Min(h.dmg, player.maxHp * 0.35f) * Mathf.Max(0.2f, 1 - player.armor);
            player.hp -= AbsorbShield(taken);
            player.hurt = 0.15f;
            lastHitBy = fallenType != null ? fallenType.name : "павший маг";
            hurtFx = Mathf.Max(hurtFx, 0.9f);
            hazardSafeUntil = time + 0.6f;
        }
    }

    // ---------- Упокоение: гримуар, золото, трофеи ----------

    void FallenSlain(Enemy e)
    {
        var col = FallenColors[Mathf.Clamp(fallenDef.element, 0, 4)];
        Boom(e.pos, 3.5f, col, false);
        LevelFx(Forbidden.Soul);
        for (int i = 0; i < 10; i++) DropGem(e.pos + Random.insideUnitCircle.normalized * Random.Range(1f, 2.6f), Mathf.Max(3, xpNext * 0.06f), false);
        hazards.Clear();
        grimoirePending = true;
        Debug.Log($"fallen: slain {fallenDef.nick} t={time:0}");
        Banner(fallenDef.nick + " упокоен", "Гримуар павшего: запретная магия");
        if (fallenDef.id <= 0 || bot) return;
        var json = JsonUtility.ToJson(new FallenSlainPost { voter = Voter(), id = fallenDef.id });
        int wgen = walletGen;
        StartCoroutine(Send("POST", "fallen/slain", json, (code, body) =>
        {
            if (code != 200) return;
            var r = JsonUtility.FromJson<FallenSlainReply>(body);
            if (r == null || !r.ok) return;
            if (r.gold > 0) SetGold(r.gold, r.wrev, wgen);
            if (r.granted != null && r.granted.Length > 0)
            {
                foreach (var id in r.granted) owned[id] = 1;
                var d = Cosmetics.Get(r.granted[r.granted.Length - 1].Replace("cos.", ""));
                Banner("Трофей: " + (d != null ? d.title : "новая одежда"), "Надень в магазине, вкладка «Одежда»");
            }
        }));
    }

    // гримуар — те же карты уровня, но из запретных заклинаний; уровень при этом не тратится
    void OpenGrimoire()
    {
        grimoirePending = false;
        choices.Clear();
        // новая запретная магия — только в свободный слот (MaxSpells), взятую можно качать; слотов нет — полное лечение ниже
        bool free = SpellsTaken < MaxSpells;
        var pool = skills.FindAll(s => s.forbidden && s.level < s.max && (s.level > 0 || !s.spell || free));
        while (choices.Count < 3 && pool.Count > 0)
        {
            int k = Random.Range(0, pool.Count);
            choices.Add(pool[k]);
            pool.RemoveAt(k);
        }
        if (choices.Count == 0) { player.hp = player.maxHp; return; }
        grimoireOpen = true;
        Debug.Log($"fallen: grimoire {choices.Count} cards");
        state = State.LevelUp;
        menuT = Time.unscaledTime;
    }

    // ---------- Свой павший: отправка после гибели ----------

    void FallenUpload()
    {
        if (coopRun || bot || time < 180) return;
        var by = new int[5];
        var top = new List<Skill>();
        foreach (var s in skills)
        {
            if (!s.spell || s.level <= 0 || s.forbidden) continue;
            by[(int)s.element] += s.level;
            top.Add(s);
        }
        int el = 0;
        for (int i = 1; i < 5; i++) if (by[i] > by[el] || el == 0 && by[i] > 0) el = i;
        top.Sort((a, b) => b.level.CompareTo(a.level));
        var names = new List<string>();
        for (int i = 0; i < top.Count && i < 3; i++) names.Add(top[i].title);
        var json = JsonUtility.ToJson(new FallenAdd
        {
            voter = Voter(), nick = Nick, hero = builtHero, hat = Wearing("hat"), robe = Wearing("robe"), staff = Wearing("staff"), glow = Wearing("glow"),
            spells = string.Join(", ", names), map = Levels[selLevel].id, element = el, lvl = level, secs = (int)time, diff = selDiff,
        });
        StartCoroutine(Send("POST", "fallen/add", json, (code, body) => { }));
    }

    // ---------- Интерфейс ----------

    // имя над павшим: чей это маг
    void FallenOverlay(float u)
    {
        if (fallen == null || !fallen.alive) return;
        if (!ToGui(World.W(fallen.pos, 1.15f * fallenType.scale + 0.9f), u, out var g)) return;
        var c = FallenColors[Mathf.Clamp(fallenDef.element, 0, 4)];
        Outline(new Rect(g.x - 200, g.y - 44, 400, 40), fallenDef.nick, Col(titleS, Color.Lerp(c, Color.white, 0.35f), TextAnchor.MiddleCenter), 2);
    }

    // меню: весть хозяину павшего, раз за запуск
    void FallenNewsTick(float W)
    {
        if (!fallenNewsAsked && !bot)
        {
            fallenNewsAsked = true;
            StartCoroutine(Send("POST", "fallen/news", JsonUtility.ToJson(new FallenAskPost { voter = Voter(), map = "" }), (code, body) =>
            {
                if (code != 200) return;
                var n = JsonUtility.FromJson<FallenNews>(body);
                if (n?.news == null || n.news.Length == 0) return;
                int gold = 0;
                foreach (var x in n.news) gold += x.gold;
                fallenNews = n.news.Length == 1 ? $"ТВОЕГО МАГА УПОКОИЛ {n.news[0].nick.ToUpperInvariant()}" : $"ТВОИХ МАГОВ УПОКОИЛИ {n.news.Length} РАЗ";
                if (gold > 0) fallenNews += $"  ·  +{gold} ЗОЛОТА";
                fallenNewsUntil = Time.unscaledTime + 20;
                // профиль тот же: перечитываем кошелёк без сброса поколения (ответ идущей покупки не должен пропасть)
                LoadWallet();
            }));
        }
        if (fallenNews != null && Time.unscaledTime < fallenNewsUntil) ChipAt(W / 2, 22, fallenNews, Forbidden.Soul, true);
    }
}
