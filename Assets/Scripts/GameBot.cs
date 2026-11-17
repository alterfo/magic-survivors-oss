using System.Text;
using UnityEngine;

// Автопилот для прогона баланса: убегает от толпы, собирает опыт, сам выбирает улучшения и пишет срезы раз в 30 с.
public partial class Game
{
    public static bool bot;
    public static readonly StringBuilder botLog = new();
    float botNext;

    Vector2 BotMove()
    {
        var away = Vector2.zero;
        foreach (var e in enemies)
        {
            if (!e.alive) continue;
            var d = player.pos - e.pos;
            float m = d.sqrMagnitude;
            if (m < 49) away += d / Mathf.Max(0.3f, m) * (e.type.boss || e.rage ? 3 : 1);
        }
        // цель: ближайший кристалл, сердце или реликвия, если вокруг не слишком тесно
        Vector2 best = Vector2.zero; float bd = 1e9f;
        foreach (var g in gems)
        {
            if (!g.alive) continue;
            float m = (g.pos - player.pos).sqrMagnitude;
            if (g.heal && player.hp < player.maxHp * 0.7f) m *= 0.2f;
            if (m < bd) { bd = m; best = g.pos; }
        }
        foreach (var r in relics) if (r.alive && (r.pos - player.pos).sqrMagnitude * 0.5f < bd) { bd = (r.pos - player.pos).sqrMagnitude * 0.5f; best = r.pos; }
        foreach (var c in chests) if (c.alive && (c.pos - player.pos).sqrMagnitude * 0.5f < bd) { bd = (c.pos - player.pos).sqrMagnitude * 0.5f; best = c.pos; }
        var v = away * 2.2f;
        if (bd < 400) v += (best - player.pos).normalized * 0.8f;
        // кружим, а не убегаем по прямой в бесконечность
        v += new Vector2(-player.pos.y, player.pos.x).normalized * 0.25f - player.pos * 0.01f;
        return v.sqrMagnitude > 1 ? v.normalized : v;
    }

    void BotTick()
    {
        if (coopRun && state == State.Play && Random.value < 0.02f) OpenChoice(); // автопилот открывает карты не сразу, как человек
        if (state == State.LevelUp)
        {
            int pick = 0, best = -999;
            int spells = skills.FindAll(s => s.spell && !s.fusion && s.level > 0).Count;
            for (int i = 0; i < choices.Count; i++)
            {
                var s = choices[i];
                int sc = s.fusion ? 100 : s.spell && s.level > 0 ? 50 + s.level : s.spell ? (spells < 4 ? 40 : 5) : 20 - s.level;
                if (sc > best) { best = sc; pick = i; }
            }
            Pick(LabOn ? LabPick() : pick);
        }
        else if (state == State.Super) PickSuper(Random.Range(0, 3));
        else if (state == State.Chest) PickArtifact(0);
        if (state == State.Play && time >= botNext)
        {
            botNext += 30;
            int alive = 0;
            foreach (var e in enemies) if (e.alive) alive++;
            botLog.AppendLine($"t={time / 60:0.0} lv={level} hp={player.hp:0}/{player.maxHp:0} kills={kills} en={alive} tier={(int)(time / 150)} spells={SpellSummary()}");
        }
    }

    string SpellSummary()
    {
        var sb = new StringBuilder();
        foreach (var s in skills) if (s.level > 0 && s.spell) sb.Append(s.icon).Append(s.level).Append(s.super >= 0 ? "*" : "").Append(' ');
        return sb.ToString();
    }
}
