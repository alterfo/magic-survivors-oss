using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;

// WebSocket для кооператива: в браузере — Coop.jslib, в редакторе и на ПК — ClientWebSocket в фоновых задачах.
// Снаружи одно и то же: Send, TryRecv (опрос из Update), Open/Dead.
public class NetSocket
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void CoopOpen(string url);
    [DllImport("__Internal")] static extern int CoopState();
    [DllImport("__Internal")] static extern void CoopSend(byte[] data, int len);
    [DllImport("__Internal")] static extern int CoopPeek();
    [DllImport("__Internal")] static extern int CoopRecv(byte[] buf, int max);
    [DllImport("__Internal")] static extern void CoopClose();

    public NetSocket(string url) => CoopOpen(url);
    public bool Open => CoopState() == 1;
    public bool Dead => CoopState() == 3;
    public void Send(byte[] data) => CoopSend(data, data.Length);
    public void Close() => CoopClose();

    public bool TryRecv(out byte[] msg)
    {
        int n = CoopPeek();
        if (n < 0) { msg = null; return false; }
        msg = new byte[n];
        CoopRecv(msg, n);
        return true;
    }
#else
    readonly System.Net.WebSockets.ClientWebSocket ws = new();
    readonly ConcurrentQueue<byte[]> inbox = new(), outbox = new();
    readonly System.Threading.SemaphoreSlim pending = new(0);
    readonly System.Threading.CancellationTokenSource stop = new();
    volatile bool open, dead;

    public NetSocket(string url) => Run(url);
    public bool Open => open && !dead;
    public bool Dead => dead;

    public void Send(byte[] data)
    {
        if (dead) return;
        outbox.Enqueue(data);
        pending.Release();
    }

    public bool TryRecv(out byte[] msg) => inbox.TryDequeue(out msg);

    public void Close()
    {
        dead = true;
        stop.Cancel();
        try { ws.Abort(); } catch { }
    }

    async void Run(string url)
    {
        try
        {
            await ws.ConnectAsync(new System.Uri(url), stop.Token);
            open = true;
            _ = Pump();
            var buf = new byte[1 << 16];
            var acc = new System.IO.MemoryStream();
            while (!stop.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(new System.ArraySegment<byte>(buf), stop.Token);
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                acc.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                inbox.Enqueue(acc.ToArray());
                acc.SetLength(0);
            }
        }
        catch (System.Exception e) { if (!stop.IsCancellationRequested) Debug.Log("coop socket: " + e.Message); }
        dead = true;
    }

    // отправка строго по одной: ClientWebSocket не любит параллельные SendAsync
    async System.Threading.Tasks.Task Pump()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await pending.WaitAsync(stop.Token);
                if (outbox.TryDequeue(out var m))
                    await ws.SendAsync(new System.ArraySegment<byte>(m), System.Net.WebSockets.WebSocketMessageType.Binary, true, stop.Token);
            }
        }
        catch { dead = true; }
    }
#endif
}
