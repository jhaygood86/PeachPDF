using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeachBrackets.WptRunner;

internal sealed class ModuleCounts
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("pass")] public int Pass { get; set; }
    [JsonPropertyName("fail")] public int Fail { get; set; }
    [JsonPropertyName("blank")] public int Blank { get; set; }
    [JsonPropertyName("skipped")] public int Skipped { get; set; }
}

/// <summary>One module's results, the file <c>&lt;out&gt;/&lt;wpt-commit&gt;/&lt;module&gt;.json</c>. Tests are listed in ordinal path order so runs diff cleanly.</summary>
internal sealed class ModuleResult
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = 1;
    [JsonPropertyName("wptCommit")] public string WptCommit { get; set; } = "";
    [JsonPropertyName("module")] public string Module { get; set; } = "";
    [JsonPropertyName("discovered")] public int Discovered { get; set; }
    [JsonPropertyName("sampleStride")] public int SampleStride { get; set; } = 1;
    [JsonPropertyName("counts")] public ModuleCounts Counts { get; set; } = new();
    [JsonPropertyName("rate")] public string Rate { get; set; } = "0.0000";
    [JsonPropertyName("pass")] public List<string> Pass { get; set; } = [];
    [JsonPropertyName("blank")] public List<string> Blank { get; set; } = [];
    [JsonPropertyName("fail")] public SortedDictionary<string, string> Fail { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("skipped")] public SortedDictionary<string, string> Skipped { get; set; } = new(StringComparer.Ordinal);

    public static ModuleResult From(string commit, string module, int discovered, int stride, IReadOnlyDictionary<string, TestResult> results)
    {
        var r = new ModuleResult { WptCommit = commit, Module = module, Discovered = discovered, SampleStride = stride };
        foreach (var (path, result) in results.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            r.Counts.Total++;
            switch (result.Outcome)
            {
                case Outcome.Pass: r.Counts.Pass++; r.Pass.Add(path); break;
                case Outcome.Blank: r.Counts.Blank++; r.Blank.Add(path); break;
                case Outcome.Fail: r.Counts.Fail++; r.Fail[path] = result.Reason ?? ""; break;
                default: r.Counts.Skipped++; r.Skipped[path] = result.Reason ?? ""; break;
            }
        }

        var runnable = r.Counts.Total - r.Counts.Skipped;
        r.Rate = (runnable == 0 ? 0 : (double)r.Counts.Pass / runnable).ToString("0.0000", CultureInfo.InvariantCulture);
        return r;
    }
}

internal static class ResultsWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(string outDirectory, ModuleResult result)
    {
        var dir = Path.Combine(outDirectory, result.WptCommit);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, result.Module + ".json"), JsonSerializer.Serialize(result, Options) + "\n");
    }

    /// <summary>Regenerates <c>summary.md</c> from every module file present, so a re-run of a subset keeps the rest.</summary>
    public static void WriteSummary(string outDirectory, string commit)
    {
        var dir = Path.Combine(outDirectory, commit);
        var modules = Directory.GetFiles(dir, "*.json")
            .Select(f => JsonSerializer.Deserialize<ModuleResult>(File.ReadAllText(f))!)
            .OrderBy(m => m.Module, StringComparer.Ordinal)
            .ToList();

        var text = new StringBuilder();
        text.AppendLine("# WPT reftest results: PeachBrackets").AppendLine();
        text.AppendLine($"- WPT commit: `{commit}`");
        text.AppendLine("- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.").AppendLine();
        text.AppendLine("| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var m in modules)
            text.AppendLine($"| {m.Module} | {m.SampleStride} | {m.Counts.Total} | {m.Counts.Pass} | {m.Counts.Fail} | {m.Counts.Blank} | {m.Counts.Skipped} | {m.Rate} |");

        text.AppendLine().AppendLine("## Most common failure reasons").AppendLine();
        foreach (var m in modules)
        {
            var top = m.Fail.Values.Select(Shape).GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(5).ToList();
            if (top.Count == 0)
                continue;
            text.AppendLine($"### {m.Module}").AppendLine();
            foreach (var g in top)
                text.AppendLine($"- {g.Count()} x {g.Key}");
            text.AppendLine();
        }

        File.WriteAllText(Path.Combine(dir, "summary.md"), text.ToString());
    }

    /// <summary>Strips the numbers and names from a failure reason so like failures group together.</summary>
    private static string Shape(string reason)
    {
        if (reason.StartsWith("error:", StringComparison.Ordinal))
            return reason.Length > 120 ? reason[..120] : reason;
        var open = reason.IndexOf(' ');
        var close = reason.IndexOf(' ', open + 1);
        return open < 0 || close < 0 ? reason : reason[..open] + " <ref>" + (reason.Contains("size ", StringComparison.Ordinal) ? " differs (size)" : reason.StartsWith("!=", StringComparison.Ordinal) ? " identical" : " differs (pixels)");
    }
}

/// <summary>The committed per-module pass-count floors CI gates on. A floor only rises: changing it down needs an explicit flag.</summary>
internal sealed class Floors
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = 1;
    [JsonPropertyName("wptCommit")] public string WptCommit { get; set; } = "";
    [JsonPropertyName("floors")] public SortedDictionary<string, int> Modules { get; set; } = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Floors Load(string file) =>
        File.Exists(file) ? JsonSerializer.Deserialize<Floors>(File.ReadAllText(file))! : new Floors();

    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, Options) + "\n");
}
