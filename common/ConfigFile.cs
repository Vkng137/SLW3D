using System.Text.Json;
using System.Text.Json.Nodes;

namespace SLW3D;

/// <summary>Thread- and cross-process-safe JSON config reads and atomic writes.</summary>
public static class ConfigFile
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    static Mutex AcquireLock()
    {
        var m = new Mutex(false, "Local\\SLW3D.ConfigLock");
        try { m.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { /* previous owner exited while holding it */ }
        return m;
    }

    public static JsonNode? ReadJson(string path, int retries = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null; }
            catch (IOException) when (i < retries - 1) { Thread.Sleep(80); }
        }
        return null;
    }

    public static void WriteTextAtomic(string path, string content, string? backupPath = null)
    {
        using var gate = AcquireLock();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (backupPath is not null && File.Exists(path))
                File.Copy(path, backupPath, overwrite: true);
            string tmp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally { try { gate.ReleaseMutex(); } catch { } }
    }

    public static void UpdateJson(string path, Action<JsonObject> mutate, string? backupPath = null)
    {
        using var gate = AcquireLock();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var doc = (ReadJson(path) as JsonObject) ?? new JsonObject();
            mutate(doc);
            if (backupPath is not null && File.Exists(path))
                File.Copy(path, backupPath, overwrite: true);
            string tmp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, doc.ToJsonString(Indented) + "\n");
            File.Move(tmp, path, overwrite: true);
        }
        finally { try { gate.ReleaseMutex(); } catch { } }
    }
}
