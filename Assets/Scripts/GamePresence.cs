using UnityEngine;

// Онлайн для админки: пока игра открыта, раз в 30 с реального времени шлём /api/ping (server/presence.py).
// Считает и меню, и паузу — в отличие от телеметрии, которая идёт по игровым минутам.
public partial class Game
{
    float presenceNext;

    void PresenceTick(float dt)
    {
        if (bot || Time.unscaledTime < presenceNext) return;
        presenceNext = Time.unscaledTime + 30;
        string st = state == State.Menu || state == State.Town ? "menu" : "play";
        StartCoroutine(Send("POST", "ping", $"{{\"voter\":\"{Voter()}\",\"state\":\"{st}\"}}", (code, body) => { }));
    }
}
