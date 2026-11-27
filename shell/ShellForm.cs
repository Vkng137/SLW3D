using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace SLW3D.Shell;

/// <summary>The 3D desktop window: a WebView2 page (Three.js + glass UI) attached to the desktop layer.</summary>
sealed class ShellForm : Form
{
    const string HostName = "slw3d.local";
    const string PageUrl = "https://" + HostName + "/index.html";
    const string MediaHost = "media.slw3d.local";

    readonly string root, webDir, configDir;
    readonly bool debug;
    readonly WebView2 web = new();
    readonly System.Windows.Forms.Timer tick = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer configDebounce = new() { Interval = 400 };
    Telemetry? telemetry;
    FileSystemWatcher? watcher;
    EventWaitHandle? quitEvent;
    RegisteredWaitHandle? quitWait;
    readonly HashSet<string> changedConfigs = new(StringComparer.OrdinalIgnoreCase);
    readonly object configGate = new();
    IntPtr desktopHost, desktopIcons;
    Size lastSentWin;
    Rectangle lastSentWork;
    bool pageReady, covered;
    JsonArray apps = new();
    ControllerLink? controllerLink;

    public ShellForm(string root, bool debug)
    {
        this.root = root;
        this.debug = debug;
        webDir = Path.Combine(root, "shell", "web");
        configDir = Path.Combine(root, "config");

        Text = "SLW3D Shell";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-32000, -32000, 16, 16); // off-screen until attached
        BackColor = Color.FromArgb(5, 12, 31);
        web.DefaultBackgroundColor = BackColor;
        web.Dock = DockStyle.Fill;
        Controls.Add(web);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= (int)Native.WS_EX_TOOLWINDOW; return cp; }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            var shellCfg = ReadJson("shell.json") as JsonObject;
            bool layered = (bool?)shellCfg?["attach_layered"] ?? false;
            var (host, defView, mode) = DesktopAttach.Attach(Handle, layered);
            // Right after login Explorer may still be building the desktop: wait up to 30 s for it.
            for (int i = 0; host == IntPtr.Zero && i < 30; i++)
            {
                if (i == 0) Log.Write($"Desktop not ready ({mode}); waiting for Explorer");
                await Task.Delay(1000);
                (host, defView, mode) = DesktopAttach.Attach(Handle, layered);
            }
            desktopHost = host;
            desktopIcons = defView;
            Log.Write($"Desktop attach: {mode}; host 0x{host:X}; icons 0x{defView:X}");
            if (host == IntPtr.Zero)
            {
                // Fallback: plain full-screen window at the bottom of the z-order.
                Bounds = SystemInformation.VirtualScreen;
                Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }

            telemetry = new Telemetry();
            await InitWebAsync();
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed: " + ex);
            Close();
            return;
        }

        StartControllerLink();
        tick.Tick += (_, _) => OnTick();
        tick.Start();
        configDebounce.Tick += (_, _) => FlushConfigChanges();

        watcher = new FileSystemWatcher(configDir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        void QueueConfig(string? fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
                lock (configGate) changedConfigs.Add(Path.GetFileName(fileName));
            SafeInvoke(() => { configDebounce.Stop(); configDebounce.Start(); });
        }
        watcher.Changed += (_, a) => QueueConfig(a.Name);
        watcher.Created += (_, a) => QueueConfig(a.Name);
        watcher.Renamed += (_, a) => QueueConfig(a.Name);

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

        quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Paths.ShellQuitEvent);
        quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, (_, _) => SafeInvoke(() => { Log.Write("Quit requested by controller"); Close(); }), null, -1, true);
    }

    void FlushConfigChanges()
    {
        configDebounce.Stop();
        List<string> files;
        lock (configGate) { files = changedConfigs.ToList(); changedConfigs.Clear(); }
        bool reloadConfig = files.Count == 0 || files.Any(f =>
            f.Equals("apps.json", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("shell.json", StringComparison.OrdinalIgnoreCase));
        bool reloadTheme = files.Count == 0 || files.Any(f =>
            f.Equals("themes.json", StringComparison.OrdinalIgnoreCase));
        if (!reloadConfig && !reloadTheme) return;
        Log.Write($"Config changed ({string.Join(", ", files)}): reloading");
        if (reloadConfig) SendConfig();
        if (reloadTheme) SendTheme();
    }

    async Task InitWebAsync()
    {
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SLW3D", "WebView2");
        // Keep the embedded browser quiet: no background updates, sync, pings or reputation lookups.
        var options = new CoreWebView2EnvironmentOptions(
            "--disable-background-networking --disable-component-update --disable-sync --no-pings --disable-domain-reliability");
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
        await web.EnsureCoreWebView2Async(env);

        var cw = web.CoreWebView2;
        var s = cw.Settings;
        s.AreDevToolsEnabled = debug;
        s.AreDefaultContextMenusEnabled = debug;
        s.AreBrowserAcceleratorKeysEnabled = debug;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsReputationCheckingRequired = false;

        cw.SetVirtualHostNameToFolderMapping(HostName, webDir, CoreWebView2HostResourceAccessKind.Deny);
        // Background pictures (copies in assets\backgrounds); the page may only show them as images (CSP img-src).
        cw.SetVirtualHostNameToFolderMapping(MediaHost, Backgrounds.Dir(root), CoreWebView2HostResourceAccessKind.Allow);
        cw.NavigationStarting += (_, a) =>
        {
            if (!a.Uri.StartsWith("https://" + HostName + "/", StringComparison.OrdinalIgnoreCase))
            {
                a.Cancel = true;
                Log.Write("Blocked navigation to " + a.Uri);
            }
        };
        cw.NewWindowRequested += (_, a) => { a.Handled = true; };
        cw.WebMessageReceived += OnWebMessage;
        cw.ProcessFailed += (_, a) =>
        {
            Log.Write($"Browser process failed: {a.ProcessFailedKind}; reloading page");
            pageReady = false;
            try { web.CoreWebView2.Reload(); } catch { }
        };
        cw.Navigate(PageUrl);
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith("https://" + HostName + "/", StringComparison.OrdinalIgnoreCase)) return;
        JsonNode? msg;
        try { msg = JsonNode.Parse(e.WebMessageAsJson); } catch { return; }
        switch ((string?)msg?["type"])
        {
            case "ready":
                pageReady = true;
                Log.Write("Page ready");
                SendConfig(); SendTheme(); SendLayout(); SendStatus(); SendTelemetry();
                break;
            case "launch":
                var result = AppLauncher.Launch(apps, (string?)msg?["name"] ?? "");
                if (result is not null) Post(new { type = "toast", text = result });
                break;
            case "start":
                Native.TapKeys(0x5B); // Windows key
                break;
            case "search":
                Native.TapKeys(0x5B, 0x53); // Win+S
                break;
            case "list_start_apps":
                ListStartAppsAsync();
                break;
            case "add_app":
                AddApp(msg!);
                break;
            case "remove_app":
                try { Post(new { type = "toast", text = AppsStore.RemoveFromDeck(root, (string?)msg?["name"] ?? "", (string?)msg?["deck"] ?? "") }); }
                catch (Exception ex) { Log.Write("Remove app failed: " + ex.Message); Post(new { type = "toast", text = "Not removed: " + ex.Message }); }
                break;
            case "unpin_app":
                try { Post(new { type = "toast", text = AppsStore.Unpin(root, (string?)msg?["name"] ?? "") }); }
                catch (Exception ex) { Log.Write("Unpin failed: " + ex.Message); Post(new { type = "toast", text = "Not unpinned: " + ex.Message }); }
                break;
            case "add_deck":
                DeckAction(() => AppsStore.AddDeck(root, (string?)msg?["name"] ?? ""));
                break;
            case "rename_deck":
                DeckAction(() => AppsStore.RenameDeck(root, (string?)msg?["oldName"] ?? "", (string?)msg?["newName"] ?? ""));
                break;
            case "remove_deck":
                DeckAction(() => AppsStore.RemoveDeck(root, (string?)msg?["name"] ?? ""));
                break;
            case "enter_game_mode":
                EnterGameMode();
                break;
            case "restore_stock_windows":
                RestoreStockWindows();
                break;
            case "power_action":
                HandlePowerAction((string?)msg?["action"] ?? "");
                break;
            case "list_backgrounds":
                SendBackgrounds();
                break;
            case "set_background":
                BackgroundAction(() =>
                {
                    string? f = (string?)msg?["file"];
                    if (f is not null && !Backgrounds.IsValidName(f)) throw new InvalidOperationException("not a background file");
                    Backgrounds.Save(root, f ?? "", null);
                });
                break;
            case "set_bg_dim":
                BackgroundAction(() => Backgrounds.Save(root, null, (double?)msg?["value"] ?? 0.45));
                break;
            case "set_bg_fit":
                BackgroundAction(() => Backgrounds.Save(root, null, null, (string?)msg?["value"] ?? "fill"));
                break;
            case "set_icon_size":
                try
                {
                    string sz = ((string?)msg?["value"] ?? "small").Trim().ToLowerInvariant();
                    if (sz is not ("small" or "medium")) sz = sz == "large" ? "medium" : "small";
                    ShellConfig.Set(root, "icon_size", sz);
                }
                catch (Exception ex) { Log.Write("Icon size change failed: " + ex.Message); }
                break;
            case "remove_background":
                BackgroundAction(() =>
                {
                    string f = (string?)msg?["file"] ?? "";
                    var current = (string?)ReadJson("shell.json")?["background"]?["image"];
                    Backgrounds.Remove(root, f);
                    if (string.Equals(current, f, StringComparison.OrdinalIgnoreCase)) Backgrounds.Save(root, "", null);
                });
                break;
            case "use_windows_wallpaper":
                BackgroundAction(() =>
                {
                    string src = Backgrounds.WindowsWallpaper() ?? throw new InvalidOperationException("no Windows wallpaper picture found");
                    Backgrounds.Save(root, Backgrounds.Import(root, src, "Windows wallpaper"), null);
                });
                break;
            case "pick_background":
                PickBackground();
                break;
            case "set_theme":
                try
                {
                    ShellConfig.Set(root, "theme_color", null);
                    ThemeStore.SetActive(root, (string?)msg?["key"] ?? ""); // file watcher then sends the new theme
                }
                catch (Exception ex) { Log.Write("Theme switch failed: " + ex.Message); Post(new { type = "toast", text = "Theme not changed: " + ex.Message }); }
                break;
            case "set_theme_color":
                try
                {
                    if ((bool?)msg?["reset"] == true)
                    {
                        ShellConfig.Update(root, doc =>
                        {
                            doc["theme_color"] = null;
                            doc["active_theme_profile"] = null;
                        });
                    }
                    else
                    {
                        int hue = Math.Clamp((int)Math.Round((double?)msg?["hue"] ?? 200), 0, 360) % 360;
                        int sat = Math.Clamp((int)Math.Round((double?)msg?["sat"] ?? 85), 0, 100);
                        string? profile = ((string?)msg?["profile"])?.Trim();
                        ShellConfig.Update(root, doc =>
                        {
                            doc["theme_color"] = new JsonObject { ["hue"] = hue, ["sat"] = sat };
                            doc["active_theme_profile"] = string.IsNullOrEmpty(profile) ? null : profile;
                        });
                    }
                }
                catch (Exception ex) { Log.Write("Theme color save failed: " + ex.Message); }
                break;
            case "save_theme_profile":
                try
                {
                    string name = ((string?)msg?["name"] ?? "").Trim();
                    if (name.Length == 0) break;
                    if (name.Length > 24) name = name[..24].Trim();
                    int hue = Math.Clamp((int)Math.Round((double?)msg?["hue"] ?? 200), 0, 360) % 360;
                    int sat = Math.Clamp((int)Math.Round((double?)msg?["sat"] ?? 85), 0, 100);
                    ShellConfig.Update(root, doc =>
                    {
                        var arr = doc["saved_themes"] as JsonArray ?? new JsonArray();
                        JsonObject? existing = null;
                        foreach (var item in arr)
                        {
                            if (item is JsonObject obj && string.Equals((string?)obj["name"], name, StringComparison.OrdinalIgnoreCase))
                            {
                                existing = obj;
                                break;
                            }
                        }
                        if (existing is not null)
                        {
                            existing["name"] = name;
                            existing["hue"] = hue;
                            existing["sat"] = sat;
                        }
                        else
                        {
                            if (arr.Count >= 16) throw new InvalidOperationException("Maximum of 16 saved themes reached");
                            arr.Add(new JsonObject { ["name"] = name, ["hue"] = hue, ["sat"] = sat });
                        }
                        doc["saved_themes"] = arr;
                        doc["theme_color"] = new JsonObject { ["hue"] = hue, ["sat"] = sat };
                        doc["active_theme_profile"] = name;
                    });
                    Post(new { type = "toast", text = $"Saved theme '{name}'" });
                }
                catch (Exception ex)
                {
                    Log.Write("Save theme profile failed: " + ex.Message);
                    Post(new { type = "toast", text = "Not saved: " + ex.Message });
                }
                break;
            case "delete_theme_profile":
                try
                {
                    string name = ((string?)msg?["name"] ?? "").Trim();
                    if (name.Length == 0) break;
                    ShellConfig.Update(root, doc =>
                    {
                        if (doc["saved_themes"] is not JsonArray arr) return;
                        for (int i = arr.Count - 1; i >= 0; i--)
                        {
                            if (arr[i] is JsonObject obj && string.Equals((string?)obj["name"], name, StringComparison.OrdinalIgnoreCase))
                                arr.RemoveAt(i);
                        }
                        if (string.Equals((string?)doc["active_theme_profile"], name, StringComparison.OrdinalIgnoreCase))
                            doc["active_theme_profile"] = null;
                    });
                    Post(new { type = "toast", text = $"Removed theme '{name}'" });
                }
                catch (Exception ex) { Log.Write("Delete theme profile failed: " + ex.Message); }
                break;
            case "focus_input":
                FocusForTyping((string?)msg?["id"]);
                break;
            case "desktop_focus":
                DismissTaskbarIfFocused();
                break;
            case "log":
                Log.Write("page: " + (string?)msg?["text"]);
                break;
        }
    }

    /// <summary>Moves Win32 keyboard focus into WebView2 so typing works reliably even when the shell is a
    /// cross-process child of Progman or after WebView2 was hidden during occlusion.</summary>
    void FocusForTyping(string? elementId)
    {
        try
        {
            IntPtr targetTop = desktopHost != IntPtr.Zero ? desktopHost : Handle;
            uint myTid = Native.GetCurrentThreadId();
            uint hostTid = desktopHost != IntPtr.Zero ? Native.GetWindowThreadProcessId(desktopHost, out _) : 0;
            IntPtr fg = Native.GetForegroundWindow();
            uint fgTid = fg != IntPtr.Zero ? Native.GetWindowThreadProcessId(fg, out _) : 0;

            // Only attach/detach to a third-party foreground thread; never detach from desktopHost (Progman),
            // which SetParent already shares input state with.
            bool attachedOther = fgTid != 0 && fgTid != myTid && fgTid != hostTid
                                 && Native.AttachThreadInput(myTid, fgTid, true);
            try
            {
                if (fg != targetTop) Native.SetForegroundWindow(targetTop);
            }
            finally
            {
                if (attachedOther) Native.AttachThreadInput(myTid, fgTid, false);
            }

            // Cycle focus onto the Form then WebView2 so WinForms always fires WebView2.OnGotFocus -> MoveFocus.
            Native.SetFocus(Handle);
            Native.SetFocus(web.Handle);
            web.Focus();
            if (!string.IsNullOrEmpty(elementId))
                Post(new { type = "focus_element", id = elementId });
        }
        catch (Exception ex) { Log.Write("FocusForTyping failed: " + ex.Message); }
    }

    async void EnterGameMode()
    {
        bool sent = controllerLink is not null && await controllerLink.SendAsync(new JsonObject { ["cmd"] = "set_mode", ["mode"] = "GameMode" });
        if (!sent)
        {
            Log.Write("Game Mode button: controller not reachable");
            Post(new { type = "toast", text = "The tray controller isn't reachable; press Ctrl+Alt+G or use the tray menu." });
        }
    }

    void RestoreStockWindows()
    {
        Log.Write("Restore Stock Windows button: shutting down controller and shell");
        try
        {
            using var quit = EventWaitHandle.OpenExisting(Paths.ControllerQuitEvent);
            quit.Set();
            return;
        }
        catch { /* standalone shell without controller */ }
        Close();
    }

    void HandlePowerAction(string action)
    {
        try
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "lock":
                    Log.Write("Power action: Lock workstation");
                    Native.LockWorkStation();
                    break;
                case "sleep":
                    Log.Write("Power action: Sleep");
                    Application.SetSuspendState(PowerState.Suspend, false, false);
                    break;
                case "restart":
                    Log.Write("Power action: Restart Windows");
                    using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true })) { }
                    break;
                case "shutdown":
                    Log.Write("Power action: Shut down Windows");
                    using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true })) { }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Power action '{action}' failed: {ex.Message}");
            Post(new { type = "toast", text = "Power action failed: " + ex.Message });
        }
    }

    void SendBackgrounds()
    {
        var bg = ReadJson("shell.json")?["background"];
        Post(new
        {
            type = "backgrounds",
            items = Backgrounds.List(root),
            active = (string?)bg?["image"],
            dim = (double?)bg?["dim"] ?? 0.45,
            fit = Backgrounds.NormalizeFit((string?)bg?["fit"]),
        });
    }

    void BackgroundAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Write("Background change failed: " + ex.Message); Post(new { type = "toast", text = "Background not changed: " + ex.Message }); }
        SendBackgrounds();
    }

    void DeckAction(Func<(string message, string? selectedDeck)> action)
    {
        try
        {
            var (message, selectedDeck) = action();
            if (!string.IsNullOrEmpty(selectedDeck)) Post(new { type = "deck_select", name = selectedDeck });
            SendConfig();
            Post(new { type = "toast", text = message });
        }
        catch (Exception ex)
        {
            Log.Write("Deck change failed: " + ex.Message);
            Post(new { type = "toast", text = "Deck not changed: " + ex.Message });
        }
    }

    void PickBackground()
    {
        // A tiny invisible top-level owner, so the dialog never disables Explorer's desktop window (our parent).
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
        using var owner = new Form
        {
            ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(area.X + area.Width / 2, area.Y + area.Height / 2, 1, 1), Opacity = 0, TopMost = true,
        };
        owner.Show();
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a background picture",
            Filter = "Pictures|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(owner) == DialogResult.OK)
            BackgroundAction(() => Backgrounds.Save(root, Backgrounds.Import(root, dlg.FileName), null));
    }

    void StartControllerLink()
    {
        var cc = ControllerConfig.Read(root);
        string token = (string?)cc?["ipc_token"] ?? "";
        if (token.Length < 32) { Log.Write("Controller link off: no token in controller.json yet"); return; }

        controllerLink = new ControllerLink((int?)cc?["ipc_port"] ?? 47800, token);
        controllerLink.Start();
    }

    void SafeInvoke(Action a)
    {
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
    }

    async void ListStartAppsAsync()
    {
        var items = await Task.Run(() => StartApps.Get(refresh: true));
        Post(new { type = "start_apps", items = items.Select(i => new { name = i.Name, id = i.Id, kind = i.Kind }) });
    }

    void AddApp(JsonNode msg)
    {
        try
        {
            string id = (string?)msg["id"] ?? "";
            var item = StartApps.Get().FirstOrDefault(i => i.Id == id);
            if (item is null) throw new InvalidOperationException("that app isn't in the Start menu list");

            string name = ((string?)msg["name"] ?? "").Trim();
            if (name.Length is 0 or > 40) name = item.Name.Length > 40 ? item.Name[..40] : item.Name;
            string icon = (string?)msg["icon"] ?? "app";
            if (!System.Text.RegularExpressions.Regex.IsMatch(icon, "^[a-z]{1,16}$")) icon = "app";

            var known = ((ReadJson("shell.json")?["decks"] as JsonArray)?.Select(d => (string?)d ?? "") ?? new[] { "Command", "Flight", "Dev" }).ToList();
            var decks = ((msg["decks"] as JsonArray)?.Select(d => (string?)d ?? "") ?? Enumerable.Empty<string>())
                .Where(d => known.Contains(d)).Distinct().ToList();
            if (decks.Count == 0) throw new InvalidOperationException("pick at least one deck");

            string result = AppsStore.Add(root, name, StartApps.LaunchUri(item.Id), icon, decks, (bool?)msg["pinned"] == true);
            Post(new { type = "toast", text = result });
        }
        catch (Exception ex)
        {
            Log.Write("Add app failed: " + ex.Message);
            Post(new { type = "toast", text = "Not added: " + ex.Message });
        }
    }

    static readonly HashSet<string> TaskbarClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow",
    };

    /// <summary>When the user clicks the 3D desktop while Windows' auto-hide taskbar or tray popup
    /// holds foreground focus, hand focus back to Progman so the taskbar slides down cleanly.</summary>
    void DismissTaskbarIfFocused()
    {
        if (desktopHost == IntPtr.Zero) return;
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == desktopHost) return;
        if (!TaskbarClasses.Contains(Native.ClassName(fg))) return;

        uint myTid = Native.GetCurrentThreadId();
        uint hostTid = Native.GetWindowThreadProcessId(desktopHost, out _);
        uint fgTid = Native.GetWindowThreadProcessId(fg, out _);
        bool attached = fgTid != 0 && fgTid != myTid && fgTid != hostTid
                        && Native.AttachThreadInput(myTid, fgTid, true);
        try { Native.SetForegroundWindow(desktopHost); }
        finally { if (attached) Native.AttachThreadInput(myTid, fgTid, false); }
    }

    void OnTick()
    {
        if (desktopHost != IntPtr.Zero)
        {
            if (!Native.IsWindow(desktopHost)) { Log.Write("Desktop host window is gone (Explorer restarted?): exiting so the controller restarts us"); Close(); return; }
            bool sizeMismatch = DesktopAttach.NeedsSizeRefit(Handle, desktopHost);
            bool belowIcons = !DesktopAttach.IsAboveIcons(Handle, desktopIcons);
            if (sizeMismatch || belowIcons)
            {
                Log.Write(sizeMismatch ? "Desktop host size changed; refitting shell" : "Desktop icons rose above shell; refitting");
                DesktopAttach.Refit(Handle, desktopHost);
                SendLayout();
            }
        }
        else
        {
            var vs = SystemInformation.VirtualScreen;
            if (Bounds != vs) { Bounds = vs; SendLayout(); }
            Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        if (web.Bounds != ClientRectangle) web.Bounds = ClientRectangle;

        if (pageReady)
        {
            var (win, work) = CurrentLayoutMetrics();
            if (win != lastSentWin || work != lastSentWork) SendLayout();
        }

        bool nowCovered = Occlusion.PrimaryDesktopCovered();
        if (nowCovered != covered)
        {
            covered = nowCovered;
            web.Visible = !covered; // hidden WebView2 stops rendering
        }
        if (!covered) SendTelemetry();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const int WM_DPICHANGED = 0x02E0;
        const int WM_DPICHANGED_AFTERPARENT = 0x02E3;
        if (m.Msg == WM_WINDOWPOSCHANGING && desktopHost == IntPtr.Zero && IsHandleCreated)
        {
            var wp = System.Runtime.InteropServices.Marshal.PtrToStructure<WINDOWPOS>(m.LParam);
            wp.hwndInsertAfter = Native.HWND_BOTTOM;
            System.Runtime.InteropServices.Marshal.StructureToPtr(wp, m.LParam, false);
        }
        base.WndProc(ref m);
        if (m.Msg is WM_DPICHANGED or WM_DPICHANGED_AFTERPARENT)
            ScheduleRefitAndLayout();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;
        ScheduleRefitAndLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ScheduleRefitAndLayout();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    void ScheduleRefitAndLayout()
    {
        SafeInvoke(RefitAndSendLayout);
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            SafeInvoke(RefitAndSendLayout);
            await Task.Delay(500);
            SafeInvoke(RefitAndSendLayout);
        });
    }

    void RefitAndSendLayout()
    {
        if (desktopHost != IntPtr.Zero && Native.IsWindow(desktopHost))
            DesktopAttach.Refit(Handle, desktopHost);
        else if (desktopHost == IntPtr.Zero)
            Bounds = SystemInformation.VirtualScreen;
        if (web.Bounds != ClientRectangle) web.Bounds = ClientRectangle;
        SendLayout();
    }

    void OnDisplayChanged(object? sender, EventArgs e) => ScheduleRefitAndLayout();

    void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => ScheduleRefitAndLayout(); // e.g. taskbar auto-hide

    // ---------- messages to the page ----------

    void SendConfig()
    {
        if (!pageReady) return;
        apps = (ReadJson("apps.json")?["apps"] as JsonArray) ?? new JsonArray();
        var shellCfg = ReadJson("shell.json") ?? new JsonObject();
        Post(new JsonObject
        {
            ["type"] = "config",
            ["apps"] = AppLauncher.Annotate(apps),
            ["shell"] = shellCfg.DeepClone(),
        });
    }

    void SendTheme()
    {
        if (!pageReady) return;
        var themes = ReadJson("themes.json");
        string active = (string?)themes?["active"] ?? "default";
        var theme = themes?["themes"]?[active];
        if (theme is null) { Log.Write($"Theme '{active}' not found in themes.json"); return; }
        var list = new JsonArray();
        if (themes?["themes"] is JsonObject all)
            foreach (var (key, value) in all)
                list.Add(new JsonObject { ["key"] = key, ["label"] = (string?)value?["label"] ?? key, ["accent"] = (string?)value?["accent"] ?? "#FFFFFF" });
        Post(new JsonObject
        {
            ["type"] = "theme",
            ["name"] = active,
            ["previous"] = (string?)themes?["previous"] ?? "",
            ["theme"] = theme.DeepClone(),
            ["list"] = list,
        });
    }

    (Size win, Rectangle work) CurrentLayoutMetrics()
    {
        var vs = SystemInformation.VirtualScreen;
        var cs = ClientSize;
        var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        IntPtr mon = Native.MonitorFromPoint(new Point(0, 0), 1);
        Rectangle work = (mon != IntPtr.Zero && Native.GetMonitorInfo(mon, ref mi))
            ? new Rectangle(mi.rcWork.Left - vs.X, mi.rcWork.Top - vs.Y, mi.rcWork.Width, mi.rcWork.Height)
            : new Rectangle(0, 0, vs.Width, vs.Height);
        return (cs, work);
    }

    void SendLayout()
    {
        if (!pageReady) return;
        var (cs, work) = CurrentLayoutMetrics();
        lastSentWin = cs;
        lastSentWork = work;
        Post(new
        {
            type = "layout",
            win = new { w = cs.Width, h = cs.Height },
            work = new { x = work.X, y = work.Y, w = work.Width, h = work.Height },
        });
    }

    void SendStatus()
    {
        Post(new { type = "status" });
    }

    void SendTelemetry()
    {
        if (!pageReady || telemetry is null) return;
        try { Post(telemetry.Sample()); } catch (Exception ex) { Log.Write("Telemetry failed: " + ex.Message); }
    }

    void Post(object payload)
    {
        try
        {
            string json = payload is JsonNode node ? node.ToJsonString() : JsonSerializer.Serialize(payload);
            web.CoreWebView2?.PostWebMessageAsJson(json);
        }
        catch (Exception ex) { Log.Write("Post failed: " + ex.Message); }
    }

    JsonNode? ReadJson(string file)
    {
        string path = Path.Combine(configDir, file);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null; }
            catch (IOException) { Thread.Sleep(100); } // file still being written
            catch (JsonException ex) { Log.Write($"{file} is not valid JSON: {ex.Message}"); return null; }
        }
        return null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Stop();
        tick.Dispose();
        configDebounce.Stop();
        configDebounce.Dispose();
        quitWait?.Unregister(null);
        quitEvent?.Dispose();
        watcher?.Dispose();
        controllerLink?.Dispose();
        telemetry?.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        base.OnFormClosed(e);
    }
}
