using System.Collections.Generic;
using UnityEngine;

// Союзные скелеты некроманта и черты героев (щит за убийства, заморозка на крите, бонус школы Льда).
// Союзники — как враги: простые объекты, апдейт из HeroTick, пул по модели, один склеенный меш на скелет.
public class Ally
{
    public GameObject go;
    public Transform tr;
    public Animator anim;
    public Renderer[] rends;
    public EnemyType type;
    public string key;
    public Vector2 pos;
    public Enemy target;
    public float hp, maxHp, dmg, life, radius, attackCd, retarget, spawnLock, dying;
    public bool alive, moving;
}

public partial class Game
{
    // черты героя: выставляет HeroContent.ApplyTrait
    public bool heroKillShield, heroFrostCrit;
    public float heroIceMul = 1, heroElemMul = 1;
    public float heroShield;
    float shieldKillAt;

    List<Ally> allies = new(), alliesDying = new();
    readonly Dictionary<string, Stack<Ally>> allyPool = new();
    List<(Vector2 pos, EnemyType type, float t)> raising = new();
    readonly List<Enemy> allyNear = new();
    RaiseDead raiseDead;
    bool raiseLooked;

    static readonly Color AllyTint = new(0.45f, 1.25f, 0.55f);

    RaiseDead Raise
    {
        get
        {
            if (!raiseLooked) { raiseDead = skills.Find(s => s is RaiseDead) as RaiseDead; raiseLooked = raiseDead != null; }
            return raiseDead;
        }
    }

    public int AllyCount => allies.Count + raising.Count;

    float ShieldMax => 15 + player.maxHp * 0.15f;

    // множитель урона от черты героя: ведьма бьёт льдом на 10% сильнее
    float HeroDmgMul(Element el) => el == Element.Ice ? heroIceMul : el == Element.Fire || el == Element.Electro || el == Element.Meteor ? heroElemMul : 1;

    // хук из Kill: щит некроманта и подъём павших
    void HeroOnKill(Enemy e)
    {
        if (heroKillShield)
        {
            heroShield = Mathf.Min(ShieldMax, heroShield + 2);
            shieldKillAt = time;
        }
        var r = Raise;
        if (r == null || r.level == 0 || e.type.boss || (e.type.elite && r.super != 2)) return;
        float reach = RaiseDead.Reach * player.area;
        if ((e.pos - player.pos).sqrMagnitude > reach * reach || AllyCount >= r.Cap || Random.value > r.Chance) return;
        // труп успевает упасть, потом из того же места встаёт союзник
        raising.Add((e.pos, e.type, 0.7f));
        Vfx.Emit(Vfx.Ember, World.W(e.pos, 0.6f), Vector3.up * 1.5f, 0.5f, 0.8f, RaiseDead.Necro);
    }

    // хук из Hit на крите: ведьма вмораживает врага
    void HeroOnCrit(Enemy e)
    {
        if (heroFrostCrit && e.alive) Freeze(e, 1f);
    }

    // хук из UpdateEnemies: щит съедает урон раньше здоровья
    float AbsorbShield(float dmg)
    {
        if (heroShield <= 0) return dmg;
        float a = Mathf.Min(heroShield, dmg);
        heroShield -= a;
        if (Random.value < 0.25f) Vfx.Burst(Vfx.Spark, World.W(player.pos, 1f), 3, 3, 0.2f, 0.25f, RaiseDead.Necro);
        return dmg - a;
    }

    public void RaiseAlly(Vector2 at, string model, string weapon)
    {
        EnemyType t = types[0];
        foreach (var x in types) if (x.model == model && x.weapon == weapon) { t = x; break; }
        SpawnAlly(t, at);
    }

    void SpawnAlly(EnemyType t, Vector2 at)
    {
        var r = Raise;
        int lvl = r != null ? Mathf.Max(1, r.level) : 1;
        bool knight = r != null && r.super == 2;
        string key = t.model + "|" + t.weapon;
        if (!allyPool.TryGetValue(key, out var pool)) allyPool[key] = pool = new Stack<Ally>();
        var a = pool.Count > 0 ? pool.Pop() : null;
        if (a == null)
        {
            a = new Ally { key = key };
            a.go = World.Character(t.model, 1f, out a.anim, out _);
            World.Attach(a.go, t.weapon, World.Skeleton);
            a.go.name = "Ally_" + t.model;
            // тот же ключ склейки, что у врагов: геометрия одна, меш печётся один раз
            a.rends = World.Combine(a.go, key, World.Skeleton);
            a.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.anim.keepAnimatorStateOnDisable = true;
            a.tr = a.go.transform;
            if (!baseScale.ContainsKey("ally|" + t.model)) baseScale["ally|" + t.model] = a.tr.localScale;
        }
        a.go.SetActive(true);
        a.type = t;
        float scale = Mathf.Min(t.scale, 1.8f) * (knight ? 1.3f : 1);
        a.tr.localScale = baseScale["ally|" + t.model] * scale;
        a.pos = at;
        a.radius = 0.32f * scale;
        a.maxHp = a.hp = RaiseDead.Hp(lvl) * (knight ? 2 : 1) * (t.hp > 10 ? 1.5f : 1) * (1 + time / 60f * 0.15f);
        // «некромант прям вообще слабый» (фидбек): урон и здоровье союзников растут с минутами, как здоровье врагов,
        // иначе к десятой минуте плоские 30 урона по тысячам здоровья — щекотка
        float grow = 1 + time / 60f * 0.32f;
        a.dmg = RaiseDead.Dmg(lvl) * (knight ? 2 : 1) * (t.hp > 10 ? 1.3f : 1) * grow;
        a.life = RaiseDead.Life(lvl);
        a.attackCd = 0.3f; a.retarget = 0; a.target = null; a.moving = false;
        a.alive = true;
        // свечение сильнее, чем у зелёного тира врагов: союзника видно в толпе
        var m = World.SkelMat(AllyTint, knight ? 0.9f : 0.6f);
        foreach (var rr in a.rends) rr.sharedMaterial = m;
        a.tr.SetPositionAndRotation(World.W(at), Quaternion.LookRotation(World.W(player.face)));
        a.anim.enabled = true;
        a.anim.speed = 1;
        a.anim.Play("Spawn", 0, 0);
        a.spawnLock = 1.1f;
        Vfx.Burst(Vfx.Smoke, World.W(at, 0.2f), 4, 1.2f, 1.2f, 0.9f, new Color(0.15f, 0.3f, 0.15f, 0.7f), 0.8f, 0.4f);
        Vfx.Emit(Vfx.Ring, World.W(at, 0.15f), Vector3.zero, 2.2f, 0.4f, RaiseDead.Necro);
        Vfx.Emit(Vfx.Glow, World.W(at, 0.5f), Vector3.zero, 1.6f, 0.3f, RaiseDead.Necro);
        allies.Add(a);
    }

    // хук из Step: союзники, отложенные подъёмы, щит
    void HeroTick(float dt)
    {
        for (int i = raising.Count - 1; i >= 0; i--)
        {
            var q = raising[i];
            q.t -= dt;
            if (q.t > 0) { raising[i] = q; continue; }
            raising.RemoveAt(i);
            SpawnAlly(q.type, q.pos);
        }

        var prev = curElement;
        curElement = Element.None;
        for (int i = allies.Count - 1; i >= 0; i--)
        {
            var a = allies[i];
            UpdateAlly(a, dt);
            if (!a.alive) { allies.RemoveAt(i); AllyDie(a); }
        }
        curElement = prev;

        for (int i = alliesDying.Count - 1; i >= 0; i--)
        {
            var a = alliesDying[i];
            a.dying -= dt;
            if (a.dying < 0.6f) a.tr.position += Vector3.down * dt * 1.2f;
            if (a.dying > 0) continue;
            a.go.SetActive(false);
            allyPool[a.key].Push(a);
            alliesDying.RemoveAt(i);
        }

        if (heroShield > 0)
        {
            // без новых убийств щит тает
            if (time - shieldKillAt > 4) heroShield = Mathf.Max(0, heroShield - 3 * dt);
            if (Random.value < 0.1f + 0.3f * heroShield / ShieldMax)
                Vfx.Emit(Vfx.Ring, World.W(player.pos, 0.15f), Vector3.zero, 2.2f, 0.35f, RaiseDead.Necro * 0.8f);
        }
    }

    void UpdateAlly(Ally a, float dt)
    {
        a.life -= dt;
        if (a.life <= 0 || a.hp <= 0) { a.alive = false; return; }
        if (a.spawnLock > 0) { a.spawnLock -= dt; return; }

        a.retarget -= dt;
        if (a.target == null || !a.target.alive || a.retarget <= 0)
        {
            a.retarget = 0.4f;
            a.target = Nearest(a.pos, 9f);
        }
        Vector2 to;
        float stop;
        if (a.target != null) { to = a.target.pos - a.pos; stop = a.target.radius + a.radius + 0.25f; }
        else
        {
            // врагов нет — держится рядом с некромантом
            to = player.pos - player.face * 1.5f - a.pos;
            stop = 1.2f;
        }
        float dist = to.magnitude;
        bool moving = dist > stop;
        if (moving) a.pos += to / dist * Mathf.Min(a.type.speed * 1.4f + 1.2f, 4.5f) * dt;
        a.pos = World.Collide(a.pos, a.radius * 0.8f);
        if (moving != a.moving)
        {
            a.anim.CrossFade(moving ? "Run" : "Idle", 0.1f);
            a.moving = moving;
        }

        a.attackCd -= dt;
        if (a.target != null && !moving && a.attackCd <= 0)
        {
            a.attackCd = 0.85f;
            a.anim.CrossFade("Attack", 0.05f);
            Hit(a.target, a.dmg, to / Mathf.Max(dist, 0.01f), 0.25f);
            // замах задевает ещё двух соседей цели вполсилы
            Query(a.target.pos, 1.1f, allyNear);
            int cleave = 0;
            foreach (var e in allyNear)
                if (e != a.target && e.alive && cleave++ < 2) Hit(e, a.dmg * 0.5f, (e.pos - a.pos).normalized, 0.2f);
            Vfx.Burst(Vfx.Spark, World.W(a.target.pos, 0.9f), 4, 4, 0.2f, 0.25f, RaiseDead.Necro);
        }

        // враги вплотную грызут союзника — он тает быстрее срока
        Query(a.pos, a.radius + 0.3f, allyNear);
        foreach (var e in allyNear)
            if (e.spawnLock <= 0 && e.stun <= 0 && e.frozen <= 0) a.hp -= e.dmg * dt * 0.5f;

        a.tr.position = World.W(a.pos);
        if (to.sqrMagnitude > 0.01f) a.tr.rotation = Quaternion.Slerp(a.tr.rotation, Quaternion.LookRotation(World.W(to)), dt * 10);
    }

    void AllyDie(Ally a)
    {
        a.anim.CrossFade("Death", 0.05f);
        a.dying = 1.6f;
        alliesDying.Add(a);
        Vfx.Burst(Vfx.Bone, World.W(a.pos, 0.6f), 8, 3, 0.1f, 0.7f, new Color(0.6f, 0.9f, 0.6f), 0.8f, 0.3f);
        var r = Raise;
        if (r == null || r.super != 1) return;
        // Костяной взрыв: павший союзник разлетается осколками
        float rad = 2.2f * player.area;
        Vfx.Burst(Vfx.Bone, World.W(a.pos, 0.6f), 18, 6, 0.12f, 0.7f, new Color(0.85f, 1f, 0.8f), 0.8f, 0.3f);
        Vfx.Emit(Vfx.Ring, World.W(a.pos, 0.15f), Vector3.zero, rad * 2.5f, 0.35f, RaiseDead.Necro);
        Vfx.Emit(Vfx.Glow, World.W(a.pos, 0.6f), Vector3.zero, rad * 2, 0.25f, RaiseDead.Necro);
        Query(a.pos, rad, allyNear);
        var prev = curElement;
        curElement = Element.None;
        foreach (var e in allyNear) Hit(e, RaiseDead.Dmg(r.level) * 2.5f, (e.pos - a.pos).normalized, 0.6f);
        curElement = prev;
        Shake(0.1f);
    }
}
