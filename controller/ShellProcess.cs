using System.Diagnostics;

namespace Desktop3D.Controller;

/// <summary>Starts, stops and watches the 3D shell. Restarts it once after a crash;
/// a second crash within 5 minutes leaves stock Windows up.</summary>
sealed class ShellProcess
{
    readonly string exe;
    readonly SynchronizationContext ui;
    Process? proc;
    bool stopping;
    DateTime lastCrash = DateTime.MinValue;

    public event Action<string>? GaveUp;

    public ShellProcess(string root)
    {
        exe = Path.Combine(root, "bin", "shell", Paths.ShellExeName + ".exe");
        ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public bool IsRunning => proc is { HasExited: false };

    public void Start()
    {
        if (IsRunning) { Log.Write("  shell already running"); return; }
        KillStrays(null);
        if (!File.Exists(exe)) throw new FileNotFoundException("Shell not built yet: " + exe);
        stopping = false;
        proc?.Dispose();
        proc = Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        })!;
        proc.EnableRaisingEvents = true;
        proc.Exited += OnExited;
        Log.Write($"  shell started (pid {proc.Id})");
    }

    public void Stop(bool force = false)
    {
        stopping = true;
        var p = proc;
        proc = null;
        if (p is null || p.HasExited)
        {
            p?.Dispose();
            KillStrays(null);
            return;
        }
        if (!force)
        {
            try
            {
                using var quit = EventWaitHandle.OpenExisting(Paths.ShellQuitEvent);
                quit.Set();
                if (p.WaitForExit(3000)) { Log.Write("  shell closed cleanly"); p.Dispose(); return; }
            }
            catch (Exception ex) { Log.Write("  clean close not possible: " + ex.Message); }
        }
        // Only the shell itself: apps launched from its tiles are its child processes and must keep running.
        // (Its WebView2 helper processes exit on their own when the shell is gone.)
        try { p.Kill(entireProcessTree: false); p.WaitForExit(3000); Log.Write("  shell process ended"); }
        catch (Exception ex) { Log.Write("  kill failed: " + ex.Message); }
        p.Dispose();
        KillStrays(null);
    }

    void OnExited(object? sender, EventArgs e)
    {
        var p = (Process)sender!;
        int code;
        try { code = p.ExitCode; } catch { code = int.MinValue; }
        if (stopping || !ReferenceEquals(p, proc)) { Log.Write($"Shell exited (requested), code {code}"); return; }

        Log.Write($"Shell exited unexpectedly, code {code}. Stock desktop is visible underneath.");
        var now = DateTime.Now;
        if (now - lastCrash < TimeSpan.FromMinutes(5))
        {
            Log.Write("Second unexpected exit within 5 minutes: not restarting. Stock Windows stays up.");
            ui.Post(_ => GaveUp?.Invoke($"The 3D desktop stopped twice (exit code {code}). Stock Windows is up. Ctrl+Alt+I tries again."), null);
            return;
        }
        lastCrash = now;
        ui.Post(async _ =>
        {
            await Task.Delay(1000);
            if (stopping) return;
            try { Log.Write("Watchdog: restarting shell"); Start(); }
            catch (Exception ex) { Log.Write("Watchdog restart failed: " + ex.Message); }
        }, null);
    }

    static void KillStrays(Process? keep)
    {
        foreach (var p in Process.GetProcessesByName(Paths.ShellExeName))
        {
            try
            {
                if (keep is not null && p.Id == keep.Id) continue;
                p.Kill(entireProcessTree: false);
                Log.Write($"  ended stray shell pid {p.Id}");
            }
            catch { }
            finally { p.Dispose(); }
        }
    }
}
