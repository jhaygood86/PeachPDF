using System.Text.Json;

namespace PeachBrackets.WptRunner;

internal sealed record WptModule(string Id, string[] Dirs);

/// <summary>The module list (which WPT directories make up each gated module) and test discovery within it.</summary>
internal static class Modules
{
    private static readonly string[] TestExtensions = [".html", ".htm", ".xht", ".xhtml", ".svg"];
    private static readonly HashSet<string> NonTestDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "support", "resources", "reference", "references", "tools",
    };

    public static List<WptModule> Load(string file)
    {
        using var stream = File.OpenRead(file);
        using var doc = JsonDocument.Parse(stream);
        return [.. doc.RootElement.GetProperty("modules").EnumerateArray().Select(m => new WptModule(
            m.GetProperty("id").GetString()!,
            [.. m.GetProperty("dirs").EnumerateArray().Select(d => d.GetString()!)]))];
    }

    /// <summary>Every reftest (a file that links a reference) or testharness test under the module's directories, as WPT-relative paths in ordinal order.</summary>
    public static List<string> Discover(string wptRoot, WptModule module)
    {
        var found = new List<string>();
        foreach (var dir in module.Dirs)
        {
            var root = Path.Combine(wptRoot, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!TestExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    continue;

                var relative = Path.GetRelativePath(wptRoot, file).Replace('\\', '/');
                if (relative.Split('/').SkipLast(1).Any(NonTestDirectories.Contains))
                    continue;

                var info = ReftestParser.Parse(relative, File.ReadAllText(file));
                if (info.Links.Count > 0 || info.IsTestharness)
                    found.Add(relative);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
