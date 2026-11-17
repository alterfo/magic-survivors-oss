using UnityEngine;

// Поднять мёртвых: стартовое заклинание некроманта. Раз в несколько секунд из земли встаёт свой скелет,
// а убитые рядом враги с шансом поднимаются союзниками. Сами союзники живут в GameAllies.cs.
public class RaiseDead : Skill
{
    float cd = 0.6f;
    public static readonly Color Necro = new(0.45f, 1f, 0.5f);
    public RaiseDead()
    {
        title = "Поднять мёртвых"; spell = true; max = 8; icon = "raisedead"; element = Element.None; color = Necro;
        superNames = new[] { "Легион", "Костяной взрыв", "Рыцари смерти" };
        superDescs = new[] { "+4 союзника, шанс подъёма ×1.5", "Павший союзник взрывается осколками костей", "Союзники крупнее, урон и здоровье ×2" };
    }
    public static float Dmg(int l) => 10 + 5 * (l - 1);
    public static float Life(int l) => 12 + l;
    public static float Hp(int l) => 30 + 10 * (l - 1);
    static int BaseCap(int l) => 3 + l / 2;
    static float BaseChance(int l) => 0.12f + 0.03f * l;
    static float Every(int l) => 3.8f - 0.25f * l;
    public int Cap => BaseCap(level) + (super == 0 ? 4 : 0);
    public float Chance => BaseChance(level) * (super == 0 ? 1.5f : 1);
    // радиус, в котором павшие враги слышат зов
    public const float Reach = 7f;
    public override string Desc(int l) => l == 1 ? "Павшие рядом скелеты встают за вас и бьют врагов"
        : $"Урон {Dmg(l - 1)} → {Dmg(l)}, союзников до {BaseCap(l)}, подъём {BaseChance(l) * 100:0}%";

    public override void Tick(Game g, float dt)
    {
        cd -= dt;
        if (cd > 0 || g.AllyCount >= Cap) return;
        cd = Every(level) * g.player.cdMul;
        // свой скелет встаёт рядом с некромантом: без этого на старте некого поднимать
        var at = g.player.pos + Random.insideUnitCircle.normalized * Random.Range(1.2f, 2f);
        g.RaiseAlly(at, level >= 5 ? "Skeleton_Warrior" : "Skeleton_Minion", level >= 5 ? "Skeleton_Axe" : null);
        g.Cast(color);
    }
}
