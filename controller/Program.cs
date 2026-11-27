namespace SLW3D.Controller;

static class Program
{
    [STAThread]
    static void Main()
    {
        string root;
        try { root = Paths.FindRoot(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SLW3D", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Log.Init(Path.Combine(root, "logs", "controller.log"));

        using var single = new Mutex(true, "SLW3D.Controller.Single", out bool first);
        if (!first)
        {
            string? otherDir = FindOtherControllerDir();
            if (otherDir is not null)
            {
                Log.Write($"Another controller is already running from '{otherDir}'; refusing to cross-wake from '{root}'.");
                MessageBox.Show(
                    $"SLW3D is already running from:\n{otherDir}\n\nRight-click its system tray icon and choose \"Exit (back to stock Windows)\" first before launching this copy from:\n{root}",
                    "SLW3D — Another Copy Is Running",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            try
            {
                using var wake = EventWaitHandle.OpenExisting(Paths.ControllerWakeEvent);
                wake.Set();
                Log.Write("Controller already running; signalled it to restore Full mode.");
            }
            catch
            {
                Log.Write("Controller already running; second copy exits.");
            }
            return;
        }

        Log.Write("==== Controller starting ====");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.ThreadException += (_, e) => Log.Write("UI exception: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Unhandled: " + e.ExceptionObject);

        Application.Run(new TrayApp(root));
        Log.Write("==== Controller stopped ====");
    }

    /// <summary>Returns the folder of another running SLW3D.Controller if it lives in a different install directory.</summary>
    static string? FindOtherControllerDir()
    {
        string myPath = Path.GetFullPath(Environment.ProcessPath ?? "");
        int myPid = Environment.ProcessId;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("SLW3D.Controller"))
        {
            try
            {
                if (p.Id == myPid) continue;
                string? theirPath = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(theirPath)) continue;
                if (!string.Equals(Path.GetFullPath(theirPath), myPath, StringComparison.OrdinalIgnoreCase))
                    return Directory.GetParent(Path.GetDirectoryName(theirPath)!)?.Parent?.FullName ?? Path.GetDirectoryName(theirPath);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return null;
    }
}
