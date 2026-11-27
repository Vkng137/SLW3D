namespace SLW3D.Controller;

enum Mode { Stock, Full, ManualControl, GameMode }

sealed class SkipException(string reason) : Exception(reason);

/// <summary>The mode state machine. Each transition is a list of logged steps;
/// a failed step is logged and the transition carries on.</summary>
sealed class ModeController
{
    readonly ShellProcess shell;

    public Mode Current { get; private set; } = Mode.Stock;
    public DateTime LastChange { get; private set; } = DateTime.MinValue;
    public event Action? Changed;
    public event Action<string, string>? Notify;

    public ModeController(string root)
    {
        shell = new ShellProcess(root);
        shell.GaveUp += msg => { Current = Mode.Stock; Changed?.Invoke(); Notify?.Invoke("3D desktop stopped", msg); };
    }

    public void SetMode(Mode target, string source)
    {
        if (target == Current && (target != Mode.Full || shell.IsRunning))
        {
            Log.Write($"Already in {target} (source: {source})");
            return;
        }
        Log.Write($"Transition {Current} -> {target} (source: {source})");

        switch (target)
        {
            case Mode.Full:
                Step("Start 3D shell", shell.Start);
                break;

            case Mode.ManualControl:
            case Mode.GameMode:
                Step("Stop 3D shell", () => shell.Stop());
                break;
        }

        Current = target;
        LastChange = DateTime.Now;
        Changed?.Invoke();
    }

    /// <summary>Ctrl+Alt+Shift+D: end the 3D shell immediately and leave stock Windows.</summary>
    public void Panic(string source)
    {
        Log.Write($"PANIC RESTORE (source: {source})");
        Step("Stop 3D shell", () => shell.Stop(force: true));
        Current = Mode.Stock;
        LastChange = DateTime.Now;
        Changed?.Invoke();
        Notify?.Invoke("Stock Windows restored", "Ctrl+Alt+I brings the 3D desktop back.");
    }

    public void Shutdown()
    {
        Log.Write("Controller exiting: stopping shell");
        Step("Stop 3D shell", () => shell.Stop());
    }

    static void Step(string name, Action work)
    {
        try
        {
            work();
            Log.Write($"  ok: {name}");
        }
        catch (SkipException ex)
        {
            Log.Write($"  skipped: {name} ({ex.Message})");
        }
        catch (Exception ex)
        {
            Log.Write($"  FAILED: {name} — {ex.Message}");
        }
    }
}
