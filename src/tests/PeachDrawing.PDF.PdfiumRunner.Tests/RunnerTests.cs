using System.Text;
using PeachDrawing.PDF.PdfiumRunner;

namespace PeachDrawing.PDF.PdfiumRunner.Tests;

/// <summary>The template expander, corpus discovery, the comparison and the floors file, against a corpus written to a temporary directory.</summary>
public sealed class RunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pdfium-runner-" + Guid.NewGuid().ToString("N"));
    private string Pixel => Path.Combine(_root, "testing", "resources", "pixel");

    public RunnerTests() => Directory.CreateDirectory(Pixel);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A one-page template that fills its 20x20 page with the given color.</summary>
    private static string Template(string color) => $$$"""
        {{header}}
        {{object 1 0}} <<
          /Type /Catalog
          /Pages 2 0 R
        >>
        endobj
        {{object 2 0}} <<
          /Type /Pages
          /Count 1
          /Kids [3 0 R]
        >>
        endobj
        {{object 3 0}} <<
          /Type /Page
          /Parent 2 0 R
          /MediaBox [0 0 20 20]
          /Contents 4 0 R
        >>
        endobj
        {{object 4 0}} <<
          {{streamlen}}
        >>
        stream
        {{{color}}} rg 0 0 20 20 re f
        endstream
        endobj
        {{xref}}
        {{trailer}}
        {{startxref}}
        %%EOF
        """;

    private static byte[] SolidPng(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (r, g, b, 255);
        return new RgbaImage(width, height, pixels).ToPng();
    }

    private void Write(string name, string template, byte[] expectedPage0)
    {
        File.WriteAllText(Path.Combine(Pixel, name + ".in"), template.ReplaceLineEndings("\r\n"), Encoding.Latin1);
        File.WriteAllBytes(Path.Combine(Pixel, name + "_expected.pdf.0.png"), expectedPage0);
    }

    [Fact]
    public void Expand_WritesObjectsOffsetsAndStreamLengthsThatTheReaderAccepts()
    {
        Write("red", Template("1 0 0"), SolidPng(20, 20, 255, 0, 0));
        var bytes = PdfTemplate.Expand(Path.Combine(Pixel, "red.in"));
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.7\n", text, StringComparison.Ordinal);
        Assert.Contains("/Length 23\n", text, StringComparison.Ordinal);              // "1 0 0 rg 0 0 20 20 re f"
        var startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref\n0 5\n", text[startxref..], StringComparison.Ordinal);
        foreach (var n in new[] { 1, 2, 3, 4 })
        {
            var entry = text.Split("xref\n0 5\n")[1].Split('\n')[n];
            Assert.StartsWith($"{n} 0 obj", text[int.Parse(entry[..10])..], StringComparison.Ordinal);
        }

        Assert.Equal(1, PdfPageSource.Open(bytes).PageCount);
    }

    [Fact]
    public void Expand_FollowsIncludesAndIgnoresCircularOnes()
    {
        File.WriteAllText(Path.Combine(Pixel, "part.fragment"), "% part\r\n{{include main.in}}\r\n");
        File.WriteAllText(Path.Combine(Pixel, "main.in"), "{{include part.fragment}}\r\nend");

        var text = Encoding.Latin1.GetString(PdfTemplate.Expand(Path.Combine(Pixel, "main.in")));

        Assert.Equal("% part\nend\n", text);
    }

    [Fact]
    public void Discover_PairsATestWithItsUnsuffixedExpectationsOnly()
    {
        Write("a", Template("1 0 0"), SolidPng(20, 20, 255, 0, 0));
        File.WriteAllBytes(Path.Combine(Pixel, "a_expected.pdf.1.png"), SolidPng(20, 20, 0, 0, 0));
        File.WriteAllBytes(Path.Combine(Pixel, "a_expected_skia.pdf.0.png"), SolidPng(20, 20, 0, 0, 0));
        File.WriteAllBytes(Path.Combine(Pixel, "orphan_expected.pdf.0.png"), SolidPng(1, 1, 0, 0, 0));     // no PDF or template

        var tests = Corpus.Discover(_root);

        var test = Assert.Single(tests);
        Assert.Equal("pixel/a", test.Id);
        Assert.Equal(2, test.ExpectedPages.Count);
        Assert.EndsWith("a_expected.pdf.0.png", test.ExpectedPages[0], StringComparison.Ordinal);
    }

    private string PdfJsPdfs => Path.Combine(_root, "test", "pdfs");

    [Fact]
    public void DiscoverPdfJs_PairsEachPdfWithTheOraclePagesCountedFromZeroWithoutAGap()
    {
        Directory.CreateDirectory(PdfJsPdfs);
        var oracle = Path.Combine(_root, "oracle");
        Directory.CreateDirectory(oracle);
        foreach (var name in new[] { "multi.v2", "gap", "none" })
            File.WriteAllBytes(Path.Combine(PdfJsPdfs, name + ".pdf"), PdfTemplate.Expand(WriteTemplate()));
        File.WriteAllText(Path.Combine(PdfJsPdfs, "ignored.link"), "https://example.test/x.pdf");
        File.WriteAllBytes(Path.Combine(PdfJsPdfs, "toobig.pdf"), new byte[5000]);
        foreach (var file in new[] { "multi.v2.0.png", "multi.v2.1.png", "gap.0.png", "gap.2.png", "orphan.0.png" })
            File.WriteAllBytes(Path.Combine(oracle, file), SolidPng(20, 20, 255, 0, 0));

        var tests = Corpus.DiscoverPdfJs(_root, oracle, maxBytes: 4000).ToDictionary(t => t.Id);

        Assert.Equal(["pdfjs/gap", "pdfjs/multi.v2", "pdfjs/none"], tests.Keys.Order(StringComparer.Ordinal));      // .link, the oversize file and the orphan PNG are not tests
        Assert.Equal(2, tests["pdfjs/multi.v2"].ExpectedPages.Count);
        Assert.Single(tests["pdfjs/gap"].ExpectedPages);                                                              // page 2 follows a gap: not counted
        Assert.Empty(tests["pdfjs/none"].ExpectedPages);
    }

    private string WriteTemplate()
    {
        var path = Path.Combine(_root, "template-" + Guid.NewGuid().ToString("N") + ".in");
        File.WriteAllText(path, Template("1 0 0").ReplaceLineEndings("\r\n"), Encoding.Latin1);
        return path;
    }

    [Fact]
    public void Evaluate_PdfJsKind_DrawsThePageIntoTheOraclesRoundedUpBitmap_AndSkipsAPdfWithNoReference()
    {
        Directory.CreateDirectory(PdfJsPdfs);
        var oracle = Path.Combine(_root, "oracle");
        Directory.CreateDirectory(oracle);
        File.WriteAllBytes(Path.Combine(PdfJsPdfs, "red.pdf"), PdfTemplate.Expand(WriteTemplate()));          // a 20x20 point page
        File.WriteAllBytes(Path.Combine(PdfJsPdfs, "unrendered.pdf"), PdfTemplate.Expand(WriteTemplate()));
        File.WriteAllBytes(Path.Combine(oracle, "red.0.png"), SolidPng(21, 21, 255, 0, 0));                  // PDFium rounded 20.4 up to 21

        var results = Corpus.DiscoverPdfJs(_root, oracle).ToDictionary(t => t.Id, new Evaluator(new Tolerance(), kind: CorpusKind.PdfJs).Evaluate);

        Assert.Equal(Outcome.Pass, results["pdfjs/red"].Outcome);
        Assert.Equal(Outcome.Skipped, results["pdfjs/unrendered"].Outcome);
        Assert.Equal("no reference rendering", results["pdfjs/unrendered"].Reason);
    }

    [Fact]
    public void DiscoverPdfJs_WithNoOracleDirectory_Throws()
    {
        Directory.CreateDirectory(PdfJsPdfs);

        Assert.Throws<DirectoryNotFoundException>(() => Corpus.DiscoverPdfJs(_root, Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Discover_WithNoPixelDirectory_Throws() =>
        Assert.Throws<DirectoryNotFoundException>(() => Corpus.Discover(Path.Combine(_root, "missing")));

    [Fact]
    public void Evaluate_PassesAMatchingPage_AndFailsADifferentOrDifferentlySizedOne()
    {
        Write("match", Template("1 0 0"), SolidPng(20, 20, 255, 0, 0));
        Write("color", Template("1 0 0"), SolidPng(20, 20, 0, 255, 0));
        Write("size", Template("1 0 0"), SolidPng(30, 20, 255, 0, 0));
        var evaluator = new Evaluator(new Tolerance());

        var results = Corpus.Discover(_root).ToDictionary(t => t.Id, evaluator.Evaluate);

        Assert.Equal(Outcome.Pass, results["pixel/match"].Outcome);
        Assert.Equal(Outcome.Fail, results["pixel/color"].Outcome);
        Assert.Contains("of pixels differ", results["pixel/color"].Reason, StringComparison.Ordinal);
        Assert.Equal(Outcome.Fail, results["pixel/size"].Outcome);
        Assert.Contains("size 20x20, expected 30x20", results["pixel/size"].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ABlankPageWhereInkIsExpected_IsBlankNotAPass()
    {
        // An all-white page against an expectation with a small mark (20 pixels, past the minimum the ink rule judges): the tolerance here is wide, so only the blank check catches it.
        var pixels = new byte[20 * 20 * 4];
        Array.Fill(pixels, (byte)255);
        for (var i = 0; i < 20; i++)
            (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2]) = (0, 0, 0);
        Write("white", Template("1 1 1"), new RgbaImage(20, 20, pixels).ToPng());

        var result = new Evaluator(new Tolerance(Threshold: 48, MaxDifferingFraction: 0.5)).Evaluate(Corpus.Discover(_root).Single());

        Assert.Equal(Outcome.Blank, result.Outcome);
    }

    [Fact]
    public void Evaluate_APageWithAFractionOfPdfiumsInk_IsBlank_EvenInsideTheTolerance()
    {
        // 30 of 400 pixels are black in the expectation; the page itself draws only a pale tint, so nothing like the ink is there.
        var pixels = new byte[20 * 20 * 4];
        Array.Fill(pixels, (byte)255);
        for (var i = 0; i < 30; i++)
            (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2]) = (0, 0, 0);
        Write("tint", Template("0.95 0.95 1"), new RgbaImage(20, 20, pixels).ToPng());

        var result = new Evaluator(new Tolerance(Threshold: 48, MaxDifferingFraction: 0.5)).Evaluate(Corpus.Discover(_root).Single());

        Assert.Equal(Outcome.Blank, result.Outcome);
    }

    [Fact]
    public void InkPixels_CountsOnlyWhatStandsOutFromWhite()
    {
        var pixels = new byte[4 * 4];
        Array.Fill(pixels, (byte)255);
        (pixels[0], pixels[1], pixels[2]) = (0, 0, 0);                  // black
        (pixels[4], pixels[5], pixels[6]) = (242, 244, 255);            // a form-field tint
        (pixels[8], pixels[9], pixels[10]) = (200, 255, 255);           // 55 below white in one channel

        Assert.Equal(2, new RgbaImage(2, 2, pixels).InkPixels(48));
    }

    [Fact]
    public void Evaluate_AnUnreadableDocument_IsAFailureNotACrash()
    {
        File.WriteAllText(Path.Combine(Pixel, "broken.pdf"), "not a pdf");
        File.WriteAllBytes(Path.Combine(Pixel, "broken_expected.pdf.0.png"), SolidPng(20, 20, 255, 255, 255));

        var result = new Evaluator(new Tolerance()).Evaluate(Corpus.Discover(_root).Single());

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.StartsWith("error:", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupResultAndFloors_CountPassesAndRoundTrip()
    {
        var results = new Dictionary<string, TestResult>
        {
            ["pixel/a"] = new(Outcome.Pass),
            ["pixel/b"] = new(Outcome.Fail, "x"),
            ["pixel/c"] = new(Outcome.Blank),
            ["pixel/d"] = new(Outcome.Skipped, "needs a password"),
        };

        var result = GroupResult.From("abc", "pixel", results);

        Assert.Equal((4, 1), (result.Total, result.PassCount));
        Assert.Equal("0.3333", result.Rate);                                  // 1 of the 3 that ran

        var file = Path.Combine(_root, "floors.json");
        var floors = new Floors { PdfiumCommit = "abc" };
        floors.Groups["pixel"] = 7;
        floors.Save(file);
        Assert.Equal(7, Floors.Load(file).Groups["pixel"]);
        Assert.Empty(Floors.Load(Path.Combine(_root, "none.json")).Groups);
    }

    [Fact]
    public void ParseOptions_ReadsValuesAndBareFlags()
    {
        var o = Cli.ParseOptions(["--corpus", "dir", "--update-floors", "--sample", "3"]);

        Assert.Equal("dir", o["corpus"]);
        Assert.Equal("true", o["update-floors"]);
        Assert.Equal("3", o["sample"]);
    }
}
