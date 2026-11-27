using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Desktop3D.Shell;

/// <summary>The shell's local loopback connection to the tray controller (for Game Mode button, etc.). Reconnects every 5 s.</summary>
sealed class ControllerLink : IDisposable
{
    readonly Uri uri;
    readonly string token;
    readonly CancellationTokenSource stop = new();
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly object gate = new();
    ClientWebSocket? current;

    /// <summary>Sends one command to the controller. False if it isn't reachable.</summary>
    public async Task<bool> SendAsync(JsonObject msg)
    {
        await sendLock.WaitAsync();
        try
        {
            ClientWebSocket? ws;
            lock (gate) ws = current;
            if (ws is null || ws.State != WebSocketState.Open) return false;
            await ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToJsonString()), WebSocketMessageType.Text, true, stop.Token);
            return true;
        }
        catch { return false; }
        finally { sendLock.Release(); }
    }

    public ControllerLink(int port, string token)
    {
        uri = new Uri($"ws://127.0.0.1:{port}/");
        this.token = token;
    }

    public void Start() => _ = Loop();

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                ws.Options.SetRequestHeader(WebSocketJson.TokenHeader, token);
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    connectTimeout.CancelAfter(5000);
                    await ws.ConnectAsync(uri, connectTimeout.Token);
                }
                await ws.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"hello\",\"role\":\"shell\"}"), WebSocketMessageType.Text, true, stop.Token);
                lock (gate) current = ws;
                while (ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
                {
                    var msg = await WebSocketJson.ReceiveAsync(ws, stop.Token);
                    if (msg is null) break;
                }
            }
            catch { /* controller not there or dropped */ }
            finally
            {
                await sendLock.WaitAsync();
                try { lock (gate) { if (ReferenceEquals(current, ws)) current = null; } ws?.Dispose(); }
                finally { sendLock.Release(); }
            }
            try { await Task.Delay(5000, stop.Token); } catch { }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        ClientWebSocket? ws;
        lock (gate) ws = current;
        try { ws?.Abort(); } catch { }
    }
}
