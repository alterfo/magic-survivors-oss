using System.Collections.Generic;
using UnityEngine;

// Осада в самом городе: в 18:00 МСК ограда падает, орда идёт на площадь, все маги в городе
// бьются вместе базовой атакой — без прокачки. Таверна — место отступления: мобы туда не заходят, внутри лечит.
// Орду считает сервер (server/siege_town.py), здесь — только рисуем мобов из снимков, стреляем и считаем своё здоровье.
public partial class Game
{
    // как KINDS в server/siege_town.py: модель, оружие, рост, оттенок, бег
    static readonly (string model, string weapon, float scale, Color tint, bool run, string name)[] SiegeKinds =
    {
        ("Skeleton_Minion", null, 1.5f, new Color(1f, 1f, 1f), false, "Скелет"),
        ("Skeleton_Rogue", "Skeleton_Blade", 1.45f, new Color(0.75f, 0.85f, 1.25f), true, "Разбойник"),
        ("Skeleton_Warrior", "Skeleton_Axe", 1.75f, new Color(1.1f, 0.95f, 0.8f), false, "Воин"),
        ("Skeleton_Minion", null, 1.3f, new Color(2.2f, 0.55f, 0.3f), true, "Бомбер"),
        ("Skeleton_Warrior", "Skeleton_Axe+Skeleton_Shield_Large_A", 2.7f, new Color(0.7f, 0.72f, 0.85f), false, "Громила"),
        ("Skeleton_Mage", "Skeleton_Staff", 1.65f, new Color(0.5f, 1.4f, 0.7f), false, "Призыватель"),
    };

    class TMob { public int id, kind, hp; public Vector2 pos, net; public GameObject go; public Transform tr; public Animator anim; public bool seen; public float atkUntil, flash; }

    readonly Dictionary<int, TMob> tmobs = new();
    readonly Dictionary<int, Stack<TMob>> tmobPool = new();
    bool siegeOn;
    int siegeLeft, siegeKilled;
    float siegeHp, siegeMax, siegeShotAt, siegeDeadUntil, siegeHurtFlash;
    bool siegeDead;
    string siegeBoard;
    readonly List<GameObject> fences = new();

    float SiegeDmg => Mathf.Clamp(22f * Mathf.Max(1f, player.dmgMul), 10, 60);   // как HIT_MAX в siege_town.py

    // ---------- сеть ----------

    void SiegeNet(byte[] m)
    {
        switch (m[1])
        {
            case 30: SiegeSnap(m); break;
            case 31:
                if (m.Length < 3 || !siegeOn || siegeDead) break;
                // в таверне не достают: удар мог лететь, пока маг входил
                if (Town.InTavernHouse(player.pos)) break;
                siegeHp -= m[2];
                siegeHurtFlash = 0.35f;
                // фидбек: «урон по персонажу не видно» — красная виньетка, тряска, цифра над магом, звук,
                // и ближайший моб замахивается, чтобы было понятно, кто ударил
                hurtFx = Mathf.Max(hurtFx, 0.8f);
                if (optShake && !optCalm) shake = Mathf.Max(shake, 0.25f);   // уважаем настройки тряски (ревью Codex)
                Sfx("hurt", 0.7f);
                if (optNumbers) numbers.Add(new DamageNumber { pos = World.W(player.pos + Random.insideUnitCircle * 0.3f, 2.3f), text = "-" + m[2], color = new Color(1f, 0.3f, 0.3f), crit = m[2] >= 20 });
                TMob hitter = null;
                float hd = 3.5f * 3.5f;
                foreach (var t in tmobs.Values) { float d = (t.pos - player.pos).sqrMagnitude; if (d < hd) { hd = d; hitter = t; } }
                if (hitter != null) { hitter.anim.CrossFade("Attack", 0.05f); hitter.atkUntil = Time.unscaledTime + 0.6f; }
                if (siegeHp <= 0) SiegeFall();
                break;
            case 32:
                if (m.Length < 5) break;
                int mid = m[3] | m[4] << 8;
                if (Town.guys.TryGetValue(m[2], out var g) && tmobs.TryGetValue(mid, out var tm)) { SiegeBolt(g.pos, tm.pos, new Color(0.6f, 0.8f, 2.2f)); tm.flash = 0.25f; }
                break;
            case 33:
                if (m.Length < 6) break;
                var at = new Vector2(System.BitConverter.ToInt16(m, 2) / 50f, System.BitConverter.ToInt16(m, 4) / 50f);
                Vfx.Burst(Vfx.Fire, World.W(at, 0.4f), 18, 5, 0.6f, 0.6f, new Color(2.4f, 0.9f, 0.3f), 1f, 0.4f);
                Vfx.Burst(Vfx.Smoke, World.W(at, 0.3f), 8, 2, 1.2f, 1f, new Color(0.2f, 0.15f, 0.1f, 0.7f), 0.8f, 0.4f);
                Sfx("boom", 0.6f);
                break;
            case 37:
                if (siegeOn && !siegeDead) SiegeFall();
                break;
            case 34:
                siegeBoard = System.Text.Encoding.UTF8.GetString(m, 2, m.Length - 2);
                break;
        }
    }

    void SiegeSnap(byte[] m)
    {
        if (m.Length < 8) return;
        bool on = m[2] == 1;
        siegeLeft = m[3] | m[4] << 8;
        siegeKilled = m[5] | m[6] << 8;
        if (on != siegeOn) SiegeSwitch(on);
        int n = m[7], i = 8;
        foreach (var t in tmobs.Values) t.seen = false;
        for (int k = 0; k < n && i + 8 <= m.Length; k++, i += 8)
        {
            int id = m[i] | m[i + 1] << 8, kind = Mathf.Min(m[i + 2], SiegeKinds.Length - 1);
            var p = new Vector2(System.BitConverter.ToInt16(m, i + 3) / 50f, System.BitConverter.ToInt16(m, i + 5) / 50f);
            if (!tmobs.TryGetValue(id, out var t)) { t = MobGet(kind); t.id = id; t.pos = p; tmobs[id] = t; t.tr.position = World.W(p); }
            t.net = p;
            t.hp = m[i + 7];
            t.seen = true;
        }
        // кого нет в снимке — убит или взорвался
        List<int> gone = null;
        foreach (var t in tmobs.Values) if (!t.seen) (gone ??= new List<int>()).Add(t.id);
        if (gone != null) foreach (var id in gone) { MobDie(tmobs[id]); tmobs.Remove(id); }
    }

    void SiegeSwitch(bool on)
    {
        siegeOn = on;
        if (player.go != null) player.go.SetActive(true);   // осада кончилась, пока маг лежал — вернуть его (ревью Codex)
        siegeMax = 300;   // как HP_MAX в siege_town.py: смерть решает сервер ([37])
        siegeHp = siegeMax;
        siegeDead = false;
        if (fences.Count == 0)
            foreach (var r in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (r.name.StartsWith("fence")) fences.Add(r.gameObject);
        // ограда падает — орда за стенами (GameHorde.cs) становится настоящей
        foreach (var f in fences) if (f != null) f.SetActive(!on);
        if (on)
        {
            Town.status = "ОСАДА! Стена пала — бей орду, отступать в таверну";
            Sfx("boom", 1.2f);
            foreach (var f in fences) if (f != null) Vfx.Burst(Vfx.Smoke, f.transform.position + Vector3.up * 0.5f, 3, 1.5f, 1.4f, 1.2f, new Color(0.3f, 0.25f, 0.2f, 0.6f), 0.6f, 0.4f);
        }
        else
        {
            foreach (var t in tmobs.Values) MobDie(t);
            tmobs.Clear();
            Town.status = siegeKilled > 0 ? $"Осада отбита! Орды убито: {siegeKilled}" : null;
            Send0x09(36, 1);
        }
    }

    void Send0x09(byte act, byte v) { if (Coop.sock != null && Coop.sock.Open) Coop.Send(new byte[] { 0x09, act, v }); }

    // ---------- мобы ----------

    TMob MobGet(int kind)
    {
        if (tmobPool.TryGetValue(kind, out var st) && st.Count > 0) { var r = st.Pop(); r.go.SetActive(true); r.kind = kind; return r; }
        var k = SiegeKinds[kind];
        var t = new TMob { kind = kind };
        t.go = World.Character(k.model, 1f, out t.anim, out _);
        t.go.name = "SiegeMob";
        if (k.weapon != null) World.Attach(t.go, k.weapon, World.Skeleton);
        var rends = World.Combine(t.go, k.model + "|" + k.weapon, World.Skeleton);
        var mat = World.SkelMat(k.tint, kind == 3 ? 0.35f : kind == 5 ? 0.2f : 0);
        foreach (var r in rends) r.sharedMaterial = mat;
        t.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        t.tr = t.go.transform;
        t.tr.localScale *= k.scale;
        t.anim.Play(k.run ? "Run" : "Walk", 0, Random.value);
        return t;
    }

    void MobDie(TMob t)
    {
        if (t.go == null) return;
        Vfx.Burst(Vfx.Bone, World.W(t.pos, 0.4f), 6, 3, 0.08f, 0.6f, new Color(0.4f, 0.32f, 0.24f), 1f);
        t.go.SetActive(false);
        if (!tmobPool.TryGetValue(t.kind, out var st)) tmobPool[t.kind] = st = new Stack<TMob>();
        st.Push(t);
    }

    // «снарядов не видно» (фидбек 17.11): раньше искра 0,45 м летела 26 м/с и пропадала за треть секунды.
    // Теперь сгусток крупнее, летит 15 м/с с хвостом, а в цели — вспышка (siegeHits ждут, пока долетит)
    readonly List<(Vector3 at, float when, Color c)> siegeHits = new();

    void SiegeBolt(Vector2 from, Vector2 to, Color c)
    {
        var a = World.W(from, 1.1f);
        var b = World.W(to, 0.9f);
        float d = Vector3.Distance(a, b), speed = 15;
        var v = (b - a).normalized * speed;
        Vfx.Emit(Vfx.Glow, a, v, 1.0f, d / speed, c);
        Vfx.Emit(Vfx.Spark, a, v, 0.5f, d / speed, Color.white);
        for (int i = 1; i <= 3; i++) Vfx.Emit(Vfx.Glow, a - v * (0.025f * i), v, 1.0f - 0.22f * i, Mathf.Max(0.05f, d / speed - 0.025f * i), c * (1 - 0.2f * i));
        siegeHits.Add((b, Time.unscaledTime + d / speed, c));
    }

    // ---------- тик ----------

    void SiegeTownTick(float dt, bool free)
    {
        if (!siegeOn) return;
        float k = 1 - Mathf.Exp(-8 * dt);
        foreach (var t in tmobs.Values)
        {
            var prev = t.pos;
            t.pos = Vector2.Lerp(t.pos, t.net, k);
            var v = t.pos - prev;
            t.tr.position = World.W(t.pos);
            var face = v.sqrMagnitude > 1e-6f ? v : player.pos - t.pos;
            if (face.sqrMagnitude > 1e-6f) t.tr.rotation = Quaternion.Slerp(t.tr.rotation, Quaternion.LookRotation(World.W(face) - World.W(Vector2.zero)), k);
        }
        siegeHurtFlash = Mathf.Max(0, siegeHurtFlash - dt);
        for (int i = siegeHits.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime < siegeHits[i].when) continue;
            Vfx.Burst(Vfx.Glow, siegeHits[i].at, 7, 3.5f, 0.55f, 0.3f, siegeHits[i].c, 0.4f, 0.2f);
            siegeHits.RemoveAt(i);
        }
        foreach (var t in tmobs.Values)
        {
            // замах доигрывает и возвращается к бегу
            if (t.atkUntil > 0 && Time.unscaledTime > t.atkUntil) { t.atkUntil = 0; t.anim.CrossFade(SiegeKinds[t.kind].run ? "Run" : "Walk", 0.1f); }
            t.flash = Mathf.Max(0, t.flash - dt);
        }
        bool safe = Town.InTavernHouse(player.pos);
        if (siegeDead)
        {
            if (Time.unscaledTime < siegeDeadUntil) return;
            // воскресаем в таверне — там и отсидеться можно
            siegeDead = false;
            siegeHp = siegeMax;
            player.pos = Town.TavernC;
            player.tr.position = World.W(player.pos);
            player.go.SetActive(true);
            Send0x09(36, 1);
            Town.status = "Ты снова в строю — в таверне не достанут";
            return;
        }
        if (safe) siegeHp = Mathf.Min(siegeMax, siegeHp + siegeMax * 0.2f * dt);   // как HP_REGEN на сервере
        // базовая атака: раз в 0,45 с по ближайшему мобу в 8,5 м — без прокачки, только снаряжение
        if (safe || !free || myPortal != 255 || Time.unscaledTime < siegeShotAt) return;   // в круге портала не бьём — там и мобы не трогают
        TMob best = null;
        float bd = 8.5f * 8.5f;
        foreach (var t in tmobs.Values)
        {
            float d = (t.pos - player.pos).sqrMagnitude;
            if (d < bd) { bd = d; best = t; }
        }
        if (best == null) return;
        siegeShotAt = Time.unscaledTime + 0.45f;
        SiegeBolt(player.pos, best.pos, new Color(1.6f, 0.7f, 2.2f));
        int dmg = Mathf.RoundToInt(SiegeDmg);
        // «мой замах не видно»: маг на месте взмахивает посохом, по мобу — цифра урона
        if (!player.moving) player.anim.CrossFade("Cast", 0.05f);
        Sfx("cast", 0.35f);
        best.flash = 0.25f;
        if (optNumbers) numbers.Add(new DamageNumber { pos = World.W(best.pos, 2.0f), text = dmg.ToString(), color = new Color(1f, 0.85f, 0.4f) });
        Coop.Send(new byte[] { 0x09, 35, (byte)best.id, (byte)(best.id >> 8), (byte)dmg, (byte)(dmg >> 8) });
    }

    void SiegeFall()
    {
        siegeDead = true;
        siegeDeadUntil = Time.unscaledTime + 4;
        siegeHp = 0;
        Send0x09(36, 0);
        Vfx.Burst(Vfx.Smoke, World.W(player.pos, 0.5f), 10, 2, 1.2f, 1f, new Color(0.3f, 0.2f, 0.35f, 0.7f), 0.8f, 0.4f);
        player.go.SetActive(false);
        Town.status = "Ты пал! Через 4 секунды — в таверне";
    }

    // ---------- интерфейс ----------

    // поверх мира: цифры (свои удары и полученный урон) и здоровье раненых мобов
    void SiegeOverlay(float u)
    {
        if (!siegeOn) return;
        foreach (var n in numbers)
        {
            if (!ToGui(n.pos + Vector3.up * n.t * 1.5f, u, out var g)) continue;
            var c = n.color; c.a = 1 - n.t / 0.8f;
            num.normal.textColor = c;
            num.fontSize = n.crit ? 46 : 32;
            Outline(new Rect(g.x - 90, g.y - 30, 180, 60), n.text, num, 3);
        }
        foreach (var t in tmobs.Values)
        {
            if (t.hp >= 100) continue;
            if (!ToGui(World.W(t.pos, 1.3f * SiegeKinds[t.kind].scale + 0.3f), u, out var g)) continue;
            float w = t.kind == 4 ? 90 : 52;
            Rect(new Rect(g.x - w / 2, g.y, w, 8), new Color(0, 0, 0, 0.75f));
            Rect(new Rect(g.x - w / 2 + 1, g.y + 1, (w - 2) * t.hp / 100f, 6), t.flash > 0 ? Color.white : new Color(0.9f, 0.3f, 0.3f));
        }
    }

    void DrawSiegeHud(float W, float H, bool port)
    {
        if (!siegeOn) return;
        // здоровье — над джойстиком, посередине
        float bw = Mathf.Min(420, W - 64), bx = (W - bw) / 2, by = port ? H - 300 : H - 70;
        var r = new Rect(bx, by, bw, 26);
        RR(r, new Color(0, 0, 0, 0.7f), 8);
        float f = siegeMax > 0 ? Mathf.Clamp01(siegeHp / siegeMax) : 0;
        RR(new Rect(r.x + 3, r.y + 3, (r.width - 6) * f, r.height - 6), siegeHurtFlash > 0 ? new Color(0.85f, 0.55f, 0.55f) : Color.Lerp(new Color(0.62f, 0.12f, 0.12f), new Color(0.16f, 0.5f, 0.24f), f), 6);
        string hs = siegeDead ? "ПАЛ" : Town.InTavernHouse(player.pos) ? $"{(int)siegeHp} / {(int)siegeMax} · ЛЕЧИШЬСЯ В ТАВЕРНЕ" : $"{(int)siegeHp} / {(int)siegeMax}";
        // светлый текст на ярко-зелёной полоске не читался (17.11): полоска темнее, текст белый с обводкой
        Outline(r, hs, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, Color.white, TextAnchor.MiddleCenter), 2);
        // защитники — справа, под кнопками (в альбоме) или над здоровьем (на телефоне)
        if (string.IsNullOrEmpty(siegeBoard)) return;
        var lines = siegeBoard.Split('\n');
        float pw = 260, ph = 38 + lines.Length * 28;
        var box = port ? new Rect(W - pw - 16, by - ph - 12, pw, ph) : new Rect(16, H * 0.36f - ph - 10, pw, ph);
        RR(box, new Color(0.04f, 0.02f, 0.03f, 0.75f), 10);
        Text(new Rect(box.x, box.y + 4, pw, 30), "ЗАЩИТНИКИ", Col(caps, new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleCenter), 2);
        float y = box.y + 34;
        foreach (var l in lines)
        {
            if (l == "ИТОГ") continue;
            var p = l.Split('\t');
            if (p.Length < 2) continue;
            Text(new Rect(box.x + 14, y, pw - 80, 26), p[0], Shrink(Col(tinyS, Bone, TextAnchor.MiddleLeft), p[0], pw - 90), 2);
            Text(new Rect(box.xMax - 70, y, 56, 26), p[1], Col(tinyS, MetaGold, TextAnchor.MiddleRight), 2);
            y += 28;
        }
    }
}
