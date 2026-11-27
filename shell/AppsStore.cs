using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SLW3D.Shell;

/// <summary>Adds and removes tiles in config\apps.json. Keeps one app per line, and saves the
/// previous version as apps.json.bak before every change.</summary>
static class AppsStore
{
    static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string FilePath(string root) => Path.Combine(root, "config", "apps.json");

    public const int MaxAppsPerDeck = 18;

    public static string Add(string root, string name, string uri, string icon, List<string> decks, bool pinned)
    {
        var (doc, apps) = Load(root);
        var existing = Find(apps, name);
        if (existing is not null)
        {
            var have = DecksOf(existing);
            var added = decks.Where(d => !have.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var d in added)
                EnsureDeckHasRoom(apps, d);
            if (added.Count == 0 && !(pinned && (bool?)existing["pinned"] != true))
                return $"{name} is already on {string.Join(" and ", have)}.";
            SetDecks(existing, have.Concat(added).ToList());
            if (pinned) existing["pinned"] = true;
            Save(root, doc, apps);
            return $"{name} added to {string.Join(" and ", added.DefaultIfEmpty("the dock"))}.";
        }

        foreach (var d in decks)
            EnsureDeckHasRoom(apps, d);

        var app = new JsonObject
        {
            ["name"] = name,
            ["uri"] = uri,
            ["decks"] = new JsonArray(decks.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()),
            ["icon"] = icon,
        };
        if (pinned) app["pinned"] = true;
        apps.Add(app);
        Save(root, doc, apps);
        Log.Write($"Added app '{name}' ({uri}) to {string.Join(", ", decks)}{(pinned ? ", pinned" : "")}");
        return $"{name} added to {string.Join(" and ", decks)}.";
    }

    public static string RemoveFromDeck(string root, string name, string deck)
    {
        var (doc, apps) = Load(root);
        var app = Find(apps, name);
        if (app is null) return $"{name} isn't in apps.json.";
        var decks = DecksOf(app).Where(d => !string.Equals(d, deck, StringComparison.OrdinalIgnoreCase)).ToList();
        if (decks.Count == 0)
        {
            apps.Remove(app);
            Save(root, doc, apps);
            Log.Write($"Removed app '{name}' (it was only on {deck})");
            return $"{name} removed. (Previous list saved as apps.json.bak)";
        }
        SetDecks(app, decks);
        Save(root, doc, apps);
        Log.Write($"Removed app '{name}' from {deck}");
        return $"{name} removed from {deck}.";
    }

    public const int MaxDecks = 8;

    static void EnsureDeckHasRoom(JsonArray apps, string deck)
    {
        int count = apps.OfType<JsonObject>()
            .Count(a => DecksOf(a).Any(d => string.Equals(d, deck, StringComparison.OrdinalIgnoreCase)));
        if (count >= MaxAppsPerDeck)
            throw new InvalidOperationException($"deck '{deck}' is full ({MaxAppsPerDeck}/{MaxAppsPerDeck} apps) — pick another deck or create a new one");
    }

    public static (string message, string? selectedDeck) AddDeck(string root, string rawName)
    {
        string name = CleanDeckName(rawName);
        string? created = null;
        ShellConfig.Update(root, doc =>
        {
            var decks = ReadDecksList(doc);
            if (decks.Count >= MaxDecks)
                throw new InvalidOperationException($"maximum of {MaxDecks} decks reached");
            if (decks.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"a deck named '{name}' already exists");
            decks.Add(name);
            doc["decks"] = new JsonArray(decks.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray());
            created = name;
        });
        Log.Write($"Added deck '{created}'");
        return ($"Deck '{created}' added.", created);
    }

    public static (string message, string? selectedDeck) RenameDeck(string root, string oldName, string rawNewName)
    {
        string newName = CleanDeckName(rawNewName);
        string? actualOld = null;
        ShellConfig.Update(root, doc =>
        {
            var decks = ReadDecksList(doc);
            int idx = decks.FindIndex(d => string.Equals(d, oldName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) throw new InvalidOperationException($"deck '{oldName}' not found");
            actualOld = decks[idx];
            if (decks.Where((_, i) => i != idx).Any(d => string.Equals(d, newName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"a deck named '{newName}' already exists");
            decks[idx] = newName;
            doc["decks"] = new JsonArray(decks.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray());
            if (string.Equals((string?)doc["start_deck"], actualOld, StringComparison.OrdinalIgnoreCase))
                doc["start_deck"] = newName;
        });

        // Update any apps referencing the renamed deck so nothing gets orphaned.
        var (appsDoc, apps) = Load(root);
        bool appsChanged = false;
        foreach (var app in apps.OfType<JsonObject>())
        {
            var ds = DecksOf(app);
            bool hit = false;
            for (int i = 0; i < ds.Count; i++)
            {
                if (string.Equals(ds[i], actualOld, StringComparison.OrdinalIgnoreCase))
                { ds[i] = newName; hit = true; }
            }
            if (hit) { SetDecks(app, ds.Distinct(StringComparer.OrdinalIgnoreCase).ToList()); appsChanged = true; }
        }
        if (appsChanged) Save(root, appsDoc, apps);

        Log.Write($"Renamed deck '{actualOld}' -> '{newName}'");
        return ($"Deck renamed to '{newName}'.", newName);
    }

    public static (string message, string? selectedDeck) RemoveDeck(string root, string name)
    {
        string fallback = "Command";
        string? removed = null;
        ShellConfig.Update(root, doc =>
        {
            var decks = ReadDecksList(doc);
            int idx = decks.FindIndex(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) throw new InvalidOperationException($"deck '{name}' not found");
            if (decks.Count <= 1) throw new InvalidOperationException("keep at least one deck");
            removed = decks[idx];
            decks.RemoveAt(idx);
            fallback = decks[0];
            doc["decks"] = new JsonArray(decks.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray());
            if (string.Equals((string?)doc["start_deck"], removed, StringComparison.OrdinalIgnoreCase))
                doc["start_deck"] = fallback;
        });

        // Remove the deleted deck from every app; apps that belonged ONLY to this deck are removed cleanly
        // (instead of dumping them onto another deck). Previous list is backed up to apps.json.bak.
        var (appsDoc, apps) = Load(root);
        int deletedApps = 0;
        bool appsChanged = false;
        for (int i = apps.Count - 1; i >= 0; i--)
        {
            if (apps[i] is not JsonObject app) continue;
            var ds = DecksOf(app);
            if (!ds.Any(d => string.Equals(d, removed, StringComparison.OrdinalIgnoreCase))) continue;
            var remaining = ds.Where(d => !string.Equals(d, removed, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remaining.Count == 0)
            {
                apps.RemoveAt(i);
                deletedApps++;
            }
            else
            {
                SetDecks(app, remaining);
            }
            appsChanged = true;
        }
        if (appsChanged) Save(root, appsDoc, apps);

        Log.Write($"Removed deck '{removed}'" + (deletedApps > 0 ? $" (removed {deletedApps} deck-only app(s))" : ""));
        string note = deletedApps > 0 ? $" Removed {deletedApps} app(s) from that deck." : "";
        return ($"Deck '{removed}' removed.{note}", fallback);
    }

    static string CleanDeckName(string raw)
    {
        string s = (raw ?? "").Trim();
        if (s.Length == 0) throw new InvalidOperationException("enter a deck name");
        if (s.Length > 16) s = s[..16].Trim();
        if (s.Length == 0) throw new InvalidOperationException("enter a deck name");
        return s;
    }

    static List<string> ReadDecksList(JsonObject doc)
    {
        if (doc["decks"] is JsonArray arr)
        {
            var list = arr.Select(d => (string?)d ?? "").Where(d => d.Length > 0).ToList();
            if (list.Count > 0) return list;
        }
        return new List<string> { "Command", "Flight", "Dev" };
    }

    public static string Unpin(string root, string name)
    {
        var (doc, apps) = Load(root);
        var app = Find(apps, name);
        if (app is null) return $"{name} isn't in apps.json.";
        app.Remove("pinned");
        Save(root, doc, apps);
        Log.Write($"Unpinned '{name}'");
        return $"{name} unpinned from the dock.";
    }

    static (JsonObject doc, JsonArray apps) Load(string root)
    {
        var doc = ConfigFile.ReadJson(FilePath(root)) as JsonObject ?? new JsonObject();
        if (doc["apps"] is not JsonArray apps) { apps = new JsonArray(); doc["apps"] = apps; }
        return (doc, apps);
    }

    static JsonObject? Find(JsonArray apps, string name) =>
        apps.OfType<JsonObject>().FirstOrDefault(a => string.Equals((string?)a["name"], name, StringComparison.OrdinalIgnoreCase));

    static List<string> DecksOf(JsonObject a)
    {
        if (a["decks"] is JsonArray arr) return arr.Select(d => (string?)d ?? "").Where(d => d.Length > 0).ToList();
        if ((string?)a["deck"] is { Length: > 0 } one) return new List<string> { one };
        return new List<string>();
    }

    static void SetDecks(JsonObject a, List<string> decks)
    {
        a.Remove("deck");
        a["decks"] = new JsonArray(decks.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray());
    }

    static void Save(string root, JsonObject doc, JsonArray apps)
    {
        string path = FilePath(root);
        var sb = new StringBuilder("{\n");
        var keys = doc.Select(kv => kv.Key).ToList();
        for (int k = 0; k < keys.Count; k++)
        {
            string key = keys[k];
            sb.Append("  ").Append(JsonSerializer.Serialize(key)).Append(": ");
            if (key == "apps")
            {
                sb.Append("[\n");
                for (int i = 0; i < apps.Count; i++)
                    sb.Append("    ").Append(apps[i]!.ToJsonString(Compact)).Append(i < apps.Count - 1 ? ",\n" : "\n");
                sb.Append("  ]");
            }
            else sb.Append(doc[key]?.ToJsonString(Compact) ?? "null");
            sb.Append(k < keys.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("}\n");

        ConfigFile.WriteTextAtomic(path, sb.ToString(), backupPath: path + ".bak");
    }
}
