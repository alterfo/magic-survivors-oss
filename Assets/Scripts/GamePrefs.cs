using System.Globalization;
using UnityEngine;

// Наряд, выбранный герой и настройки — на сервере (server/prefs.py), у аккаунта. Фидбек: «чтобы игра
// запоминала, в каком наряде я вышел, какие настройки стояли — всё пускай хранится; а то в городе я в одном шмоте,
// а бегаю в другом». PlayerPrefs остаются рабочей копией: раз в 3 с сверяем их с последним отправленным и шлём,
// если что-то поменялось; при первой загрузке кошелька серверное значение главнее (новое устройство — свой наряд).
public partial class Game
{
    static readonly string[] PrefInts = { "sel.hero", "sel.mode", "chat.min", "opt.numbers", "opt.shake", "opt.bloom", "opt.fps", "opt.calm", "opt.high", "opt.mute" };
    static readonly string[] PrefFloats = { "music", "sfx" };
    static readonly string[] PrefStrings = { "cos.hat", "cos.robe", "cos.staff", "cos.glow" };
    static string prefsSent;        // что сервер уже знает; null — ещё не сверялись
    public static bool prefsSwitched;   // профиль сменился входом или выходом (GameAccount) — не наследуем настройки прежнего

    static void ClearPrefKeys()
    {
        foreach (var k in PrefInts) PlayerPrefs.DeleteKey(k);
        foreach (var k in PrefFloats) PlayerPrefs.DeleteKey(k);
        foreach (var k in PrefStrings) PlayerPrefs.DeleteKey(k);
    }

    // только то, что поменялось с последней отправки: сервер сливает (server/prefs.py)
    static string PrefsDelta(string blob, string sent)
    {
        var old = new System.Collections.Generic.HashSet<string>((sent ?? "").Split('\n'));
        var sb = new System.Text.StringBuilder();
        foreach (var l in blob.Split('\n')) if (l.Length > 0 && !old.Contains(l)) sb.Append(l).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
    static bool prefsBusy;
    float prefsAt;
    string builtLook;               // наряд, в котором собран маг этой сцены

    static string PrefsBlob()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var k in PrefInts) if (PlayerPrefs.HasKey(k)) sb.Append(k).Append('=').Append(PlayerPrefs.GetInt(k)).Append('\n');
        foreach (var k in PrefFloats) if (PlayerPrefs.HasKey(k)) sb.Append(k).Append('=').Append(PlayerPrefs.GetFloat(k).ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var k in PrefStrings) if (PlayerPrefs.HasKey(k)) sb.Append(k).Append('=').Append(PlayerPrefs.GetString(k)).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    // из ApplyWallet при первой загрузке кошелька профиля
    void ApplyServerPrefs(string data)
    {
        // у сервера пусто: первое устройство — отправим свои; сменили аккаунт — настройки прежнего не тянем
        if (string.IsNullOrEmpty(data))
        {
            if (prefsSwitched) { ClearPrefKeys(); optLoaded = false; LoadOpts(); }
            prefsSwitched = false;
            prefsSent = "";
            return;
        }
        prefsSwitched = false;
        ClearPrefKeys();   // ключей нет на сервере — значения по умолчанию, а не прежнего профиля
        foreach (var line in data.Split('\n'))
        {
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            string k = line.Substring(0, i), v = line.Substring(i + 1);
            if (System.Array.IndexOf(PrefInts, k) >= 0 && int.TryParse(v, out var n)) PlayerPrefs.SetInt(k, n);
            else if (System.Array.IndexOf(PrefFloats, k) >= 0 && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) PlayerPrefs.SetFloat(k, f);
            else if (System.Array.IndexOf(PrefStrings, k) >= 0) PlayerPrefs.SetString(k, v);
        }
        PlayerPrefs.Save();
        prefsSent = PrefsBlob();
        // настройки перечитываем, наряд и герой подхватят LookTick и город (sel.hero)
        optLoaded = false;
        LoadOpts();
        selMode = Mathf.Clamp(PlayerPrefs.GetInt("sel.mode", 0), 0, 1);
        chatCollapsed = PlayerPrefs.GetInt("chat.min", 0) == 1;
    }

    void PrefsTick()
    {
        if (bot || LabOn || !walletOk || prefsSent == null || prefsBusy || Time.unscaledTime < prefsAt) return;
        prefsAt = Time.unscaledTime + 3;
        LookTick();
        var blob = PrefsBlob();
        if (blob == prefsSent) return;
        var delta = PrefsDelta(blob, prefsSent);
        if (delta.Length == 0) { prefsSent = blob; return; }
        prefsBusy = true;
        int gen = walletGen;
        // через NetRunner: корутина сцены умерла бы при входе в забег, и prefsBusy остался бы навсегда (ревью Codex)
        NetRunner.I.StartCoroutine(Send("POST", "prefs", JsonUtility.ToJson(new PrefsPost { voter = Voter(), data = delta, token = AccToken }), (code, body) =>
        {
            prefsBusy = false;
            if (code == 200 && gen == walletGen) prefsSent = blob;
            else if (code != 200) prefsAt = Time.unscaledTime + 20;   // нет связи или не вошёл — попробуем позже
        }));
    }

    [System.Serializable] class PrefsPost { public string voter, data, token; }

    static string LookKey() => Wearing("hat") + "|" + Wearing("robe") + "|" + Wearing("staff") + "|" + Wearing("glow");

    // купленное узнаём после загрузки кошелька: сцена могла собраться в бесплатном наряде — переодеваем
    void LookTick()
    {
        if (builtLook == null || player.go == null || Heroes[selHero].id != "mage" || builtHero != "mage") return;
        var key = LookKey();
        if (key == builtLook) return;
        builtLook = key;
        Cosmetics.Apply(player.go, Wearing("hat"), Wearing("robe"), Wearing("staff"), Wearing("glow"));
        ApplyMyWeapon();
        SetXRay(false);
        playerRends = PlayerRends();
        // остальные в городе видят наряд из описания при входе — перезаходим (в пати — позже, как с ником)
        if (state == State.Town) TownRejoin();
    }
}
