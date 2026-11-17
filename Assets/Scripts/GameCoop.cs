using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

// Маг как набор «своего»: статы, заклинания, артефакты, черта героя, союзники некроманта.
// В соло он один (me). В кооперативе их два, Game.Use(m) подставляет нужного в поля Game — весь код заклинаний
// работает как раньше через g.player и g.skills, не зная, чей это маг.
public class Mage
{
    public Player player;
    public List<Skill> skills;
    public List<Artifact> artifacts = new();
    public float ashRadius, heroIceMul = 1, heroElemMul = 1, heroShield, shieldKillAt;
    public bool heroKillShield, heroFrostCrit;
    public List<Ally> allies = new(), alliesDying = new();
    public List<(Vector2 pos, EnemyType type, float t)> raising = new();
    public RaiseDead raiseDead;
    public bool raiseLooked;

    public string hero = "mage", nick = "";
    public int slot;                  // слот игрока в комнате (0..3)
    public bool down, gone;           // пал и ждёт подъёма / друг отключился
    public float reviveT, air;        // прогресс подъёма; высота прыжка
    // друг по сети: последняя позиция и состояние
    public Vector2 netPos, netFace = Vector2.up;
    public bool netMoving;
    public Game.State netState = Game.State.Play;
}

// Сеть кооператива переживает перезагрузку сцены (смена уровня, «Ещё раз»): всё здесь статическое.
public static class Coop
{
    public enum Role { Off, Host, Guest }
    public static Role role;
    public static NetSocket sock;
    public static byte[] pendingOp;          // создать/войти — уходит, когда сокет откроется
    public static string code, status, error;
    // В комнате до Size игроков, у каждого свой слот. Хозяин комнаты (выбирает уровень, жмёт «Играть») —
    // младший занятый слот; мир в забеге считает runHost (сначала хозяин; если он вышел — младший из оставшихся).
    public const int Size = 4, Proto = 5;
    public static int slot;                  // мой слот
    public static bool seated;               // сервер подтвердил комнату и слот
    public static readonly bool[] present = new bool[Size];   // кто ещё в комнате, кроме меня
    public class Member { public string nick = "", hero = "mage"; public bool inRun; }
    public static readonly Member[] lobby = new Member[Size]; // что остальные рассказали о себе в лобби
    public static int runHost;               // слот того, кто считает мир в текущем забеге
    public static bool peer { get { foreach (var b in present) if (b) return true; return false; } }
    public static int Peers { get { int n = 0; foreach (var b in present) if (b) n++; return n; } }
    public static int HostSlot { get { for (int i = 0; i < Size; i++) if (i == slot || present[i]) return i; return slot; } }
    public static bool starting;             // сцена грузится под сетевой забег с общим сидом
    public static int seed;
    public static byte runId;
    // имена остальных через запятую (null — никого или ещё не представились)
    public static string mateNick
    {
        get
        {
            string s = null;
            for (int i = 0; i < Size; i++)
                if (present[i] && lobby[i] != null && lobby[i].nick.Length > 0) s = s == null ? lobby[i].nick : s + ", " + lobby[i].nick;
            return s;
        }
    }
    public static bool mateInRun => HostSlot != slot && lobby[HostSlot] != null && lobby[HostSlot].inRun;
    public static float keepAlive;
    public static string urlOverride;
    public static bool autoStart;            // тестовый запуск из командной строки: хозяин стартует сам

    // своя комната: код запоминается на устройстве и просится снова — ссылка для друга не меняется от раза к разу
    public static string MyCode => PlayerPrefs.GetString("coop.code");
    public static bool wantHost;
    public static string wantCode;
    public static float reconnectAt;
    public static int reconnects;
    public static string keyOverride;
    static string Key()
    {
        // ?key=… в адресе — свой ключ для вкладки: проверка нескольких игроков в одном браузере
        var dbg = System.Text.RegularExpressions.Regex.Match(Application.absoluteURL ?? "", "[?&]key=(\\w{4,32})");
        if (dbg.Success) return dbg.Groups[1].Value;
        if (keyOverride != null) return keyOverride;   // -key в командной строке: два тестовых процесса на одной машине
        var k = PlayerPrefs.GetString("coop.key");
        if (k.Length < 16) { k = System.Guid.NewGuid().ToString("N"); PlayerPrefs.SetString("coop.key", k); PlayerPrefs.Save(); }
        return k;
    }

    public static bool InRoom => role != Role.Off;
    public static bool IsHost => role != Role.Off && (seated ? HostSlot == slot : role == Role.Host);
    public static bool IsGuest => role != Role.Off && !IsHost;

    public static string Url()
    {
        if (!string.IsNullOrEmpty(urlOverride)) return urlOverride;
        var abs = Application.absoluteURL ?? "";
        if (Application.platform == RuntimePlatform.WebGLPlayer && abs.StartsWith("http"))
        {
            var u = new System.Uri(abs);
            // ?ws=ws://127.0.0.1:8798 — свой ретранслятор для отладки. Только когда сама страница открыта локально:
            // на настоящем домене ссылка с чужим ws увела бы игрока на чужой сервер вместе с токеном аккаунта
            var m = System.Text.RegularExpressions.Regex.Match(abs, "[?&]ws=([^&#]+)");
            if (m.Success && (u.Host == "127.0.0.1" || u.Host == "localhost")) return UnityWebRequest.UnEscapeURL(m.Groups[1].Value);
            return (u.Scheme == "https" ? "wss://" : "ws://") + u.Authority + "/coop";
        }
        return "wss://magicsurvival.neuraldeep.ru/coop";
    }

    public static string Link()
    {
        var abs = Application.absoluteURL ?? "";
        if (Application.platform == RuntimePlatform.WebGLPlayer && abs.StartsWith("http"))
        {
            var u = new System.Uri(abs);
            return u.Scheme + "://" + u.Authority + u.AbsolutePath + "?room=" + code;
        }
        return "https://magicsurvival.neuraldeep.ru/?room=" + code;
    }

    public static void Connect(bool host, string joinCode = "")
    {
        Close();
        error = null;
        code = host ? joinCode : joinCode.Trim();
        status = "Подключаюсь…";
        role = host ? Role.Host : Role.Guest;
        wantHost = host; wantCode = code;
        // код|ключ устройства: по ключу сервер отдаёт «мою комнату» обратно после перезагрузки вкладки или обрыва
        var op = code + "|" + Key() + "|" + Proto;
        pendingOp = new byte[1 + op.Length];
        pendingOp[0] = (byte)(host ? 0x01 : 0x02);
        for (int i = 0; i < op.Length; i++) pendingOp[1 + i] = (byte)op[i];
        // комната ещё не подтверждена: до ответа сервера код — только просьба
        code = null;
        sock = new NetSocket(Url());
        keepAlive = Time.unscaledTime + 10;
    }

    // сокет без комнаты: только глобальный чат (одиночный забег, GameChat.cs SoloChatTick)
    public static void ListenGlobal()
    {
        Close();
        error = null;
        pendingOp = new byte[] { 0x09, 22 };
        sock = new NetSocket(Url());
        listening = true;
        keepAlive = Time.unscaledTime + 10;
    }

    public static bool listening;   // открыт сокет-слушатель глобального чата, а не комната и не город

    public static void Close()
    {
        listening = false;
        sock?.Close();
        sock = null;
        pendingOp = null;
        role = Role.Off;
        seated = false;
        slot = 0;
        for (int i = 0; i < Size; i++) { present[i] = false; lobby[i] = null; }
        code = null;
        status = null;
    }

    public static void Send(byte[] m)
    {
        if (sock != null && sock.Open) sock.Send(m);
    }
}

public partial class Game
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void BrowserShare(string text, string url);
    [DllImport("__Internal")] static extern void BrowserDropParam(string name);
#endif

    // ---------- Маги и подстановка ----------

    public Mage me, cur;
    public readonly List<Mage> mates = new();   // остальные маги пати (до трёх), у каждого свой слот
    Mage MateAt(int slot) { foreach (var m in mates) if (m.slot == slot) return m; return null; }
    bool AnyMate { get { foreach (var m in mates) if (!m.gone) return true; return false; } }
    Mage FirstAlive { get { foreach (var m in mates) if (Alive(m)) return m; return null; } }
    // все маги забега, кто ещё в нём: я и неушедшие напарники
    readonly List<Mage> party = new();
    List<Mage> All
    {
        get
        {
            party.Clear();
            party.Add(me);
            foreach (var m in mates) if (!m.gone) party.Add(m);
            return party;
        }
    }
    // где встаёт маг в начале забега: по слоту, чтобы четверо не стояли в одной точке
    static readonly Vector2[] SlotPos = { Vector2.zero, new(1.6f, 0), new(-1.6f, 0), new(0, 1.6f) };
    bool coopRun;            // этот забег — сетевой
    bool netRunning, hostLinked, hostLost, waitingMate, leaving;
    float waitUntil, snapNext, stateNext, lobbyNext;
    State hostState = State.Play;
    readonly List<int> pendingChests = new();   // редкости сундуков, ждущих выбора (кооператив)
    int heldChoice;                             // отложенный выбор: 1 — карты уровня, 2 — суперсила, 3 — сундук
    int coopLevels, nextEnemyId, nextChestId, nextRelicId, nextGemId, seenStamp;
    string lobbySent;
    static bool urlChecked, argsChecked;
    static bool soloStart;
    public static bool auto;   // автопилот (тест кооператива из командной строки)

    // гость забега — тот, кто не считает мир сам, а получает его снимками от runHost
    bool Guest => coopRun && Coop.InRoom && Coop.runHost != Coop.slot;
    const float ReviveTime = 3f, ReviveR = 2.2f;

    void Save(Mage m)
    {
        m.player = player; m.skills = skills; m.artifacts = artifacts; m.ashRadius = ashRadius;
        m.heroKillShield = heroKillShield; m.heroFrostCrit = heroFrostCrit; m.heroIceMul = heroIceMul; m.heroElemMul = heroElemMul;
        m.heroShield = heroShield; m.shieldKillAt = shieldKillAt;
        m.allies = allies; m.alliesDying = alliesDying; m.raising = raising; m.raiseDead = raiseDead; m.raiseLooked = raiseLooked;
    }

    public void Use(Mage m)
    {
        if (m == null || m == cur) return;
        if (cur != null) Save(cur);
        player = m.player; skills = m.skills; artifacts = m.artifacts; ashRadius = m.ashRadius;
        heroKillShield = m.heroKillShield; heroFrostCrit = m.heroFrostCrit; heroIceMul = m.heroIceMul; heroElemMul = m.heroElemMul;
        heroShield = m.heroShield; shieldKillAt = m.shieldKillAt;
        allies = m.allies; alliesDying = m.alliesDying; raising = m.raising; raiseDead = m.raiseDead; raiseLooked = m.raiseLooked;
        cur = m;
    }

    bool Alive(Mage m) => m != null && !m.gone && !m.down;

    // ---------- Выбор без остановки мира ----------
    // Вдвоём уровень не открывает карты сам и мир не стоит: копится счётчик, игрок открывает карты кнопкой
    // «Прокачка» (E / Enter), когда ему удобно, и может закрыть их «Позже» (Esc) — набор карт при этом сохраняется.

    static bool IsChoice(State s) => s == State.LevelUp || s == State.Super || s == State.Chest;
    bool Choosing => IsChoice(state);
    bool WorldRuns => (state == State.Play || (coopRun && Choosing)) && Running;
    int ChoiceCount => pendingLevels + pendingChests.Count + (heldChoice != 0 ? 1 : 0);

    void OpenChoice()
    {
        if (state != State.Play || me.down || ChoiceCount == 0) return;
        if (heldChoice != 0)
        {
            state = heldChoice == 1 ? State.LevelUp : heldChoice == 2 ? State.Super : State.Chest;
            heldChoice = 0;
            menuT = Time.unscaledTime;
        }
        else if (pendingChests.Count > 0) { int r = pendingChests[0]; pendingChests.RemoveAt(0); ChestChoices(r); }
        else OpenLevelUp();
    }

    void HoldChoice()
    {
        if (!Choosing) return;
        heldChoice = state == State.LevelUp ? 1 : state == State.Super ? 2 : 3;
        state = State.Play;
    }

    // после выбора — сразу следующий, если накопилось: игрок уже в картах
    void NextChoice()
    {
        if (coopRun && state == State.Play && ChoiceCount > 0) OpenChoice();
    }

    void ChoiceButton(float W, float H)
    {
        int n = ChoiceCount;
        if (state != State.Play || me.down || n == 0) return;
        float bw = Portrait ? 380 : 440, bh = 84;
        var r = new Rect((W - bw) / 2, Portrait ? H * 0.74f : H - 200, bw, bh);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5);
        var c = Color.Lerp(new Color(0.55f, 0.8f, 1f), Color.white, pulse * 0.5f);
        RR(new Rect(r.x - 6, r.y - 6, r.width + 12, r.height + 12), new Color(c.r, c.g, c.b, 0.12f + 0.16f * pulse), 20);
        if (Btn(r, n > 1 ? $"Прокачка ×{n}" : "Прокачка", c)) OpenChoice();
    }

    // мир идёт, только когда оба в бою или выбирают карты: пауза и статы у любого — стоят оба
    bool Running
    {
        get
        {
            if (!coopRun) return true;
            if (Guest) return netRunning && hostLinked && !hostLost;
            if (waitingMate) return false;
            // пауза или статы у любого из пати останавливают мир у всех
            foreach (var m in mates)
                if (!m.gone && (m.netState == State.Pause || m.netState == State.Stats)) return false;
            return true;
        }
    }

    // куда смотрит камера: павший следит за другом
    Vector2 ViewPos => me != null && me.down && FirstAlive != null ? FirstAlive.player.pos : player.pos;

    // точка спавна волн, сундуков и реликвий: вокруг случайного живого мага
    Vector2 Anchor()
    {
        Mage pick = null;
        int n = 0;
        foreach (var m in All)
            if (Alive(m) && Random.Range(0, ++n) == 0) pick = m;
        return (pick ?? me).player.pos;
    }

    // за кем бегут скелеты: ближайший живой маг
    Vector2 TargetOf(Vector2 p) => Nearest(p).player.pos;

    // ближайший к точке живой маг; если живых нет — я
    Mage Nearest(Vector2 p)
    {
        Mage best = null;
        float bd = float.MaxValue;
        foreach (var m in All)
        {
            if (!Alive(m)) continue;
            float d = (m.player.pos - p).sqrMagnitude;
            if (d < bd) { bd = d; best = m; }
        }
        return best ?? me;
    }

    // кто из живых магов стоит в точке (сундук, реликвия)
    Mage Toucher(Vector2 p, float sqr)
    {
        foreach (var m in All)
            if (Alive(m) && (m.player.pos - p).sqrMagnitude <= sqr) return m;
        return null;
    }

    float JumpH => jumpT > 0 ? Mathf.Sin((1 - jumpT / JumpTime) * Mathf.PI) * 1.8f : 0;

    void TickMages(float dt)
    {
        var all = All;
        for (int k = 0; k < all.Count; k++)
        {
            var m = all[k];
            Use(m);
            if (!m.down)
                foreach (var s in skills)
                    if (s.level > 0 && s.spell) { curElement = s.element; s.Tick(this, dt); }
            curElement = Element.None;
            HeroTick(dt);
        }
        Use(me);
    }

    // верёвка: маги пати расходятся не дальше Leash от середины остальных — волны, сундуки и подъём павшего
    // рассчитаны на то, что вы рядом. Нить тянется к ближайшему живому напарнику.
    const float Leash = 16f;
    LineRenderer tether;

    void Tether(float dt)
    {
        Vector2 mid = Vector2.zero;
        int cnt = 0;
        Mage near = null;
        float nd = float.MaxValue;
        if (coopRun && !me.down)
            foreach (var m in mates)
            {
                if (!Alive(m)) continue;
                mid += m.player.pos; cnt++;
                float dd = (m.player.pos - player.pos).sqrMagnitude;
                if (dd < nd) { nd = dd; near = m; }
            }
        bool on = cnt > 0;
        if (on) mid /= cnt;
        float d = on ? (player.pos - mid).magnitude : 0;
        if (on && d > Leash) player.pos = mid + (player.pos - mid) / d * Leash;
        // нить видна, когда натянута
        float k = on ? Mathf.InverseLerp(Leash * 0.75f, Leash, d) : 0;
        if (tether == null)
        {
            if (k <= 0) return;
            tether = MakeLine("Tether", 2, 0.12f, new Color(0.6f, 2.2f, 1f));
        }
        tether.gameObject.SetActive(k > 0);
        if (k <= 0) return;
        tether.SetPosition(0, World.W(player.pos, 1.1f));
        tether.SetPosition(1, World.W(near.player.pos, 1.1f));
        tether.widthMultiplier = 0.05f + 0.12f * k;
        tether.startColor = tether.endColor = new Color(1, 1, 1, 0.3f + 0.7f * k);
    }

    void SetDown(Mage m, bool on)
    {
        if (m.down == on) return;
        m.down = on;
        m.reviveT = 0;
        foreach (var s in m.skills) s.Show(!on);
        var p = m.player;
        p.moving = false;
        p.anim.CrossFade(on ? "Death" : "Idle", 0.1f);
        if (p.light != null) p.light.intensity = on ? 2 : 7;
        if (on && m == me) { jumpT = 0; stickOn = false; HoldChoice(); }
        if (!on) Vfx.Emit(Vfx.Ring, World.W(p.pos, 0.15f), Vector3.zero, 5, 0.5f, new Color(0.6f, 1f, 0.7f));
    }

    // ---------- Друг: создание по INIT и движение по сети ----------

    static void PutStats(BinaryWriter w, Player p)
    {
        foreach (var v in new[] { p.hp, p.maxHp, p.speed, p.magnet, p.regen, p.vamp, p.dmgMul, p.cdMul, p.area, p.crit, p.critMul,
                     p.armor, p.thorns, p.xpMul, p.killHeal, p.necro, p.findMul, p.goldMul, p.cloak ? 1 : 0, p.revives, p.extraChoice })
            w.Write(v);
    }

    static void GetStats(BinaryReader r, Player p)
    {
        p.hp = r.ReadSingle(); p.maxHp = r.ReadSingle(); p.speed = r.ReadSingle(); p.magnet = r.ReadSingle(); p.regen = r.ReadSingle();
        p.vamp = r.ReadSingle(); p.dmgMul = r.ReadSingle(); p.cdMul = r.ReadSingle(); p.area = r.ReadSingle(); p.crit = r.ReadSingle();
        p.critMul = r.ReadSingle(); p.armor = r.ReadSingle(); p.thorns = r.ReadSingle(); p.xpMul = r.ReadSingle(); p.killHeal = r.ReadSingle();
        p.necro = r.ReadSingle(); p.findMul = r.ReadSingle(); p.goldMul = r.ReadSingle(); p.cloak = r.ReadSingle() > 0.5f;
        p.revives = (int)r.ReadSingle(); p.extraChoice = (int)r.ReadSingle();
    }

    void SendInit()
    {
        var w = RunMsg(0x12);
        w.Write(builtHero); w.Write(Nick);
        foreach (var slot in new[] { "hat", "robe", "staff", "glow" }) w.Write(Wearing(slot));
        PutStats(w, player);
        w.Write((byte)skills.Count);
        foreach (var s in skills) { w.Write((byte)s.level); w.Write((sbyte)s.super); }
        Flush();
    }

    Mage BuildMate(BinaryReader r, int slot)
    {
        var hc = HeroContent.Get(r.ReadString());
        var m = new Mage { hero = hc.id, nick = r.ReadString(), slot = slot };
        string hat = r.ReadString(), robe = r.ReadString(), staff = r.ReadString(), glow = r.ReadString();
        var stats = new Player();
        GetStats(r, stats);
        var p = m.player = new Player();
        p.go = hc.Spawn(out p.anim);
        if (hc.id == "mage") Cosmetics.Apply(p.go, hat, robe, staff, glow);
        p.go.name = "Mate";
        p.tr = p.go.transform;
        p.light = new GameObject("MateLight").AddComponent<Light>();
        p.light.type = LightType.Point;
        p.light.range = 8; p.light.intensity = 6; p.light.shadows = LightShadows.None;
        m.skills = NewSkills();
        hc.StartSkill(m.skills);
        Use(m);
        hc.ApplyTrait(this);
        int n = r.ReadByte();
        for (int i = 0; i < n; i++)
        {
            int lv = r.ReadByte(), sup = r.ReadSByte();
            if (i >= skills.Count) continue;
            while (skills[i].level < lv) LevelSkill(skills[i]);
            if (sup >= 0) { skills[i].super = sup; skills[i].OnSuper(this); }
        }
        // итог — статы отправителя: героя, прокачку и бонусы считает он сам
        var keepLight = p.light;
        foreach (var f in typeof(Player).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            if (f.FieldType == typeof(float) || f.FieldType == typeof(int) || f.FieldType == typeof(bool))
                if (f.Name != "hurt" && f.Name != "moving") f.SetValue(p, f.GetValue(stats));
        p.light = keepLight;
        p.anim.Play("Idle");
        Use(me);
        m.netPos = p.pos = SlotPos[slot % SlotPos.Length];
        p.tr.position = World.W(p.pos);
        Debug.Log($"coop: mate {m.nick} ({m.hero}) joined the run");
        return m;
    }

    void MatesTick(float dt)
    {
        foreach (var m in mates) MateTick(m, dt);
    }

    void MateTick(Mage m, float dt)
    {
        if (m == null || m.gone) return;
        var p = m.player;
        if ((p.pos - m.netPos).sqrMagnitude > 25) p.pos = m.netPos;
        else p.pos = Vector2.Lerp(p.pos, m.netPos, 1 - Mathf.Exp(-18 * dt));
        if (m.netFace.sqrMagnitude > 0.01f) p.face = Vector2.Lerp(p.face, m.netFace, 1 - Mathf.Exp(-12 * dt)).normalized;
        if (!m.down)
        {
            if (m.netMoving != p.moving) { p.anim.CrossFade(m.netMoving ? "Run" : "Idle", 0.12f); p.moving = m.netMoving; }
            if (!Guest) p.hp = Mathf.Min(p.maxHp, p.hp + p.regen * (p.hurt > 0 ? 0.35f : 1f) * dt);
        }
        p.hurt = Mathf.Max(0, p.hurt - dt);
        p.tr.position = World.W(p.pos, m.down ? 0 : m.air);
        if (!m.down) p.tr.rotation = Quaternion.Slerp(p.tr.rotation, Quaternion.LookRotation(World.W(p.face)), dt * 12);
        p.light.transform.position = World.W(p.pos, 2.6f);
    }

    // Маг вышел из забега (закрыл вкладку, ушёл в город, пропала связь). Его модель исчезает, забег идёт дальше.
    // Если вышел тот, кто считал мир, — мир забирает младший по слоту из оставшихся (все клиенты решают одинаково).
    void MageLeft(int slot)
    {
        if (!coopRun || state == State.Menu || state == State.Town) return;
        var m = MateAt(slot);
        if (m != null && !m.gone)
        {
            m.gone = true;
            foreach (var sk in m.skills) sk.Show(false);
            m.player.go.SetActive(false);
            m.player.light.gameObject.SetActive(false);
        }
        bool over = state == State.Dead || state == State.Won;
        if (slot == Coop.runHost)
        {
            int next = Coop.slot;
            foreach (var o in mates) if (!o.gone && o.slot < next) next = o.slot;
            Coop.runHost = next;
            if (over) return;
            if (next == Coop.slot) TakeOver();
            else { hostLinked = false; Banner(m != null ? $"{m.nick} вышел из похода" : "Маг вышел из похода", "Мир переходит к другому магу…"); }
        }
        else if (!over) Banner(m != null ? $"{m.nick} вышел из похода" : "Маг вышел из похода", AnyMate ? "Забег продолжается" : "Ты продолжаешь один");
        if (!Guest && waitingMate && mates.Count >= Coop.Peers) waitingMate = false;
    }

    // своя связь с сервером пропала посреди забега: остальных я больше не вижу, продолжаю один
    // wasGuest считают до Coop.Close(): закрытие сбрасывает мой слот, и гость выглядел бы хозяином
    void AloneNow(bool wasGuest)
    {
        if (state == State.Dead || state == State.Won || state == State.Menu || state == State.Town) return;
        foreach (var m in mates)
        {
            if (m.gone) continue;
            m.gone = true;
            foreach (var sk in m.skills) sk.Show(false);
            m.player.go.SetActive(false);
            m.player.light.gameObject.SetActive(false);
        }
        Coop.runHost = Coop.slot;
        waitingMate = false;
        if (wasGuest)
        {
            // мир ещё ни разу не пришёл — продолжать нечего
            if (!hostLinked) { hostLost = true; state = State.Dead; SaveRecords(false); AwardGold(false); Banner("Связь потеряна", "Забег окончен"); return; }
            TakeOver();
        }
        else Banner("Связь с пати потеряна", "Ты продолжаешь один");
    }

    // Я становлюсь тем, кто считает мир. До этого у меня были только «тени» врагов из снимков (позиция и доля
    // здоровья) — превращаем их в настоящих и восстанавливаем счётчики волн по времени забега. Остальные маги
    // пати остаются: их копии с этого момента наносят настоящий урон, а я начинаю слать им снимки.
    void TakeOver()
    {
        Coop.runHost = Coop.slot;
        // теперь снимки шлю я: начинаю с ключевого, про каждого врага — заново
        snapKeyDue = true;
        sentList.Clear(); sentGems.Clear();
        foreach (var g in gems) g.sentKnown = false;
        int maxId = 0;
        foreach (var e in enemies)
        {
            if (!e.alive) continue;
            float frac = Mathf.Clamp01(e.hp / Mathf.Max(0.0001f, e.maxHp));
            e.maxHp = EnemyHp(e.type, e.tier);
            e.hp = Mathf.Max(1, frac * e.maxHp);
            // у гостя флаги состояния были «включено/выключено» — даём им обычную короткую длительность
            if (e.frozen > 0) e.frozen = 1.5f;
            if (e.slow > 0) e.slow = 1.5f;
            if (e.stun > 0) e.stun = 0.6f;
            if (e.spawnLock > 0) e.spawnLock = 0.5f;
            e.ring = false;
            e.attackCd = 0;
            maxId = Mathf.Max(maxId, e.id);
        }
        netEnemies.Clear();
        netGems.Clear();
        nextEnemyId = maxId;
        foreach (var g in gems) if (g.alive) nextGemId = Mathf.Max(nextGemId, g.id);
        foreach (var c in chests) nextChestId = Mathf.Max(nextChestId, c.id);
        foreach (var rl in relics) if (rl.alive) nextRelicId = Mathf.Max(nextRelicId, rl.id);
        ringOn = false;
        ringLine.gameObject.SetActive(false);
        // волны, боссы и элита идут по времени: считаем, что уже было
        waveIdx = 0;
        while (waveIdx < WaveTimes.Length && WaveTimes[waveIdx] <= time) waveIdx++;
        bossStage = time < 450 ? 0 : time < 660 ? 1 : time < 14 * 60 ? 2 : 3;
        nextElite = 110;
        while (nextElite <= time) nextElite += CurDiff.elite;
        nextChest = time + 30;
        nextRelic = time + 30;
        spawnAcc = 0;
        surgeUntil = 0;
        waitingMate = false;
        coopLevels = 0;
        hostLinked = true;
        Banner(AnyMate ? "Мир теперь считаешь ты" : "Ты продолжаешь один", AnyMate ? "Прежний хозяин вышел из похода" : "Остальные вышли из похода");
        Debug.Log($"coop: took over the run at t={time:0}, enemies={enemies.Count}, mates={mates.Count}");
    }

    // ---------- Смерть, подъём, конец забега (у хозяина) ----------

    bool CoopDeaths(float dt)
    {
        if (!AnyMate) return false;
        var all = All;
        for (int k = 0; k < all.Count; k++)
        {
            var m = all[k];
            if (m.down || m.player.hp > 0) continue;
            Use(m);
            if (player.revives > 0)
            {
                player.revives--;
                player.hp = player.maxHp * 0.5f;
                Boom(player.pos, 4, new Color(1f, 0.6f, 0.2f), false);
                Query(player.pos, 5, tmp);
                foreach (var e in tmp) Hit(e, 200, (e.pos - player.pos).normalized, 2f);
                WorldBanner("Перо феникса!", $"{m.nick} восстаёт из пепла");
                if (m == me) screenFlash = 1;
                else { var w = RunMsg(0x16); w.Write((byte)8); w.Write((byte)m.slot); Flush(); }
            }
            else
            {
                player.hp = 0;
                SetDown(m, true);
                WorldBanner($"{m.nick} пал", "Встань рядом на 3 секунды, чтобы поднять");
            }
            Use(me);
        }
        bool allDown = true;
        for (int k = 0; k < all.Count; k++)
        {
            Revive(all[k], all, dt);
            if (!all[k].down) allDown = false;
        }
        if (allDown)
        {
            state = State.Dead;
            SaveRecords(false);
            AwardGold(false);
            SendEnd(false);
        }
        return true;
    }

    // павшего поднимает любой живой маг пати, простояв рядом ReviveTime
    void Revive(Mage m, List<Mage> all, float dt)
    {
        if (!m.down) return;
        bool near = false;
        foreach (var h in all)
            if (h != m && Alive(h) && (h.player.pos - m.player.pos).sqrMagnitude < ReviveR * ReviveR) { near = true; break; }
        m.reviveT = near ? m.reviveT + dt : Mathf.Max(0, m.reviveT - dt * 0.5f);
        if (near && Random.value < 0.4f)
            Vfx.Emit(Vfx.Ember, World.W(m.player.pos + Random.insideUnitCircle * 0.8f, 0.2f), Vector3.up * 2, 0.2f, 0.7f, new Color(0.6f, 1f, 0.7f));
        if (m.reviveT < ReviveTime) return;
        SetDown(m, false);
        m.player.hp = m.player.maxHp * 0.4f;
        WorldBanner($"{m.nick} снова в строю", null);
    }

    void SendEnd(bool won)
    {
        var w = RunMsg(0x16); w.Write((byte)4); w.Write(won); Flush();
    }

    void WorldBanner(string title, string sub)
    {
        Banner(title, sub);
        if (!coopRun || Guest || !AnyMate) return;
        var w = RunMsg(0x16); w.Write((byte)1); w.Write(title); w.Write(sub ?? ""); Flush();
    }

    // ---------- Сообщения ----------

    static readonly MemoryStream ms = new();
    static readonly BinaryWriter bw = new(ms);

    static BinaryWriter Msg(byte type) { ms.SetLength(0); bw.Write(type); return bw; }
    static BinaryWriter RunMsg(byte type) { Msg(type); bw.Write(Coop.runId); return bw; }
    static int sentBytes, recvBytes;
    static void Flush() { bw.Flush(); sentBytes += (int)ms.Length; Coop.Send(ms.ToArray()); }

    void SendEvent(byte kind)
    {
        if (!coopRun) return;
        RunMsg(0x16).Write(kind);
        Flush();
    }

    // выбор игрока уходит другу: он повторяет его на копии этого мага (у хозяина копия гостя — настоящая)
    void CoopAct(byte kind, int idx, int arg = 0)
    {
        if (!coopRun || !AnyMate || idx < 0) return;
        var w = RunMsg(0x13); w.Write(kind); w.Write((short)idx); w.Write((short)arg); Flush();
    }

    void ApplyAct(Mage m, byte kind, int idx, int arg)
    {
        if (m == null || m.gone) return;
        Use(m);
        switch (kind)
        {
            case 1: if (idx < skills.Count) LevelSkill(skills[idx]); break;
            case 2: if (idx < Endless().Count) LevelSkill(Endless()[idx]); break;
            case 3:
                if (idx < skills.Count && skills[idx].superNames != null) { skills[idx].super = arg; skills[idx].OnSuper(this); }
                break;
            case 4:
                if (idx < artifactPool.Count) { artifactPool[idx].apply(this); artifacts.Add(artifactPool[idx]); }
                break;
        }
        Use(me);
    }

    void CoopSendWave(Color c)
    {
        // только своя волна: чужую мы сами проигрываем по событию, пересылать её обратно нельзя
        if (!coopRun || cur != me) return;
        var w = RunMsg(0x16); w.Write((byte)6); w.Write(c.r); w.Write(c.g); w.Write(c.b); Flush();
    }

    void CoopSendLand() { if (coopRun && cur == me) SendEvent(5); }

    // ---------- Каждый кадр: сокет, лобби, снимки ----------

    void CoopTick()
    {
        CoopArgs();
        CoopUrl();
        var sock = Coop.sock;
        if (shotDir != null && Time.unscaledTime > shotNext) { shotNext = Time.unscaledTime + 15; ScreenCapture.CaptureScreenshot($"{shotDir}/shot_{(int)Time.unscaledTime:000}.png"); }
        if (soloStart && state == State.Menu) { soloStart = false; StartRun(); return; }
        if (sock == null && Coop.reconnectAt > 0 && Time.unscaledTime > Coop.reconnectAt)
        {
            Coop.reconnectAt = 0;
            if (state == State.Menu) { Coop.reconnects++; Coop.Connect(Coop.wantHost, Coop.wantCode); sock = Coop.sock; }
        }
        SoloChatTick();
        sock = Coop.sock;
        if (sock == null) { TownNetTick(); sock = Coop.sock; }
        if (sock == null) return;
        if (Coop.pendingOp != null && sock.Open) { sock.Send(Coop.pendingOp); Coop.pendingOp = null; }
        while (!leaving && Coop.sock != null && Coop.sock.TryRecv(out var m)) OnNet(m);
        if (Coop.sock == null || leaving) return;
        if (Coop.sock.Dead) { LostLink(); return; }

        float now = Time.unscaledTime;
        if (now > Coop.keepAlive) { Coop.keepAlive = now + 10; Coop.Send(new byte[] { 0x08 }); }
        if (state == State.Menu && now > lobbyNext) { lobbyNext = now + 0.5f; SendLobby(false); }
        if (Coop.autoStart && Coop.IsHost && Coop.peer && state == State.Menu && !coopRun) { StartRun(); return; }
        if (!coopRun) return;
        if (now > statNext)
        {
            // для отладки по Player.log: сколько гоняем по сети и что в мире
            statNext = now + 10;
            int alive = 0;
            foreach (var e in enemies) if (e.alive) alive++;
            Debug.Log($"coop: t={time:0} state={state} run={Running} out={sentBytes / 10240f:0.0}KB/s (шапка {snapHead / 10240f:0.0} враги {snapEnemy / 10240f:0.0} прочее {snapRest / 10240f:0.0}) in={recvBytes / 10240f:0.0}KB/s enemies={alive} gems={gems.Count} lvl={level} hp={player.hp:0} mates={mates.Count} host={Coop.runHost} slot={Coop.slot} me={player.pos} down={me.down}");
            sentBytes = recvBytes = 0; snapHead = snapEnemy = snapRest = 0;
        }

        if (!Guest)
        {
            // ждём, пока все, кто был в комнате на старте, пришлют своих магов (или выйдут)
            if (waitingMate && (mates.Count >= Coop.Peers || now > waitUntil || !Coop.peer)) waitingMate = false;
            if (coopLevels > 0 && AnyMate)
            {
                var w = RunMsg(0x16); w.Write((byte)2); w.Write((byte)coopLevels); Flush();
                coopLevels = 0;
            }
            if (AnyMate && state != State.Dead && state != State.Won && now > snapNext)
            {
                bool full = (snapCount++ % 5) == 0;
                // враги идут дельтой (SendSnap), снимок мал при любой толпе — частота постоянная
                snapNext = now + 1 / 15f;
                SendSnap(full);
            }
        }
        else
        {
            if (now > stateNext && state != State.Menu) { stateNext = now + 1 / 20f; SendState(); }
        }
    }
    int snapCount;
    float statNext, shotNext;
    static string shotDir;

    void LostLink()
    {
        // в городе без комнаты: обрыв — просто заходим в город заново
        if (state == State.Town && !Coop.InRoom) { TownLost(); return; }
        bool host = Coop.IsHost;
        bool wasGuest = coopRun && Coop.runHost != Coop.slot;
        string code = Coop.code;
        Coop.Close();
        if (!coopRun && state != State.Menu && state != State.Town) return;   // слушатель чата в одиночном забеге — SoloChatTick переподключит
        Coop.error = "Связь с сервером потеряна";
        if (state == State.Town) { TownLost(); return; }
        // в меню комнату возвращаем сами: до пяти попыток с тем же кодом и в той же роли
        if (state == State.Menu && !string.IsNullOrEmpty(code) && Coop.reconnects < 5)
        {
            Coop.wantHost = host; Coop.wantCode = code;
            Coop.reconnectAt = Time.unscaledTime + 2;
            Coop.error = "Связь потеряна — возвращаюсь в комнату…";
        }
        if (coopRun) AloneNow(wasGuest);
    }

    void SendLobby(bool force)
    {
        if (!Coop.peer) return;
        string key = $"{Nick}|{builtHero}|{selLevel}|{DiffByte}|{coopRun}|{Coop.Peers}";
        if (!force && key == lobbySent) return;
        lobbySent = key;
        var w = Msg(0x10);
        w.Write(Nick); w.Write(Heroes[selHero].id); w.Write((byte)selLevel); w.Write(DiffByte); w.Write(state != State.Menu && state != State.Town);
        Flush();
    }

    void OnNet(byte[] m)
    {
        if (m.Length == 0) return;
        recvBytes += m.Length;
        byte t = m[0];
        string text = t < 0x10 && m.Length > 1 ? System.Text.Encoding.UTF8.GetString(m, 1, m.Length - 1) : "";
        switch (t)
        {
            case 0x03:
            case 0x04:
            {
                // «код|слот»: комната подтверждена, 0x03 — создал я, 0x04 — вошёл в чужую
                var f = text.Split('|');
                Coop.code = f[0];
                Coop.slot = f.Length > 1 && int.TryParse(f[1], out var sl) ? Mathf.Clamp(sl, 0, Coop.Size - 1) : (t == 0x03 ? 0 : 1);
                Coop.seated = true;
                Coop.status = null; Coop.reconnects = 0; lobbySent = null;
                Debug.Log($"coop: room {f[0]} slot {Coop.slot}");
                if (state == State.Town)
                {
                    // поход из города: комната разовая, «мою комнату» и последний код не трогаем
                    Coop.role = t == 0x03 ? Coop.Role.Host : Coop.Role.Guest;
                    if (t == 0x04) { partyGo = false; invitedBy = 0; guysOpen = false; }
                    return;
                }
                PlayerPrefs.SetString(t == 0x03 ? "coop.code" : "coop.last", f[0]); PlayerPrefs.Save();
                return;
            }
            case 0x05: Coop.Close(); Coop.error = text; return;
            case 0x09: case 0x0A: case 0x0B: case 0x0C: case 0x0D: case 0x0E: case 0x0F:
                // глобальный чат приходит и в город, и в поход (GameChat.cs)
                if (t == 0x09 && m.Length >= 2 && (m[1] == 20 || m[1] == 21)) { GlobalChatIn(m); break; }
                if (t == 0x09 && m.Length >= 2 && (m[1] == 23 || m[1] == 24)) { SystemChatIn(m); break; }
                // отказ сервера в походе (например, «войди, чтобы писать всем») — в ленту чата, иначе сообщение пропадёт молча
                if (t == 0x09 && m.Length >= 2 && m[1] == 0 && state != State.Town) { RunSay("Сервер", System.Text.Encoding.UTF8.GetString(m, 2, m.Length - 2), false); break; }
                if (state == State.Town) TownNet(t, m);
                return;
            case 0x06:
                if (m.Length < 2 || m[1] >= Coop.Size) return;
                Coop.present[m[1]] = true;
                lobbySent = null; SendLobby(true);
                Debug.Log($"coop: slot {m[1]} is in the room");
                return;
            case 0x07:
            {
                if (m.Length < 2 || m[1] >= Coop.Size) return;
                int sl = m[1];
                Debug.Log($"coop: slot {sl} left");
                Coop.present[sl] = false;
                Coop.lobby[sl] = null;
                lobbySent = null;
                if (coopRun) MageLeft(sl);
                // в городе ждали похода, а позвавший пропал — возвращаемся на площадь
                if (state == State.Town && !Coop.peer) { Coop.Close(); Town.retryAt = 0; partyGo = false; Town.status = "Поход не состоялся"; }
                return;
            }
        }
        if (t < 0x10 || m.Length < 2) return;
        // игровой кадр: второй байт — слот отправителя (ставит сервер)
        int from = m[1];
        if (from >= Coop.Size) return;
        using var r = new BinaryReader(new MemoryStream(m, 2, m.Length - 2));
        if (t == 0x10)
        {
            var mem = Coop.lobby[from] ??= new Coop.Member();
            mem.nick = r.ReadString(); mem.hero = r.ReadString();
            int lvl = r.ReadByte(), diff = r.ReadByte();
            mem.inRun = r.ReadBoolean();
            // уровень и сложность выбирает хозяин комнаты
            if (from == Coop.HostSlot && Coop.IsGuest && lvl < Levels.Length) { selLevel = lvl; SetDiffByte(diff); }
            return;
        }
        if (t == 0x11)
        {
            if (from != Coop.HostSlot || Coop.IsHost) return;
            // гость без входа в забег не идёт (его забег не засчитался бы): выходит из комнаты, остаётся на экране входа
            if (LoginGate) { Coop.Close(); return; }
            // хозяин начал заново посреди забега — у гостя он засчитывается здесь, до смены уровня и сложности
            if (coopRun && !freeRun && runGold < 0 && time >= 5 && state != State.Dead && state != State.Won && state != State.Menu && state != State.Town)
            { SaveRecords(false); AwardGold(false); }
            Coop.runId = r.ReadByte();
            selLevel = Mathf.Min(r.ReadByte(), Levels.Length - 1);
            SetDiffByte(r.ReadByte());
            Coop.seed = r.ReadInt32();
            Coop.starting = true;
            Coop.runHost = from;
            Debug.Log($"coop: start run {Coop.runId} level {selLevel}");
            if (state != State.Menu && state != State.Dead && state != State.Won && state != State.Town && !coopRun) SendTelemetry("coop");
            skipMenu = true;
            leaving = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }
        if (r.ReadByte() != Coop.runId || !coopRun) return;
        switch (t)
        {
            case 0x12:
                // новый маг в забеге мог пропустить появление врагов — следующий снимок ключевой
                if (MateAt(from) == null) { mates.Add(BuildMate(r, from)); snapKeyDue = true; }
                break;
            case 0x13:
            {
                byte kind = r.ReadByte();
                int idx = r.ReadInt16(), arg = r.ReadInt16();
                ApplyAct(MateAt(from), kind, idx, arg);
                break;
            }
            case 0x14: if (!Guest) ReadState(MateAt(from), r); break;
            case 0x15: if (Guest && from == Coop.runHost) ReadSnap(r); break;
            case 0x16: OnEvent(r, from); break;
        }
    }

    void OnEvent(BinaryReader r, int from)
    {
        byte kind = r.ReadByte();
        bool fromHost = from == Coop.runHost;
        var who = MateAt(from);
        switch (kind)
        {
            case 1: if (fromHost) { var title = r.ReadString(); var sub = r.ReadString(); Banner(title, sub.Length > 0 ? sub : null); } break;
            case 2: if (fromHost) pendingLevels += r.ReadByte(); break;
            // сундук и перо феникса адресованы одному магу: следом идёт его слот
            case 3: if (fromHost && r.ReadByte() == Coop.slot) pendingChests.Add(r.ReadByte()); break;
            case 4:
            {
                if (!fromHost) break;
                bool won = r.ReadBoolean();
                if (state == State.Dead || state == State.Won) break;
                state = won ? State.Won : State.Dead;
                SaveRecords(won);
                AwardGold(won);
                if (!won) player.anim.CrossFade("Death", 0.1f);
                break;
            }
            case 5: if (who != null && !who.gone && !who.down) { Use(who); Land(); Use(me); } break;
            case 6:
            {
                var c = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                if (who != null && !who.gone) { Use(who); PickWave(c); Use(me); }
                break;
            }
            // маг ушёл из забега в меню, оставшись в комнате
            case 7: MageLeft(from); break;
            case 8: if (fromHost && r.ReadByte() == Coop.slot) { player.revives--; screenFlash = 1; } break;
            case 9: RunSay(who != null ? who.nick : Coop.lobby[from]?.nick ?? "Маг", r.ReadString(), false); break;
            case 11: { var p = new Vector2(r.ReadSingle(), r.ReadSingle()); float rr = r.ReadSingle(); if (fromHost && Guest) { slams.Add((p, SlamDelay, rr, 0)); Vfx.Decal(p, rr * 2, new Color(0.9f, 0.1f, 0.08f, 0.55f), SlamDelay, false); } break; }
            case 10: { var p = new Vector2(r.ReadSingle(), r.ReadSingle()); var v = new Vector2(r.ReadSingle(), r.ReadSingle()); if (fromHost && Guest) FireBolt(p, v, 0); break; }
        }
    }

    // ---------- Гость → хозяин: где я ----------

    void SendState()
    {
        var w = RunMsg(0x14);
        w.Write(player.pos.x); w.Write(player.pos.y);
        w.Write(player.face.x); w.Write(player.face.y);
        w.Write(player.moving); w.Write(JumpH); w.Write((byte)state);
        Flush();
    }

    void ReadState(Mage m, BinaryReader r)
    {
        if (m == null) return;
        m.netPos = new Vector2(r.ReadSingle(), r.ReadSingle());
        m.netFace = new Vector2(r.ReadSingle(), r.ReadSingle());
        m.netMoving = r.ReadBoolean();
        m.air = r.ReadSingle();
        m.netState = (State)r.ReadByte();
    }

    // ---------- Хозяин → гость: снимок мира ----------

    static short Q(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 32), -32767, 32767);

    int TypeIdx(EnemyType t)
    {
        int i = System.Array.IndexOf(types, t);
        if (i >= 0) return i;
        int s = System.Array.IndexOf(Specials, t);
        if (s >= 0) return types.Length + 3 + s;
        return t == elite ? types.Length : t == boss1 ? types.Length + 1 : types.Length + 2;
    }

    EnemyType TypeAt(int i) => i < types.Length ? types[i] : i == types.Length ? elite : i == types.Length + 1 ? boss1 : i == types.Length + 2 ? boss2
        : i - types.Length - 3 < Specials.Length ? Specials[i - types.Length - 3] : boss2;

    void SendSnap(bool full)
    {
        var w = RunMsg(0x15);
        float now = Time.unscaledTime, tickDt = Mathf.Max(0.02f, now - snapTickAt);
        snapTickAt = now;
        // ключевой снимок перечисляет всё целиком (и врагов, и кристаллы): гость убирает то, чего в нём нет
        bool key = snapKeyDue || now > snapKeyAt;
        if (key) { snapKeyDue = false; snapKeyAt = now + KeyEvery; full = true; }
        w.Write(full);
        w.Write(time); w.Write((byte)state); w.Write(WorldRuns);
        w.Write((short)level); w.Write(xp); w.Write(xpNext); w.Write(kills);
        var a = me.player.pos;
        w.Write(a.x); w.Write(a.y);
        // все маги забега: слот, где стоит и куда смотрит, здоровье, пал ли, щит героя
        var all = All;
        w.Write((byte)all.Count);
        foreach (var m in all)
        {
            var p = m.player;
            w.Write((byte)(m == me ? Coop.slot : m.slot));
            w.Write(p.pos.x); w.Write(p.pos.y); w.Write(p.face.x); w.Write(p.face.y); w.Write(p.moving); w.Write(m == me ? JumpH : m.air);
            w.Write(p.hp); w.Write(p.maxHp); w.Write(m.down); w.Write(m.reviveT);
            w.Write(m == cur ? heroShield : m.heroShield);
        }
        w.Write(elixirUntil); w.Write(shieldUntil); w.Write(windsUntil); w.Write(clockUntil); w.Write(rageUntil);
        w.Write(ringOn); w.Write(ringCenter.x); w.Write(ringCenter.y); w.Write(ringR);
        w.Write(bossShown != null && bossShown.alive ? bossShown.id : -1);

        // Враги — только изменения. Сокет надёжный и упорядоченный, поэтому базой служит предыдущий снимок без
        // подтверждений. Гость ведёт каждого врага сам: от присланной точки с присланной скоростью; хозяин считает
        // ту же прямую и шлёт поправку, когда настоящий враг ушёл от неё дальше SnapDrift. Здоровье и флаги — когда
        // поменялись, умершие — списком номеров. Раз в SnapRound снимков каждый враг уходит целиком (так его увидит
        // и тот, кто пропустил появление), раз в KeyEvery секунд — ключевой снимок со всеми: гость убирает лишних.
        long headEnd = ms.Length;
        snapBuf.SetLength(0);
        int n = 0;
        foreach (var e in enemies)
        {
            if (!e.alive) continue;
            var vel = e.sentKnown ? Vector2.ClampMagnitude((e.pos - e.tickPos) / tickDt, 15) : Vector2.zero;
            e.tickPos = e.pos;
            byte hp = (byte)Mathf.Clamp(Mathf.CeilToInt(e.hp / e.maxHp * 255), 1, 255);
            int f = (e.frozen > 0 ? 1 : 0) | (e.slow > 0 ? 2 : 0) | (e.stun > 0 ? 4 : 0) | (e.charged ? 8 : 0) | (e.rage ? 16 : 0)
                  | (e.spawnLock > 0 ? 32 : 0) | (e.attackCd > 1.05f ? 64 : 0);
            int mask = 0;
            if (!e.sentKnown || key || (e.id + snapCount) % SnapRound == 0) mask = 15;
            else
            {
                // где враг сейчас у гостя: замороженный и встающий из земли стоят на месте
                var pred = (e.sentFlags & 33) != 0 ? e.sentPos : e.sentPos + e.sentVel * (now - e.sentAt);
                if ((pred - e.pos).sqrMagnitude > SnapDrift * SnapDrift) mask |= 1;
                if (Mathf.Abs(hp - e.sentHp) >= 6) mask |= 2;
                if (f != e.sentFlags) mask |= 5;
            }
            if (mask == 0) continue;
            if (!e.sentKnown) sentList.Add((e, e.id));
            e.sentKnown = true;
            n++;
            snapW.Write((ushort)e.id); snapW.Write((byte)mask);
            if ((mask & 8) != 0) { snapW.Write((byte)TypeIdx(e.type)); snapW.Write((byte)e.tier); }
            if ((mask & 1) != 0)
            {
                short qx = Q(e.pos.x - a.x), qy = Q(e.pos.y - a.y);
                sbyte vx = (sbyte)Mathf.Clamp(Mathf.RoundToInt(vel.x * 8), -127, 127), vy = (sbyte)Mathf.Clamp(Mathf.RoundToInt(vel.y * 8), -127, 127);
                snapW.Write(qx); snapW.Write(qy); snapW.Write(vx); snapW.Write(vy);
                // запоминаем ровно то, что получит гость (после округления)
                e.sentPos = a + new Vector2(qx, qy) / 32f; e.sentVel = new Vector2(vx, vy) / 8f; e.sentAt = now;
            }
            if ((mask & 2) != 0) { snapW.Write(hp); e.sentHp = hp; }
            if ((mask & 4) != 0) { snapW.Write((byte)f); e.sentFlags = (byte)f; }
        }
        snapW.Flush();
        w.Write(key); w.Write((ushort)n);
        w.Write(snapBuf.GetBuffer(), 0, (int)snapBuf.Length);
        // умершие с прошлого снимка (объект врага мог уже уйти в пул и вернуться под другим номером)
        deadIds.Clear();
        for (int i = sentList.Count - 1; i >= 0; i--)
        {
            var (e, id) = sentList[i];
            if (e.alive && e.id == id && e.sentKnown) continue;
            deadIds.Add(id);
            sentList[i] = sentList[sentList.Count - 1];
            sentList.RemoveAt(sentList.Count - 1);
        }
        w.Write((ushort)deadIds.Count);
        foreach (int id in deadIds) w.Write((ushort)id);
        bw.Flush();
        long enemyEnd = ms.Length;
        snapHead += (int)headEnd; snapEnemy += (int)(enemyEnd - headEnd);
        if (full)
        {
            // кристаллы лежат на месте: шлём только новые и список пропавших (в ключевом снимке — все)
            n = 0;
            foreach (var g in gems) if (g.alive && (key || !g.sentKnown || g.pulled)) n++;
            w.Write((ushort)n);
            foreach (var g in gems)
            {
                // летящий к магу кристалл шлём каждый раз: гость притяжение сам не считает
                if (!g.alive || !(key || !g.sentKnown || g.pulled)) continue;
                if (!g.sentKnown) sentGems.Add((g, g.id));
                g.sentKnown = true;
                w.Write((ushort)g.id); w.Write(Q(g.pos.x - a.x)); w.Write(Q(g.pos.y - a.y)); w.Write((byte)(g.heal ? 255 : g.tier));
            }
            deadIds.Clear();
            for (int i = sentGems.Count - 1; i >= 0; i--)
            {
                var (g, id) = sentGems[i];
                if (g.alive && g.id == id && g.sentKnown) continue;
                deadIds.Add(id);
                sentGems[i] = sentGems[sentGems.Count - 1];
                sentGems.RemoveAt(sentGems.Count - 1);
            }
            w.Write((ushort)deadIds.Count);
            foreach (int id in deadIds) w.Write((ushort)id);
            w.Write((byte)chests.Count);
            foreach (var c in chests) { w.Write((byte)c.id); w.Write(Q(c.pos.x - a.x)); w.Write(Q(c.pos.y - a.y)); w.Write((byte)c.rarity); }
            n = 0;
            foreach (var rl in relics) if (rl.alive) n++;
            w.Write((byte)n);
            foreach (var rl in relics)
            {
                if (!rl.alive) continue;
                w.Write((byte)rl.id); w.Write((byte)rl.kind); w.Write(Q(rl.pos.x - a.x)); w.Write(Q(rl.pos.y - a.y)); w.Write((byte)Mathf.Clamp(rl.t, 0, 255));
            }
        }
        bw.Flush();
        snapRest += (int)(ms.Length - enemyEnd);
        Flush();
    }

    readonly List<(Gem g, int id)> sentGems = new();
    static int snapHead, snapEnemy, snapRest;   // байты снимков по частям — в строку «coop:» журнала
    const float SnapDrift = 0.3f, KeyEvery = 5;
    const int SnapRound = 30;
    static readonly MemoryStream snapBuf = new();
    static readonly BinaryWriter snapW = new(snapBuf);
    readonly List<(Enemy e, int id)> sentList = new();
    readonly List<int> deadIds = new();
    float snapTickAt, snapKeyAt;
    bool snapKeyDue = true;

    readonly Dictionary<int, Enemy> netEnemies = new();
    readonly Dictionary<int, Gem> netGems = new();
    readonly List<int> netDrop = new();
    float lastSnapAt;
    static readonly float[] GemValues = { 1, 3, 10, 40, 100 };

    void ReadSnap(BinaryReader r)
    {
        float now = Time.unscaledTime;
        if (lastSnapAt > 0) tGap = Mathf.Max(tGap, now - lastSnapAt);
        lastSnapAt = now;
        hostLinked = true;
        bool full = r.ReadBoolean();
        time = r.ReadSingle();
        hostState = (State)r.ReadByte();
        netRunning = r.ReadBoolean();
        level = r.ReadInt16(); xp = r.ReadSingle(); xpNext = r.ReadSingle(); kills = r.ReadInt32();
        var a = new Vector2(r.ReadSingle(), r.ReadSingle());
        // маги: про себя берём только здоровье и «пал», про остальных — ещё и где они
        int nm = r.ReadByte();
        for (int k = 0; k < nm; k++)
        {
            int sl = r.ReadByte();
            var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
            var face = new Vector2(r.ReadSingle(), r.ReadSingle());
            bool moving = r.ReadBoolean();
            float air = r.ReadSingle();
            float hp = r.ReadSingle(), maxHp = r.ReadSingle();
            bool down = r.ReadBoolean();
            float rev = r.ReadSingle(), shield = r.ReadSingle();
            var m = sl == Coop.slot ? me : MateAt(sl);
            if (m == null || m.gone) continue;
            if (m == me && hp < player.hp - 0.5f) { hurtFx = Mathf.Max(hurtFx, 0.6f); player.hurt = 0.15f; }
            m.player.hp = hp; m.player.maxHp = maxHp; m.reviveT = rev;
            if (m == me) heroShield = shield;
            else { m.heroShield = shield; m.netPos = pos; m.netFace = face; m.netMoving = moving; m.air = air; }
            SetDown(m, down);
        }
        elixirUntil = r.ReadSingle(); shieldUntil = r.ReadSingle(); windsUntil = r.ReadSingle(); clockUntil = r.ReadSingle(); rageUntil = r.ReadSingle();
        ringOn = r.ReadBoolean(); ringCenter = new Vector2(r.ReadSingle(), r.ReadSingle()); ringR = r.ReadSingle();
        int boss = r.ReadInt32();

        seenStamp++;
        bool key = r.ReadBoolean();
        int n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadUInt16(), mask = r.ReadByte();
            int ti = 0, tier = 0, hp = 0, f = 0;
            Vector2 pos = default, vel = default;
            if ((mask & 8) != 0) { ti = r.ReadByte(); tier = r.ReadByte(); }
            if ((mask & 1) != 0) { pos = a + new Vector2(r.ReadInt16(), r.ReadInt16()) / 32f; vel = new Vector2(r.ReadSByte(), r.ReadSByte()) / 8f; }
            if ((mask & 2) != 0) hp = r.ReadByte();
            if ((mask & 4) != 0) f = r.ReadByte();
            if (!netEnemies.TryGetValue(id, out var e) || !e.alive)
            {
                // про незнакомого врага нужна полная запись; обрывок (пропустили появление) ждёт следующей полной
                if (mask != 15) continue;
                e = SpawnEnemy(TypeAt(ti), pos, tier, (f & 32) != 0);
                e.id = id;
                netEnemies[id] = e;
            }
            if ((mask & 1) != 0) { e.netPos = pos; e.netVel = vel; }
            if ((mask & 2) != 0) { e.maxHp = 1; e.hp = hp / 255f; }
            if ((mask & 4) != 0) NetFlags(e, f);
            e.netSeen = seenStamp;
        }
        netDrop.Clear();
        // ключевой снимок перечисляет всех: кого в нём нет — того нет и в мире
        if (key) foreach (var kv in netEnemies) if (kv.Value.netSeen != seenStamp || !kv.Value.alive) netDrop.Add(kv.Key);
        int dead = r.ReadUInt16();
        for (int i = 0; i < dead; i++) { int id = r.ReadUInt16(); if (netEnemies.ContainsKey(id) && !netDrop.Contains(id)) netDrop.Add(id); }
        foreach (var id in netDrop)
        {
            var e = netEnemies[id];
            netEnemies.Remove(id);
            if (!e.alive) continue;
            e.alive = false;
            if (e.type.bomber) BomberFx(e.pos);
            KillFx(e);
        }
        bossShown = boss >= 0 && netEnemies.TryGetValue(boss, out var b) ? b : null;
        if (!full) return;

        n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadUInt16();
            var pos = a + new Vector2(r.ReadInt16(), r.ReadInt16()) / 32f;
            int tier = r.ReadByte();
            if (!netGems.TryGetValue(id, out var g) || !g.alive)
            {
                g = MakeGem(pos, tier == 255 ? 0 : GemValues[Mathf.Min(tier, 4)], tier == 255);
                g.id = id;
                netGems[id] = g;
            }
            g.netPos = pos;
            g.netSeen = seenStamp;
        }
        netDrop.Clear();
        if (key) foreach (var kv in netGems) if (kv.Value.netSeen != seenStamp || !kv.Value.alive) netDrop.Add(kv.Key);
        int deadGems = r.ReadUInt16();
        for (int i = 0; i < deadGems; i++) { int id = r.ReadUInt16(); if (netGems.ContainsKey(id) && !netDrop.Contains(id)) netDrop.Add(id); }
        foreach (var id in netDrop)
        {
            var g = netGems[id];
            netGems.Remove(id);
            if (!g.alive) continue;
            g.alive = false;
            Vfx.Emit(Vfx.Glow, World.W(g.pos, 0.6f), Vector3.zero, 0.8f, 0.2f, g.heal ? new Color(1f, 0.3f, 0.35f) : new Color(0.4f, 0.7f, 1f));
        }

        n = r.ReadByte();
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadByte();
            var pos = a + new Vector2(r.ReadInt16(), r.ReadInt16()) / 32f;
            int rar = r.ReadByte();
            var c = chests.Find(x => x.id == id);
            if (c == null) { c = SpawnChest(pos, rar); if (c == null) continue; c.id = id; }
            c.netSeen = seenStamp;
        }
        for (int i = chests.Count - 1; i >= 0; i--)
            if (chests[i].netSeen != seenStamp) { ChestFx(chests[i]); chests.RemoveAt(i); }

        n = r.ReadByte();
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadByte(), kind = r.ReadByte();
            var pos = a + new Vector2(r.ReadInt16(), r.ReadInt16()) / 32f;
            float t = r.ReadByte();
            seen.Add(id);
            var rl = relics.Find(x => x.alive && x.id == id) ?? SpawnRelic(kind, pos);
            rl.id = id; rl.t = t;
        }
        foreach (var rl in relics)
            if (rl.alive && !seen.Contains(rl.id))
            {
                rl.alive = false;
                rl.go.SetActive(false);
                Vfx.Emit(Vfx.Ring, World.W(rl.pos, 0.15f), Vector3.zero, 5, 0.5f, RelicColors[rl.kind]);
            }
    }

    void NetFlags(Enemy e, int f)
    {
        bool changed = false;
        bool frozen = (f & 1) != 0, slow = (f & 2) != 0, stun = (f & 4) != 0, charged = (f & 8) != 0, rage = (f & 16) != 0;
        if (frozen != e.frozen > 0) { e.frozen = frozen ? 99 : 0; e.anim.speed = frozen ? 0 : 1; changed = true; }
        if (slow != e.slow > 0) { e.slow = slow ? 99 : 0; changed = true; }
        e.stun = stun ? 99 : 0;
        if (charged != e.charged) { e.charged = charged; changed = true; }
        if (rage != e.rage)
        {
            e.rage = rage;
            float k = rage ? 1.2f : 1 / 1.2f;
            e.tr.localScale *= k; e.radius *= k;
            changed = true;
        }
        e.spawnLock = (f & 32) != 0 ? 1 : 0;
        bool atk = (f & 64) != 0;
        if (atk && !e.netAtk && e.frozen <= 0) e.anim.CrossFade("Attack", 0.05f);
        e.netAtk = atk;
        if (changed) Visual(e);
    }

    // ---------- Мир у гостя: тот же кадр, что у хозяина, но без своей логики ----------

    void GuestStep(float dt)
    {
        time += dt;
        WatchFeather();   // воскрешение гостю приходит от хозяина — перо из запаса тратится и здесь
        TelemetryTick(dt);
        if (!me.down) MoveMe(dt);
        MatesTick(dt);
        Ambient(dt, player.moving);
        UpdateStage();
        GuestEnemies(dt);
        BuildGrid();
        TickMages(dt);
        UpdateMeteors(dt);
        UpdateProjs(dt);
        UpdateBolts(dt);
        UpdateSlams(dt);
        UpdatePools(dt);
        foreach (var g in gems)
        {
            if (!g.alive) continue;
            g.pos = Vector2.Lerp(g.pos, g.netPos, 1 - Mathf.Exp(-15 * dt));
            GemPose(g);
        }
        UpdateChests(dt);
        UpdateLoot(dt);
        ringLine.gameObject.SetActive(ringOn);
        if (ringOn) DrawRing();
        Sweep();
        World.Follow(ViewPos);
        SetXRay(World.Occluded(player.pos));
    }

    void GuestEnemies(float dt)
    {
        float k = 1 - Mathf.Exp(-12 * dt);
        // хозяин меряет скорость и прогноз настоящим временем — гость ведёт врагов им же, а не игровым шагом
        // (тот ограничен 1/30 с и замирает на хитстопе: при низком FPS враги отставали бы от своих настоящих мест)
        float real = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
        foreach (var e in enemies)
        {
            if (!e.alive) continue;
            // ведём врага по прямой, которую прислал хозяин (он считает ту же и поправит, когда враг с неё сойдёт);
            // снимков нет дольше полусекунды — стоим, чтобы толпа не убежала по прямым
            if (e.frozen <= 0 && e.spawnLock <= 0 && Time.unscaledTime - lastSnapAt < 0.5f) e.netPos += e.netVel * real;
            e.pos = Vector2.Lerp(e.pos, e.netPos, k);
            if (e.flash > 0) { e.flash -= dt; Visual(e); }
            if (e.stun > 0 && Random.value < 0.1f)
                Vfx.Emit(Vfx.Spark, World.W(e.pos, 1.2f * e.type.scale), Random.onUnitSphere * 2, 0.15f, 0.2f, new Color(0.7f, 0.85f, 1f));
            e.tr.position = World.W(e.pos);
            var vp = cam.WorldToViewportPoint(e.tr.position);
            bool vis = vp.z > 0 && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.25f && vp.y < 1.2f;
            if (e.anim.enabled != vis) e.anim.enabled = vis;
            var look = World.W(TargetOf(e.pos) - e.pos);
            if (look.sqrMagnitude > 0.01f && e.spawnLock <= 0 && e.frozen <= 0)
                e.tr.rotation = Quaternion.Slerp(e.tr.rotation, Quaternion.LookRotation(look), dt * 8);
        }
    }

    // ---------- Старт и выход ----------

    // хозяин жмёт «Играть»: общий сид и номер забега уходят другу, обе сцены грузятся заново
    void CoopBegin()
    {
        if (Levels[selLevel].hidden && !SiegeOpen) selLevel = 0;   // «Ещё раз» пати после осады: окно закрыто — кладбище всем
        Coop.runId++;
        Coop.seed = Random.Range(1, int.MaxValue);
        Coop.starting = true;
        Coop.runHost = Coop.slot;
        var w = Msg(0x11);
        w.Write(Coop.runId); w.Write((byte)selLevel); w.Write(DiffByte); w.Write(Coop.seed);
        Flush();
        Debug.Log($"coop: begin run {Coop.runId}");
        skipMenu = true;
        leaving = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // забег начался в новой сцене: рассказываем другу о своём маге
    void CoopRunStart()
    {
        Coop.starting = false;
        player.pos = SlotPos[Coop.slot % SlotPos.Length];
        player.tr.position = World.W(player.pos);
        if (!Guest)
        {
            waitingMate = Coop.peer;
            waitUntil = Time.unscaledTime + 20;
        }
        SendInit();
    }

    // уйти из сетевого забега в меню: друг узнаёт, комната остаётся
    void CoopLeaveRun()
    {
        if (!coopRun) return;
        SendEvent(7);
    }

    // ---------- Вход по ссылке и тестовый запуск ----------

    void CoopUrl()
    {
        if (urlChecked) return;
        urlChecked = true;
        var url = Application.absoluteURL ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(url, "[?&]room=(\\d{4})");
        if (!m.Success) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        BrowserDropParam("room");
#endif
        // ссылка на собственную комнату открывает её хозяином, чужая — входом
        // с отладочным ?key= вкладка — отдельный игрок, даже если код совпал с сохранённым «моим»
        Coop.Connect(m.Groups[1].Value == Coop.MyCode && !url.Contains("key="), m.Groups[1].Value);
        menuTab = 6;
    }

    // -coop host:1234 | join:1234, -coopUrl ws://…, -autoplay — два окна на одной машине
    void CoopArgs()
    {
        if (argsChecked) return;
        argsChecked = true;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-autoplay") { auto = true; Coop.autoStart = true; }
            if (args[i] == "-coopUrl" && i + 1 < args.Length) Coop.urlOverride = args[i + 1];
            if (args[i] == "-key" && i + 1 < args.Length) Coop.keyOverride = args[i + 1];
            if (args[i] == "-nick" && i + 1 < args.Length) { PlayerPrefs.SetString("nick", args[i + 1]); }
            if (args[i] == "-shots" && i + 1 < args.Length) shotDir = args[i + 1];
            // замер производительности одному: -autoplay -solo [-timescale 2] — в логе строки «perf:» раз в 10 с
            if (args[i] == "-solo") soloStart = true;
            if (args[i] == "-fallen" && i + 1 < args.Length) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fallenAtArg);
            if (args[i] == "-timescale" && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ts)) Time.timeScale = ts;
        }
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-coop")
            {
                var p = args[i + 1].Split(':');
                Coop.Connect(p[0] == "host", p.Length > 1 ? p[1] : "");
            }
    }

    // ---------- Экран «Вместе» ----------

    string joinCode = "";
    bool joinPrefilled;

    void DrawCoop(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.9f));
        MenuHeader(W, 40, "ИГРА ВМЕСТЕ ПО СЕТИ", "Вместе", Gold);
        float bw = Mathf.Min(620, W - 60), x = (W - bw) / 2, y = 220;
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        var sock = Coop.sock;
        bool connecting = sock != null && !sock.Open && !sock.Dead;

        if (!Coop.InRoom)
        {
            Text(new Rect(x, y, bw, 120), "Создай комнату и отправь друзьям код или ссылку — в комнате до четырёх магов. Уровень и сложность выбирает хозяин, героя — каждый свой. Опыт общий, павшего можно поднять, если постоять рядом.",
                Col(ds, Soft(0.85f), TextAnchor.UpperCenter), 2);
            y += 140;
            if (Btn(new Rect(x, y, bw, 96), Coop.MyCode.Length == 4 ? "Моя комната " + Coop.MyCode : "Создать комнату", Gold)) Coop.Connect(true, Coop.MyCode);
            if (joinCode.Length == 0 && !joinPrefilled) { joinPrefilled = true; joinCode = PlayerPrefs.GetString("coop.last"); }
            y += 116;
            var cr = new Rect(x, y, bw * 0.55f, 96);
            RR(cr, new Color(0.03f, 0.025f, 0.04f, 0.96f), 12);
            RRLine(cr, new Color(Gold.r, Gold.g, Gold.b, 0.5f), 1, 12);
#if UNITY_WEBGL && !UNITY_EDITOR
            Outline(cr, joinCode.Length > 0 ? joinCode : "<color=#77736C>код</color>", Col(heavy, Bone), 2);
            if (GUI.Button(cr, GUIContent.none, GUIStyle.none)) PhoneInput("Код комнаты (4 цифры)", 4, t => joinCode = Digits(t), joinCode, 2);
#else
            var fs = new GUIStyle(heavy) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Bone }, focused = { textColor = Bone } };
            joinCode = Digits(GUI.TextField(cr, joinCode, 4, fs));
#endif
            if (Btn(new Rect(cr.xMax + 16, y, bw - cr.width - 16, 96), "Войти", Bone) && joinCode.Length == 4) Coop.Connect(false, joinCode);
            y += 116;
            if (Coop.error != null) Text(new Rect(x, y, bw, 40), Coop.error, Col(small, new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleCenter), 2);
        }
        else if (connecting || Coop.code == null)
        {
            Text(new Rect(x, y + 60, bw, 50), Coop.status ?? "Подключаюсь…", Col(small, Bone, TextAnchor.MiddleCenter), 2);
        }
        else
        {
            ChipAt(W / 2, y, "КОД КОМНАТЫ", Gold);
            Outline(new Rect(0, y + 34, W, 120), Coop.code, Col(big, new Color(1f, 0.88f, 0.62f)), 3);
            y += 170;
            if (Coop.IsHost && Btn(new Rect(x, y, bw, 84), "Поделиться ссылкой", Gold)) ShareLink();
            if (Coop.IsHost) y += 104;
            string who = Coop.peer && Coop.mateNick != null
                ? $"В комнате ({Coop.Peers + 1} из {Coop.Size}): <color=#FFD45E>{Coop.mateNick}</color>"
                : Coop.IsHost ? "Ждём друзей… отправь им код или ссылку" : "Соединяемся с хозяином…";
            Text(new Rect(x, y, bw, 40), who, Shrink(Col(small, Bone, TextAnchor.MiddleCenter), who, bw), 2);
            y += 50;
            string hint = Coop.IsHost
                ? (Coop.peer ? "Выбери уровень и жми «Играть вдвоём»" : "Пока друг не пришёл, можно играть одному")
                : Coop.mateInRun ? "Хозяин сейчас в забеге — подожди его конца" : "Хозяин выбирает уровень — забег начнётся сам";
            Text(new Rect(x, y, bw, 60), hint, Col(ds, Soft(0.75f), TextAnchor.UpperCenter), 2);
            y += 80;
            if (Btn(new Rect(x, y, bw, 76), "Выйти из комнаты", new Color(0.8f, 0.3f, 0.25f))) { Coop.Close(); Coop.reconnectAt = 0; }
        }
        float bb = Mathf.Min(460, W - 80);
        if (Btn(new Rect((W - bb) / 2, H - 104, bb, 84), "Назад", Bone)) menuTab = 0;
    }

    static string Digits(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s ?? "") if (ch >= '0' && ch <= '9' && sb.Length < 4) sb.Append(ch);
        return sb.ToString();
    }

    static string HeroTitle(string id)
    {
        foreach (var h in Heroes) if (h.id == id) return h.title;
        return "Маг";
    }

    void ShareLink()
    {
        var link = Coop.Link();
#if UNITY_WEBGL && !UNITY_EDITOR
        BrowserShare($"Заходи в Magic Survivors вместе со мной, комната {Coop.code}", link);
#else
        GUIUtility.systemCopyBuffer = link;
#endif
        Coop.status = "Ссылка скопирована";
    }

    // плашка комнаты над главным меню
    void CoopMenuChip(float W, float y)
    {
        if (!Coop.InRoom) return;
        string s = Coop.peer && Coop.mateNick != null ? $"ВМЕСТЕ С {Up(Coop.mateNick)}" : Coop.code != null ? $"КОМНАТА {Coop.code} · ЖДЁМ ДРУГА" : "ПОДКЛЮЧЕНИЕ…";
        var r = ChipAt(W / 2, y, s, new Color(0.6f, 1f, 0.7f), Coop.peer);
        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) menuTab = 6;
    }

    // ---------- Чат в забеге ----------
    // Вдвоём можно переписываться прямо в бою: T (или кнопка «Чат») открывает поле, Enter отправляет, Esc закрывает.
    // Сообщение идёт другу событием 9 через ту же комнату. На телефоне — системное окно ввода.

    readonly List<(string line, float at)> runChat = new();
    bool runChatOn, runChatSend;
    string runChatDraft = "";

    bool Typing => chatFocus || runChatOn || phoneInput || noteOpen;

    void RunSay(string nick, string text, bool mine)
    {
        text = (text ?? "").Replace('<', '‹').Replace('>', '›').Trim();
        if (text.Length == 0) return;
        if (text.Length > 160) text = text.Substring(0, 160);
        runChat.Add(($"<color=#{(mine ? "9FE8A8" : "FFD45E")}>{nick.Replace('<', '‹')}</color>  {text}", Time.unscaledTime));
        if (runChat.Count > 6) runChat.RemoveAt(0);
    }

    void RunChatOpen()
    {
        if (!(coopRun && AnyMate) && !SoloChat) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        if (TouchUi) { PhoneInput("Сообщение в поход", 160, RunChatSendText); return; }
#endif
        runChatOn = true;
        runChatDraft = "";
    }

    void RunChatSendText(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 160) text = text.Substring(0, 160);
        if (runToAll || !coopRun) { GlobalSay(text); return; }   // всем — эхо придёт от сервера с меткой [все]; в одиночке писать больше некому
        RunSay(Nick, text, true);
        var w = RunMsg(0x16); w.Write((byte)9); w.Write(text); Flush();
    }

    void DrawRunChat(float W, float H)
    {
        bool live = coopRun ? AnyMate : SoloChat;   // не вошёл — сервер ответит «войди», ответ встанет в ленту
        chatS ??= new GUIStyle(tiny) { fontSize = 21, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false, richText = true };
        float now = Time.unscaledTime, cw = Mathf.Min(520, W - 32), y = Portrait ? 196 : H * 0.36f;
        // свежие реплики слева; старше 12 секунд гаснут, при открытом поле видны все
        foreach (var (line, at) in runChat)
        {
            // свежие 12 с; последняя строка — ещё минуту, чтобы было видно, что чат живой
            if (!runChatOn && now - at > (line == runChat[runChat.Count - 1].line ? 60 : 12)) continue;
            var r = new Rect(16, y, cw, 30);
            RR(r, new Color(0.02f, 0.015f, 0.03f, 0.6f), 8);
            Text(new Rect(r.x + 10, r.y, r.width - 20, 30), line, Col(chatS, Bone, TextAnchor.MiddleLeft), 2);
            y += 34;
        }
        if (!live || state != State.Play) { runChatOn = false; return; }
        if (!runChatOn)
        {
            // кнопка маленькая и в стороне от джойстика; на ПК ещё и клавиша T
            var br = new Rect(16, Portrait ? 150 : H * 0.36f - 50, 110, 42);
            if (Btn(br, TouchUi ? "Чат" : "Чат · T", Bone)) RunChatOpen();
            // кому пишем: своим в поход или всем (город и другие походы) — только вошедшим, как и в городе
            if (LoggedIn && coopRun)
            {
                var tr = new Rect(br.xMax + 8, br.y, 130, 42);
                Chip(tr, runToAll ? "КОМУ: ВСЕМ" : "КОМУ: СВОИМ", runToAll ? new Color(0.56f, 0.83f, 1f) : Bone, runToAll);
                if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) runToAll = !runToAll;
            }
            return;
        }
        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { e.Use(); runChatSend = true; }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); runChatOn = false; GUI.FocusControl(null); return; }
        var fr = new Rect(16, y + 4, cw, 54);
        runChatDraft = Field(fr, runChatDraft, 160, "Enter — отправить, Esc — закрыть", "", "runchat");
        GUI.FocusControl("runchat");
        if (runChatSend)
        {
            runChatSend = false;
            RunChatSendText(runChatDraft);
            runChatOn = false;
            GUI.FocusControl(null);
        }
    }

    // ---------- HUD забега ----------

    float quickAt = -9;

    bool QuickBlocked(Vector2 sp)
    {
        if (!coopRun || !AnyMate) return false;
        float u = UiScale, W = Screen.width / u, H = Screen.height / u;
        var g = new Vector2(sp.x / u, (Screen.height - sp.y) / u);
        return new Rect(W - 216, H * 0.4f, 216, 3 * 64).Contains(g);
    }

    void CoopHud(float W, float H)
    {
        if (!coopRun) { DrawRunChat(W, H); return; }   // одиночный забег: только глобальный чат
        ChoiceButton(W, H);
        DrawRunChat(W, H);
        // «без голосового чата сложно координироваться» (фидбек): быстрые фразы одним нажатием
        if (state == State.Play && AnyMate && !me.down)
        {
            float qw = 200, qh = 56, qx = W - qw - 16, qy = H * 0.4f;
            string[] quick = { "За мной!", "К сундуку!", "Помогите!" };
            for (int i = 0; i < quick.Length; i++)
                if (Btn(new Rect(qx, qy + i * (qh + 8), qw, qh), quick[i], Bone) && Time.unscaledTime - quickAt > 1.5f)
                {
                    quickAt = Time.unscaledTime;
                    RunChatSendText(quick[i]);
                }
        }
        float y = Portrait ? 200 : 150;
        if (state == State.Play && !Running)
        {
            var other = hostState;
            if (!Guest)
            {
                other = State.Play;
                foreach (var m in mates) if (!m.gone && (m.netState == State.Pause || m.netState == State.Stats)) other = m.netState;
            }
            string s = Guest && !hostLinked ? "ПОДКЛЮЧАЕМСЯ К ПАТИ…"
                : !Guest && waitingMate ? "ЖДЁМ ОСТАЛЬНЫХ…"
                : other == State.LevelUp || other == State.Super ? "ДРУГ ВЫБИРАЕТ МАГИЮ…"
                : other == State.Chest ? "ДРУГ ОТКРЫВАЕТ СУНДУК…"
                : other == State.Pause ? "ДРУГ НА ПАУЗЕ"
                : other == State.Stats ? "ДРУГ СМОТРИТ СТАТЫ" : "ЖДЁМ ДРУГА…";
            ChipAt(W / 2, y, s, Gold, Mathf.Sin(Time.unscaledTime * 4) > 0);
        }
        if (me.down && state == State.Play)
        {
            Outline(new Rect(0, H * 0.58f, W, 80), "Ты пал", Col(big, new Color(1f, 0.55f, 0.45f)), 3);
            Text(new Rect(40, H * 0.58f + 80, W - 80, 40), FirstAlive != null ? "Любой маг пати поднимет тебя, если постоит рядом" : "Ждём конца забега",
                Col(small, Bone, TextAnchor.MiddleCenter), 2);
        }
    }

    // таблички магов пати над головой, полоса подъёма, стрелка у края экрана
    void CoopOverlay(float u, float W, float H)
    {
        if (!coopRun) return;
        if (me.down) ReviveBar(u, me);
        foreach (var mate in mates)
        {
            if (mate.gone) continue;
            var p = mate.player;
            bool front = ToGui(World.W(p.pos, 2.4f), u, out var g);
            float margin = 70;
            if (front && g.x > margin && g.x < W - margin && g.y > 120 && g.y < H - margin)
            {
                var green = new Color(0.55f, 1f, 0.6f);
                Outline(new Rect(g.x - 160, g.y - 44, 320, 36), mate.nick, Col(tiny, green, TextAnchor.MiddleCenter), 2);
                Rect(new Rect(g.x - 60, g.y - 6, 120, 12), new Color(0, 0, 0, 0.75f));
                Rect(new Rect(g.x - 58, g.y - 4, 116 * Mathf.Clamp01(p.hp / p.maxHp), 8), new Color(0.35f, 0.9f, 0.45f));
                if (mate.down) ReviveBar(u, mate);
                continue;
            }
            var ctr = new Vector2(W / 2, H / 2);
            var dir = g - ctr;
            if (!front) dir = -dir;
            if (dir.sqrMagnitude < 1) dir = Vector2.up;
            dir.Normalize();
            float sx = (W / 2 - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.x));
            float sy = (H / 2 - margin - 70) / Mathf.Max(0.001f, Mathf.Abs(dir.y));
            var e = ctr + dir * Mathf.Min(sx, sy);
            var c = mate.down ? new Color(1f, 0.5f, 0.4f) : new Color(0.55f, 1f, 0.6f);
            GUI.color = c;
            GUI.DrawTexture(new Rect(e.x - 14, e.y - 14, 28, 28), Art.Tex("circle_05"));
            GUI.color = Color.white;
            float dist = (p.pos - player.pos).magnitude;
            Outline(new Rect(e.x - 110, e.y + 14, 220, 40), mate.down ? $"ПОДНИМИ · {dist:0} м" : $"{Up(mate.nick)} · {dist:0} м", Col(caps, c, TextAnchor.MiddleCenter), 2);
        }
    }

    void ReviveBar(float u, Mage m)
    {
        if (!ToGui(World.W(m.player.pos, 0.2f), u, out var g)) return;
        Rect(new Rect(g.x - 80, g.y + 20, 160, 14), new Color(0, 0, 0, 0.8f));
        Rect(new Rect(g.x - 78, g.y + 22, 156 * Mathf.Clamp01(m.reviveT / ReviveTime), 10), new Color(0.55f, 1f, 0.6f));
        Outline(new Rect(g.x - 120, g.y + 36, 240, 34), "ВСТАНЬ РЯДОМ", Col(caps, new Color(0.7f, 1f, 0.75f), TextAnchor.MiddleCenter), 2);
    }
}
