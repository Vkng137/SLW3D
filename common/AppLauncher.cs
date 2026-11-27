using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SLW3D;

/// <summary>Launches only apps listed in config\apps.json, by name. The page can never run anything else.</summary>
public static class AppLauncher
{
    static readonly ConcurrentDictionary<string, DateTime> LastLaunch = new(StringComparer.OrdinalIgnoreCase);
    static readonly TimeSpan LaunchCooldown = TimeSpan.FromMilliseconds(1500);

    /// <summary>Adds "ready" and "note" to each app so the page can dim tiles that can't launch yet.</summary>
    public static JsonArray Annotate(JsonArray apps)
    {
        var result = new JsonArray();
        foreach (var node in apps)
        {
            if (node is not JsonObject a) continue;
            var copy = (JsonObject)a.DeepClone();
            var (ready, note) = Check(a);
            copy["ready"] = ready;
            if (note is not null) copy["note"] = note;
            result.Add(copy);
        }
        return result;
    }

    public static string? Launch(JsonArray apps, string name)
    {
        var app = apps.OfType<JsonObject>().FirstOrDefault(a => string.Equals((string?)a["name"], name, StringComparison.OrdinalIgnoreCase));
        if (app is null) return $"{name} isn't in apps.json.";
        var (ready, note) = Check(app);
        if (!ready) return $"{name}: {note}";

        var now = DateTime.UtcNow;
        if (LastLaunch.TryGetValue(name, out var prev) && now - prev < LaunchCooldown)
        {
            Log.Write($"Ignored duplicate launch of {name} within cooldown");
            return null;
        }
        LastLaunch[name] = now;

        try
        {
            AllowSetForegroundWindow(-1); // let the new app come to the front
            var uri = (string?)app["uri"];
            Process? started;
            if (!string.IsNullOrWhiteSpace(uri) && uri.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                // All-apps entries (Store apps, Start-menu programs): Explorer resolves and starts them.
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(uri);
                started = Process.Start(psi);
            }
            else if (!string.IsNullOrWhiteSpace(uri))
            {
                started = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            else
            {
                string exe = Expand((string)app["exe"]!);
                var psi = new ProcessStartInfo(exe, (string?)app["args"] ?? "") { UseShellExecute = true };
                if (Path.IsPathRooted(exe)) psi.WorkingDirectory = Path.GetDirectoryName(exe)!;
                started = Process.Start(psi);
            }
            started?.Dispose();
            Log.Write($"Launched {name}");
            return null;
        }
        catch (Exception ex)
        {
            LastLaunch.TryRemove(name, out _);
            Log.Write($"Launch {name} failed: {ex.Message}");
            return $"{name} didn't start: {ex.Message}";
        }
    }

    static (bool ready, string? note) Check(JsonObject a)
    {
        if (!string.IsNullOrWhiteSpace((string?)a["uri"])) return (true, null);
        var exeRaw = (string?)a["exe"];
        if (string.IsNullOrWhiteSpace(exeRaw)) return (false, "no exe or uri set");
        if (exeRaw.Contains("PATH/TO", StringComparison.OrdinalIgnoreCase)) return (false, "path not set up yet in apps.json");
        string exe = Expand(exeRaw);
        if (Path.IsPathRooted(exe) && !File.Exists(exe)) return (false, "not found at " + exe);
        return (true, null);
    }

    static string Expand(string p) => Environment.ExpandEnvironmentVariables(p).Replace('/', '\\');

    /// <summary>Reads config\apps.json's "apps" list (empty if missing or unreadable).</summary>
    public static JsonArray ReadApps(string root)
    {
        try { return (ConfigFile.ReadJson(Path.Combine(root, "config", "apps.json"))?["apps"] as JsonArray) ?? new JsonArray(); }
        catch { return new JsonArray(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
}
