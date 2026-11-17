using System.Collections.Generic;
using UnityEngine;

// Звуки (Kenney, CC0; отбор и выравнивание пиков — tools/sfx_import.py) и микшер, чтобы не было какофонии:
// у каждого звука громкость, лимит голосов, минимальный интервал, разброс высоты и приоритет; общий пул голосов
// с вытеснением младших; убийства за кадр сливаются в один звук, громкость растёт по логарифму от числа;
// кристаллы подряд поднимаются по полутонам; джинглы и босс приглушают музыку (MusicDuck).
// События не вызываются из геймплея, а замечаются по разнице состояния между кадрами — Game.cs и Skills.cs не трогаем.
public partial class Game
{
    class SfxMix
    {
        public float vol, gap, pitchVar; public int voices, prio; public bool duck;
        public AudioClip[] clips; public float last = -9; public int playing;
    }

    // id: громкость, голосов, мин. интервал с, разброс высоты, приоритет, глушить музыку
    static readonly (string id, float vol, int voices, float gap, float pitchVar, int prio, bool duck)[] SfxTable =
    {
        ("kill",     0.30f, 3, 0.06f, 0.12f, 1, false),
        ("gem",      0.22f, 2, 0.05f, 0f,    1, false),
        ("cast",     0.16f, 2, 0.15f, 0.10f, 2, false),
        ("boom",     0.45f, 2, 0.18f, 0.10f, 2, false),
        ("hurt",     0.55f, 1, 0.35f, 0.08f, 3, false),
        ("pick",     0.45f, 1, 0.08f, 0.03f, 3, false),
        ("open",     0.40f, 1, 0.10f, 0f,    3, false),
        ("close",    0.40f, 1, 0.10f, 0f,    3, false),
        ("levelUp",  0.26f, 1, 0.40f, 0.04f, 3, false),
        ("super",    0.65f, 1, 0.40f, 0f,    4, true),
        ("chest",    0.60f, 1, 0.40f, 0f,    4, true),
        ("bossIn",   0.85f, 1, 2.00f, 0f,    5, true),
        ("bossDead", 0.90f, 1, 2.00f, 0f,    5, true),
        ("dead",     0.75f, 1, 1.00f, 0f,    5, true),
        ("won",      0.75f, 1, 1.00f, 0f,    5, true),
    };
    const int SfxVoices = 16;
    const float DuckLevel = 0.4f;  // ≈ -8 дБ музыки на джинглах

    Dictionary<string, SfxMix> sfx;
    AudioSource[] sfxSrc;
    SfxMix[] sfxOwner;
    int[] sfxPrio;
    float duckUntil, gemCombo, gemComboT;

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat("sfx", 0.7f);
        set { PlayerPrefs.SetFloat("sfx", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    // множитель громкости музыки: 1 обычно, DuckLevel пока звучит джингл (GameMusic умножает на него)
    float MusicDuck => Time.unscaledTime < duckUntil ? DuckLevel : 1;

    void SfxInit()
    {
        sfx = new Dictionary<string, SfxMix>();
        foreach (var t in SfxTable)
        {
            var clips = new List<AudioClip>();
            for (int i = 0; i < 8; i++)
            {
                var c = Resources.Load<AudioClip>($"Sfx/{t.id}_{i}");
                if (!c) break;
                clips.Add(c);
            }
            sfx[t.id] = new SfxMix { vol = t.vol, voices = t.voices, gap = t.gap, pitchVar = t.pitchVar, prio = t.prio, duck = t.duck, clips = clips.ToArray() };
        }
        sfxSrc = new AudioSource[SfxVoices];
        sfxOwner = new SfxMix[SfxVoices];
        sfxPrio = new int[SfxVoices];
        for (int i = 0; i < SfxVoices; i++)
        {
            sfxSrc[i] = gameObject.AddComponent<AudioSource>();
            sfxSrc[i].playOnAwake = false;
            sfxSrc[i].spatialBlend = 0;
        }
    }

    // проиграть звук; gain — доп. множитель громкости, pitch — доп. множитель высоты
    public void Sfx(string id, float gain = 1, float pitch = 1)
    {
        if (bot || SfxVolume <= 0) return;
        if (sfx == null) SfxInit();
        if (!sfx.TryGetValue(id, out var m) || m.clips.Length == 0) return;
        float now = Time.unscaledTime;
        if (now - m.last < m.gap) return;
        // освобождаем закончившие голоса
        for (int i = 0; i < SfxVoices; i++)
            if (sfxOwner[i] != null && !sfxSrc[i].isPlaying) { sfxOwner[i].playing--; sfxOwner[i] = null; }
        if (m.playing >= m.voices) return;
        int v = -1;
        for (int i = 0; i < SfxVoices && v < 0; i++) if (sfxOwner[i] == null) v = i;
        if (v < 0)
        {
            // все голоса заняты — вытесняем самый младший по приоритету (из равных — дольше всех звучащий)
            float best = float.MaxValue;
            for (int i = 0; i < SfxVoices; i++)
            {
                if (sfxPrio[i] > m.prio) continue;
                float score = sfxPrio[i] * 1000 - sfxSrc[i].time;
                if (score < best) { best = score; v = i; }
            }
            if (v < 0) return;
            sfxSrc[v].Stop();
            sfxOwner[v].playing--;
        }
        var s = sfxSrc[v];
        s.clip = m.clips[Random.Range(0, m.clips.Length)];
        s.volume = m.vol * gain * SfxVolume;
        s.pitch = pitch * (1 + Random.Range(-m.pitchVar, m.pitchVar));
        s.Play();
        sfxOwner[v] = m; sfxPrio[v] = m.prio; m.playing++; m.last = now;
        if (m.duck) duckUntil = Mathf.Max(duckUntil, now + s.clip.length / Mathf.Max(0.1f, s.pitch) + 0.3f);
    }

    // ---------- слежение за событиями ----------

    State sfxState = State.Menu;
    int sfxKills, sfxLevel;
    float sfxXp, sfxHp, sfxRune, sfxShake;
    bool sfxBoss;

    // вызывать каждый кадр из Update (в т.ч. в меню и на паузе), до или после шага мира — без разницы
    void SfxTick(float dt)
    {
        if (bot) return;
        var st = state;
        if (st != sfxState)
        {
            switch (st)
            {
                case State.LevelUp: Sfx("levelUp"); break;
                case State.Super: Sfx("super"); break;
                case State.Chest: Sfx("chest"); break;
                case State.Dead: Sfx("dead"); break;
                case State.Won: Sfx("won"); break;
                case State.Pause: Sfx("open"); break;
                case State.Play:
                    if (sfxState == State.Pause) Sfx("close");
                    else if (sfxState == State.LevelUp || sfxState == State.Super || sfxState == State.Chest) Sfx("pick");
                    break;
            }
            if (sfxState == State.Menu || st == State.Menu) SfxSnapshot();  // новый забег — без звуков за прошлый
            sfxState = st;
        }
        if (st != State.Play) { SfxSnapshot(); return; }

        int dk = kills - sfxKills;
        if (dk > 0) Sfx("kill", Mathf.Min(1.6f, 1 + 0.25f * Mathf.Log(dk, 2)));
        if (level > sfxLevel || xp > sfxXp + 0.001f)
        {
            // кристаллы подряд — выше на полутон, до октавы; пауза 0.5 с — сброс
            gemCombo = Time.unscaledTime - gemComboT < 0.5f ? Mathf.Min(gemCombo + 1, 12) : 0;
            gemComboT = Time.unscaledTime;
            Sfx("gem", 1, Mathf.Pow(2, gemCombo / 12f));
        }
        if (player.hp < sfxHp - 0.01f) Sfx("hurt");
        if (runeT > sfxRune + 0.3f) Sfx("cast");
        if (shake > sfxShake + 0.15f) Sfx("boom", Mathf.Clamp(shake, 0.6f, 1.3f));
        bool boss = AnyBoss();
        if (boss && !sfxBoss) Sfx("bossIn");
        if (!boss && sfxBoss) Sfx("bossDead");
        sfxBoss = boss;
        SfxSnapshot();
    }

    void SfxSnapshot()
    {
        sfxKills = kills; sfxLevel = level; sfxXp = xp; sfxRune = runeT; sfxShake = shake;
        sfxHp = player != null ? player.hp : 0;
        if (state != State.Play) sfxBoss = state != State.Menu && sfxBoss;
    }
}
