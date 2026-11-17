using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Город: общая площадь, куда игрок попадает при входе в игру и после забега. По ней свободно бегают все, кто
// сейчас в игре, есть чат, на севере стоят порталы в данжи. В городе собирают пати до четырёх магов (нажать на
// мага или открыть список «Маги рядом»); лидер встаёт в круг портала и ведёт всех в поход — дальше это обычная
// комната кооператива (см. docs/town.md). В круге можно войти и одному.
// Сеть — тот же сокет, что у комнат (Coop.sock), кадры 0x09–0x0F разбирает server/town.py.
public static class Town
{
    public static bool home = true;       // куда возвращаться: в город или в классическое меню выбора уровня
    public static bool checkedArgs;
    public const float Radius = 19.5f, PortalR = 2.3f;
    public static readonly Vector2[] Portals = { new(-8, 9), new(0, 12), new(8, 9), new(-14.5f, 2.5f), new(14.5f, 2.5f) };
    // таверна — дом в юго-западном углу (стены и бочки — Levels.Town, мебель — GameTavern.cs). С 16.11 зал 13×11:
    // в прежнем 9×9 было тесно ходить, поэтому дом выходит за ограду — ограду, деревья и орду там убираем.
    public static readonly Rect TavernHouse = new(-18.5f, -16.1f, 13f, 11.2f);
    public static Vector2 TavernC => TavernHouse.center;
    public static bool InTavernHouse(Vector2 p) => TavernHouse.Contains(p);
    // круг доски «Ищу пати» — у западной стены, в стороне от двери: иначе окно открывалось на каждом входе
    public static readonly Vector2 Tavern = TavernHouse.center + new Vector2(-3.6f, -1.6f);
    public const float TavernR = 1.8f;
    public static bool meAdmin, meLfg;

    public class Guy
    {
        public int id, portal = 255, owned = 1;   // owned — какие порталы куплены (биты по номеру портала)
        public bool admin, lfg;                   // админ игры; «ищу пати» на доске таверны
        public string nick = "", hero = "mage";
        public Vector2 pos, netPos, face = Vector2.down;
        public bool moving;
        public GameObject go;
        public Transform tr;
        public Animator anim;
        public string say;
        public float sayUntil;
    }

    public static readonly Dictionary<int, Guy> guys = new();
    public static readonly List<string> chat = new();
    public static int myId;
    public static bool entered;
    public static float retryAt;
    public static bool replaced;   // аккаунт вошёл в город с другого устройства — сами не переподключаемся
    public static string status;
}

public partial class Game
{
    bool townScene;                 // эта сцена — город
    bool welcome;                   // стартовый экран: ник и вход
    string welcomeNick = "";
    float townSendAt, partyAt;
    int myPortal = 255, sentPortal = 255;
    Vector2 sentPos;
    string chatDraft = "", statusSeen, townToken = "";
    int townMask;
    bool inTavern, rejoinLater;
    bool chatFocus, partyGo, chatSend;
    static bool chatMinLoaded;
    // пати: состав по id в городе (первый — лидер, пусто — пати нет); кто позвал меня; кого и когда позвал я
    readonly List<int> townParty = new();
    readonly Dictionary<int, float> asked = new();
    readonly List<Rect> guyZones = new();   // области касания магов на площади (в координатах ввода)
    int invitedBy, partyNeed, guysPage, guyFirst;
    float invitedAt, statusAt;
    bool guysOpen;                          // открыт список магов города
    const float InviteTtl = 40;
    string mySay;
    float mySayUntil;
    Light[] portalLights;

    // Сигилы на полу города вместо тонких линий: текстура рун (Fx/sigil_*), аддитивно, цветом портала.
    // Вращаются, дышат, а когда маг внутри — ярче и быстрее
    class Sigil { public Transform tr; public Material mat; public Color color; public float spin, r, glow; public Vector2 at; public int portal; }
    readonly List<Sigil> sigils = new();
    static Mesh quadMesh;

    Transform FloorDecal(string tex, Vector2 at, float size, float y, out Material mat)
    {
        if (quadMesh == null)
        {
            var prim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadMesh = prim.GetComponent<MeshFilter>().sharedMesh;
            Destroy(prim);
        }
        var go = new GameObject("Sigil_" + tex);
        go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        var r = go.AddComponent<MeshRenderer>();
        mat = new Material(Resources.Load<Material>("Mats/Additive")) { mainTexture = Art.Tex(tex) };
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.transform.SetPositionAndRotation(World.W(at, y), Quaternion.Euler(90, 0, 0));
        go.transform.localScale = Vector3.one * size;
        return go.transform;
    }

    int PortalCount(int i)
    {
        int n = 0;
        foreach (var o in Town.guys.Values) if (o.portal == i) n++;
        return n;
    }

    void SigilTick(float dt)
    {
        foreach (var s in sigils)
        {
            bool inside = (player.pos - s.at).sqrMagnitude < s.r * s.r || (s.portal >= 0 && PortalCount(s.portal) > 0);
            s.glow = Mathf.MoveTowards(s.glow, inside ? 1 : 0, dt * 2.5f);
            float breathe = 0.85f + 0.15f * Mathf.Sin(time * 1.6f + s.at.x);
            s.tr.Rotate(0, 0, s.spin * (1 + 2 * s.glow) * dt, Space.Self);
            // закрытый (не купленный) портал тлеет втрое тусклее — сразу видно, куда можно идти
            float open = s.portal >= 0 && !Owns(Levels[s.portal].item) ? 0.35f : 1f;
            s.mat.SetColor("_Color", s.color * breathe * open * (1 + 1.2f * s.glow));
        }
    }
    GUIStyle chatS, fieldS;
    readonly List<Rect> townUi = new();   // прямоугольники интерфейса в координатах ввода: касание в них — не джойстик

    // город — стартовая сцена, если игру не запустили для теста и не пришли по ссылке в комнату друга
    static bool TownWanted()
    {
        if (!Town.checkedArgs)
        {
            Town.checkedArgs = true;
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-coop" || a == "-solo" || a == "-autoplay" || a == "-labFile" || a == "-batchmode") Town.home = false;
            if (System.Text.RegularExpressions.Regex.IsMatch(Application.absoluteURL ?? "", "[?&]room=\\d{4}")) Town.home = false;
        }
        return Town.home && !bot;
    }

    // ---------- Сцена ----------

    void TownStart()
    {
        state = State.Town;
        menuTab = 0;
        foreach (var s in skills) s.Show(false);
        player.pos = new Vector2(Random.Range(-2.5f, 2.5f), Random.Range(-6f, -3f));
        player.face = Vector2.up;
        player.tr.position = World.W(player.pos);
        portalLights = new Light[Town.Portals.Length];
        for (int i = 0; i < Town.Portals.Length; i++)
        {
            var c = Levels[i].color;
            // сигил портала: кольцо рун на полу (медленно вращается) и вихрь внутри навстречу; цвет — цвет уровня
            sigils.Add(new Sigil { tr = FloorDecal("sigil_portal", Town.Portals[i], Town.PortalR * 2.5f, 0.05f, out var m1), mat = m1, color = c * 1.6f, spin = 9, at = Town.Portals[i], r = Town.PortalR, portal = i });
            sigils.Add(new Sigil { tr = FloorDecal("twirl_01", Town.Portals[i], Town.PortalR * 1.7f, 0.06f, out var m2), mat = m2, color = c * 0.9f, spin = -55, at = Town.Portals[i], r = Town.PortalR, portal = i });
            var l = portalLights[i] = new GameObject("PortalLight" + i).AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = 9;
            l.intensity = 6;
            l.shadows = LightShadows.None;
            l.transform.position = World.W(Town.Portals[i], 1.6f);
        }
        if (GameObject.Find("TavernInside") == null) BuildTavern();   // стойка, бармен, бильярд (GameTavern.cs)
        // сигил таверны — огонь в венке, тёплый, как очаг
        sigils.Add(new Sigil { tr = FloorDecal("sigil_tavern", Town.Tavern, Town.TavernR * 2.5f, 0.05f, out var tm), mat = tm, color = new Color(2.2f, 1.1f, 0.35f), spin = 4, at = Town.Tavern, r = Town.TavernR, portal = -1 });
        // сокет, оставшийся от прошлой сцены (комната законченного похода), городу не нужен: входим заново
        Coop.Close();
        Coop.reconnectAt = 0;
        TownClear();
        Town.retryAt = 0;
        Town.replaced = false;
        Town.status = null;
        welcome = PlayerPrefs.GetInt("welcome.done") == 0 && PlayerPrefs.GetInt("rec.runs") == 0 && !LoggedIn
                  && !(Application.absoluteURL ?? "").Contains("ms_login=");
        welcomeNick = Nick;
        World.Follow(player.pos);
    }

    void TownClear()
    {
        foreach (var g in Town.guys.Values) if (g.go != null) Destroy(g.go);
        Town.guys.Clear();
        Town.entered = false;
        Town.meAdmin = Town.meLfg = false;
        sentPortal = 255;
        townParty.Clear(); asked.Clear();
        invitedBy = 0; guysOpen = false;
        foreach (var t in tmobs.Values) MobDie(t);
        tmobs.Clear();
        siegeOn = false; siegeDead = false; siegeBoard = null;
        if (player.go != null) player.go.SetActive(true);
        foreach (var f in fences) if (f != null) f.SetActive(true);
    }

    void TownStep(float dt)
    {
        time += dt;
        bool free = !welcome && menuTab == 0 && !chatFocus && confirm == null && !guysOpen && !phoneInput;
        var before = player.pos;
        if (free && PendingNews == null && !LoginGate) MoveMe(dt);   // под объявлением и экраном входа маг стоит
        else if (player.moving) { player.moving = false; player.anim.CrossFade("Idle", 0.12f); }
        if (player.pos.magnitude > Town.Radius && !Town.InTavernHouse(player.pos))   // в доме за оградой держат стены
        {
            // у стены дома проекция на круг вдавила бы мага в стену (стены и край площади спорят) — стоим, где были (ревью Codex)
            var nearHouse = new Rect(Town.TavernHouse.x - 1f, Town.TavernHouse.y - 1f, Town.TavernHouse.width + 2, Town.TavernHouse.height + 2);
            player.pos = nearHouse.Contains(player.pos) && before.magnitude <= Town.Radius + 0.01f ? before : player.pos.normalized * Town.Radius;
            player.tr.position = World.W(player.pos);
        }
        Ambient(dt, player.moving);
        World.Follow(player.pos);
        WorldTick();
        HordeTick(dt);

        bool wasTavern = inTavern;
        TavernTick(free);
        inTavern = free && !atBar && (player.pos - Town.Tavern).sqrMagnitude < Town.TavernR * Town.TavernR;
        if (inTavern && !wasTavern && leaders == null) LoadLeaders();
        myPortal = 255;
        for (int i = 0; i < Town.Portals.Length; i++)
        {
            var c = Levels[i].color;
            // «в круге» — значит готов идти: с открытым магазином или чатом в поход не зовут
            if (free && (player.pos - Town.Portals[i]).sqrMagnitude < Town.PortalR * Town.PortalR) myPortal = i;
            portalLights[i].intensity = (myPortal == i ? 8 : 5) + 2 * Mathf.Sin(time * 2 + i * 2);
            // искры поднимаются из круга
            if (Random.value < dt * 14)
            {
                float a = Random.value * Mathf.PI * 2;
                Vfx.Emit(Vfx.Ember, World.W(Town.Portals[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Town.PortalR * Random.Range(0.3f, 1f), 0.1f),
                    Vector3.up * Random.Range(1.5f, 3f), 0.16f, 1.1f, new Color(c.r * 2, c.g * 2, c.b * 2));
            }
        }
        SiegeTick(dt, free);
        SiegeTownTick(dt, free);

        SigilTick(dt);
        float k = 1 - Mathf.Exp(-10 * dt);
        foreach (var g in Town.guys.Values)
        {
            var d = g.netPos - g.pos;
            bool moving = d.sqrMagnitude > 0.03f;
            if (d.sqrMagnitude > 64) g.pos = g.netPos;
            else g.pos = Vector2.Lerp(g.pos, g.netPos, k);
            if (moving) g.face = d.normalized;
            if (moving != g.moving) { g.moving = moving; g.anim.CrossFade(moving ? "Run" : "Idle", 0.15f); }
            g.tr.position = World.W(g.pos);
            g.tr.rotation = Quaternion.Slerp(g.tr.rotation, Quaternion.LookRotation(World.W(g.face)), dt * 10);
        }

        float now = Time.unscaledTime;
        if (Town.entered && Coop.sock != null && Coop.sock.Open && !Coop.InRoom)
        {
            if (now > townSendAt && (player.pos - sentPos).sqrMagnitude > 0.0025f)
            {
                townSendAt = now + 0.12f;
                sentPos = player.pos;
                var w = Msg(0x0D); w.Write(player.pos.x); w.Write(player.pos.y); Flush();
            }
            if (myPortal != sentPortal)
            {
                sentPortal = myPortal;
                Coop.Send(new byte[] { 0x0F, (byte)myPortal });
            }
        }
        // лидер повёл пати, сервер рассадил всех по комнате — запускаем общий забег (кто не дошёл за полторы секунды, остаётся)
        if (partyGo && Coop.IsHost && Coop.peer && (Coop.Peers >= partyNeed || now > partyAt + 1.5f)) { partyGo = false; TownClear(); CoopBegin(); return; }
        if (partyGo && !Coop.InRoom && now > partyAt + 6) { partyGo = false; Town.status = "Поход не состоялся"; }
        if (invitedBy != 0 && (now > invitedAt + InviteTtl || !Town.guys.ContainsKey(invitedBy))) invitedBy = 0;
        // вошёл или вышел из аккаунта, стоя в городе, — заходим заново: сервер привязывает чат и ник к аккаунту
        // смена аккаунта — сразу, даже в пати: иначе чат шёл бы от имени прежнего аккаунта (ник и метка — из входа)
        if (Town.entered && !Coop.InRoom && townToken != AccToken) { townToken = AccToken; TownRejoin(true); }
        // купленные порталы узнаём после загрузки кошелька (и после покупки): остальным нужна свежая маска. В пати
        // не перезаходим — выход из города распустил бы её; маска уйдёт при следующем входе
        if (Town.entered && !Coop.InRoom && !InParty && !partyGo && townMask != OwnMask()) { townMask = OwnMask(); TownRejoin(); }
        if (rejoinLater && Town.entered && !InParty && !partyGo) TownRejoin();
        // сохранённый платный герой: при старте кошелёк ещё не загружен, и сцена построилась магом — теперь он куплен
        int saved = PlayerPrefs.GetInt("sel.hero", 0);
        if (menuTab == 0 && saved != selHero && saved >= 0 && saved < Heroes.Length && !Heroes[saved].soon && Owns(Heroes[saved].item) && !InParty && !partyGo && !Coop.InRoom)
        {
            selHero = saved;
            TownLeave();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }
        // подсказки и отказы сервера гаснут сами
        if (Town.status != statusSeen) { statusSeen = Town.status; statusAt = now; }
        else if (Town.status != null && Town.entered && now > statusAt + 6) Town.status = null;
    }

    // ---------- Общий счёт: онлайн и убитые за день ----------

    [System.Serializable] class WorldReply { public int online, kills, goal, left, sale, next, horde; public bool open; }
    static WorldReply world;
    static float worldAt;

    void WorldTick()
    {
        if (Time.unscaledTime < worldAt) return;
        worldAt = Time.unscaledTime + 30;
        StartCoroutine(Send("GET", "world", null, (code, body) => { if (code == 200) world = JsonUtility.FromJson<WorldReply>(body); }));
    }

    static string Thousands(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    // ---------- Сеть ----------

    void TownConnect()
    {
        Coop.Close();
        Coop.reconnectAt = 0;
        TownClear();
        Town.retryAt = Time.unscaledTime + 6;
        Coop.sock = new NetSocket(Coop.Url());
        Coop.keepAlive = Time.unscaledTime + 10;
        var info = string.Join("\t", Nick, builtHero, Wearing("hat"), Wearing("robe"), Wearing("staff"), Wearing("glow"), Coop.Proto.ToString(), OwnMask().ToString(), AccToken, WeaponInfo());
        townToken = AccToken;
        townMask = OwnMask();
        var b = System.Text.Encoding.UTF8.GetBytes(info);
        var op = new byte[b.Length + 1];
        op[0] = 0x0A;
        b.CopyTo(op, 1);
        Coop.pendingOp = op;
    }

    // сокета нет (первый вход, обрыв, вернулись из забега) — заходим в город заново
    void TownNetTick()
    {
        if (state != State.Town || welcome || partyGo || Coop.sock != null) return;
        if (Time.unscaledTime < Town.retryAt) return;
        TownConnect();
    }

    // ник или одежда сменились — входим заново, чтобы остальные увидели
    // В пати перезаход распустил бы её (сервер считает выход из города выходом из пати) — откладываем до выхода
    void TownRejoin(bool now = false)
    {
        if (state != State.Town || Coop.InRoom) return;
        if (!now && (InParty || partyGo)) { rejoinLater = true; return; }
        rejoinLater = false;
        if (Town.replaced) return;   // нас вытеснило другое устройство — без кнопки не возвращаемся
        Coop.Close();
        Town.retryAt = 0;
    }

    void TownLost()
    {
        Coop.Close();
        TownClear();
        Town.retryAt = Time.unscaledTime + 4;
        Town.status = "Нет связи с городом — пробую снова";
        partyGo = false;
    }

    int ReadGuy(byte[] m, int i)
    {
        if (i + 11 > m.Length) return m.Length;
        int id = m[i];
        var pos = new Vector2(System.BitConverter.ToSingle(m, i + 1), System.BitConverter.ToSingle(m, i + 5));
        int portal = m[i + 9], len = m[i + 10];
        if (i + 11 + len > m.Length) return m.Length;
        var f = System.Text.Encoding.UTF8.GetString(m, i + 11, len).Split('\t');
        i += 11 + len;
        if (id == Town.myId || f.Length < 6) return i;
        if (Town.guys.TryGetValue(id, out var old)) { if (old.go != null) Destroy(old.go); Town.guys.Remove(id); }
        var hc = HeroContent.Get(f[1]);
        var g = new Town.Guy { id = id, nick = f[0], hero = hc.id, pos = pos, netPos = pos, portal = portal };
        if (f.Length > 6 && int.TryParse(f[6], out var ow)) g.owned = ow;
        g.admin = f.Length > 7 && f[7] == "1";
        g.go = hc.Spawn(out g.anim);
        if (hc.id == "mage")
        {
            Cosmetics.Apply(g.go, f[2], f[3], f[4], f[5]);
            // девятое поле — надетое оружие «вид.заточка»: чужой +16 светится красным и у тебя на экране
            var (wk, we) = f.Length > 8 ? ParseWeaponInfo(f[8]) : (-1, 0);
            if (wk >= 0) Cosmetics.ApplyWeapon(g.go, WeaponKinds[wk], GlowTier(we));
        }
        g.go.name = "Townsman";
        g.tr = g.go.transform;
        g.tr.position = World.W(pos);
        g.anim.Play("Idle");
        Town.guys[id] = g;
        return i;
    }

    void TownNet(byte t, byte[] m)
    {
        switch (t)
        {
            case 0x0A:
            {
                if (m.Length < 3) return;
                TownClear();
                Town.myId = m[1];
                Town.entered = true;
                // сервер ставит вошедшего в центр: шлём своё место сразу, даже если стоим
                sentPos = new Vector2(999, 999);
                Town.status = null;
                Town.chat.Clear();
                int n = m[2], i = 3;
                for (int k = 0; k < n && i < m.Length; k++) i = ReadGuy(m, i);
                break;
            }
            case 0x0B: ReadGuy(m, 1); break;
            case 0x0C:
                if (m.Length > 1 && Town.guys.TryGetValue(m[1], out var gone)) { Destroy(gone.go); Town.guys.Remove(m[1]); asked.Remove(m[1]); }
                break;
            case 0x0D:
                for (int i = 1; i + 9 <= m.Length; i += 9)
                    if (Town.guys.TryGetValue(m[i], out var g))
                        g.netPos = new Vector2(System.BitConverter.ToSingle(m, i + 1), System.BitConverter.ToSingle(m, i + 5));
                break;
            case 0x0E:
            {
                if (m.Length < 3) return;
                // ник \t текст \t админ \t unix-время (сервер вырезает табы из текста, так что полей ровно четыре)
                var f = System.Text.Encoding.UTF8.GetString(m, 2, m.Length - 2).Split('\t');
                if (f.Length < 2) return;
                // текст чужой: угловые скобки убираем, чтобы никто не раскрасил или не раздул чат разметкой
                string nick = f[0].Replace('<', '‹').Replace('>', '›'), text = f[1].Replace('<', '‹').Replace('>', '›');
                bool adm = f.Length > 2 && f[2] == "1";
                string at = f.Length > 3 && long.TryParse(f[3], out var ts) ? ChatTime(ts) + "  " : "";
                // номер 0 — строка из истории (сказано до нашего прихода): без облачка над головой и не «моя»
                string nc = adm ? "FF8A6B" : m[1] == 0 ? "B9B2A6" : m[1] == Town.myId ? "9FE8A8" : "FFD45E";
                Town.chat.Add($"<color=#8A857D>{at}</color><color=#{nc}>{nick}{(adm ? " · ADMIN" : "")}</color>  {text}");
                if (Town.chat.Count > 40) Town.chat.RemoveAt(0);
                if (m[1] == 0) break;
                if (chatTab != 0 || chatCollapsed) unreadTown++;
                if (m[1] == Town.myId) { mySay = text; mySayUntil = Time.unscaledTime + 6; }
                else if (Town.guys.TryGetValue(m[1], out var who)) { who.say = text; who.sayUntil = Time.unscaledTime + 6; }
                break;
            }
            case 0x0F:
                if (m.Length > 2 && Town.guys.TryGetValue(m[1], out var p)) p.portal = m[2];
                break;
            case 0x09:
                if (m.Length < 2) return;
                if (m[1] == 1 && m.Length >= 3) { invitedBy = m[2]; invitedAt = Time.unscaledTime; }
                else if (m[1] == 4 && m.Length >= 4)
                {
                    // «ищу пати» на доске таверны
                    if (m[2] == Town.myId) Town.meLfg = m[3] == 1;
                    else if (Town.guys.TryGetValue(m[2], out var lg)) lg.lfg = m[3] == 1;
                }
                else if (m[1] == 5 && m.Length >= 3) Town.meAdmin = m[2] == 1;
                else if (m[1] >= 30 && m[1] <= 37) SiegeNet(m);   // осада в городе (GameSiegeTown.cs)
                else if (m[1] == 6)
                {
                    // этот аккаунт вошёл с другого устройства: выходим из города и ждём кнопку «Вернуться сюда»
                    Coop.Close();
                    TownClear();
                    Town.replaced = true;
                    Town.retryAt = float.MaxValue;
                    Town.status = m.Length > 2 ? System.Text.Encoding.UTF8.GetString(m, 2, m.Length - 2) : "Ты вошёл с другого устройства";
                }
                else if (m[1] == 3 && m.Length >= 3)
                {
                    // состав пати: первый — лидер; пустой — пати распалась или я вышел
                    townParty.Clear();
                    for (int i = 0; i < m[2] && 3 + i < m.Length; i++) townParty.Add(m[3 + i]);
                    foreach (int id in townParty) asked.Remove(id);
                    if (townParty.Count > 0) invitedBy = 0;
                }
                else if (m[1] == 0)
                {
                    partyGo = false;
                    Town.status = m.Length > 2 ? System.Text.Encoding.UTF8.GetString(m, 2, m.Length - 2) : "Не получилось собрать поход";
                }
                break;
        }
    }

    // время сообщения по часам игрока: смещение пояса считаем сами — в браузерной сборке TimeZoneInfo бывает UTC
    static readonly System.TimeSpan tzOffset = System.DateTime.Now - System.DateTime.UtcNow;
    static string ChatTime(long unix) => System.DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.Add(tzOffset).ToString("HH:mm");

    void TownSay(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 160) text = text.Substring(0, 160);
        if (!Town.entered || Coop.sock == null || !Coop.sock.Open) { Town.status = "Нет связи с городом"; return; }
        var b = System.Text.Encoding.UTF8.GetBytes(text);
        var op = new byte[b.Length + 1];
        op[0] = 0x0E;
        b.CopyTo(op, 1);
        Coop.Send(op);
    }

    // ---------- Походы ----------

    void TownSolo(int level)
    {
        selLevel = level;
        TownLeave();
        skipMenu = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ---------- Пати ----------

    bool InParty => townParty.Count > 1;
    bool PartyLeader => townParty.Count > 1 && townParty[0] == Town.myId;
    string GuyNick(int id) => id == Town.myId ? Nick : Town.guys.TryGetValue(id, out var g) ? g.nick : "Маг";

    static int OwnMask()
    {
        int m = 0;
        for (int i = 0; i < Town.Portals.Length; i++) if (Owns(Levels[i].item)) m |= 1 << i;
        return m;
    }

    // портал куплен у кого-нибудь из пати — лидер может вести туда всех
    bool PartyOwns(int portal)
    {
        if (Owns(Levels[portal].item)) return true;
        foreach (int id in townParty)
            if (Town.guys.TryGetValue(id, out var g) && (g.owned >> portal & 1) != 0) return true;
        return false;
    }

    bool CanInvite(int id) => (!InParty || (PartyLeader && townParty.Count < Coop.Size)) && !townParty.Contains(id)
                              && !(asked.TryGetValue(id, out var at) && Time.unscaledTime < at + InviteTtl);

    void PartyInvite(int id)
    {
        if (!Town.entered || Coop.InRoom || !CanInvite(id)) return;
        asked[id] = Time.unscaledTime;
        Town.status = $"Позвали в пати: {GuyNick(id)}";
        Coop.Send(new byte[] { 0x09, 10, (byte)id });
    }

    void PartyAccept()
    {
        if (invitedBy == 0 || Coop.InRoom) return;
        Coop.Send(new byte[] { 0x09, 11, (byte)invitedBy });
        invitedBy = 0;
    }

    void PartyLeave()
    {
        if (!Coop.InRoom) Coop.Send(new byte[] { 0x09, 12 });
    }

    // лидер ведёт пати в поход: сервер сажает всех в одну комнату, уровень и сложность уйдут со стартом забега
    void PartyStart(int level)
    {
        if (!PartyLeader || partyGo || Coop.InRoom) return;
        selLevel = level;
        partyGo = true;
        partyAt = Time.unscaledTime;
        partyNeed = townParty.Count - 1;
        Town.status = null;
        Coop.Send(new byte[] { 0x09, 13, (byte)level, DiffByte });
    }

    // уходим из города в другую сцену: сцена сменится только в конце кадра, до тех пор заново не подключаемся
    void TownLeave()
    {
        TownClear();
        Coop.Close();
        Town.retryAt = float.MaxValue;
    }

    // в классическое меню: выбор героя, комната для друга по коду
    void TownToMenu()
    {
        Town.home = false;
        TownLeave();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void MenuToTown()
    {
        Town.home = true;
        Coop.Close();
        Coop.reconnectAt = 0;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ---------- Интерфейс ----------

    // запомнить прямоугольник интерфейса (в единицах GUI) для джойстика
    void UiBlock(Rect r)
    {
        float u = UiScale;
        townUi.Add(new Rect(r.x * u, Screen.height - (r.y + r.height) * u, r.width * u, r.height * u));
    }

    bool TownBlocked(Vector2 screen)
    {
        if (welcome || menuTab != 0 || guysOpen) return true;
        foreach (var r in townUi) if (r.Contains(screen)) return true;
        foreach (var r in guyZones) if (r.Contains(screen)) return true;
        return false;
    }

    static string RandomNick() => $"{NickA[Random.Range(0, NickA.Length)]} {NickB[Random.Range(0, NickB.Length)]} {Random.Range(10, 100)}";

    static bool TouchUi => Application.isMobilePlatform;

    // поле ввода: на ПК — обычное, на телефоне в браузере — системное окно (иначе не вылезет клавиатура)
    string fieldName, fieldDone;   // поле, для которого открыта строка ввода телефона, и её ответ
    string Field(Rect r, string value, int max, string hint, string prompt, string name)
    {
        RR(r, new Color(0.03f, 0.025f, 0.04f, 0.92f), 12);
        RRLine(r, new Color(Gold.r, Gold.g, Gold.b, 0.5f), 1, 12);
        fieldS ??= new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, richText = false, wordWrap = false, clipping = TextClipping.Clip };
        fieldS.normal.textColor = fieldS.focused.textColor = fieldS.hover.textColor = Bone;
        var inner = new Rect(r.x + 16, r.y + 2, r.width - 32, r.height - 4);
        if (TouchUi)
        {
            Text(inner, string.IsNullOrEmpty(value) ? $"<color=#77736C>{hint}</color>" : value, Col(small, Bone, TextAnchor.MiddleLeft), 2);
            // ответ строки ввода приходит позже — подставляем его этому полю в следующих кадрах
            if (fieldName == name && fieldDone != null) { value = fieldDone.Length > max ? fieldDone.Substring(0, max) : fieldDone; fieldDone = null; fieldName = null; }
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                fieldName = name; fieldDone = null;
                PhoneInput(string.IsNullOrEmpty(prompt) ? hint : prompt, max, t => fieldDone = t, value, 1);
            }
            return value;
        }
        GUI.SetNextControlName(name);
        value = GUI.TextField(inner, value ?? "", max, fieldS);
        if (string.IsNullOrEmpty(value) && GUI.GetNameOfFocusedControl() != name) Text(inner, $"<color=#77736C>{hint}</color>", Col(small, Bone, TextAnchor.MiddleLeft), 2);
        return value;
    }

    void DrawTown(float W, float H)
    {
        WarmTextures();
        if (Event.current.type == EventType.Repaint) townUi.Clear();
        if (welcome) { DrawWelcome(W, H); return; }
        // прокачка, магазин, лидеры и идеи — те же экраны, что в меню; «Назад» возвращает в город
        // портрет окна персонажа держит мага на своём слое — вне окна вернуть, иначе основная камера его не видит
        if (menuTab == 0) PortraitOff();
        if (menuTab != 0) { DrawMenu(W, H); return; }
        GUI.enabled = confirm == null && !guysOpen;
        bool port = W < H;
        float th = port ? 50 : 56, ty = port ? 12 : 16;
        float aw = port ? 300 : 360;

        // верх: звук, ник, вход
        var mr = new Rect(16, ty, th + 8, th);
        MuteButton(mr); UiBlock(mr);
        var nr = new Rect(16 + th + 16, ty, Mathf.Min(320, W - aw - 16 - (16 + th + 16) - 12), th);
        DrawNickButton(nr); UiBlock(nr);
        var ar = new Rect(W - aw - 16, ty, aw, th);
        DrawAccountButton(ar); UiBlock(ar);

        int near = Town.guys.Count;
        string head = !Town.entered ? (Town.status ?? "ВХОДИМ В ГОРОД…") : near == 0 ? "ГОРОД · ПОКА ТЫ ОДИН" : $"ГОРОД · МАГОВ РЯДОМ: {near}";
        ChipAt(W / 2, port ? ty + th + 8 : ty + 10, Up(head), Gold, Town.entered);
        // общий счёт: сколько игроков сейчас в игре и сколько монстров все вместе убили за сегодня
        if (world != null && world.goal > 0)
        {
            string ws = world.open
                ? $"В ИГРЕ: {world.online}  ·  ЗА ДЕНЬ УБИТО {Thousands(world.kills)}  ·  ЦЕЛЬ {Thousands(world.goal)} ВЗЯТА"
                : $"В ИГРЕ: {world.online}  ·  ЗА ДЕНЬ УБИТО {Thousands(world.kills)} ИЗ {Thousands(world.goal)}";
            if (port && world.sale > 0) ws += $"  ·  СКИДКА −{world.sale}%";   // на телефоне вторая строка не помещается
            if (!(port && SiegeSoon)) ChipAt(W / 2, port ? ty + th + 112 : ty + 44, ws, world.open ? new Color(0.6f, 1f, 0.7f) : Bone);
            // скидка торговцев и орда у стен (GameHorde.cs) — строкой ниже
            string sl = (world.sale > 0 ? $"СКИДКА ТОРГОВЦЕВ −{world.sale}%" : "СКИДОК ПОКА НЕТ")
                + (world.next > 0 ? $"  ·  ДО СЛЕДУЮЩЕЙ {Thousands(world.next - world.kills)}" : "")
                + (world.horde > 0 ? $"  ·  ОРДА У СТЕН {Thousands(world.horde)}" : "  ·  ОРДА ОТБИТА");
            if (!port) ChipAt(W / 2, ty + 88, sl, world.sale > 0 ? MetaGold : new Color(1f, 0.6f, 0.5f));
        }
        // на телефоне места под строку нет — осада встаёт вместо общего счёта, когда до неё меньше часа или она идёт
        if (!port || SiegeSoon) ChipAt(W / 2, port ? ty + th + 112 : ty + 132, SiegeLine(), SiegeOpen ? new Color(1f, 0.45f, 0.35f) : new Color(1f, 0.75f, 0.6f), SiegeOpen);

        // кнопки: в альбоме — столбик справа, на телефоне — ряд под шапкой
        string[] names = { port ? $"Прокачка" : $"Прокачка  <color=#FFD45E>· {Coins}</color>", NewGear() ? "Герой <color=#86FF8F>●</color>" : "Герой", "Магазин", "Лидеры", "Идеи", "Меню" };
        int nb = names.Length;
        float chatTop;
        if (port)
        {
            float bw = (W - 32 - (nb - 1) * 8) / nb, by = ty + th + 50;
            for (int i = 0; i < nb; i++)
            {
                var r = new Rect(16 + i * (bw + 8), by, bw, 54);
                UiBlock(r);
                if (Btn(r, "", i == 0 ? new Color(1f, 0.82f, 0.35f) : Bone)) TownButton(i);
                Text(r, names[i], Shrink(Col(small, Bone, TextAnchor.MiddleCenter), names[i], bw - 10), 2);
            }
            chatTop = by + 54 + 46;   // между кнопками и чатом — строка общего счёта
        }
        else
        {
            float bw = 250, bx = W - bw - 16, by = ty + th + 18;
            for (int i = 0; i < nb; i++)
            {
                var r = new Rect(bx, by + i * 66, bw, 58);
                UiBlock(r);
                if (Btn(r, "", i == 0 ? new Color(1f, 0.82f, 0.35f) : Bone)) TownButton(i);
                Text(r, names[i], Shrink(Col(small, Bone, TextAnchor.MiddleCenter), names[i], bw - 20), 2);
            }
            chatTop = 0;
        }

        float chatBottom = DrawChat(W, H, port, chatTop);
        DrawParty(W, H, port, port ? chatBottom + 10 : ty + th + 18);
        // полоска осады и миникарта — под окнами портала и таверны: раньше лезли поверх текста окна (прогон 17.11)
        DrawSiegeHud(W, H, port);
        DrawMinimap(W, H, port, chatBottom);
        DrawPortalPanel(W, H, port);
        DrawTavern(W, H, port);
        DrawBar(W, H, port);
        if (Town.replaced)
        {
            float rw = Mathf.Min(620, W - 48);
            var rr = new Rect((W - rw) / 2, H * 0.42f, rw, 150);
            UiBlock(rr);
            Plate(rr, new Color(0.06f, 0.04f, 0.08f, 0.96f), Gold, 16, 2);
            Text(new Rect(rr.x, rr.y + 12, rw, 40), "Ты вошёл в город с другого устройства", Shrink(Col(small, Bone, TextAnchor.MiddleCenter), "Ты вошёл в город с другого устройства", rw - 30), 2);
            if (Btn(new Rect(rr.x + 40, rr.y + 64, rw - 80, 70), "Вернуться в город здесь", Gold))
            { Town.replaced = false; Town.status = null; Town.retryAt = 0; }
        }
        DrawInvite(W, H, port);
        TapGuys(W, H);
        if (Town.status != null && Town.entered)
            Text(new Rect(0, port ? H * 0.5f : H - 150, W, 36), Town.status, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(1f, 0.8f, 0.55f), TextAnchor.MiddleCenter), 2);
        GUI.enabled = confirm == null;
        if (guysOpen) DrawGuys(W, H);
        GUI.enabled = true;
        DrawConfirm(W, H);
    }

    // пати: состав и кнопки; без пати — кнопка списка магов
    void DrawParty(float W, float H, bool port, float y)
    {
        float w = port ? W - 32 - MiniSize(port) - 12 : 330, x = 16;
        int near = Town.guys.Count;
        if (!InParty)
        {
            var r = new Rect(x, y, port ? w / 2 - 4 : w, 54);
            UiBlock(r);
            if (Btn(r, near == 0 ? "Маги рядом: 0" : $"Маги рядом: {near} · пати", new Color(0.6f, 1f, 0.7f))) { guysOpen = true; guysPage = 0; guyFirst = 0; }
            return;
        }
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        float h = 44 + townParty.Count * 32 + 64;
        var box = new Rect(x, y, w, h);
        UiBlock(box);
        Plate(box, new Color(0.03f, 0.05f, 0.05f, 0.9f), new Color(0.6f, 1f, 0.7f, 0.6f), 14, 1);
        Text(new Rect(x + 14, y + 6, w - 28, 34), $"ПАТИ · {townParty.Count} ИЗ {Coop.Size}", Col(caps, new Color(0.6f, 1f, 0.7f), TextAnchor.MiddleLeft), 2);
        for (int i = 0; i < townParty.Count; i++)
        {
            string s = GuyNick(townParty[i]) + (i == 0 ? "  · лидер" : "");
            Text(new Rect(x + 14, y + 42 + i * 32, w - 28, 30), s, Shrink(Col(ds, townParty[i] == Town.myId ? new Color(0.62f, 0.91f, 0.66f) : Bone, TextAnchor.MiddleLeft), s, w - 28), 2);
        }
        float by = y + 44 + townParty.Count * 32 + 4, bw = (w - 28 - 8) / 2;
        bool more = PartyLeader && townParty.Count < Coop.Size;
        if (more && Btn(new Rect(x + 14, by, bw, 50), "Позвать ещё", new Color(0.6f, 1f, 0.7f))) { guysOpen = true; guysPage = 0; guyFirst = 0; }
        if (Btn(new Rect(more ? x + 14 + bw + 8 : x + 14, by, more ? bw : w - 28, 50), "Выйти из пати", Bone)) PartyLeave();
    }

    // Герой в городе: выбрать другого (сцена пересобирается, город заходит заново) и переодеть своего — гардероб
    // это вкладка «Одежда» магазина, одежда шьётся только на мага
    void DrawTownHero(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.8f));
        GUI.enabled = confirm == null;
        bool port = W < H;
        // вкладки: снаряжение (окно персонажа как в L2), облик (одежда на живой модели, GameLook.cs), выбор героя
        string[] tabs = { "Снаряжение", "Облик", "Герои" };
        float tw = Mathf.Min(300, (W - 48 - 24) / 3), tx = (W - tw * 3 - 24) / 2, tyy = port ? 40 : 24;
        for (int i = 0; i < tabs.Length; i++)
        {
            var tr = new Rect(tx + i * (tw + 12), tyy, tw, 54);
            bool on = heroTab == i;
            Plate(tr, on ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : new Color(0.05f, 0.04f, 0.07f, 0.9f), on ? Gold : new Color(1, 1, 1, 0.12f), 10);
            Text(tr, Up(tabs[i]), Shrink(Col(caps, on ? Gold : Soft(0.8f), TextAnchor.MiddleCenter), Up(tabs[i]), tw - 16), 2);
            if (GUI.Button(tr, GUIContent.none, GUIStyle.none) && confirm == null && gearPick == null) { heroTab = i; if (i != 0) MarkGearSeen(); }
        }
        if (heroTab == 0) { GUI.enabled = true; DrawCharacter(W, H); return; }
        if (heroTab == 1)
        {
            DrawLook(W, H);
            float lb = Mathf.Min(560, W - 48);
            if (buyStatus != null) Text(new Rect(0, H - 150, W, 36), buyStatus, Col(small, new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleCenter), 2);
            if (Btn(new Rect((W - lb) / 2, H - 112, lb, 86), "Назад в город", Bone) && confirm == null) menuTab = 0;
            GUI.enabled = true;
            DrawConfirm(W, H);
            return;
        }
        tilePicked = false;
        float y = TileRow(W, port ? 150 : 120, "Кем играть", Heroes.Length, port ? 250 : 236, i => Heroes[i].title, i => Heroes[i].desc,
            i => Heroes[i].color, i => Heroes[i].soon, i => Owns(Heroes[i].item) ? null : ShopList(1)[i], ref selHero, (r, i, dim) =>
            {
                RR(r, new Color(0.05f, 0.04f, 0.07f, 0.92f), 14);
                float sz = Mathf.Min(r.width * 0.4f, (r.height - 118) / 1.52f);
                var ir = new Rect(r.center.x - sz / 2, r.y + 38 + sz * 0.26f, sz, sz);
                GUI.color = dim ? new Color(0.4f, 0.4f, 0.45f) : Color.white;
                IconTex(ir, Heroes[i].icon, dim ? new Color(0.35f, 0.35f, 0.4f) : Heroes[i].color);
                GUI.color = Color.white;
            });
        if (tilePicked) SaveHeroPick();
        float bw = Mathf.Min(560, W - 48), x = (W - bw) / 2;
        y += 30;
        bool mage = Heroes[selHero].id == "mage" && builtHero == "mage";
        if (Btn(new Rect(x, y, bw, 80), mage ? "Облик мага: шляпа, мантия, посох" : "Облик — только у мага", mage ? Gold : Bone * 0.6f) && mage && confirm == null)
        { heroTab = 1; lookScroll = 0; buyStatus = null; if (!walletOk) LoadWallet(); }
        if (Btn(new Rect(x, H - 112, bw, 86), "Назад в город", Bone) && confirm == null) menuTab = 0;
        GUI.enabled = true;
        DrawConfirm(W, H);
        // выбран другой (и он куплен) — пересобираем сцену: модель, статы и черта героя строятся в Awake
        if (confirm == null && Heroes[selHero].id != builtHero && Owns(Heroes[selHero].item) && !Heroes[selHero].soon)
        {
            menuTab = 0;
            TownLeave();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    // ---------- Таверна ----------

    void DrawTavern(float W, float H, bool port)
    {
        if (!inTavern || myPortal != 255) return;
        float pw = Mathf.Min(620, W - 32), x = (W - pw) / 2;
        if (!port)
        {
            x = Mathf.Max(x, 16 + Mathf.Min(560, W * 0.42f) + 16);
            pw = Mathf.Min(pw, W - 16 - x - 262);
        }
        var lfg = new List<Town.Guy>();
        foreach (var g in Town.guys.Values) if (g.lfg && !townParty.Contains(g.id)) lfg.Add(g);
        int nl = Mathf.Min(4, lfg.Count);
        var best = leaders?.best;
        int nb = best == null ? 0 : Mathf.Min(3, best.Length);
        float ph = 64 + 72 + 40 + Mathf.Max(1, nl) * 60 + 44 + Mathf.Max(1, nb) * 32 + 20;
        float y = port ? H - ph - 190 : H - ph - 24;
        var box = new Rect(x, y, pw, ph);
        UiBlock(box);
        var warm = new Color(1f, 0.66f, 0.3f);
        Plate(box, new Color(0.06f, 0.035f, 0.02f, 0.94f), new Color(warm.r, warm.g, warm.b, 0.75f), 16, 2);
        Outline(new Rect(x, y + 10, pw, 50), "Таверна «Кривой череп»", Fit("Таверна «Кривой череп»", pw - 40, Color.Lerp(warm, Bone, 0.4f)), 2);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        float cy = y + 64;
        // доска «ищу пати»: отметься — тебя увидят все в городе, на нике появится «ищет пати»
        bool can = Town.entered && !InParty && !Coop.InRoom;
        string lt = InParty ? "Ты уже в пати" : Town.meLfg ? "Ищу пати — снять объявление" : "Повесить объявление «Ищу пати»";
        if (Btn(new Rect(x + 20, cy, pw - 40, 62), lt, Town.meLfg ? new Color(0.6f, 1f, 0.7f) : warm) && can)
            Coop.Send(new byte[] { 0x09, 14, (byte)(Town.meLfg ? 0 : 1) });
        cy += 72;
        Text(new Rect(x + 20, cy, pw - 40, 34), "ИЩУТ ПАТИ", Col(caps, warm, TextAnchor.MiddleLeft), 2);
        cy += 40;
        if (nl == 0) { Text(new Rect(x + 20, cy, pw - 40, 52), "Пока никто — повесь объявление первым", Shrink(Col(ds, Soft(0.6f), TextAnchor.MiddleLeft), "Пока никто — повесь объявление первым", pw - 40), 2); cy += 60; }
        for (int i = 0; i < nl; i++)
        {
            var g = lfg[i];
            var row = new Rect(x + 20, cy, pw - 40, 52);
            RR(row, new Color(1, 1, 1, 0.05f), 10);
            Text(new Rect(row.x + 12, row.y, row.width - 200, 52), g.nick, Shrink(Col(small, Bone, TextAnchor.MiddleLeft), g.nick, row.width - 200), 2);
            var br = new Rect(row.xMax - 176, row.y + 5, 168, 42);
            if (g.id == invitedBy) { if (Btn(br, "Принять", new Color(0.6f, 1f, 0.7f))) PartyAccept(); }
            else if (asked.TryGetValue(g.id, out var at) && Time.unscaledTime < at + InviteTtl) Text(br, "позвали…", Col(ds, Soft(0.7f), TextAnchor.MiddleCenter), 2);
            else if (CanInvite(g.id)) { if (Btn(br, "Позвать", new Color(0.6f, 1f, 0.7f))) PartyInvite(g.id); }
            cy += 60;
        }
        Text(new Rect(x + 20, cy, pw - 40, 34), "ЛУЧШИЕ ЗАБЕГИ", Col(caps, warm, TextAnchor.MiddleLeft), 2);
        cy += 44;
        if (nb == 0) Text(new Rect(x + 20, cy, pw - 40, 30), leaders == null ? "Загружаю…" : "Пока пусто", Col(ds, Soft(0.6f), TextAnchor.MiddleLeft), 2);
        for (int i = 0; i < nb; i++)
        {
            var r = best[i];
            string s = $"{i + 1}. {r.nick} — {Mmss(r.secs)}, ур. {r.level}";
            Text(new Rect(x + 20, cy, pw - 40, 30), s, Shrink(Col(ds, i == 0 ? MetaGold : Bone, TextAnchor.MiddleLeft), s, pw - 40), 2);
            cy += 32;
        }
    }

    // ---------- Миникарта ----------

    static float MiniSize(bool port) => port ? 170 : 230;

    // круг площади сверху: порталы цветом уровня, таверна, маги (пати — голубые, админ — красный), я — стрелка
    void DrawMinimap(float W, float H, bool port, float chatBottom)
    {
        if (atBar || Town.replaced || myPortal != 255) return;   // плашка бармена и «вошёл с другого устройства» стоят на его месте
        float sz = MiniSize(port);
        var r = port ? new Rect(W - 16 - sz, chatBottom + 10, sz, sz) : new Rect(W - 16 - sz, 16 + 56 + 18 + 6 * 66 + 10, sz, sz);
        if (!port && r.yMax > H - 24) return;
        UiBlock(r);
        RR(r, new Color(0.02f, 0.015f, 0.03f, 0.7f), sz / 2);
        RRLine(r, new Color(Gold.r, Gold.g, Gold.b, 0.45f), 1, sz / 2);
        float k = (sz / 2 - 8) / 21.5f;
        var c = r.center;
        System.Func<Vector2, Vector2> at = p => new Vector2(c.x + p.x * k, c.y - p.y * k);
        void Dot(Vector2 p, float d, Color col) => RR(new Rect(at(p).x - d / 2, at(p).y - d / 2, d, d), col, d / 2);
        for (int i = 0; i < Town.Portals.Length; i++)
        {
            var lc = Levels[i].color;
            Dot(Town.Portals[i], Town.PortalR * 2 * k, new Color(lc.r, lc.g, lc.b, 0.55f));
        }
        Dot(Town.Tavern, Town.TavernR * 2 * k, new Color(1f, 0.6f, 0.25f, 0.6f));
        if (SiegeOpen) Dot(SiegeGate, SiegeGateR * 2 * k, new Color(1f, 0.3f, 0.2f, 0.8f));
        var tp = at(Town.Tavern);
        Text(new Rect(tp.x - 40, tp.y - 11, 80, 22), "Т", Col(ds2 ??= new GUIStyle(tiny) { fontSize = 16 }, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter), 1);
        foreach (var g in Town.guys.Values)
        {
            var col = g.admin ? new Color(1f, 0.45f, 0.35f) : townParty.Contains(g.id) ? new Color(0.5f, 0.9f, 1f) : g.lfg ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.85f, 0.4f);
            Dot(g.pos, port ? 7 : 8, col);
        }
        // я: точка и короткий штрих туда, куда смотрю
        var me = at(player.pos);
        var f = new Vector2(player.face.x, -player.face.y).normalized;
        for (int i = 1; i <= 3; i++) RR(new Rect(me.x + f.x * i * 3 - 2, me.y + f.y * i * 3 - 2, 4, 4), Color.white, 2);
        RR(new Rect(me.x - 5, me.y - 5, 10, 10), new Color(0.62f, 0.95f, 0.66f), 5);
    }
    GUIStyle ds2;

    // меня зовут в пати
    void DrawInvite(float W, float H, bool port)
    {
        if (Coop.IsGuest)
        {
            ChipAt(W / 2, port ? H * 0.46f : 170, "ЛИДЕР ВЕДЁТ ПАТИ В ПОХОД — ВХОДИМ…", new Color(0.6f, 1f, 0.7f));
            return;
        }
        if (invitedBy == 0 || !Town.guys.TryGetValue(invitedBy, out var g)) return;
        float pw = Mathf.Min(560, W - 32), x = (W - pw) / 2, y = port ? H * 0.42f : 150;
        var box = new Rect(x, y, pw, 150);
        UiBlock(box);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5);
        RR(new Rect(box.x - 5, box.y - 5, box.width + 10, box.height + 10), new Color(0.6f, 1f, 0.7f, 0.12f + 0.2f * pulse), 18);
        Plate(box, new Color(0.03f, 0.05f, 0.05f, 0.95f), new Color(0.6f, 1f, 0.7f, 0.8f), 16, 2);
        string s = $"{g.nick} зовёт в пати";
        Text(new Rect(x + 16, y + 10, pw - 32, 50), s, Shrink(Col(small, Color.white, TextAnchor.MiddleCenter), s, pw - 32), 2);
        float bw = (pw - 40 - 12) / 2;
        if (Btn(new Rect(x + 20, y + 70, bw, 64), "Принять", new Color(0.6f, 1f, 0.7f))) PartyAccept();
        if (Btn(new Rect(x + 20 + bw + 12, y + 70, bw, 64), "Нет", Bone)) invitedBy = 0;
    }

    // касание мага на площади открывает список с ним в первой строке
    void TapGuys(float W, float H)
    {
        float u = UiScale;
        var e = Event.current;
        if (e.type == EventType.Repaint) guyZones.Clear();
        if (guysOpen || Coop.InRoom) return;
        foreach (var o in Town.guys.Values)
        {
            if (!ToGui(World.W(o.pos, 2.5f), u, out var g)) continue;
            if (g.x < 0 || g.x > W || g.y < 0 || g.y > H) continue;
            var r = new Rect(g.x - 85, g.y - 26, 170, 76);
            if (e.type == EventType.Repaint) guyZones.Add(new Rect(r.x * u, Screen.height - (r.y + r.height) * u, r.width * u, r.height * u));
            if (!GUI.Button(r, GUIContent.none, GUIStyle.none)) continue;
            // маг под окном интерфейса — касание было по окну
            var sp = new Vector2(e.mousePosition.x * u, Screen.height - e.mousePosition.y * u);
            bool ui = false;
            foreach (var b in townUi) if (b.Contains(sp)) ui = true;
            if (ui) continue;
            guysOpen = true; guysPage = 0; guyFirst = o.id;
            return;
        }
    }

    // список магов города: позвать в пати
    void DrawGuys(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.6f));
        var list = new List<Town.Guy>(Town.guys.Values);
        list.Sort((a, b) => a.id == guyFirst ? -1 : b.id == guyFirst ? 1 : a.id.CompareTo(b.id));
        const int Rows = 6;
        int pages = Mathf.Max(1, (list.Count + Rows - 1) / Rows);
        guysPage = Mathf.Clamp(guysPage, 0, pages - 1);
        int from = guysPage * Rows, n = Mathf.Min(Rows, list.Count - from);
        float pw = Mathf.Min(640, W - 32), ph = 84 + Mathf.Max(1, n) * 72 + 34 + 84, x = (W - pw) / 2, y = Mathf.Max(20, (H - ph) / 2);
        var box = new Rect(x, y, pw, ph);
        Plate(box, new Color(0.04f, 0.03f, 0.06f, 0.97f), new Color(0.6f, 1f, 0.7f, 0.7f), 16, 2);
        Outline(new Rect(x, y + 12, pw, 50), "МАГИ В ГОРОДЕ", Col(small, new Color(0.6f, 1f, 0.7f), TextAnchor.MiddleCenter), 2);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        float cy = y + 76, bw = 190;
        if (list.Count == 0)
        {
            Text(new Rect(x + 20, cy, pw - 40, 64), "Пока в городе никого — загляни чуть позже", Shrink(Col(ds, Soft(0.7f), TextAnchor.MiddleCenter), "Пока в городе никого — загляни чуть позже", pw - 40), 2);
            cy += 72;
        }
        for (int i = from; i < from + n; i++)
        {
            var g = list[i];
            var row = new Rect(x + 16, cy, pw - 32, 64);
            RR(row, g.id == guyFirst ? new Color(0.6f, 1f, 0.7f, 0.12f) : new Color(1, 1, 1, 0.05f), 10);
            bool mine = townParty.Contains(g.id);
            string nick = g.nick + (mine && townParty[0] == g.id ? "  · лидер" : "");
            Text(new Rect(row.x + 14, row.y, row.width - bw - 30, 64), nick, Shrink(Col(small, mine ? new Color(0.6f, 1f, 0.7f) : Bone, TextAnchor.MiddleLeft), nick, row.width - bw - 30), 2);
            var br = new Rect(row.xMax - bw - 8, row.y + 7, bw, 50);
            if (mine) Text(br, "в пати", Col(ds, new Color(0.6f, 1f, 0.7f), TextAnchor.MiddleCenter), 2);
            else if (g.id == invitedBy) { if (Btn(br, "Принять", new Color(0.6f, 1f, 0.7f))) { PartyAccept(); guysOpen = false; } }
            else if (asked.TryGetValue(g.id, out var at) && Time.unscaledTime < at + InviteTtl) Text(br, "позвали…", Col(ds, Soft(0.7f), TextAnchor.MiddleCenter), 2);
            else if (CanInvite(g.id)) { if (Btn(br, "В пати", new Color(0.6f, 1f, 0.7f))) PartyInvite(g.id); }
            cy += 72;
        }
        string hint = InParty && !PartyLeader ? "Звать в пати может лидер" : townParty.Count >= Coop.Size ? "Пати собрана" : $"В пати до {Coop.Size} магов. Лидер ведёт всех в портал";
        Text(new Rect(x + 20, cy, pw - 40, 30), hint, Shrink(Col(ds, Soft(0.65f), TextAnchor.MiddleCenter), hint, pw - 40), 2);
        cy += 38;
        float cw = pages > 1 ? pw - 40 - 2 * 92 : pw - 40;
        if (pages > 1)
        {
            if (Btn(new Rect(x + 20, cy, 84, 64), "‹", Bone)) guysPage--;
            if (Btn(new Rect(x + pw - 20 - 84, cy, 84, 64), "›", Bone)) guysPage++;
        }
        if (Btn(new Rect(x + (pw - cw) / 2, cy, cw, 64), "Закрыть", Bone)) guysOpen = false;
        var ev = Event.current;
        if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape) { ev.Use(); guysOpen = false; }
    }

    void TownButton(int i)
    {
        if (confirm != null) return;
        switch (i)
        {
            case 0: menuTab = 3; buyStatus = null; break;
            case 1: menuTab = 7; heroTab = 0; gearPick = null; gearStatus = null; buyStatus = null; if (!walletOk) LoadWallet(); LoadGear(); break;
            case 2: menuTab = 5; buyStatus = null; if (!walletOk) LoadWallet(); break;
            case 3: menuTab = 4; LoadLeaders(); break;
            case 4: menuTab = 2; ideaStatus = null; ideas = null; LoadIdeas(); break;
            case 5: TownToMenu(); break;
        }
    }

    float DrawChat(float W, float H, bool port, float top)
    {
        chatS ??= new GUIStyle(tiny) { fontSize = 21, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false, richText = true };
        int lines = port ? 4 : 7;
        float lh = 28, cw = port ? W - 32 : Mathf.Min(560, W * 0.42f), ih = 54;
        float ch = lines * lh + 12;
        float cy = port ? top + 46 : H - ch - ih - 26;   // в портрете над чатом — вкладки «Город / Все»
        var box = new Rect(16, cy, cw, ch);
        var ir = new Rect(16, box.yMax + 8, cw, ih);
        if (!chatMinLoaded) { chatMinLoaded = true; chatCollapsed = PlayerPrefs.GetInt("chat.min", 0) == 1; }
        if (DrawChatHead(new Rect(box.x, box.y, box.width, ir.yMax - box.y), port)) { chatFocus = false; return ir.yMax; }
        RR(box, new Color(0.02f, 0.015f, 0.03f, 0.55f), 12);
        var list = chatTab == 0 ? Town.chat : chatTab == 1 ? globalChat : systemChat.ConvertAll(x => x.line);
        int from = Mathf.Max(0, list.Count - lines);
        for (int i = from; i < list.Count; i++)
            Text(new Rect(box.x + 12, box.y + 6 + (i - from + lines - (list.Count - from)) * lh, box.width - 24, lh), list[i], Col(chatS, Bone, TextAnchor.MiddleLeft), 2);
        if (list.Count == 0)
            Text(box, chatTab == 0 ? "Здесь общий чат города" : chatTab == 1 ? "Здесь пишут все: город и походы" : "Здесь объявления — пока их нет", Col(chatS, Soft(0.6f), TextAnchor.MiddleCenter), 2);

        UiBlock(box); UiBlock(ir);
        if (chatTab == 2 && !Town.meAdmin)
        {
            Text(ir, "Объявления повторяются раз в 15 минут", Shrink(Col(chatS, Soft(0.7f), TextAnchor.MiddleCenter), "Объявления повторяются раз в 15 минут", ir.width - 20), 2);
            chatFocus = false;
            return ir.yMax;
        }
        // писать в чат может только вошедший через neuraldeep: сообщения привязаны к аккаунту
        if (!LoggedIn)
        {
            if (accEnabled == 1) { if (Btn(ir, "Войти, чтобы писать в чат", new Color(0.6f, 1f, 0.7f))) StartHubLogin(); }
            else Text(ir, "Чат доступен после входа через neuraldeep", Shrink(Col(chatS, Soft(0.7f), TextAnchor.MiddleCenter), "Чат доступен после входа через neuraldeep", ir.width - 20), 2);
            chatFocus = false;
            return ir.yMax;
        }
        if (TouchUi)
        {
            if (Btn(ir, chatTab == 0 ? "Написать в чат города" : "Написать всем", Bone))
                PhoneInput(chatTab == 0 ? "Сообщение в чат города" : chatTab == 2 ? "Объявление всем на сутки (off — снять)" : "Сообщение всем — город и походы", 160,
                    chatTab == 0 ? (System.Action<string>)TownSay : chatTab == 2 ? (System.Action<string>)(d => GlobalSay("/sys " + d)) : GlobalSay);
            return ir.yMax;
        }
        // Enter: в поле — отправить, вне поля — встать в поле; Esc — выйти из поля
        var e = Event.current;
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        bool esc = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
        if (enter) { e.Use(); if (chatFocus) chatSend = true; else GUI.FocusControl("chat"); }
        if (esc && chatFocus) { e.Use(); GUI.FocusControl(null); }
        float sw = 130;
        chatDraft = Field(new Rect(ir.x, ir.y, ir.width - sw - 8, ih), chatDraft, 160, chatTab == 0 ? "Enter — написать в чат города" : chatTab == 2 ? "Объявление всем на сутки · off — снять" : "Enter — написать всем", "", "chat");
        chatFocus = GUI.GetNameOfFocusedControl() == "chat";
        if (Btn(new Rect(ir.xMax - sw, ir.y, sw, ih), "Сказать", Bone)) chatSend = true;
        if (chatSend)
        {
            chatSend = false;
            if (chatTab == 0) TownSay(chatDraft);
            else if (chatTab == 2) { var d = (chatDraft ?? "").Trim(); if (d.Length > 0) GlobalSay(d.StartsWith("/sys") ? d : "/sys " + d); }
            else GlobalSay(chatDraft);
            chatDraft = "";
            GUI.FocusControl(null);
            chatFocus = false;
        }
        return ir.yMax;
    }

    void DrawPortalPanel(float W, float H, bool port)
    {
        if (myPortal == 255) return;
        var lv = Levels[myPortal];
        float pw = Mathf.Min(620, W - 32), x = (W - pw) / 2;
        // в альбоме чат слева внизу — окно портала встаёт правее него
        if (!port)
        {
            x = Mathf.Max(x, 16 + Mathf.Min(560, W * 0.42f) + 16);
            pw = Mathf.Min(pw, W - 16 - x);
        }
        bool owns = Owns(lv.item), lead = PartyLeader, called = Coop.IsGuest;
        // лидер ведёт пати и в портал, купленный у любого из её магов
        bool canLead = lead && PartyOwns(myPortal);
        float ph = called || partyGo ? 150 : !owns && !canLead ? (InParty ? 224 : 190) : 64 + 66 + 68 + (owns ? 88 : 0) + (canLead ? 88 : 40) + 16;
        float y = port ? H - ph - 190 : H - ph - 24;
        var box = new Rect(x, y, pw, ph);
        UiBlock(box);
        Plate(box, new Color(0.04f, 0.03f, 0.06f, 0.93f), new Color(lv.color.r, lv.color.g, lv.color.b, 0.7f), 16, 2);
        Outline(new Rect(x, y + 10, pw, 50), lv.title, Fit(lv.title, pw - 40, Color.Lerp(lv.color, Bone, 0.5f)), 2);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        float cy = y + 64;
        if (called || partyGo)
        {
            string s = called ? "Лидер ведёт пати в поход — входим…" : "Собираем пати в поход…";
            Text(new Rect(x + 20, cy, pw - 40, 60), s, Shrink(Col(small, Bone, TextAnchor.MiddleCenter), s, pw - 40), 2);
            return;
        }
        if (!owns && !canLead)
        {
            Text(new Rect(x + 20, cy, pw - 40, 34), $"Портал откроется за {Sale(lv.price)} золота", Col(ds, Soft(0.85f), TextAnchor.MiddleCenter), 2);
            if (Btn(new Rect(x + 20, cy + 44, pw - 40, 66), "В магазин", MetaGold)) { TownButton(2); shopTab = 0; }
            if (InParty)
            {
                string s = PartyOwns(myPortal) ? $"Сюда может повести лидер пати: {GuyNick(townParty[0])}" : "В пати этот портал пока ни у кого не открыт";
                Text(new Rect(x + 20, cy + 118, pw - 40, 30), s, Shrink(Col(ds, Soft(0.65f), TextAnchor.MiddleCenter), s, pw - 40), 2);
            }
            return;
        }
        // режим и сложность
        cy = ModeRow(x + 20, cy, pw - 40, false) + 10;
        float dw = (pw - 40 - 16) / 3;
        for (int i = 0; i < Diffs.Length; i++)
        {
            var d = Diffs[i];
            var r = new Rect(x + 20 + i * (dw + 8), cy, dw, 56);
            bool on = selDiff == i;
            Plate(r, on ? new Color(d.color.r * 0.3f, d.color.g * 0.3f, d.color.b * 0.3f, 0.95f) : new Color(0.05f, 0.04f, 0.07f, 0.9f), on ? d.color : new Color(1, 1, 1, 0.12f), 10);
            Text(new Rect(r.x, r.y + 2, r.width, 30), Up(d.title), Col(caps, on ? Color.Lerp(d.color, Color.white, 0.4f) : Soft(0.8f), TextAnchor.MiddleCenter), 2);
            Text(new Rect(r.x, r.y + 27, r.width, 26), d.desc, Shrink(Col(ds, on ? Bone : Soft(0.6f), TextAnchor.MiddleCenter), d.desc, r.width - 8), 2);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) selDiff = i;
        }
        cy += 68;
        if (canLead)
        {
            if (Btn(new Rect(x + 20, cy, pw - 40, 76), $"В поход с пати · {townParty.Count}", new Color(0.6f, 1f, 0.7f))) { PartyStart(myPortal); return; }
            cy += 88;
        }
        if (owns)
        {
            if (Btn(new Rect(x + 20, cy, pw - 40, 76), "Войти одному", Gold) && Owns(lv.item)) { TownSolo(myPortal); return; }
            cy += 88;
        }
        if (!canLead)
        {
            string s = InParty ? $"В поход пати ведёт лидер: {GuyNick(townParty[0])}" : "Вместе веселее: собери пати — «Маги рядом» или нажми на мага";
            Text(new Rect(x + 20, cy, pw - 40, 30), s, Shrink(Col(ds, Soft(0.65f), TextAnchor.MiddleCenter), s, pw - 40), 2);
        }
    }

    // ники над головами, реплики, подписи порталов
    void TownOverlay(float u, float W, float H)
    {
        SiegeOverlay(u);
        if (welcome || menuTab != 0) return;
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        for (int i = 0; i < Town.Portals.Length; i++)
        {
            if (!ToGui(World.W(Town.Portals[i], 3.4f), u, out var g)) continue;
            var lv = Levels[i];
            Outline(new Rect(g.x - 200, g.y - 30, 400, 40), lv.title, Col(small, Color.Lerp(lv.color, Color.white, 0.35f), TextAnchor.MiddleCenter), 2);
            int n = 0;
            foreach (var o in Town.guys.Values) if (o.portal == i) n++;
            string sub = !Owns(lv.item) ? $"{Sale(lv.price)} золота" : n > 0 ? $"в круге: {n}" : "встань в круг";
            Text(new Rect(g.x - 200, g.y + 6, 400, 28), sub, Col(ds, Soft(0.8f), TextAnchor.MiddleCenter), 2);
        }
        float now = Time.unscaledTime;
        foreach (var o in Town.guys.Values)
        {
            if (!ToGui(World.W(o.pos, 2.5f), u, out var g)) continue;
            if (g.x < -100 || g.x > W + 100 || g.y < 0 || g.y > H + 60) continue;
            int pi = townParty.IndexOf(o.id);
            string label = o.nick + (o.admin ? " · ADMIN" : "") + (pi == 0 ? " · лидер" : "") + (o.lfg && pi < 0 ? " · ищет пати" : "");
            var lc = o.admin ? new Color(1f, 0.54f, 0.42f) : pi >= 0 ? new Color(0.55f, 0.9f, 1f) : new Color(1f, 0.83f, 0.37f);
            Outline(new Rect(g.x - 240, g.y - 20, 480, 34), label, Col(tiny, lc, TextAnchor.MiddleCenter), 2);
            if (o.say != null && now < o.sayUntil) Bubble(g, o.say);
        }
        if (ToGui(World.W(player.pos, 2.5f), u, out var me2))
        {
            Outline(new Rect(me2.x - 240, me2.y - 20, 480, 34), Nick + (Town.meAdmin ? " · ADMIN" : "") + (PartyLeader ? " · лидер" : ""), Col(tiny, new Color(0.62f, 0.91f, 0.66f), TextAnchor.MiddleCenter), 2);
            if (mySay != null && now < mySayUntil) Bubble(me2, mySay);
        }
    }

    void Bubble(Vector2 g, string s)
    {
        if (s.Length > 60) s = s.Substring(0, 60) + "…";
        chatS ??= new GUIStyle(tiny) { fontSize = 21, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false, richText = true };
        float w = Mathf.Min(520, chatS.CalcSize(new GUIContent(s)).x + 28);
        var r = new Rect(g.x - w / 2, g.y - 62, w, 36);
        RR(r, new Color(0.02f, 0.015f, 0.03f, 0.85f), 10);
        Text(r, s, Col(chatS, Color.white, TextAnchor.MiddleCenter), 2);
    }

    // ---------- Стартовый экран: как тебя зовут и вход ----------

    void DrawWelcome(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.01f, 0.008f, 0.015f, 0.78f));
        bool port = W < H;
        float y = port ? H * 0.12f : H * 0.1f;
        Outline(new Rect(0, y, W, 100), "Magic Survivors", Col(big, new Color(1f, 0.88f, 0.62f)), 3);
        Divider(W / 2, y + 102, Mathf.Min(W * 0.6f, 640), Gold);
        var ds = tinyS ??= new GUIStyle(tiny) { fontSize = 21 };
        float bw = Mathf.Min(620, W - 48), x = (W - bw) / 2;
        y += 140;
        Text(new Rect(x, y, bw, 40), "Как зовут твоего мага?", Col(small, Bone, TextAnchor.MiddleCenter), 2);
        y += 52;
        float rw = 170;
        welcomeNick = Field(new Rect(x, y, bw - rw - 12, 76), welcomeNick, 24, "Имя мага", "Как зовут твоего мага?", "wnick");
        if (Btn(new Rect(x + bw - rw, y, rw, 76), "Другое", Bone)) { welcomeNick = RandomNick(); GUI.FocusControl(null); }
        y += 92;
        Text(new Rect(x, y, bw, 30), "Под этим именем тебя видят в городе, в чате и в таблице лидеров", Shrink(Col(ds, Soft(0.7f), TextAnchor.MiddleCenter), "Под этим именем тебя видят в городе, в чате и в таблице лидеров", bw), 2);
        y += 56;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3);
        var go = new Rect(x, y, bw, 92);
        GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.12f + 0.1f * pulse);
        GUI.DrawTexture(Grow(go, 0.25f), Gen.Glow().texture);
        GUI.color = Color.white;
        if (Btn(go, "В город", Gold)) WelcomeDone();
        y += 128;
        // вход — по желанию: без него всё хранится на этом устройстве
        DrawAccountButton(new Rect(x, y, bw, 72));
        if (accEnabled == 1 && !LoggedIn && !accCodeMode && accStatus == null)
            Text(new Rect(x, y + 80, bw, 60), "Вход сохранит золото, покупки и рекорды на всех устройствах.\nМожно и позже — кнопка есть в городе.",
                Col(tinyW ??= new GUIStyle(tiny) { fontSize = 20, wordWrap = true }, Soft(0.7f), TextAnchor.UpperCenter), 2);
        // вошёл с этого экрана через код (ПК) — ник берём из аккаунта и идём в город
        if (LoggedIn) { welcomeNick = Nick; WelcomeDone(); }
    }

    void WelcomeDone()
    {
        PlayerPrefs.SetInt("welcome.done", 1);
        PlayerPrefs.Save();
        nickDraft = welcomeNick;
        SaveNick();
        welcome = false;
        GUI.FocusControl(null);
    }
}
