extern alias ShellAsm;

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using SLW3D;
using SLW3D.Controller;
using ShellAsm::SLW3D.Shell;

int passed = 0, failed = 0;

void Run(string name, Action body)
{
    try { body(); Console.WriteLine($"  PASS  {name}"); passed++; }
    catch (Exception ex) { Console.WriteLine($"  FAIL  {name}: {ex.Message}"); failed++; }
}

async Task RunAsync(string name, Func<Task> body)
{
    try { await body(); Console.WriteLine($"  PASS  {name}"); passed++; }
    catch (Exception ex) { Console.WriteLine($"  FAIL  {name}: {ex.Message}"); failed++; }
}

void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }

string MakeTempRoot()
{
    string dir = Path.Combine(Path.GetTempPath(), "SLW3D-Tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(dir, "config"));
    Directory.CreateDirectory(Path.Combine(dir, "assets", "backgrounds"));
    return dir;
}

Console.WriteLine("Running SLW3D C# tests...");

Run("ControllerConfig.EnsureToken generates token and preserves valid token", () =>
{
    string root = MakeTempRoot();
    try
    {
        string path = Path.Combine(root, "config", "controller.json");
        File.WriteAllText(path, """{"ipc_port":47800,"ipc_token":"GENERATED_ON_FIRST_RUN"}""");
        ControllerConfig.EnsureToken(root);
        string tok1 = (string?)ControllerConfig.Read(root)?["ipc_token"] ?? "";
        Assert(tok1.Length == 64 && tok1 != "GENERATED_ON_FIRST_RUN", $"Expected 64-char hex token, got '{tok1}'");
        ControllerConfig.EnsureToken(root);
        string tok2 = (string?)ControllerConfig.Read(root)?["ipc_token"] ?? "";
        Assert(tok1 == tok2, "Existing valid token should not be overwritten");
        Assert(ControllerConfig.TokenMatches(tok1, tok2), "Matching token must pass");
        Assert(!ControllerConfig.TokenMatches(tok1, tok1 + "x"), "Mismatched token must fail");
    }
    finally { Directory.Delete(root, true); }
});

Run("ControllerConfig.EnsureToken seeds controller.json from controller.example.json on fresh install", () =>
{
    string root = MakeTempRoot();
    try
    {
        string example = Path.Combine(root, "config", "controller.example.json");
        File.WriteAllText(example, """{"ipc_port":47899,"ipc_token":"GENERATED_ON_FIRST_RUN","game_processes":["DCS","Starfield"],"game_exit_grace_s":15}""");
        ControllerConfig.EnsureToken(root);
        var doc = ControllerConfig.Read(root);
        Assert(doc is not null, "controller.json should be created");
        Assert((int?)doc!["ipc_port"] == 47899, "ipc_port from example should be preserved");
        Assert((doc["game_processes"] as JsonArray)?.Count == 2, "game_processes from example should be preserved");
        string tok = (string?)doc["ipc_token"] ?? "";
        Assert(tok.Length == 64 && tok != "GENERATED_ON_FIRST_RUN", "Fresh token should be generated");
    }
    finally { Directory.Delete(root, true); }
});

Run("ThemeStore.SetActive switches active/previous cleanly without regex bugs", () =>
{
    string root = MakeTempRoot();
    try
    {
        string path = Path.Combine(root, "config", "themes.json");
        File.WriteAllText(path, """
        {
          "active": "default",
          "themes": {
            "default": { "label": "Default", "accent": "#D9DEE6" },
            "custom-$1": { "label": "Special $1", "accent": "#FF0000" }
          }
        }
        """);
        ThemeStore.SetActive(root, "custom-$1");
        var (active, prev, themes) = ThemeStore.Read(root);
        Assert(active == "custom-$1", $"Expected active 'custom-$1', got '{active}'");
        Assert(prev == "default", $"Expected previous 'default', got '{prev}'");
        Assert(themes.Count == 2, "Themes list must stay intact");

        bool threw = false;
        try { ThemeStore.SetActive(root, "nonexistent"); } catch (ArgumentException) { threw = true; }
        Assert(threw, "Unknown theme must throw ArgumentException");
    }
    finally { Directory.Delete(root, true); }
});

Run("ShellConfig and Backgrounds.Save preserve each other's keys and fit mode", () =>
{
    string root = MakeTempRoot();
    try
    {
        ShellConfig.Set(root, "icon_size", "medium");
        Backgrounds.Save(root, "space.png", 0.65, "fit");
        var doc = ShellConfig.Read(root);
        Assert((string?)doc["icon_size"] == "medium", "icon_size should persist");
        Assert((string?)doc["background"]?["image"] == "space.png", "background image should persist");
        Assert(Math.Abs(((double?)doc["background"]?["dim"] ?? 0) - 0.65) < 0.001, "background dim should persist");
        Assert((string?)doc["background"]?["fit"] == "fit", "background fit should persist");
        Backgrounds.Save(root, null, null, "invalid-mode");
        Assert((string?)ShellConfig.Read(root)["background"]?["fit"] == "fill", "invalid fit must default to fill");
    }
    finally { Directory.Delete(root, true); }
});

Run("Backgrounds.IsValidName blocks traversal and bad extensions", () =>
{
    Assert(Backgrounds.IsValidName("Alien Dark.jpeg"), "Valid jpeg should pass");
    Assert(!Backgrounds.IsValidName("../secret.png"), "Parent traversal must fail");
    Assert(!Backgrounds.IsValidName("sub\\pic.png"), "Subdirectory path must fail");
    Assert(!Backgrounds.IsValidName("malware.exe"), "Non-image extension must fail");
    Assert(!Backgrounds.IsValidName(""), "Empty name must fail");
});

Run("AppsStore Add, RemoveFromDeck, Unpin, and backup", () =>
{
    string root = MakeTempRoot();
    try
    {
        File.WriteAllText(Path.Combine(root, "config", "apps.json"), "{\"apps\":[]}\n");
        AppsStore.Add(root, "TestApp", "shell:AppsFolder\\Test", "app", new List<string> { "Command" }, pinned: true);
        AppsStore.Add(root, "TestApp", "shell:AppsFolder\\Test", "app", new List<string> { "Dev" }, pinned: true);
        Assert(File.Exists(Path.Combine(root, "config", "apps.json.bak")), "Backup .bak file must be created");

        var apps = AppLauncher.ReadApps(root);
        Assert(apps.Count == 1, "Adding existing app to second deck should merge, not duplicate");
        Assert((apps[0]?["decks"] as JsonArray)?.Count == 2, "App should be on 2 decks");

        AppsStore.Unpin(root, "TestApp");
        Assert(AppLauncher.ReadApps(root)[0]?["pinned"] is null, "App should be unpinned");

        AppsStore.RemoveFromDeck(root, "TestApp", "Command");
        Assert((AppLauncher.ReadApps(root)[0]?["decks"] as JsonArray)?.Count == 1, "One deck should remain");

        AppsStore.RemoveFromDeck(root, "TestApp", "Dev");
        Assert(AppLauncher.ReadApps(root).Count == 0, "Removing last deck should delete app");

        for (int i = 1; i <= AppsStore.MaxAppsPerDeck; i++)
            AppsStore.Add(root, $"App{i}", $"shell:AppsFolder\\App{i}", "app", new List<string> { "Command" }, pinned: false);
        bool fullThrew = false;
        try { AppsStore.Add(root, "AppOverflow", "shell:AppsFolder\\Overflow", "app", new List<string> { "Command" }, pinned: false); }
        catch (InvalidOperationException) { fullThrew = true; }
        Assert(fullThrew, $"Adding past {AppsStore.MaxAppsPerDeck} apps on a deck must throw");
    }
    finally { Directory.Delete(root, true); }
});

Run("AppsStore AddDeck, RenameDeck, RemoveDeck preserve apps and enforce limits", () =>
{
    string root = MakeTempRoot();
    try
    {
        File.WriteAllText(Path.Combine(root, "config", "shell.json"), """{"decks":["Command","Flight"],"start_deck":"Command"}""");
        File.WriteAllText(Path.Combine(root, "config", "apps.json"), """{"apps":[{"name":"Sim","exe":"sim.exe","decks":["Flight"]}]}""");

        var (_, added) = AppsStore.AddDeck(root, "Media");
        Assert(added == "Media", "Should return new deck name");
        var decks = (ShellConfig.Read(root)["decks"] as JsonArray)!.Select(n => (string)n!).ToList();
        Assert(decks.SequenceEqual(new[] { "Command", "Flight", "Media" }), "Deck should be appended");

        // Duplicate rejected
        bool dupThrew = false;
        try { AppsStore.AddDeck(root, "media"); } catch (InvalidOperationException) { dupThrew = true; }
        Assert(dupThrew, "Duplicate deck name must throw");

        // Rename migrates apps
        AppsStore.RenameDeck(root, "Flight", "Sims");
        var appDecks = (AppLauncher.ReadApps(root)[0]?["decks"] as JsonArray)!.Select(n => (string)n!).ToList();
        Assert(appDecks.SequenceEqual(new[] { "Sims" }), "Renaming deck must update apps on that deck");

        // Removing deck removes deck-only apps cleanly instead of dumping them into another deck
        AppsStore.RemoveDeck(root, "Sims");
        Assert(AppLauncher.ReadApps(root).Count == 0, "Deck-only app must be removed when its deck is deleted");

        // Cannot remove last remaining deck
        AppsStore.RemoveDeck(root, "Media");
        bool lastThrew = false;
        try { AppsStore.RemoveDeck(root, "Command"); } catch (InvalidOperationException) { lastThrew = true; }
        Assert(lastThrew, "Deleting last deck must throw");
    }
    finally { Directory.Delete(root, true); }
});

Run("GameWatcher enters, honors manual override, handles back-to-back games, and exits after grace", () =>
{
    string root = MakeTempRoot();
    try
    {
        string cfg = Path.Combine(root, "config", "controller.json");
        File.WriteAllText(cfg, """{"game_processes":["DCS","Starfield"],"game_exit_grace_s":15}""");

        Mode mode = Mode.Full;
        DateTime lastChange = new(2026, 11, 20, 12, 0, 0);
        using var gw = new GameWatcher(
            () => mode,
            () => lastChange,
            (m, _) => { mode = m; lastChange = lastChange.AddSeconds(1); },
            cfg,
            startTimer: false);

        var t0 = new DateTime(2026, 11, 20, 12, 1, 0);
        gw.Check("DCS", t0);
        Assert(mode == Mode.GameMode, "Should auto-enter GameMode when DCS starts");

        // User manually returns to Full while DCS is still running: must stay Full.
        mode = Mode.Full;
        lastChange = t0.AddSeconds(5);
        gw.Check("DCS", t0.AddSeconds(10));
        Assert(mode == Mode.Full, "Manual switch to Full during DCS must be respected");

        // DCS closes, and Starfield starts 5 s later (inside 15 s grace): must enter GameMode!
        gw.Check(null, t0.AddSeconds(12));
        gw.Check("Starfield", t0.AddSeconds(17));
        Assert(mode == Mode.GameMode, "Second distinct game inside grace window must trigger GameMode");

        // Starfield closes: must wait full 15 s grace before returning to Full.
        gw.Check(null, t0.AddSeconds(25));
        Assert(mode == Mode.GameMode, "Must remain in GameMode before grace expires");
        gw.Check(null, t0.AddSeconds(33));
        Assert(mode == Mode.Full, "Must return to Full after grace expires");
    }
    finally { Directory.Delete(root, true); }
});

await RunAsync("IpcServer enforces token/Origin, supports header and query token, handles multi-frame JSON", async () =>
{
    int port;
    using (var tcp = new TcpListener(IPAddress.Loopback, 0))
    {
        tcp.Start();
        port = ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    string token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    using var server = new IpcServer(port, token, (role, msg) =>
        new JsonObject { ["ok"] = true, ["echo"] = (string?)msg["cmd"], ["role"] = role });
    server.Start();

    // 1. Bad token must be rejected with 403
    using (var badWs = new ClientWebSocket())
    {
        bool rejected = false;
        try { await badWs.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/?token=wrong"), CancellationToken.None); }
        catch (WebSocketException) { rejected = true; }
        Assert(rejected, "Invalid token must be rejected");
    }

    // 2. Origin header must be rejected even with valid token
    using (var originWs = new ClientWebSocket())
    {
        originWs.Options.SetRequestHeader(WebSocketJson.TokenHeader, token);
        originWs.Options.SetRequestHeader("Origin", "https://evil.example");
        bool rejected = false;
        try { await originWs.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None); }
        catch (WebSocketException) { rejected = true; }
        Assert(rejected, "Connection with Origin header must be rejected");
    }

    // 3. Header token + multi-frame command round-trip
    using (var ws = new ClientWebSocket())
    {
        ws.Options.SetRequestHeader(WebSocketJson.TokenHeader, token);
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await ws.SendAsync(Encoding.UTF8.GetBytes("""{"type":"hello","role":"shell"}"""), WebSocketMessageType.Text, true, CancellationToken.None);

        // Send a command split across two WebSocket frames
        await ws.SendAsync(Encoding.UTF8.GetBytes("""{"id":7,"cm"""), WebSocketMessageType.Text, false, CancellationToken.None);
        await ws.SendAsync(Encoding.UTF8.GetBytes("""d":"status"}"""), WebSocketMessageType.Text, true, CancellationToken.None);

        var reply = await WebSocketJson.ReceiveAsync(ws, CancellationToken.None);
        Assert((bool?)reply?["ok"] == true, "Command should succeed");
        Assert((int?)reply?["id"] == 7, "Reply must echo id");
        Assert((string?)reply?["echo"] == "status", "Multi-frame JSON must reassemble");
    }

    // 4. Non-shell role must be rejected
    using (var badRoleWs = new ClientWebSocket())
    {
        badRoleWs.Options.SetRequestHeader(WebSocketJson.TokenHeader, token);
        await badRoleWs.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await badRoleWs.SendAsync(Encoding.UTF8.GetBytes("""{"type":"hello","role":"unknown"}"""), WebSocketMessageType.Text, true, CancellationToken.None);
        var buf = new byte[64];
        var res = await badRoleWs.ReceiveAsync(buf, CancellationToken.None);
        Assert(res.MessageType == WebSocketMessageType.Close, "Unknown role must be closed by server");
    }
});

Console.WriteLine($"\nSummary: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
