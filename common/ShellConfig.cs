using System.Text.Json.Nodes;

namespace Desktop3D;

/// <summary>Small reads and writes of config\shell.json shared by the controller and the shell.</summary>
public static class ShellConfig
{
    static string FilePath(string root) => Path.Combine(root, "config", "shell.json");

    public static JsonObject Read(string root)
    {
        try { return ConfigFile.ReadJson(FilePath(root)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    public static void Set(string root, string key, JsonNode? value) =>
        ConfigFile.UpdateJson(FilePath(root), doc => doc[key] = value);

    public static void Update(string root, Action<JsonObject> mutate) =>
        ConfigFile.UpdateJson(FilePath(root), mutate);
}
