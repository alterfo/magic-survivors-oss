using System.Collections.Generic;
using UnityEngine;

// Орда у стен города: остаток до цели дня (убитые всеми, /api/world → horde) — это скелеты, идущие на город.
// За стеной площади стоят скелеты, по одному на каждые 50 000 оставшихся (до 20): убиваешь в данжах — их меньше.
// Скидка торговцев растёт с убитыми (server/presence.py SALE_TIERS) — Sale() применяет её к ценам на экране,
// списывает со скидкой сервер. Идея: «монстры могут нападать на город, убивая их в данже — сокращаешь».
public partial class Game
{
    static int SalePct => world != null ? Mathf.Clamp(world.sale, 0, 90) : 0;
    static int Sale(int price) => price <= 0 ? price : price - price * SalePct / 100;   // как presence.discounted

    readonly List<(Transform tr, Animator anim, float phase)> horde = new();
    static readonly string[] HordeModels = { "Skeleton_Minion", "Skeleton_Warrior", "Skeleton_Rogue", "Skeleton_Mage" };

    void HordeTick(float dt)
    {
        // у стен тесно: один на каждые 15 000 оставшихся, не меньше 12, пока орда не отбита (фидбек 16.11 «мобов мало»);
        // в осаду стена падает — эти уходят, на площадь идут настоящие (GameSiegeTown.cs)
        int want = world == null || world.horde <= 0 || siegeOn ? 0 : Mathf.Clamp(Mathf.CeilToInt(world.horde / 15000f), 12, 60);
        while (horde.Count > want)
        {
            var h = horde[horde.Count - 1];
            if (h.tr != null) Destroy(h.tr.gameObject);
            horde.RemoveAt(horde.Count - 1);
        }
        while (horde.Count < want)
        {
            int i = horde.Count;
            var go = World.Character(HordeModels[i % HordeModels.Length], 1f, out var anim, out _);
            go.name = "Horde";
            go.transform.localScale *= 1.5f;
            // за стеной (площадь — радиус Town.Radius), веером с юга и боков: видно с площади, дойти нельзя
            float a = Mathf.PI * (1.15f + 0.7f * (i * 0.618f % 1f));
            var pos = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Town.Radius + 3.5f + (i % 3) * 1.4f);
            // таверна выходит за ограду: тех, кто попал бы в дом, отодвигаем дальше за его стены
            while (new Rect(Town.TavernHouse.x - 1.5f, Town.TavernHouse.y - 1.5f, Town.TavernHouse.width + 3, Town.TavernHouse.height + 3).Contains(pos)) pos *= 1.08f;
            go.transform.position = World.W(pos);
            go.transform.rotation = Quaternion.LookRotation(World.W(-pos));
            anim.Play("Idle", 0, Random.value);
            horde.Add((go.transform, anim, Random.value * 6));
        }
        // переминаются и изредка замахиваются на стену — живая угроза, а не декорация
        foreach (var (tr, anim, ph) in horde)
            if (tr != null && Random.value < dt * 0.06f) anim.CrossFade("Attack", 0.1f);
    }
}
