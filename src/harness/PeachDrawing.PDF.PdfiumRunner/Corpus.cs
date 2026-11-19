using System.Text.RegularExpressions;

namespace PeachDrawing.PDF.PdfiumRunner;

/// <summary>One test of the corpus: a PDF (or a template that expands to one) and the PNG PDFium's suite expects for each of its pages.</summary>
internal sealed record CorpusTest(string Id, string Source, IReadOnlyList<string> ExpectedPages);

/// <summary>Which corpus is run: PDFium's own pixel tests (reference images shipped with them) or the pdf.js regression PDFs (PDFium is the reference).</summary>
internal enum CorpusKind
{
    Pdfium,
    PdfJs,
}

/// <summary>
/// Finds the tests in a checkout of PDFium's <c>testing/resources</c>: every <c>pixel/&lt;name&gt;.in</c> or <c>.pdf</c> that has a
/// <c>&lt;name&gt;_expected.pdf.&lt;page&gt;.png</c> beside it. The unsuffixed expectation is PDFium's reference (AGG) rendering; the
/// <c>_mac</c>, <c>_win</c> and <c>_skia</c> variants are other platforms' and backends' and are not used.
/// </summary>
internal static partial class Corpus
{
    public const string Group = "pixel";
    public const string PdfJsGroup = "pdfjs";

    /// <summary>The largest PDF the pdf.js corpus runs, in bytes; the oracle script takes the same limit.</summary>
    public const long DefaultMaxBytes = 3_000_000;

    public static string GroupOf(CorpusKind kind) => kind == CorpusKind.PdfJs ? PdfJsGroup : Group;

    [GeneratedRegex(@"^(?<name>.+)\.(?<page>\d+)\.png$", RegexOptions.CultureInvariant)]
    private static partial Regex OraclePage();

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

    /// <summary>
    /// Finds the tests in a checkout of pdf.js: every <c>test/pdfs/&lt;name&gt;.pdf</c> up to <paramref name="maxBytes"/>, with the PNG of each
    /// page that <c>oracle/render_pdfium.py</c> wrote for it into <paramref name="oracleDirectory"/> (<c>&lt;name&gt;.&lt;page&gt;.png</c>, counted
    /// from 0 without a gap). A PDF PDFium could not render has no pages and is reported as skipped rather than dropped.
    /// </summary>
    public static IReadOnlyList<CorpusTest> DiscoverPdfJs(string corpusRoot, string oracleDirectory, long maxBytes = DefaultMaxBytes)
    {
        var directory = Path.Combine(corpusRoot, "test", "pdfs");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"{directory} does not exist; run 'fetch --kind pdfjs' first or point --corpus at a pdf.js checkout.");
        if (!Directory.Exists(oracleDirectory))
            throw new DirectoryNotFoundException($"{oracleDirectory} does not exist; run 'oracle' first, or point --oracle at its output.");

        var rendered = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(oracleDirectory, "*.png"))
        {
            if (OraclePage().Match(Path.GetFileName(file)) is not { Success: true } m)
                continue;
            var name = m.Groups["name"].Value;
            if (!rendered.TryGetValue(name, out var pages))
                rendered[name] = pages = [];
            pages[int.Parse(m.Groups["page"].Value, System.Globalization.CultureInfo.InvariantCulture)] = file;
        }

        var tests = new List<CorpusTest>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.pdf").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (new FileInfo(file).Length > maxBytes)
                continue;
            var name = Path.GetFileNameWithoutExtension(file);
            var expected = new List<string>();
            if (rendered.TryGetValue(name, out var pages))
            {
                for (var i = 0; pages.TryGetValue(i, out var path); i++)
                    expected.Add(path);
            }

            tests.Add(new CorpusTest($"{PdfJsGroup}/{name}", file, expected));
        }

        return tests;
    }

    /// <summary>The bytes of the test's PDF: a template is expanded, anything else read as it is.</summary>
    public static byte[] LoadPdf(CorpusTest test) =>
        test.Source.EndsWith(".in", StringComparison.Ordinal) ? PdfTemplate.Expand(test.Source) : File.ReadAllBytes(test.Source);
}
