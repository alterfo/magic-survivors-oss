using UnityEngine;

// Фоновая музыка: мрачная оркестровка в меню и забеге, драм-н-бейс пока жив босс (AnyBoss).
// Треки сгенерированы Lyria 3 через OpenRouter, лежат в StreamingAssets/Music (<уровень>-calm|boss.mp3): не в Resources,
// чтобы не раздувать стартовую загрузку — браузер качает только пару треков выбранного уровня, уже после старта. Два источника — переход за FadeTime,
// повтор трека тоже через переход (треки не идеально зациклены). Громкость — PlayerPrefs "music" (0..1).
public partial class Game
{
    const float FadeTime = 1.5f, LoopOverlap = 3f;
    AudioSource[] musicSrc;
    int musicCur;
    AudioClip musicCalm, musicBoss;
    static readonly System.Collections.Generic.Dictionary<string, AudioClip> musicCache = new();  // переживает перезагрузку сцены

    const float MusicGain = 0.3f;   // общий потолок музыки: на «ГРОМКО» она не должна перекрывать звуки боя

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat("music", 0.5f);
        set { PlayerPrefs.SetFloat("music", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    void MusicInit()
    {
        // у каждого уровня своя пара треков; пока трек качается — тишина
        string key = Levels[selLevel].id;
        // своих треков у новых уровней пока нет: пустошь звучит как склеп, разлом — как топи
        key = key == "frost" ? "crypt" : key == "abyss" || key == "siege" ? "swamp" : key;
        StartCoroutine(LoadMusic(key + "-calm", c => musicCalm = c));
        StartCoroutine(LoadMusic(key + "-boss", c => musicBoss = c));
        if (!FindAnyObjectByType<AudioListener>()) (Camera.main ? Camera.main.gameObject : gameObject).AddComponent<AudioListener>();
        musicSrc = new AudioSource[2];
        for (int i = 0; i < 2; i++)
        {
            musicSrc[i] = gameObject.AddComponent<AudioSource>();
            musicSrc[i].playOnAwake = false;
            musicSrc[i].spatialBlend = 0;
            musicSrc[i].volume = 0;
        }
    }

    // вызывать каждый кадр из Update (в т.ч. в меню и на паузе)
    void MusicTick(float dt)
    {
        if (bot)
        {
            // музыка могла успеть заиграть в меню до того, как запуск распознан как тестовый
            if (musicSrc != null) foreach (var s in musicSrc) if (s.isPlaying) s.Stop();
            return;
        }
        if (musicSrc == null) MusicInit();
        var want = state == State.Menu || !AnyBoss() ? musicCalm : musicBoss;
        if (want && want.loadState != AudioDataLoadState.Loaded) want = null;  // в WebGL клип ещё декодируется
        float level = state == State.Dead || state == State.Won ? 0
                    : state == State.Play ? 1 : state == State.Menu ? 0.6f : 0.45f;  // меню тише боя; карточки, пауза — ещё тише
        var cur = musicSrc[musicCur];
        if (want && (cur.clip != want || (cur.isPlaying && cur.time > want.length - LoopOverlap) || !cur.isPlaying && level > 0))
        {
            musicCur ^= 1;
            cur = musicSrc[musicCur];
            cur.clip = want;
            cur.time = 0;
            cur.volume = 0;
            cur.Play();
        }
        float step = dt / FadeTime;
        for (int i = 0; i < 2; i++)
        {
            var s = musicSrc[i];
            float target = i == musicCur ? level * MusicVolume * MusicDuck * MusicGain : 0;
            // нарастает втрое медленнее, чем стихает: музыка не бьёт по ушам при запуске и после паузы
            s.volume = Mathf.MoveTowards(s.volume, target, step * (target > s.volume ? 0.35f : 1f) * Mathf.Max(MusicVolume, 0.01f));
            if (i != musicCur && s.isPlaying && s.volume <= 0) s.Stop();
        }
    }

    System.Collections.IEnumerator LoadMusic(string name, System.Action<AudioClip> done)
    {
        if (musicCache.TryGetValue(name, out var cached)) { done(cached); yield break; }
        string path = Application.streamingAssetsPath + "/Music/" + name + ".mp3";
        if (!path.Contains("://")) path = "file://" + path;  // desktop: обычный путь на диске
        using var r = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(path, AudioType.MPEG);
        yield return r.SendWebRequest();
        if (r.result != UnityEngine.Networking.UnityWebRequest.Result.Success) yield break;
        var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(r);
        musicCache[name] = clip;
        done(clip);
    }
}
