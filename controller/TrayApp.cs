using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Desktop3D.Controller;

/// <summary>Lives in the notification area. Owns the mode controller, hotkeys, local shell link, and game watcher.</summary>
sealed class TrayApp : ApplicationContext
{
    const int HkFull = 1, HkGame = 2, HkPanic = 3;

    readonly ModeController modes;
    readonly HotkeyWindow hotkeys = new();
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem miFull, miManual, miGame, miAutostart;
    readonly SynchronizationContext ui = SynchronizationContext.Current ?? new SynchronizationContext();
    readonly EventWaitHandle wakeEvent, quitEvent;
    readonly RegisteredWaitHandle wakeWait, quitWait;
    readonly GameWatcher games;
    readonly string root;
    IpcServer? link;
    CancellationTokenSource? pendingModeCts;
    bool autostartCached;
    bool exiting;

    public TrayApp(string root)
    {
        this.root = root;
        try { ControllerConfig.EnsureToken(root); }
        catch (Exception ex) { Log.Write("Token setup failed: " + ex.Message); }
        modes = new ModeController(root);
        SystemEvents.SessionEnding += OnSessionEnding;

        var menu = new ContextMenuStrip();
        miFull = new ToolStripMenuItem("Full 3D desktop", null, (_, _) => modes.SetMode(Mode.Full, "tray")) { ShortcutKeyDisplayString = "Ctrl+Alt+I" };
        miManual = new ToolStripMenuItem("Manual control", null, (_, _) => modes.SetMode(Mode.ManualControl, "tray"));
        miGame = new ToolStripMenuItem("Game mode", null, (_, _) => modes.SetMode(Mode.GameMode, "tray")) { ShortcutKeyDisplayString = "Ctrl+Alt+G" };
        menu.Items.AddRange(new ToolStripItem[] { miFull, miManual, miGame, new ToolStripSeparator() });
        menu.Items.Add(new ToolStripMenuItem("Panic restore", null, (_, _) => modes.Panic("tray")) { ShortcutKeyDisplayString = "Ctrl+Alt+Shift+D" });
        miAutostart = new ToolStripMenuItem("Start at login");
        miAutostart.Click += (_, _) => ToggleAutostart();
        menu.Opening += (_, _) => { miAutostart.Checked = autostartCached; RefreshAutostartAsync(); };
        menu.Items.Add(miAutostart);
        menu.Items.Add(new ToolStripMenuItem("Open log", null, (_, _) => OpenLog()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit (back to stock Windows)", null, (_, _) => ExitAll()));

        tray = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Windows 3D Desktop",
            Visible = true,
            ContextMenuStrip = menu,
        };
        tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && modes.Current != Mode.Full)
                modes.SetMode(Mode.Full, "tray double-click");
        };

        wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Paths.ControllerWakeEvent);
        wakeWait = ThreadPool.RegisterWaitForSingleObject(wakeEvent, (_, _) =>
            ui.Post(_ => { if (!exiting) modes.SetMode(Mode.Full, "shortcut"); }, null), null, -1, executeOnlyOnce: false);
        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Paths.ControllerQuitEvent);
        quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, (_, _) =>
            ui.Post(_ => ExitAll(), null), null, -1, executeOnlyOnce: true);

        modes.Changed += UpdateMenu;
        modes.Notify += (title, text) => tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

        hotkeys.Pressed += OnHotkey;
        var failed = new List<string>();
        if (!hotkeys.Register(HkFull, HotkeyWindow.ModCtrl | HotkeyWindow.ModAlt, Keys.I)) failed.Add("Ctrl+Alt+I");
        if (!hotkeys.Register(HkGame, HotkeyWindow.ModCtrl | HotkeyWindow.ModAlt, Keys.G)) failed.Add("Ctrl+Alt+G");
        if (!hotkeys.Register(HkPanic, HotkeyWindow.ModCtrl | HotkeyWindow.ModAlt | HotkeyWindow.ModShift, Keys.D)) failed.Add("Ctrl+Alt+Shift+D");
        if (failed.Count > 0)
        {
            Log.Write("Hotkeys already taken by another program: " + string.Join(", ", failed));
            tray.ShowBalloonTip(6000, "Hotkey conflict", "Another program already uses: " + string.Join(", ", failed) + ". Use the tray menu instead.", ToolTipIcon.Warning);
        }
        else Log.Write("Hotkeys registered: Ctrl+Alt+I, Ctrl+Alt+G, Ctrl+Alt+Shift+D");

        RefreshAutostartAsync();
        StartLink();
        modes.SetMode(Mode.Full, "startup");
        games = new GameWatcher(modes, root);
    }

    void StartLink()
    {
        var cc = ControllerConfig.Read(root);
        string token = (string?)cc?["ipc_token"] ?? "";
        int port = (int?)cc?["ipc_port"] ?? 47800;
        if (token.Length < 32) { Log.Write("Link off: no token"); return; }
        try
        {
            link = new IpcServer(port, token, HandleCommand);
            link.Start();
            Log.Write($"Link listening on 127.0.0.1:{port} (local shell with token only)");
        }
        catch (Exception ex) { Log.Write("Link not started: " + ex.Message); link = null; }
    }

    JsonObject HandleCommand(string role, JsonObject msg)
    {
        string cmd = (string?)msg["cmd"] ?? "";
        Log.Write($"Link: {role} asks '{cmd}' {msg["mode"]}");
        switch (cmd)
        {
            case "set_mode":
                Mode? target = ((string?)msg["mode"])?.ToLowerInvariant() switch
                {
                    "full" => Mode.Full,
                    "manualcontrol" or "manual" => Mode.ManualControl,
                    "gamemode" or "game" => Mode.GameMode,
                    _ => null,
                };
                if (target is null) return Fail("unknown mode");
                ScheduleModeSwitch(target.Value, role);
                return new JsonObject { ["ok"] = true };

            case "status":
                return new JsonObject { ["ok"] = true, ["mode"] = modes.Current.ToString() };
        }
        return Fail("unknown command");

        static JsonObject Fail(string why) => new() { ["ok"] = false, ["error"] = why };
    }

    void OnHotkey(int id)
    {
        switch (id)
        {
            case HkFull: modes.SetMode(Mode.Full, "hotkey"); break;
            case HkGame: modes.SetMode(modes.Current == Mode.GameMode ? Mode.Full : Mode.GameMode, "hotkey"); break;
            case HkPanic: modes.Panic("hotkey"); break;
        }
    }

    void UpdateMenu()
    {
        miFull.Checked = modes.Current == Mode.Full;
        miManual.Checked = modes.Current == Mode.ManualControl;
        miGame.Checked = modes.Current == Mode.GameMode;
        tray.Text = "Windows 3D Desktop: " + modes.Current switch
        {
            Mode.Full => "Full",
            Mode.ManualControl => "Manual Control",
            Mode.GameMode => "Game Mode",
            _ => "stock Windows",
        };
    }

    void ScheduleModeSwitch(Mode target, string role)
    {
        pendingModeCts?.Cancel();
        pendingModeCts?.Dispose();
        var cts = pendingModeCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, cts.Token);
                ui.Post(_ => { if (!cts.IsCancellationRequested && !exiting) modes.SetMode(target, role); }, null);
            }
            catch (OperationCanceledException) { }
        });
    }

    void RefreshAutostartAsync()
    {
        _ = Task.Run(() =>
        {
            try
            {
                bool on = Autostart.IsOn();
                ui.Post(_ => { autostartCached = on; if (!exiting) miAutostart.Checked = on; }, null);
            }
            catch { }
        });
    }

    async void ToggleAutostart()
    {
        miAutostart.Enabled = false;
        try
        {
            bool enable = !autostartCached;
            string exe = Environment.ProcessPath!;
            bool after = await Task.Run(() =>
            {
                if (enable) Autostart.Enable(exe); else Autostart.Disable();
                return Autostart.IsOn();
            });
            autostartCached = after;
            miAutostart.Checked = after;
        }
        catch (Exception ex)
        {
            Log.Write("Start at login change failed: " + ex.Message);
            tray.ShowBalloonTip(5000, "Start at login not changed", ex.Message, ToolTipIcon.Warning);
        }
        finally { miAutostart.Enabled = true; }
    }

    static void OpenLog()
    {
        try { using var p = Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Open log failed: " + ex.Message); }
    }

    void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        Log.Write($"Windows session ending ({e.Reason}): shutting down cleanly");
        ExitAll();
    }

    void ExitAll()
    {
        if (exiting) return;
        exiting = true;
        SystemEvents.SessionEnding -= OnSessionEnding;
        wakeWait.Unregister(null);
        wakeEvent.Dispose();
        quitWait.Unregister(null);
        quitEvent.Dispose();
        pendingModeCts?.Cancel();
        pendingModeCts?.Dispose();
        games.Dispose();
        link?.Dispose();
        modes.Shutdown();
        hotkeys.Dispose();
        tray.Visible = false;
        tray.Dispose();
        ExitThread();
    }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var accent = Color.FromArgb(92, 200, 255);
            using var ring = new Pen(accent, 3f);
            g.DrawEllipse(ring, 3, 3, 26, 26);
            using var dot = new SolidBrush(accent);
            g.FillEllipse(dot, 11, 11, 10, 10);
        }
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally { DestroyIcon(hIcon); }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
}
