using System.Text.RegularExpressions;

namespace PeachBrackets.WptRunner;

/// <summary>An allowance for a pixel difference: the largest per-channel difference and the number of differing pixels, each as an inclusive range.</summary>
internal readonly record struct Fuzzy(int MaxDiffMin, int MaxDiffMax, int PixelsMin, int PixelsMax)
{
    public static Fuzzy None => new(0, 0, 0, 0);

    public bool Allows(int maxDifference, int differingPixels) =>
        maxDifference >= MaxDiffMin && maxDifference <= MaxDiffMax && differingPixels >= PixelsMin && differingPixels <= PixelsMax;
}

internal enum ReftestRelation { Match, Mismatch }

internal sealed record ReftestLink(ReftestRelation Relation, string Path);

/// <summary>What the runner needs to know about one test or reference file: where it points to, how fuzzy the comparison may be, and why it cannot run.</summary>
internal sealed class ReftestInfo
{
    public required string Path { get; init; }

    public List<ReftestLink> Links { get; } = [];

    /// <summary>Fuzzy allowances keyed by reference path; the empty key applies to every reference.</summary>
    public Dictionary<string, Fuzzy> Fuzzies { get; } = [];

    /// <summary>Why the file cannot be rendered as a reftest (a script, testharness.js, reftest-wait), or null.</summary>
    public string? SkipReason { get; set; }

    public bool IsTestharness { get; set; }
}

/// <summary>Reads the reftest declarations of a WPT file: <c>link rel=match|mismatch</c>, <c>meta name=fuzzy</c>, and what makes it unrunnable without script.</summary>
internal static partial class ReftestParser
{
    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTag();

    [GeneratedRegex("""\b([a-zA-Z-]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))""", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<script\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"<html\b[^>]*\bclass\s*=\s*[""']?[^""'>]*\breftest-wait\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReftestWait();

    [GeneratedRegex(@"\b(maxDifference|totalPixels)\s*=\s*(\d+)(?:\s*-\s*(\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex FuzzyField();

    public static ReftestInfo Parse(string wptPath, string source)
    {
        var info = new ReftestInfo { Path = wptPath };
        var directory = DirectoryOf(wptPath);

        foreach (Match tag in LinkTag().Matches(source))
        {
            var attributes = Attributes(tag.Value);
            if (!attributes.TryGetValue("rel", out var rel) || !attributes.TryGetValue("href", out var href))
                continue;

            var relation = rel.Trim().ToLowerInvariant() switch
            {
                "match" => (ReftestRelation?)ReftestRelation.Match,
                "mismatch" => ReftestRelation.Mismatch,
                _ => null,
            };

            if (relation is { } r)
                info.Links.Add(new ReftestLink(r, Resolve(directory, href)));
        }

        foreach (Match tag in MetaTag().Matches(source))
        {
            var attributes = Attributes(tag.Value);
            if (attributes.TryGetValue("name", out var name) && name.Equals("fuzzy", StringComparison.OrdinalIgnoreCase)
                && attributes.TryGetValue("content", out var content))
            {
                ParseFuzzy(directory, content, info.Fuzzies);
            }
        }

        if (source.Contains("testharness.js", StringComparison.Ordinal))
        {
            info.IsTestharness = true;
            info.SkipReason = "testharness.js test";
        }
        else if (ReftestWait().IsMatch(source))
        {
            info.SkipReason = "reftest-wait (needs script)";
        }
        else if (ScriptTag().IsMatch(source))
        {
            info.SkipReason = "script-dependent";
        }

        return info;
    }

    /// <summary><c>[ref:]maxDifference=a[-b];totalPixels=c[-d]</c>; a single number means exactly that many, a range is inclusive.</summary>
    internal static void ParseFuzzy(string directory, string content, Dictionary<string, Fuzzy> into)
    {
        var text = content.Trim();
        var reference = string.Empty;

        var colon = text.IndexOf(':');
        if (colon > 0 && !text.AsSpan(0, colon).Contains('='))
        {
            reference = Resolve(directory, text[..colon].Trim());
            text = text[(colon + 1)..];
        }

        int maxMin = 0, maxMax = 0, pixelsMin = 0, pixelsMax = 0;
        foreach (Match field in FuzzyField().Matches(text))
        {
            var low = int.Parse(field.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var high = field.Groups[3].Success ? int.Parse(field.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : low;
            if (field.Groups[1].Value == "maxDifference")
                (maxMin, maxMax) = (low, high);
            else
                (pixelsMin, pixelsMax) = (low, high);
        }

        into[reference] = new Fuzzy(maxMin, maxMax, pixelsMin, pixelsMax);
    }

    private static Dictionary<string, string> Attributes(string tag)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attribute().Matches(tag))
        {
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
            result.TryAdd(m.Groups[1].Value, System.Net.WebUtility.HtmlDecode(value));
        }

        return result;
    }

    internal static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    /// <summary>Resolves a reference href against the directory of the file that names it; a leading slash is the WPT root.</summary>
    internal static string Resolve(string directory, string href)
    {
        var hash = href.IndexOfAny(['#', '?']);
        if (hash >= 0)
            href = href[..hash];

        var parts = new List<string>();
        if (!href.StartsWith('/'))
            parts.AddRange(directory.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in href.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(Uri.UnescapeDataString(segment));
            }
        }

        return string.Join('/', parts);
    }
}
