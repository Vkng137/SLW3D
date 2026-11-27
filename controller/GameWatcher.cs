using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SLW3D.Controller;

/// <summary>Automatic Game Mode. Every 4 s: is one of the games in controller.json "game_processes"
/// running? If so (and we're in Full mode), switch to Game Mode so everything this project runs shuts
/// down. When no game has run for "game_exit_grace_s", switch back to Full.
/// If Chuck leaves Game Mode by hand while a game is still running, it stays off until that game closes.</summary>
sealed class GameWatcher : IDisposable
{
    readonly string cfgPath;
    readonly Func<Mode> getMode;
    readonly Func<DateTime> getLastChange;
    readonly Action<Mode, string> setMode;
    readonly HashSet<string> games = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Forms.Timer? timer;
    TimeSpan grace = TimeSpan.FromSeconds(15);
    DateTime cfgMtime = DateTime.MinValue;
    bool autoEntered;
    string? activeGame;
    DateTime sessionStart = DateTime.MaxValue, lastSeen = DateTime.MinValue;

    public GameWatcher(ModeController modes, string root)
        : this(() => modes.Current, () => modes.LastChange, modes.SetMode, Path.Combine(root, "config", "controller.json"), startTimer: true)
    { }

    internal GameWatcher(Func<Mode> getMode, Func<DateTime> getLastChange, Action<Mode, string> setMode,
                         string cfgPath, bool startTimer = false)
    {
        this.getMode = getMode;
        this.getLastChange = getLastChange;
        this.setMode = setMode;
        this.cfgPath = cfgPath;
        ReloadConfig(logAlways: true);
        if (startTimer)
        {
            timer = new System.Windows.Forms.Timer { Interval = 4000 };
            timer.Tick += (_, _) => { ReloadConfig(logAlways: false); Check(FindRunningGame(), DateTime.Now); };
            timer.Start();
        }
    }

    void ReloadConfig(bool logAlways)
    {
        try
        {
            if (!File.Exists(cfgPath)) return;
            var mtime = File.GetLastWriteTimeUtc(cfgPath);
            if (!logAlways && mtime == cfgMtime) return;
            cfgMtime = mtime;
            var cfg = ConfigFile.ReadJson(cfgPath);
            if (cfg is null) return;
            games.Clear();
            if (cfg["game_processes"] is JsonArray arr)
                foreach (var n in arr) if ((string?)n is { Length: > 0 } s) games.Add(s);
            int graceS = (int?)cfg["game_exit_grace_s"] ?? 15;
            grace = TimeSpan.FromSeconds(Math.Max(5, graceS));
            Log.Write($"Game watcher on: {string.Join(", ", games)} (back to Full {grace.TotalSeconds:0} s after the game closes)");
        }
        catch (Exception ex) { Log.Write("Game watcher: controller.json not readable: " + ex.Message); }
    }

    internal void Check(string? game, DateTime now)
    {
        if (game is not null)
        {
            bool isNewGame = activeGame is null || !string.Equals(activeGame, game, StringComparison.OrdinalIgnoreCase);
            if (sessionStart == DateTime.MaxValue || isNewGame)
            {
                sessionStart = now;
                activeGame = game;
                Log.Write($"Game detected: {game}");
            }
            lastSeen = now;
            // Enter only if Full mode was set before this game started (a manual switch to Full during the game wins).
            if (getMode() == Mode.Full && getLastChange() < sessionStart)
            {
                autoEntered = true;
                setMode(Mode.GameMode, $"auto: {game} is running");
            }
            return;
        }

        if (sessionStart != DateTime.MaxValue && now - lastSeen >= grace)
        {
            Log.Write("Game closed");
            sessionStart = DateTime.MaxValue;
            activeGame = null;
            if (autoEntered && getMode() == Mode.GameMode)
                setMode(Mode.Full, "auto: game closed");
            autoEntered = false;
        }
    }

    string? FindRunningGame()
    {
        if (games.Count == 0) return null;
        string? found = null;
        foreach (var p in Process.GetProcesses())
        {
            if (found is null && games.Contains(p.ProcessName)) found = p.ProcessName;
            p.Dispose();
        }
        return found;
    }

    public void Dispose()
    {
        if (timer is not null) { timer.Stop(); timer.Dispose(); }
    }
}
