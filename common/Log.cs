namespace Desktop3D;

/// <summary>Tiny thread-safe file logger with timestamps. Rolls to .old at ~2 MB.</summary>
public static class Log
{
    static string? path;
    static readonly object Gate = new();

    public static string FilePath => path ?? "";

    public static void Init(string filePath)
    {
        path = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists && fi.Length > 2_000_000)
            {
                File.Copy(filePath, filePath + ".old", overwrite: true);
                File.WriteAllText(filePath, "");
            }
        }
        catch { /* logging must never crash the app */ }
    }

    public static void Write(string message)
    {
        if (path is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
            catch { /* ignore */ }
        }
    }
}
