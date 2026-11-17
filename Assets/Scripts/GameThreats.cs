using System.Collections.Generic;
using UnityEngine;

// Угрозы, которые нельзя выкосить, стоя на месте (фидбек 12–14.11: «после 10 минуты прям очень легко»,
// «враги даже зайти в зону видимости экрана не могут»):
// — режиссёр: мобы гибнут за краем экрана, а маг цел — новые мобы толще; маг проседает — режиссёр отпускает;
// — потолок мобов по FPS: лишние не рождаются, а вливаются в живых (толще, опыта больше) — кадр не тонет, сложность та же;
// — три особых врага (идея из комментария к посту): ходок не замедляется и не мёрзнет (буран его не держит),
//   голема не отбрасывает и не оглушает (землетрясение), лучник стреляет туда, где маг будет через миг.
public partial class Game
{
    static readonly EnemyType Walker = new() { name = "Ходок", model = "Skeleton_Warrior", weapon = "Skeleton_Blade", hp = 34, speed = 1.25f, dmg = 12, xp = 4, scale = 1.95f, walker = true };
    static readonly EnemyType Golem = new() { name = "Костяной голем", model = "Skeleton_Minion", weapon = "+Skeleton_Shield_Large_A", hp = 110, speed = 1.05f, dmg = 16, xp = 7, scale = 2.7f, golem = true };
    static readonly EnemyType Archer = new() { name = "Косоглазый стрелок", model = "Skeleton_Rogue", weapon = "Skeleton_Staff", hp = 12, speed = 2.3f, dmg = 11, xp = 3, scale = 1.5f, archer = true };
    // с 16.11 (фидбек «давай мобов разнообразим»): бомбер и призыватель — как в осаде города
    static readonly EnemyType Bomber = new() { name = "Бомбер", model = "Skeleton_Minion", hp = 10, speed = 2.9f, dmg = 9, xp = 2, scale = 1.3f, bomber = true };
    static readonly EnemyType Summoner = new() { name = "Призыватель", model = "Skeleton_Mage", weapon = "Skeleton_Staff", hp = 45, speed = 1.6f, dmg = 6, xp = 6, scale = 1.75f, summoner = true };
    // порядок не менять: гость кооператива узнаёт вид по индексу (GameCoop.cs TypeAt) — новые только в конец
    static readonly EnemyType[] Specials = { Walker, Golem, Archer, Bomber, Summoner };
    static readonly Color WalkerTint = new(0.75f, 0.45f, 1.25f), GolemTint = new(0.75f, 0.68f, 0.55f), ArcherTint = new(0.55f, 1.25f, 0.5f);
    static readonly Color BomberTint = new(2.2f, 0.6f, 0.3f), SummonerTint = new(0.65f, 0.6f, 1.7f);
    static readonly Color BoltColor = new(2.2f, 3f, 0.6f);
    readonly HashSet<EnemyType> specialSeen = new();

    // с какой минуты и с какой долей обычного спавна приходят особые
    EnemyType RollSpecial(float mins)
    {
        float r = Random.value;
        // стрелков живых не больше 8: их не вливает потолок мобов, и к 30-й минуте десяток стрел в секунду убивал сразу
        if (mins >= 3 && r < 0.05f && ArchersAlive() < 8) return Archer;
        if (mins >= 4.5f && r < 0.09f) return Walker;
        if (mins >= 6 && r < 0.12f) return Golem;
        // бомберов немного: взрыв бьёт до 30% здоровья (при 12% спавна бот падал на 3-й минуте)
        float r2 = Random.value;
        if (mins >= 2 && r2 < (mins >= 8 ? 0.06f : 0.04f)) return Bomber;
        if (mins >= 5 && r2 > 0.97f && SummonersAlive() < 4) return Summoner;
        return null;
    }

    int SummonersAlive()
    {
        int n = 0;
        foreach (var e in enemies) if (e.alive && e.type.summoner) n++;
        return n;
    }

    // бомбер: вплотную к магу взрывается — больно (до 30% здоровья), но один раз; сам при этом гибнет без опыта
    bool BomberTick(Enemy e, float dist)
    {
        if (dist > 1.25f || e.spawnLock > 0 || e.stun > 0 || e.frozen > 0) return false;
        BomberBoom(e.pos, true);
        e.alive = false;   // уборка мёртвых (Game.cs) проиграет падение и вернёт в пул; опыта и счёта нет
        return true;
    }

    // у гостя кооператива только картинка и звук: урон считает хозяин (GameCoop.cs, смерть врага из снимка)
    void BomberFx(Vector2 at)
    {
        Vfx.Burst(Vfx.Fire, World.W(at, 0.4f), 18, 5, 0.6f, 0.6f, new Color(2.4f, 0.9f, 0.3f), 1f, 0.4f);
        Vfx.Burst(Vfx.Smoke, World.W(at, 0.3f), 8, 2, 1.2f, 1f, new Color(0.2f, 0.15f, 0.1f, 0.7f), 0.8f, 0.4f);
        Sfx("boom", 0.7f);
    }

    void BomberBoom(Vector2 at, bool hurtMages)
    {
        Vfx.Burst(Vfx.Fire, World.W(at, 0.4f), 18, 5, 0.6f, 0.6f, new Color(2.4f, 0.9f, 0.3f), 1f, 0.4f);
        Vfx.Burst(Vfx.Smoke, World.W(at, 0.3f), 8, 2, 1.2f, 1f, new Color(0.2f, 0.15f, 0.1f, 0.7f), 0.8f, 0.4f);
        Sfx("boom", 0.7f);
        if (hurtMages && time >= shieldUntil && time >= graceUntil)
            foreach (var m in All)
            {
                if (!Alive(m) || (m == me ? jumpT > 0 : m.air > 0.05f)) continue;   // в прыжке — мимо
                if ((m.player.pos - at).sqrMagnitude > 2.6f * 2.6f) continue;
                Use(m);
                float dmg = Mathf.Min(player.maxHp * 0.3f, Bomber.dmg * 3 * (1 + time / 60f * 0.18f) * CurDiff.dmg) * Mathf.Max(0.2f, 1 - player.armor);
                player.hp -= AbsorbShield(dmg);
                player.hurt = 0.2f;
                if (m == me) { lastHitBy = Bomber.name; hurtFx = 1f; labTaken += dmg; }
                Use(me);
            }
        // убитый бомбер взрывается и среди своих: можно подорвать толпу
        if (!hurtMages)
            for (int i = 0; i < enemies.Count; i++)
            {
                var o = enemies[i];
                if (!o.alive || o.type.boss || (o.pos - at).sqrMagnitude > 2.6f * 2.6f) continue;
                o.hp -= Mathf.Max(30, o.maxHp * 0.35f);
                o.flash = 0.12f;
                if (o.hp <= 0) Kill(o);
            }
    }

    // призыватель: держится в 7–9 м, раз в 6 с поднимает троих скелетов рядом с собой
    Vector2 SummonerMove(Enemy e, Vector2 dir, float dist, float dt)
    {
        e.shotCd -= dt;
        if (e.shotCd <= 0 && dist < 14 && e.stun <= 0 && e.frozen <= 0 && e.spawnLock <= 0)
        {
            e.shotCd = Random.Range(5.5f, 7f);
            e.anim.CrossFade("Attack", 0.05f);
            Vfx.Burst(Vfx.Glow, World.W(e.pos, 1.4f), 6, 1.5f, 0.6f, 0.6f, SummonerTint, 0.5f, 0.3f);
            // в список врагов нельзя добавлять посреди его перебора — заявки встанут в начале следующего Spawn (ревью Codex)
            for (int k = 0; k < 3; k++) summons.Add(e.pos + Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.2f));
        }
        if (dist < 7) return -dir * 0.7f;
        if (dist < 9) return new Vector2(-dir.y, dir.x) * (e.id % 2 == 0 ? 0.3f : -0.3f);
        return dir;
    }

    readonly List<Vector2> summons = new();
    const int RaisedMax = 15;

    // из начала Spawn: поднять призванных, сколько влезает под потолок мобов
    void FlushSummons()
    {
        if (summons.Count == 0) return;
        // телеметрия 17.11: с призывателями к 8-й минуте на телефонах было 150 врагов вместо 74 и FPS на Adreno упал с 38 до 25.
        // Поднятых держим не больше RaisedMax и не дальше 60% потолка: призыв — помеха, а не вторая волна
        int alive = 0, raised = 0;
        foreach (var e in enemies) if (e.alive) { alive++; if (e.raised) raised++; }
        int room = Mathf.Min(EnemyCap * 6 / 10 - alive, RaisedMax - raised);
        for (int i = 0; i < summons.Count && i < room; i++)
        {
            var e = SpawnEnemy(types[0], World.Collide(summons[i], 0.3f), 0, true);
            e.raised = true;
        }
        summons.Clear();
    }

    int ArchersAlive()
    {
        int n = 0;
        foreach (var e in enemies) if (e.alive && e.type.archer) n++;
        return n;
    }

    void SpecialIntro(EnemyType t)
    {
        if (!specialSeen.Add(t)) return;
        WorldBanner(t.name, t.walker ? "Не замедляется и не мёрзнет" : t.golem ? "Его не отбросить и не оглушить" : t.bomber ? "Взрывается рядом — убей издалека, взрыв заденет своих"
            : t.summoner ? "Поднимает скелетов — бей его первым" : "Стреляет на упреждение — меняй направление");
    }

    // «А в игре есть конец?», «не понимаю прокачку», «огнешары не зависят от кликов» (фидбек):
    // в начале забега — цель и как играть. Первые пять забегов с подсказкой про автоатаку, дальше — только цель.
    void RunGoalHint()
    {
        if (bot || LabOn) return;
        if (survival) { Banner("ВЫЖИВАНИЕ", "Без конца по времени · после 15:00 боссы каждые 2.5 минуты", 7); return; }
        int n = PlayerPrefs.GetInt("hint.runs");
        PlayerPrefs.SetInt("hint.runs", n + 1);
        string sub = n < 5 ? "Магия бьёт сама — бегай, собирай кристаллы, на уровне выбирай заклинание"
                           : $"{boss2.name} придёт на 14-й минуте — победи его";
        Banner($"ПРОДЕРЖИСЬ {(int)RunLength / 60}:00", sub, 7);
    }

    // ---------- Режиссёр ----------

    float press = 1, pressT, killDistSum;
    int killDistN;

    void NoteKill(Enemy e)
    {
        if (e.type.boss) return;
        killDistSum += (e.pos - TargetOf(e.pos)).magnitude;
        killDistN++;
    }

    void DirectorTick(float dt)
    {
        pressT += dt;
        if (pressT < 1) return;
        pressT = 0;
        float avg = killDistN > 0 ? killDistSum / killDistN : 0;
        int n = killDistN;
        killDistSum = 0; killDistN = 0;
        if (time < 240) return;   // первые четыре минуты — без режиссёра
        float hp = 1;
        foreach (var m in All) if (Alive(m)) hp = Mathf.Min(hp, m.player.hp / Mathf.Max(1, m.player.maxHp));
        if (hp < 0.4f) press = Mathf.Max(1, press * 0.97f);
        else if (n >= 3 && avg > 8f && hp > 0.65f) press = Mathf.Min(10, press * 1.018f);   // ×2 примерно за 40 секунд
        else if (n >= 3 && avg < 4.5f) press = Mathf.Max(1, press * 0.995f);
    }

    // ---------- Потолок мобов ----------

    float fpsSm = 60;
    // от доли выбранной частоты, а не от абсолютных кадров: телефон на 30 FPS (настройка) не должен навсегда
    // получать урезанный потолок — пороги те же, что были при 60: 26 и 36 кадров
    // потолок живых врагов; сверх него новые вливаются в живых (MergeSpawn) — давление то же, кадр легче.
    // На телефонах (LiteUi) с 17.11 потолок ниже: 100–160 вместо 150–240
    int EnemyCap { get { float k = fpsSm / Mathf.Min(60, Mathf.Max(30, optFps)); return LiteUi ? (k < 0.43f ? 100 : k < 0.6f ? 130 : 160) : k < 0.43f ? 150 : k < 0.6f ? 190 : 240; } }

    void FpsTick()
    {
        float u = Time.unscaledDeltaTime;
        if (u > 0 && u < 0.5f) fpsSm = Mathf.Lerp(fpsSm, 1 / u, 0.01f);
    }

    // моб сверх потолка вливается в случайного живого: его здоровье и опыт прибавляются, тот чуть подрастает
    void MergeSpawn(EnemyType t, int tier)
    {
        for (int k = 0; k < 4; k++)
        {
            var e = enemies[Random.Range(0, enemies.Count)];
            if (!e.alive || e.type.boss || e.ring || e.type.archer) continue;
            float add = EnemyHp(t, tier);
            e.hp += add; e.maxHp += add;
            e.merged++;
            e.scale0 = baseScale[PoolKey(e.type)] * e.type.scale * (1 + 0.06f * e.tier) * Mathf.Min(1.45f, 1 + 0.035f * e.merged);
            if (e.flash <= 0) e.tr.localScale = e.scale0;
            return;
        }
    }

    static string PoolKey(EnemyType t) => t.model + "|" + t.weapon;

    // ---------- Стрелы ----------

    class Bolt { public Vector2 pos, vel; public float life, dmg; public Transform tr; public bool alive; }
    readonly List<Bolt> bolts = new();
    readonly Stack<Bolt> boltPool = new();
    const float BoltSpeed = 9f;
    static Mesh boltMesh;

    // стрелок держит дистанцию 6–9 и раз в 2.6 с стреляет; целится в точку, где маг будет, когда стрела долетит
    // (не дальше трёх корпусов вперёд по ходу движения) — бег по кругу больше не спасает
    Vector2 ArcherMove(Enemy e, Vector2 dir, float dist, float dt)
    {
        e.shotCd -= dt;
        if (e.shotCd <= 0.35f && e.shotCd + dt > 0.35f) Vfx.Emit(Vfx.Glow, World.W(e.pos, 1.3f), Vector3.zero, 1.2f, 0.35f, BoltColor);
        if (e.shotCd <= 0 && dist < 12 && e.stun <= 0 && e.frozen <= 0 && e.spawnLock <= 0)
        {
            e.shotCd = Random.Range(2.3f, 2.9f);
            var m = Nearest(e.pos);
            var p = m.player;
            var lead = Vector2.ClampMagnitude(p.vel * (dist / BoltSpeed), 3f);
            var aim = p.pos + lead - e.pos;
            if (aim.sqrMagnitude > 0.01f)
            {
                FireBolt(e.pos + aim.normalized * 0.6f, aim.normalized * BoltSpeed, e.dmg * 1.3f);
                e.anim.CrossFade("Attack", 0.05f);
            }
        }
        if (dist < 6) return -dir * 0.8f;
        if (dist < 9) return new Vector2(-dir.y, dir.x) * (e.id % 2 == 0 ? 0.35f : -0.35f);   // кружит на дистанции
        return dir;
    }

    void FireBolt(Vector2 pos, Vector2 vel, float dmg)
    {
        var b = boltPool.Count > 0 ? boltPool.Pop() : null;
        if (b == null)
        {
            // физика в сборке вырезана: примитив ругается на коллайдер — сферу берём один раз и дальше только её меш
            if (boltMesh == null)
            {
                var prim = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                boltMesh = prim.GetComponent<MeshFilter>().sharedMesh;
                Destroy(prim);
            }
            var go = new GameObject("Bolt");
            go.AddComponent<MeshFilter>().sharedMesh = boltMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = World.GlowMat(BoltColor);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            b = new Bolt { tr = go.transform };
        }
        b.tr.gameObject.SetActive(true);
        b.pos = pos; b.vel = vel; b.life = 2.2f; b.dmg = dmg; b.alive = true;
        b.tr.position = World.W(pos, 1.1f);
        bolts.Add(b);
        // гостям — только картинка: попадание считает хозяин по их позициям
        if (coopRun && !Guest && AnyMate)
        {
            var w = RunMsg(0x16); w.Write((byte)10); w.Write(pos.x); w.Write(pos.y); w.Write(vel.x); w.Write(vel.y); Flush();
        }
    }

    void UpdateBolts(float dt)
    {
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            b.pos += b.vel * dt;
            b.life -= dt;
            b.tr.position = World.W(b.pos, 1.1f);
            if (Random.value < 0.5f) Vfx.Emit(Vfx.Ember, b.tr.position, Vector3.zero, 0.22f, 0.25f, BoltColor);
            if (!Guest && b.life > 0 && time >= shieldUntil && time >= graceUntil)
                foreach (var m in All)
                {
                    if (!Alive(m) || (m == me ? jumpT > 0 : m.air > 0.05f)) continue;
                    if ((m.player.pos - b.pos).sqrMagnitude > 0.45f * 0.45f) continue;
                    Use(m);
                    float taken = Mathf.Min(b.dmg, player.maxHp * 0.25f) * Mathf.Max(0.2f, 1 - player.armor);   // стрела — не больше четверти здоровья
                    player.hp -= AbsorbShield(taken);
                    player.hurt = 0.15f;
                    if (m == me) { labTaken += taken; lastHitBy = Archer.name; hurtFx = Mathf.Max(hurtFx, 0.8f); }
                    Use(me);
                    Vfx.Burst(Vfx.Spark, b.tr.position, 8, 4, 0.25f, 0.3f, BoltColor);
                    b.life = 0;
                    break;
                }
            if (b.life > 0 && World.Collide(b.pos, 0.1f) != b.pos) b.life = 0;   // о надгробие
            if (b.life > 0) continue;
            b.tr.gameObject.SetActive(false);
            boltPool.Push(b);
            bolts.RemoveAt(i);
        }
    }

    // ---------- Удар босса ----------
    // «Босс непонятный» (фидбек): у босса своя атака — раз в 5–7 с под магом загорается красный круг,
    // через 1.2 с туда бьёт. Стоять на месте нельзя, видно, что это босс и что он делает.
    readonly List<(Vector2 pos, float t, float r, float dmg)> slams = new();
    static readonly Color SlamColor = new(1.6f, 0.2f, 0.15f);
    const float SlamDelay = 1.2f;

    void BossTick(Enemy e, float dt)
    {
        e.shotCd -= dt;
        if (e.shotCd > 0 || e.spawnLock > 0 || e.frozen > 0) return;
        e.shotCd = Random.Range(5f, 7f);
        var m = Nearest(e.pos);
        if ((m.player.pos - e.pos).sqrMagnitude > 16 * 16) return;
        float r = e.type == boss2 ? 3.6f : 3f;
        Slam(m.player.pos + m.player.vel * 0.4f, r, e.dmg * 2.2f);
        e.anim.CrossFade("Attack", 0.05f);
    }

    void Slam(Vector2 at, float r, float dmg)
    {
        slams.Add((at, SlamDelay, r, dmg));
        Vfx.Decal(at, r * 2, new Color(0.9f, 0.1f, 0.08f, 0.55f), SlamDelay, false);
        Vfx.Emit(Vfx.Ring, World.W(at, 0.15f), Vector3.zero, r * 2.2f, SlamDelay, SlamColor);
        if (coopRun && !Guest && AnyMate)
        {
            var w = RunMsg(0x16); w.Write((byte)11); w.Write(at.x); w.Write(at.y); w.Write(r); Flush();
        }
    }

    void UpdateSlams(float dt)
    {
        for (int i = slams.Count - 1; i >= 0; i--)
        {
            var s = slams[i];
            s.t -= dt;
            if (s.t > 0) { slams[i] = s; continue; }
            slams.RemoveAt(i);
            Boom(s.pos, s.r, SlamColor, true);
            Shake(0.35f);
            if (Guest || dmg0(s.dmg) || time < shieldUntil || time < graceUntil) continue;
            foreach (var m in All)
            {
                if (!Alive(m) || (m == me ? jumpT > 0 : m.air > 0.05f)) continue;
                if ((m.player.pos - s.pos).sqrMagnitude > s.r * s.r) continue;
                Use(m);
                float taken = Mathf.Min(s.dmg, player.maxHp * 0.55f) * Mathf.Max(0.2f, 1 - player.armor);   // удар босса виден за 1.2 с, но с полного не убивает
                player.hp -= AbsorbShield(taken);
                player.hurt = 0.3f;
                if (m == me) { labTaken += taken; lastHitBy = "удар босса"; hurtFx = 1; }
                Use(me);
            }
        }
    }

    static bool dmg0(float d) => d <= 0;

    // скорость каждого мага — для упреждения стрелка
    void TrackVel(float dt)
    {
        if (dt <= 0) return;
        foreach (var m in All)
        {
            var p = m.player;
            var v = (p.pos - p.lastPos) / dt;
            p.lastPos = p.pos;
            if (v.sqrMagnitude > 400) v = Vector2.zero;   // телепорт, подъём — не бег
            p.vel = Vector2.Lerp(p.vel, v, 0.25f);
        }
    }
}
