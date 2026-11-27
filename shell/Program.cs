namespace Desktop3D.Shell;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string root;
        try { root = Paths.FindRoot(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "3D desktop"); return; }

        Log.Init(Path.Combine(root, "logs", "shell.log"));
        using var single = new Mutex(true, "Desktop3D.Shell.Single", out bool first);
        if (!first) { Log.Write("Shell already running; second copy exits."); return; }

        Log.Write("==== Shell starting ====");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log.Write("UI exception: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Unhandled: " + e.ExceptionObject);

        bool debug = args.Contains("--debug");
        Application.Run(new ShellForm(root, debug));
        Log.Write("==== Shell stopped ====");
    }
}
