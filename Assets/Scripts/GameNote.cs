using UnityEngine;

// Отзыв прямо из забега — на экране конца и в паузе (просьба игроков 16.11: «из самой игры, из сессии сразу»).
// Уходит той же дорогой, что идеи (/api/ideas → issue в GitHub), но с подписью забега: карта, минута, уровень,
// герой, сложность, вампиризм и заклинания — по ней разбирать жалобы вида «вампиризм не работает».
public partial class Game
{
    bool noteOpen, noteBusy;
    string noteText = "", noteStatus;

    void NoteOpen()
    {
        noteStatus = null;
#if UNITY_WEBGL && !UNITY_EDITOR
        if (TouchUi) { PhoneInput("Что понравилось или сломалось?", 600, t => { noteText = t; NoteSend(); }, noteText, 1); return; }
#endif
        noteOpen = true;
    }

    void NoteKeys()
    {
        var e = Event.current;
        if (noteOpen && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); noteOpen = false; }
    }

    string RunContext()
    {
        var spells = new System.Collections.Generic.List<string>();
        foreach (var s in skills) if (s.level > 0) spells.Add($"{s.title} {s.level}");
        return $"[из забега · {Levels[selLevel].title} · {CurDiff.title} · {Heroes[selHero].title} · {(int)time / 60}:{(int)time % 60:00} · ур. {level} · " +
               $"здоровье {(int)player.maxHp} · вампиризм {player.vamp * 100:0}% · {string.Join(", ", spells)}]";
    }

    void NoteSend()
    {
        var text = (noteText ?? "").Trim();
        if (noteBusy) return;
        if (text.Length < 4) { noteStatus = "Напиши хотя бы пару слов"; return; }
        noteBusy = true;
        noteStatus = "Отправляю…";
        // подпись забега не должна отрезаться: место под неё оставляем заранее (сервер берёт 600 символов)
        string ctx = RunContext();
        if (ctx.Length > 300) ctx = ctx.Substring(0, 300);
        if (text.Length > 600 - 2 - ctx.Length) text = text.Substring(0, 600 - 2 - ctx.Length);
        var json = JsonUtility.ToJson(new IdeaPost { text = text + "\n\n" + ctx, voter = Voter() });
        StartCoroutine(Send("POST", "ideas", json, (code, body) =>
        {
            noteBusy = false;
            if (code == 200) { noteText = ""; noteStatus = "Спасибо! Отзыв ушёл разработчику"; noteOpen = false; }
            else
            {
                string err = null;
                try { err = JsonUtility.FromJson<ApiReply>(body)?.error; } catch { }   // nginx на 502 отдаёт HTML
                noteStatus = code == 0 ? "Нет связи с сервером" : err ?? "Ошибка " + code;
            }
        }));
    }

    // окно поверх экрана; true — открыто (остальное под ним не нажимается)
    bool DrawNote(float W, float H)
    {
        if (!noteOpen)
        {
            if (noteStatus != null) ChipAt(W / 2, 112, noteStatus, new Color(0.6f, 1f, 0.7f));
            return false;
        }
        NoteKeys();
        Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.7f));
        float pw = Mathf.Min(760, W - 32), ph = 360, x = (W - pw) / 2, y = Mathf.Max(20, (H - ph) / 2 - 40);
        var box = new Rect(x, y, pw, ph);
        Plate(box, new Color(0.05f, 0.04f, 0.07f, 0.97f), Gold, 16, 2);
        Outline(new Rect(x, y + 10, pw, 46), "Отзыв о забеге", Fit("Отзыв о забеге", pw - 40, Gold), 2);
        var field = new Rect(x + 20, y + 66, pw - 40, 170);
        RR(field, new Color(0.03f, 0.025f, 0.04f, 0.96f), 12);
        var area = new GUIStyle(tiny) { normal = { textColor = Bone }, focused = { textColor = Bone }, wordWrap = true };
        noteText = GUI.TextArea(new Rect(field.x + 12, field.y + 10, field.width - 24, field.height - 20), noteText, 500, area);
        if (string.IsNullOrEmpty(noteText))
            Text(new Rect(field.x + 16, field.y + 12, field.width - 32, 30), "<color=#77736C>Что понравилось, что сломалось, чего не хватает…</color>", Col(tiny, Bone, TextAnchor.UpperLeft), 2);
        if (noteStatus != null) Text(new Rect(x, field.yMax + 4, pw, 30), noteStatus, Col(tinyS ??= new GUIStyle(tiny) { fontSize = 21 }, new Color(1f, 0.85f, 0.55f), TextAnchor.MiddleCenter), 2);
        float bw = (pw - 56) / 2;
        if (Btn(new Rect(x + 20, box.yMax - 84, bw, 68), "Закрыть", Bone)) noteOpen = false;
        if (Btn(new Rect(x + 36 + bw, box.yMax - 84, bw, 68), noteBusy ? "…" : "Отправить", Gold)) NoteSend();
        return true;
    }
}
