using System.Text.RegularExpressions;

namespace PeachDrawing.PDF.PdfiumRunner;

/// <summary>One test of the corpus: a PDF (or a template that expands to one) and the PNG PDFium's suite expects for each of its pages.</summary>
internal sealed record CorpusTest(string Id, string Source, IReadOnlyList<string> ExpectedPages);

/// <summary>
/// Finds the tests in a checkout of PDFium's <c>testing/resources</c>: every <c>pixel/&lt;name&gt;.in</c> or <c>.pdf</c> that has a
/// <c>&lt;name&gt;_expected.pdf.&lt;page&gt;.png</c> beside it. The unsuffixed expectation is PDFium's reference (AGG) rendering; the
/// <c>_mac</c>, <c>_win</c> and <c>_skia</c> variants are other platforms' and backends' and are not used.
/// </summary>
internal static partial class Corpus
{
    public const string Group = "pixel";

    [GeneratedRegex(@"^(?<name>.+)_expected\.pdf\.(?<page>\d+)\.png$", RegexOptions.CultureInvariant)]
    private static partial Regex ExpectedFile();

    public static IReadOnlyList<CorpusTest> Discover(string corpusRoot)
    {
        var directory = Path.Combine(corpusRoot, "testing", "resources", "pixel");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"{directory} does not exist; run 'fetch' first or point --corpus at a PDFium checkout.");

        var expected = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
        {
            if (ExpectedFile().Match(Path.GetFileName(file)) is not { Success: true } m)
                continue;
            var name = m.Groups["name"].Value;
            if (!expected.TryGetValue(name, out var pages))
                expected[name] = pages = [];
            pages[int.Parse(m.Groups["page"].Value, System.Globalization.CultureInfo.InvariantCulture)] = file;
        }

        var tests = new List<CorpusTest>();
        foreach (var (name, pages) in expected.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // A template wins over a stale generated file of the same name, as PDFium's runner regenerates it.
            var source = new[] { ".in", ".pdf" }.Select(e => Path.Combine(directory, name + e)).FirstOrDefault(File.Exists);
            if (source is not null)
                tests.Add(new CorpusTest($"{Group}/{name}", source, [.. pages.OrderBy(p => p.Key).Select(p => p.Value)]));
        }

        return tests;
    }

    /// <summary>The bytes of the test's PDF: a template is expanded, anything else read as it is.</summary>
    public static byte[] LoadPdf(CorpusTest test) =>
        test.Source.EndsWith(".in", StringComparison.Ordinal) ? PdfTemplate.Expand(test.Source) : File.ReadAllBytes(test.Source);
}
