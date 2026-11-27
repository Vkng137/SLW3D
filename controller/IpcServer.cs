using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace SLW3D.Controller;

/// <summary>The controller's local link (ws://127.0.0.1:ipc_port). Accepts local connections from the 3D shell
/// that present the token from controller.json and send no Origin header.</summary>
sealed class IpcServer : IDisposable
{
    static readonly HashSet<string> Roles = new() { "shell" };
    const int MaxActiveConnections = 16;

    sealed class Client
    {
        public required WebSocket Ws;
        public required string Role;
        public string State = "online";
        public readonly SemaphoreSlim SendLock = new(1, 1);
    }

    readonly HttpListener listener = new();
    readonly string token;
    readonly SynchronizationContext ui;
    readonly Func<string, JsonObject, JsonObject> handle;
    readonly Dictionary<string, Client> clients = new();
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    int activeConnections;

    /// <summary>(role, state) on the UI thread; state "not-linked" when a client leaves.</summary>
    public event Action<string, string>? StateChanged;

    public IpcServer(int port, string token, Func<string, JsonObject, JsonObject> handle)
    {
        this.token = token;
        this.handle = handle;
        ui = SynchronizationContext.Current ?? new SynchronizationContext();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        listener.Start();
        _ = AcceptLoop();
    }

    public string StateOf(string role)
    {
        lock (gate) return clients.TryGetValue(role, out var c) ? c.State : "not-linked";
    }

    public bool IsLinked(string role) => StateOf(role) != "not-linked";

    public Task SendTo(string role, JsonObject msg)
    {
        Client? c;
        lock (gate) clients.TryGetValue(role, out c);
        return c is null ? Task.CompletedTask : SendAsync(c, msg);
    }

    async Task AcceptLoop()
    {
        while (listener.IsListening && !stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; }
            var req = ctx.Request;
            // Accept token via X-SLW3D-Token header (preferred) or ?token= query parameter.
            string? givenToken = req.Headers[WebSocketJson.TokenHeader] ?? req.QueryString["token"];
            bool ok = req.IsWebSocketRequest
                      && IPAddress.IsLoopback(req.RemoteEndPoint.Address)
                      && string.IsNullOrEmpty(req.Headers["Origin"])
                      && ControllerConfig.TokenMatches(token, givenToken);
            if (!ok)
            {
                Log.Write("Link: refused a connection (not a valid local program)");
                try { ctx.Response.StatusCode = 403; ctx.Response.Close(); } catch { }
                continue;
            }
            if (Interlocked.Increment(ref activeConnections) > MaxActiveConnections)
            {
                Interlocked.Decrement(ref activeConnections);
                Log.Write("Link: refused a connection (too many active sockets)");
                try { ctx.Response.StatusCode = 503; ctx.Response.Close(); } catch { }
                continue;
            }
            try
            {
                var ws = (await ctx.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(20))).WebSocket;
                _ = Serve(ws);
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref activeConnections);
                Log.Write("Link accept failed: " + ex.Message);
            }
        }
    }

    async Task Serve(WebSocket ws)
    {
        Client? me = null;
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var hello = await WebSocketJson.ReceiveAsync(ws, helloTimeout.Token);
            string role = (string?)hello?["role"] ?? "";
            if ((string?)hello?["type"] != "hello" || !Roles.Contains(role))
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "hello first", CancellationToken.None);
                return;
            }
            me = new Client { Ws = ws, Role = role };
            Client? old;
            lock (gate) { clients.TryGetValue(role, out old); clients[role] = me; }
            try { old?.Ws.Abort(); } catch { }
            Log.Write($"Link: {role} connected");
            ui.Post(_ => StateChanged?.Invoke(role, me.State), null);

            while (ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var msg = await WebSocketJson.ReceiveAsync(ws, stop.Token);
                if (msg is null) break;
                if ((string?)msg["type"] == "status")
                {
                    me.State = (string?)msg["state"] ?? "online";
                    ui.Post(_ => StateChanged?.Invoke(role, me.State), null);
                    continue;
                }
                if (msg["cmd"] is null) continue;
                var reply = await DispatchOnUiAsync(role, msg);
                if (msg["id"] is JsonNode id) reply["id"] = id.DeepClone();
                await SendAsync(me, reply);
            }
        }
        catch { /* dropped */ }
        finally
        {
            Interlocked.Decrement(ref activeConnections);
            if (me is not null)
            {
                bool wasCurrent;
                lock (gate)
                {
                    wasCurrent = clients.TryGetValue(me.Role, out var cur) && ReferenceEquals(cur, me);
                    if (wasCurrent) clients.Remove(me.Role);
                }
                if (wasCurrent)
                {
                    Log.Write($"Link: {me.Role} disconnected");
                    try { ui.Post(_ => StateChanged?.Invoke(me.Role, "not-linked"), null); } catch { }
                }
            }
            try { ws.Dispose(); } catch { }
        }
    }

    async Task<JsonObject> DispatchOnUiAsync(string role, JsonObject msg)
    {
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ui.Post(_ =>
            {
                try { tcs.TrySetResult(handle(role, msg)); }
                catch (Exception ex) { tcs.TrySetResult(new JsonObject { ["ok"] = false, ["error"] = ex.Message }); }
            }, null);
        }
        catch (Exception ex)
        {
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { return await tcs.Task.WaitAsync(timeout.Token); }
        catch (OperationCanceledException) { return new JsonObject { ["ok"] = false, ["error"] = "controller busy or shutting down" }; }
    }

    static async Task SendAsync(Client c, JsonObject msg)
    {
        await c.SendLock.WaitAsync();
        try
        {
            if (c.Ws.State == WebSocketState.Open)
                await c.Ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
        finally { c.SendLock.Release(); }
    }

    public void Dispose()
    {
        stop.Cancel();
        lock (gate) foreach (var c in clients.Values) { try { c.Ws.Abort(); } catch { } }
        try { listener.Stop(); listener.Close(); } catch { }
        stop.Dispose();
    }
}
