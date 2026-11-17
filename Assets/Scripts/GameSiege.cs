using UnityEngine;

// Осада города — раз в день в 18:00 по Москве. У южных ворот на SiegeOpenMin минут открывается
// красный портал: войти можно одному или пати, как в обычный портал (GameTown.cs DrawPortalPanel). Сам бой — уровень
// «siege» (Levels.Siege): обычный забег на 15 минут на мостовой города, волны плотнее. Убитые в осаде идут в общий
// дневной счёт — орда у стен редеет, скидки торговцев растут. Сервер даёт за осаду добычу как с топей, но только
// если забег кончился в окне осады (server/siege.py) — иначе это был бы ещё один бесплатный уровень.
public partial class Game
{
    public const int SiegeHourMsk = 18, SiegeOpenMin = 15;   // как HOUR_MSK, OPEN_MIN в server/siege.py
    public const int SiegeLevel = 5;                         // индекс «Осады города» в Levels
    static readonly Vector2 SiegeGate = new(0f, -14.5f);     // у южного проёма ограды
    const float SiegeGateR = 2.3f;

    // часы браузера: для подписи и портала хватает, а добычу сервер сверяет по своим
    static System.DateTime MskNow => System.DateTime.UtcNow.AddHours(3);
    static double SiegeSecs   // секунд с начала сегодняшней осады (отрицательно — до неё)
    {
        get { var n = MskNow; return (n - n.Date.AddHours(SiegeHourMsk)).TotalSeconds; }
    }
    static bool SiegeOpen => siegeForce || SiegeSecs >= 0 && SiegeSecs < SiegeOpenMin * 60;
    bool SiegeSoon => siegeOn || SiegeOpen || SiegeSecs < 0 && SiegeSecs > -3600;
    static bool siegeForce;   // ?ui=siege на localhost — портал открыт в любое время, для скриншотов

    Transform siegeSigil, siegeTwirl;
    Material siegeMat, siegeTwirlMat;
    Light siegeLight;

    void BuildSiegeGate()
    {
        var c = Levels[SiegeLevel].color;
        siegeSigil = FloorDecal("sigil_portal", SiegeGate, SiegeGateR * 2.5f, 0.05f, out siegeMat);
        siegeTwirl = FloorDecal("twirl_01", SiegeGate, SiegeGateR * 1.7f, 0.06f, out siegeTwirlMat);
        siegeMat.SetColor("_Color", c * 1.8f);
        siegeTwirlMat.SetColor("_Color", c);
        siegeLight = new GameObject("SiegeLight").AddComponent<Light>();
        siegeLight.type = LightType.Point; siegeLight.color = c; siegeLight.range = 7; siegeLight.shadows = LightShadows.None;
        siegeLight.transform.position = World.W(SiegeGate, 1.6f);
    }

    // из TownStep после обычных порталов: в окне осады круг у ворот — тоже портал (myPortal = SiegeLevel)
    void SiegeTick(float dt, bool free)
    {
        // с 16.11 осада идёт в самом городе (GameSiegeTown.cs); портал не строится — BuildSiegeGate не зовётся
        bool open = SiegeOpen;
        if (siegeSigil == null) return;
        siegeSigil.gameObject.SetActive(open);
        siegeTwirl.gameObject.SetActive(open);
        siegeLight.enabled = open;
        if (!open) return;
        bool inside = (player.pos - SiegeGate).sqrMagnitude < SiegeGateR * SiegeGateR;
        if (free && inside) myPortal = SiegeLevel;
        siegeSigil.Rotate(0, 0, 14 * (inside ? 3 : 1) * dt, Space.Self);
        siegeTwirl.Rotate(0, 0, -70 * dt, Space.Self);
        siegeLight.intensity = (inside ? 9 : 6) + 2.5f * Mathf.Sin(time * 4);
        if (Random.value < dt * 22)
        {
            float a = Random.value * Mathf.PI * 2;
            Vfx.Emit(Vfx.Ember, World.W(SiegeGate + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * SiegeGateR * Random.Range(0.3f, 1f), 0.1f),
                Vector3.up * Random.Range(2f, 3.5f), 0.18f, 1.1f, new Color(2.2f, 0.7f, 0.4f));
        }
    }

    // строка в шапке города: до осады — обратный отсчёт, во время — что делать
    string SiegeLine()
    {
        if (siegeOn) return $"ОСАДА! ОРДА В ГОРОДЕ · УБИТО {siegeKilled} · ЕЩЁ {siegeLeft / 60}:{siegeLeft % 60:00} · ОТСТУПАТЬ — В ТАВЕРНУ";
        double s = SiegeSecs;
        int until = (int)(s < 0 ? -s : 86400 - s);
        return until < 3600 ? $"СТЕНА ПАДЁТ ЧЕРЕЗ {until / 60}:{until % 60:00} — ОСАДА ГОРОДА" : $"ОСАДА ГОРОДА В {SiegeHourMsk}:00 МСК · ЧЕРЕЗ {until / 3600} Ч {until % 3600 / 60:00} МИН";
    }
}
