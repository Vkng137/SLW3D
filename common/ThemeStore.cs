using System.Text.Json.Nodes;

namespace SLW3D;

/// <summary>Reads the theme list and switches the active theme in config\themes.json.
/// The 3D shell watches the file and crossfades to the new theme.</summary>
public static class ThemeStore
{
    static string FilePath(string root) => Path.Combine(root, "config", "themes.json");

    public static (string active, string previous, List<(string key, string label, string accent)> themes) Read(string root)
    {
        var node = ConfigFile.ReadJson(FilePath(root)) ?? throw new InvalidOperationException("themes.json not found");
        var list = new List<(string, string, string)>();
        if (node["themes"] is JsonObject themes)
            foreach (var (key, value) in themes)
                list.Add((key, (string?)value?["label"] ?? key, (string?)value?["accent"] ?? "#FFFFFF"));
        return ((string?)node["active"] ?? "", (string?)node["previous"] ?? "", list);
    }

    public static void SetActive(string root, string key)
    {
        string old = "";
        bool changed = false;
        ConfigFile.UpdateJson(FilePath(root), doc =>
        {
            old = (string?)doc["active"] ?? "";
            if (old == key) return;
            if (doc["themes"]?[key] is null)
                throw new ArgumentException($"Theme '{key}' is not in themes.json");
            doc["active"] = key;
            doc["previous"] = old;
            changed = true;
        });
        if (changed) Log.Write($"Theme {old} -> {key}");
    }
}
