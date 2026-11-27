namespace Desktop3D;

public static class Paths
{
    /// <summary>Walks up from the exe folder to the project root (the folder holding PROJECT.md and config\).</summary>
    public static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PROJECT.md")) &&
                Directory.Exists(Path.Combine(dir.FullName, "config")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Project root (PROJECT.md + config) not found above " + AppContext.BaseDirectory);
    }

    public const string ShellExeName = "Desktop3D.Shell";
    public const string ShellQuitEvent = "Desktop3D.Shell.Quit";
    public const string ControllerWakeEvent = "Desktop3D.Controller.Wake";
    public const string ControllerQuitEvent = "Desktop3D.Controller.Quit";
}
