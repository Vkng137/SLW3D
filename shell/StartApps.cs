using System.Text.RegularExpressions;

namespace SLW3D.Shell;

/// <summary>Windows' own "All apps" list (shell:AppsFolder): desktop programs, Store apps and Steam items.
/// Every entry launches the same way, through shell:AppsFolder\&lt;id&gt; (or its own steam:// link).</summary>
static class StartApps
{
    public sealed record Item(string Name, string Id, string Kind);

    static List<Item>? cache;
    static DateTime cachedAt;

    static readonly Regex Clutter = new(
        @"^(uninstall|repair|update|readme|release notes)\b|\buninstall\b|\bhelp$|\bwebsite\b|on the web$|support center|visit our website|\bmanual$|\bdocumentation\b",
        RegexOptions.IgnoreCase);
    static readonly string[] DocExtensions = { ".url", ".pdf", ".chm", ".txt", ".htm", ".html", ".rtf", ".md" };

    public static List<Item> Get(bool refresh = false)
    {
        if (!refresh && cache is not null && DateTime.Now - cachedAt < TimeSpan.FromMinutes(2)) return cache;
        var list = new List<Item>();
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application")!;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic items = shell.NameSpace("shell:AppsFolder").Items();
                int count = items.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic it = items.Item(i);
                    string name = (string)it.Name ?? "";
                    string id = (string)it.Path ?? "";
                    if (name.Length == 0 || id.Length == 0) continue;
                    if (Clutter.IsMatch(name)) continue;
                    if (id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    if (DocExtensions.Any(e => id.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                    string kind = id.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ? "Steam"
                        : id.Contains('!') ? "Store app" : "Program";
                    list.Add(new Item(name, id, kind));
                }
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        bool completed = t.Join(15000);
        if (!completed)
        {
            Log.Write("Start apps list timed out after 15 s");
            return cache ?? list;
        }
        if (error is not null)
        {
            Log.Write("Start apps list failed: " + error.Message);
            return cache ?? list;
        }

        cache = list
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        cachedAt = DateTime.Now;
        return cache;
    }

    /// <summary>The launch link stored in apps.json for an All-apps entry.</summary>
    public static string LaunchUri(string id) =>
        Regex.IsMatch(id, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://") ? id : @"shell:AppsFolder\" + id;
}
