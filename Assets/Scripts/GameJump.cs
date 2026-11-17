using UnityEngine;

// Прыжок-подлёт: пробел или круглая кнопка справа внизу. Маг по дуге проносится вперёд над толпой и декором,
// в воздухе неуязвим, при приземлении волна отбрасывает скелетов. Перезарядка показана заливкой кнопки.
public partial class Game
{
    const float JumpTime = 0.55f, JumpCooldown = 2.5f;
    float jumpT, jumpCd;
    Vector2 jumpDir = Vector2.up;

    static float JumpR => Mathf.Min(Screen.width, Screen.height) * 0.085f;
    // в экранных координатах ввода (y снизу)
    static Vector2 JumpCenter => new(Screen.width - JumpR * 1.5f, JumpR * 1.5f + Screen.height * 0.03f);
    static bool InJumpBtn(Vector2 p) => (p - JumpCenter).magnitude < JumpR * 1.25f;

    void TryJump(Vector2 move)
    {
        if (state != State.Play || jumpT > 0 || jumpCd > 0) return;
        jumpT = JumpTime;
        jumpCd = JumpCooldown;
        jumpDir = move.sqrMagnitude > 0.01f ? move.normalized : player.face;
        Vfx.Burst(Vfx.Smoke, World.W(player.pos, 0.2f), 6, 2.5f, 0.9f, 0.6f, new Color(0.35f, 0.3f, 0.28f, 0.6f), 0.6f, 0.5f);
        Vfx.Burst(Vfx.Ember, World.W(player.pos, 0.4f), 12, 3, 0.15f, 0.5f, new Color(0.8f, 0.7f, 1f), 1.2f, 0.4f);
    }

    void Land()
    {
        // приземление: волна отбрасывает всех рядом и чуть ранит
        const float r = 2.6f;
        Query(player.pos, r, tmp);
        foreach (var e in tmp) Hit(e, 6 + level * 1.5f, (e.pos - player.pos).normalized, 1.6f);
        Vfx.Emit(Vfx.Ring, World.W(player.pos, 0.1f), Vector3.zero, r * 2.4f, 0.4f, new Color(0.75f, 0.6f, 1f));
        Vfx.Burst(Vfx.Smoke, World.W(player.pos, 0.2f), 10, 4, 1.1f, 0.7f, new Color(0.3f, 0.26f, 0.24f, 0.7f), 0.3f, 1f);
        Vfx.Burst(Vfx.Bone, World.W(player.pos, 0.1f), 8, 5, 0.08f, 0.5f, new Color(0.35f, 0.28f, 0.2f), 1f);
        Shake(0.25f);
        squashT = 1;
        // если приземлился в надгробие — вытолкнет
        player.pos = World.Collide(player.pos, 0.65f);
        CoopSendLand();
    }

    void DrawJumpButton(float u)
    {
        if (state != State.Play) return;
        var c = JumpCenter;
        float R = JumpR / u;
        var g = new Vector2(c.x / u, (Screen.height - c.y) / u);
        var r = new Rect(g.x - R, g.y - R, 2 * R, 2 * R);
        float k = 1 - jumpCd / JumpCooldown;
        RR(r, new Color(0.04f, 0.03f, 0.06f, 0.55f), R);
        // перезарядка — заливка снизу вверх
        if (k < 1)
        {
            GUI.BeginGroup(new Rect(r.x, r.y + r.height * (1 - k), r.width, r.height * k));
            RR(new Rect(0, -r.height * (1 - k), r.width, r.height), new Color(0.6f, 0.45f, 1f, 0.25f), R);
            GUI.EndGroup();
        }
        RRLine(r, k >= 1 ? new Color(0.8f, 0.7f, 1f, 0.9f) : new Color(1, 1, 1, 0.2f), k >= 1 ? 2 : 1, R);
        Text(r, k >= 1 ? "ПРЫЖОК" : $"{jumpCd:0.0}", Col(caps, k >= 1 ? Color.white : new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter), 2);
    }
}
