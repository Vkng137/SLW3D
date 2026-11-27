using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Desktop3D.Shell;

/// <summary>Background pictures. Chosen pictures are COPIED into assets\backgrounds (the originals are never
/// touched), served to the page from https://media.desktop3d.local/. The choice and the dim level live in
/// config\shell.json "background": {"image": "&lt;file&gt;" or null, "dim": 0.0-0.9}.</summary>
static class Backgrounds
{
    static readonly string[] Exts = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };
    static readonly HashSet<string> FitModes = new(StringComparer.OrdinalIgnoreCase) { "fill", "fit", "stretch", "center" };

    public static string NormalizeFit(string? fit) =>
        !string.IsNullOrWhiteSpace(fit) && FitModes.Contains(fit.Trim()) ? fit.Trim().ToLowerInvariant() : "fill";

    public static string Dir(string root)
    {
        string d = Path.Combine(root, "assets", "backgrounds");
        Directory.CreateDirectory(d);
        return d;
    }

    public static List<string> List(string root) =>
        new DirectoryInfo(Dir(root)).GetFiles()
            .Where(f => Exts.Contains(f.Extension.ToLowerInvariant()))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => f.Name).ToList();

    public static bool IsValidName(string? f) =>
        !string.IsNullOrWhiteSpace(f) && f == Path.GetFileName(f) && Exts.Contains(Path.GetExtension(f).ToLowerInvariant());

    /// <summary>Copies a picture in and returns its new file name.</summary>
    public static string Import(string root, string source, string? nameHint = null)
    {
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext.Length == 0) ext = ".jpg"; // Windows' TranscodedWallpaper has no extension (it is a JPEG)
        if (!Exts.Contains(ext)) throw new InvalidOperationException("that file type isn't a supported picture");

        string baseName = new string((nameHint ?? Path.GetFileNameWithoutExtension(source))
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '-').ToArray()).Trim();
        if (baseName.Length == 0) baseName = "background";
        if (baseName.Length > 40) baseName = baseName[..40];

        string dir = Dir(root), name = baseName + ext;
        for (int i = 2; File.Exists(Path.Combine(dir, name)); i++) name = $"{baseName} ({i}){ext}";
        File.Copy(source, Path.Combine(dir, name));
        Log.Write($"Background added: {name}");
        return name;
    }

    public static void Remove(string root, string file)
    {
        if (!IsValidName(file)) throw new InvalidOperationException("not a background file");
        string path = Path.Combine(Dir(root), file);
        if (File.Exists(path)) File.Delete(path);
        Log.Write($"Background removed: {file}");
    }

    /// <summary>The current Windows wallpaper file, if there is one.</summary>
    public static string? WindowsWallpaper()
    {
        string? wp = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;
        if (!string.IsNullOrWhiteSpace(wp) && File.Exists(wp)) return wp;
        string transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    /// <summary>Changes config\shell.json "background" (the shell's file watcher then sends it to the page).</summary>
    public static void Save(string root, string? image, double? dim, string? fit = null)
    {
        ShellConfig.Update(root, doc =>
        {
            var bg = doc["background"] as JsonObject ?? new JsonObject();
            if (image is not null) bg["image"] = image.Length == 0 ? null : image;
            if (dim is not null) bg["dim"] = Math.Round(Math.Clamp(dim.Value, 0, 0.9), 2);
            if (!bg.ContainsKey("dim")) bg["dim"] = 0.45;
            if (fit is not null) bg["fit"] = NormalizeFit(fit);
            else if (!bg.ContainsKey("fit")) bg["fit"] = "fill";
            doc["background"] = bg.Parent is null ? bg : bg.DeepClone();
        });
    }
}
