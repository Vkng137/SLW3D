using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace SLW3D;

/// <summary>config\controller.json: ports and the shared token that local connections must present.</summary>
public static class ControllerConfig
{
    const string Placeholder = "GENERATED_ON_FIRST_RUN";

    static string FilePath(string root) => Path.Combine(root, "config", "controller.json");
    static string ExamplePath(string root) => Path.Combine(root, "config", "controller.example.json");

    public static JsonNode? Read(string root)
    {
        try { return ConfigFile.ReadJson(FilePath(root)); }
        catch (Exception ex) { Log.Write("controller.json not readable: " + ex.Message); return null; }
    }

    /// <summary>Creates controller.json from controller.example.json if missing, and generates a random token if unset.</summary>
    public static void EnsureToken(string root)
    {
        string path = FilePath(root);
        if (!File.Exists(path) && File.Exists(ExamplePath(root)))
            ConfigFile.WriteTextAtomic(path, File.ReadAllText(ExamplePath(root)));

        var node = ConfigFile.ReadJson(path);
        string token = (string?)node?["ipc_token"] ?? "";
        if (token.Length >= 32 && token != Placeholder) return;

        string fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        ConfigFile.UpdateJson(path, doc => doc["ipc_token"] = fresh);
        Log.Write("Generated a new local connection token in controller.json");
    }

    public static bool TokenMatches(string expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || given is null) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(given);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
