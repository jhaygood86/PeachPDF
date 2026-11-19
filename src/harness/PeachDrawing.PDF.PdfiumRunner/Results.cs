using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeachDrawing.PDF.PdfiumRunner;

/// <summary>One run's results, the file <c>&lt;out&gt;/&lt;pdfium-commit&gt;/&lt;group&gt;.json</c>. Tests are listed in ordinal order so runs diff cleanly.</summary>
internal sealed class GroupResult
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = 1;
    [JsonPropertyName("pdfiumCommit")] public string PdfiumCommit { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("pass")] public int PassCount { get; set; }
    [JsonPropertyName("rate")] public string Rate { get; set; } = "0.0000";
    [JsonPropertyName("passed")] public List<string> Passed { get; set; } = [];
    [JsonPropertyName("blank")] public List<string> Blank { get; set; } = [];
    [JsonPropertyName("fail")] public SortedDictionary<string, string> Fail { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("skipped")] public SortedDictionary<string, string> Skipped { get; set; } = new(StringComparer.Ordinal);

    public static GroupResult From(string commit, string group, IReadOnlyDictionary<string, TestResult> results)
    {
        var r = new GroupResult { PdfiumCommit = commit, Group = group, Total = results.Count };
        foreach (var (id, result) in results.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            switch (result.Outcome)
            {
                case Outcome.Pass: r.PassCount++; r.Passed.Add(id); break;
                case Outcome.Blank: r.Blank.Add(id); break;
                case Outcome.Fail: r.Fail[id] = result.Reason ?? ""; break;
                default: r.Skipped[id] = result.Reason ?? ""; break;
            }
        }

        var runnable = r.Total - r.Skipped.Count;
        r.Rate = (runnable == 0 ? 0 : (double)r.PassCount / runnable).ToString("0.0000", CultureInfo.InvariantCulture);
        return r;
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Write(string outDirectory)
    {
        var dir = Path.Combine(outDirectory, PdfiumCommit);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Group + ".json"), JsonSerializer.Serialize(this, Options) + "\n");
    }
}

/// <summary>The committed pass-count floors CI gates on. A floor only rises: lowering it needs an explicit flag.</summary>
internal sealed class Floors
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = 1;
    [JsonPropertyName("pdfiumCommit")] public string PdfiumCommit { get; set; } = "";
    [JsonPropertyName("floors")] public SortedDictionary<string, int> Groups { get; set; } = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Floors Load(string file) =>
        File.Exists(file) ? JsonSerializer.Deserialize<Floors>(File.ReadAllText(file))! : new Floors();

    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, Options) + "\n");
}
