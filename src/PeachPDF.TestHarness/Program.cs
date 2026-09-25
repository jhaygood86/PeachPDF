using PeachPDF;
using PeachPDF.Layout;
using PeachPDF.PdfSharpCore;
using ScottPlot;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

PdfGenerateConfig pdfConfig = new()
{
    PageSize = PageSize.A4,
    PageOrientation = PageOrientation.Portrait,
    ShrinkToFit = true
};

PdfGenerator generator = new();

// --benchmark [--benchmark-iterations N]: instead of writing any output, times and measures
// allocation for every showcase's render (GeneratePdf + Save, the same two calls a real caller
// makes), N times each (default 10), and prints a per-showcase report plus totals at the end. A
// reusable regression tool for exactly the kind of corpus-wide allocation change issue #971 needed -
// no files are written in this mode, so it's safe to point at any outputDir. Parsed out of args
// before the positional outputDir argument below, so "--benchmark" is never mistaken for a directory.
var positionalArgs = new List<string>();
var benchmarkMode = false;
var benchmarkIterations = 10;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--benchmark")
    {
        benchmarkMode = true;
    }
    else if (args[i] == "--benchmark-iterations" && i + 1 < args.Length
        && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIterations)
        && parsedIterations > 0)
    {
        benchmarkIterations = parsedIterations;
        i++;
    }
    else
    {
        positionalArgs.Add(args[i]);
    }
}

List<BenchmarkResult> benchmarkResults = [];

// Optional first (positional) argument: directory to write showcase output into (used by the
// docs-site build to publish /showcase). Defaults to the current directory,
// matching the historical local workflow.
var outputDir = positionalArgs.Count > 0 ? Path.GetFullPath(positionalArgs[0]) : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);

// Opt-in PDF/A conformance sweep (PDFA_SWEEP=1) - alongside every showcase's normal PDF, also
// renders the exact same (sourceHtml, renderConfig) pair under PdfAConformance.PdfA2B into a
// pdfa-sweep/ subfolder, for batch-validating the whole showcase library (not just the dedicated
// pdf_a_conformance showcase) against a real PDF/A validator. PdfA2B specifically - not 2A or any
// "1" level - because it needs no document language (many showcases don't set one) and permits
// transparency (opacity/gradients/masks - which most showcases use freely), so a generation failure
// here is never an expected/by-design PDF/A-1 rejection; it's always a genuine bug to investigate.
var pdfASweep = Environment.GetEnvironmentVariable("PDFA_SWEEP") == "1";
var pdfASweepDir = Path.Combine(outputDir, "pdfa-sweep");
if (pdfASweep)
    Directory.CreateDirectory(pdfASweepDir);

// A fixed date (not DateTimeOffset.UtcNow) so re-running the sweep produces byte-stable XMP output
// across runs, which makes diffing two sweep runs meaningful.
var pdfASweepCreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

static PdfGenerateConfig ClonePdfAConfig(PdfGenerateConfig source, DateTimeOffset creationDate) => new()
{
    PixelsPerInch = source.PixelsPerInch,
    ScaleToPageSize = source.ScaleToPageSize,
    ShrinkToFit = source.ShrinkToFit,
    MinContentWidth = source.MinContentWidth,
    Media = source.Media,
    PreferredColorScheme = source.PreferredColorScheme,
    IgnoreAuthorStyleSheets = source.IgnoreAuthorStyleSheets,
    PageSize = source.PageSize,
    ManualPageWidth = source.ManualPageWidth,
    ManualPageHeight = source.ManualPageHeight,
    PageOrientation = source.PageOrientation,
    NetworkLoader = source.NetworkLoader,
    AllowLocalFileAccess = source.AllowLocalFileAccess,
    DefaultLanguage = source.DefaultLanguage,
    CompressContentStreams = source.CompressContentStreams,
    EnableTaggedPdf = source.EnableTaggedPdf,
    EnableInteractivePdfForms = source.EnableInteractivePdfForms,
    DownscaleImages = source.DownscaleImages,
    DownscaleQuality = source.DownscaleQuality,
    MaximumDownscaleMultiplier = source.MaximumDownscaleMultiplier,
    RasterizationDpi = source.RasterizationDpi,
    MaxRasterPixels = source.MaxRasterPixels,
    MarginTop = source.MarginTop,
    MarginBottom = source.MarginBottom,
    MarginLeft = source.MarginLeft,
    MarginRight = source.MarginRight,
    PdfAConformance = PdfAConformance.PdfA2B,
    Metadata = new PdfDocumentMetadata { CreationDate = creationDate },
};

List<ShowcaseEntry> showcaseManifest = [];

// The manifest's category/title/description are plain text: docs/showcase.html HTML-escapes them itself, so a
// value written as "Caps &amp; Numerals" is escaped twice and prints the literal "&amp;" on the site.
static void RequirePlainTextMetadata(string slug, params string[] values)
{
    foreach (var value in values)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"&(?:[A-Za-z][A-Za-z0-9]*|#[0-9]+|#[xX][0-9A-Fa-f]+);"))
        {
            throw new ArgumentException(
                $"Showcase '{slug}' metadata contains an HTML entity ('{value}'). Write plain text (& not &amp;, < not &lt;) - the site escapes it.");
        }
    }
}

// Every showcase goes through here so the manifest (showcases.json) that drives
// the website's /showcase page always matches the files actually written.
async Task SaveShowcaseAsync(string slug, string category, string cardTitle, string cardDescription, string sourceHtml, PdfGenerateConfig renderConfig)
{
    RequirePlainTextMetadata(slug, category, cardTitle, cardDescription);

    if (benchmarkMode)
    {
        await BenchmarkShowcaseAsync(slug, sourceHtml, renderConfig);
        return;
    }

    var showcaseDocument = await generator.GeneratePdf(sourceHtml, renderConfig);
    using var pdfStream = new MemoryStream();
    showcaseDocument.Save(pdfStream);
    File.WriteAllBytes(Path.Combine(outputDir, $"{slug}.pdf"), pdfStream.ToArray());
    File.WriteAllText(Path.Combine(outputDir, $"{slug}.html"), sourceHtml);
    showcaseManifest.Add(new ShowcaseEntry(slug, category, cardTitle, cardDescription, $"{slug}.pdf", $"{slug}.html"));
    Console.WriteLine($"Saved {slug}.pdf + {slug}.html");

    if (pdfASweep)
    {
        try
        {
            var pdfAConfig = ClonePdfAConfig(renderConfig, pdfASweepCreationDate);
            var pdfADocument = await generator.GeneratePdf(sourceHtml, pdfAConfig);
            using var pdfAStream = new MemoryStream();
            pdfADocument.Save(pdfAStream);
            File.WriteAllBytes(Path.Combine(pdfASweepDir, $"{slug}.pdf"), pdfAStream.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PDF/A SWEEP GENERATION FAILED for {slug}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Declarative-API showcases build via PdfGenerator.CreateDocument instead of parsing an HTML string, so
// there is no HTML source to show a reader - csharpSource (the exact call the showcase makes) is written
// as the "source" file instead, wrapped in a minimal valid HTML document so it stays compatible with the
// existing ShowcaseEntry.Html manifest field/site build (docs/showcase.html, .github/workflows/pages.yml)
// without changing that schema for every other (HTML-based) showcase.
async Task SaveDeclarativeShowcaseAsync(string slug, string category, string cardTitle, string cardDescription,
    string csharpSource, Func<PdfGenerator, Task<PeachPdfDocument>> build)
{
    RequirePlainTextMetadata(slug, category, cardTitle, cardDescription);

    // --benchmark measures HTML-parsing showcases only (BenchmarkShowcaseAsync times GeneratePdf
    // specifically) - a declarative showcase has no comparable "parse this HTML" cost to measure, so it
    // simply doesn't run in that mode rather than writing files a benchmark pass isn't meant to produce.
    if (benchmarkMode) return;

    var showcaseDocument = await build(generator);
    using var pdfStream = new MemoryStream();
    showcaseDocument.Save(pdfStream);
    File.WriteAllBytes(Path.Combine(outputDir, $"{slug}.pdf"), pdfStream.ToArray());

    var sourceHtml =
        $"""
        <!DOCTYPE html><html><head><meta charset="utf-8"><title>{System.Net.WebUtility.HtmlEncode(cardTitle)}</title></head>
        <body><pre><code>{System.Net.WebUtility.HtmlEncode(csharpSource)}</code></pre></body></html>
        """;
    File.WriteAllText(Path.Combine(outputDir, $"{slug}.html"), sourceHtml);
    showcaseManifest.Add(new ShowcaseEntry(slug, category, cardTitle, cardDescription, $"{slug}.pdf", $"{slug}.html", ShowcaseSourceKind.CSharp));
    Console.WriteLine($"Saved {slug}.pdf + {slug}.html (declarative)");
}

// Renders sourceHtml the same way a real caller would - GeneratePdf, then Save to a stream - without
// writing anything to disk. This, not just GeneratePdf alone, is what --benchmark times/measures: PDF
// serialization is a real, distinct cost a caller always pays, not an artifact of the showcase harness.
async Task RenderOnceAsync(string sourceHtml, PdfGenerateConfig renderConfig)
{
    var document = await generator.GeneratePdf(sourceHtml, renderConfig);
    using var stream = new MemoryStream();
    document.Save(stream);
}

async Task BenchmarkShowcaseAsync(string slug, string sourceHtml, PdfGenerateConfig renderConfig)
{
    // Warm: JIT the whole render path (parser, cascade, layout, PDF writer) before anything is
    // counted - the first render of any process pays a one-time JIT/tiering cost that would otherwise
    // swamp a real per-render number.
    await RenderOnceAsync(sourceHtml, renderConfig);

    var wallTimesMs = new double[benchmarkIterations];
    var allocatedBytes = new long[benchmarkIterations];

    for (var i = 0; i < benchmarkIterations; i++)
    {
        // GC.GetTotalAllocatedBytes(precise: true) rather than the per-thread counter this repo's
        // xUnit allocation tests use (e.g. GposPositionerAllocationTests): those run under parallel
        // test-class execution, where a process-wide counter would pick up unrelated tests'
        // allocation. The TestHarness is a single-threaded console app with nothing else running
        // concurrently, so the process-wide precise counter is the more complete measurement here -
        // it also captures allocation on any thread-pool continuation the async render hops onto,
        // which a per-thread counter would miss.
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        await RenderOnceAsync(sourceHtml, renderConfig);
        stopwatch.Stop();
        var after = GC.GetTotalAllocatedBytes(precise: true);

        wallTimesMs[i] = stopwatch.Elapsed.TotalMilliseconds;
        allocatedBytes[i] = after - before;
    }

    benchmarkResults.Add(new BenchmarkResult(slug, wallTimesMs, allocatedBytes));
    Console.WriteLine($"Benchmarked {slug} ({benchmarkIterations} iterations)");
}

void PrintBenchmarkReport()
{
    Console.WriteLine();
    Console.WriteLine($"=== Benchmark report ({benchmarkResults.Count} showcases x {benchmarkIterations} iterations) ===");
    Console.WriteLine($"{"Showcase",-40} {"Mean ms",10} {"Median ms",10} {"Mean KB",10} {"Median KB",10}");

    foreach (var result in benchmarkResults.OrderBy(r => r.Slug, StringComparer.Ordinal))
    {
        Console.WriteLine($"{result.Slug,-40} {result.MeanWallTimeMs,10:F2} {result.MedianWallTimeMs,10:F2} " +
            $"{result.MeanAllocatedBytes / 1024.0,10:F1} {result.MedianAllocatedBytes / 1024.0,10:F1}");
    }

    // Totals sum each showcase's own mean, rather than averaging per-iteration numbers across
    // showcases directly, so a showcase rendered zero or a different iteration count (were that ever
    // to vary) still contributes its fair share - and because "total time/allocation for one pass
    // over the whole corpus" (matching how this repo's other allocation fixes report a "per corpus
    // pass" figure) is what a caller actually wants out of this report, not a per-iteration average.
    var totalMeanMs = benchmarkResults.Sum(r => r.MeanWallTimeMs);
    var totalMeanBytes = benchmarkResults.Sum(r => r.MeanAllocatedBytes);
    Console.WriteLine(new string('-', 40 + 4 * 11));
    Console.WriteLine($"{"TOTAL (one corpus pass)",-40} {totalMeanMs,10:F2} {"",10} " +
        $"{totalMeanBytes / 1024.0,10:F1} {"",10}");
    Console.WriteLine($"Total: {totalMeanMs:F2} ms, {totalMeanBytes / 1024.0 / 1024.0:F2} MB per pass over the corpus.");
}

static string Swatch(string desc, string css) =>
    "<td>" +
    $"<div class=\"box\" style=\"background-image: {css}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{css}</div>" +
    "</td>";

static string RadiusSwatch(string desc, string borderRadiusCss, string boxCss = "") =>
    "<td>" +
    $"<div class=\"rbox\" style=\"{boxCss}border-radius: {borderRadiusCss}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border-radius: {borderRadiusCss}</div>" +
    "</td>";

static string BorderStyleSwatch(string desc, string style) =>
    "<td>" +
    $"<div class=\"bsbox\" style=\"border: 16px {style} #4a90d9\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border-style: {style}</div>" +
    "</td>";

static string BorderColorSwatch(string desc, string style, string color) =>
    "<td>" +
    $"<div class=\"bsbox\" style=\"border: 16px {style} {color}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border: 16px {style} {color}</div>" +
    "</td>";

static string BorderFitSwatch(string desc, string style, string width) =>
    "<td>" +
    $"<div class=\"bsbox\" style=\"border: 10px {style} #4a90d9; width: {width}; box-sizing: border-box\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border: 10px {style}; width: {width}</div>" +
    "</td>";

/// <summary>One cell stacking the same style at each of <paramref name="widths"/>, so the styles can be
/// read down a column and the widths across a row.</summary>
static string WidthSwatch(string style, int[] widths) =>
    "<td>" +
    string.Join("", widths.Select(w =>
        $"<div class=\"wbox\" style=\"border: {w}px {style} #4a90d9\"></div>" +
        $"<div class=\"wlabel\">{w}px</div>")) +
    $"<div class=\"desc\">{style}</div>" +
    "</td>";

static string SideSwatch(string desc, string inlineCss) =>
    "<td>" +
    $"<div class=\"bsbox\" style=\"{inlineCss}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{inlineCss}</div>" +
    "</td>";

/// <summary>
/// One 2x2 table drawn twice at the same border declaration: collapsed above, separate below. A
/// collapsed grid line belongs to the boxes on both sides of it, so a bevelled one shows both faces -
/// which makes the two models legitimately differ, and makes a collapsed <c>inset</c> read as a
/// <c>ridge</c>. Painting a collapsed <c>inset</c>/<c>outset</c> as one flat face on every side, as
/// this used to, made the upper table indistinguishable from a <c>solid</c> one for those two
/// keywords; <c>groove</c>/<c>ridge</c> were already two bands and are shown here for the comparison.
/// </summary>
static string CollapsedTableSwatch(string desc, string style)
{
    static string Grid(string borderStyle) =>
        string.Concat(Enumerable.Repeat(
            $"<tr><td style=\"border: 12px {borderStyle} #4a90d9\"></td>" +
            $"<td style=\"border: 12px {borderStyle} #4a90d9\"></td></tr>", 2));

    return "<td>" +
        $"<table class=\"ct\">{Grid(style)}</table>" +
        $"<table class=\"ct cs\">{Grid(style)}</table>" +
        $"<div class=\"desc\">{desc}</div>" +
        $"<div class=\"css\">collapse / separate, border: 12px {style}</div>" +
        "</td>";
}

/// <summary>
/// A 2x2 collapsed table whose row lines and column lines carry different colours, so every crossing
/// says out loud which of the two lines painted it. Two things are on show and neither is visible on a
/// single-coloured table: the four corners are painted at all (each run used to stop at the
/// perpendicular line's centre, leaving a notch), and the square at a crossing goes to exactly one
/// line - the row line everywhere except along the table's first row line, where a column line takes
/// it unless it is the one at the table's inline start. <paramref name="rowStyle"/> and
/// <paramref name="columnStyle"/> carry a bevel, or two differing styles, through the same fixture: a
/// wrongly-owned bevelled joint bands the wrong way rather than merely taking the wrong colour.
/// </summary>
/// <param name="desc">The swatch's own caption.</param>
/// <param name="rowStyle">The <c>border-style</c> every row (block-axis) line carries.</param>
/// <param name="columnStyle">The <c>border-style</c> every column (inline-axis) line carries.</param>
static string CollapsedJointSwatch(string desc, string rowStyle, string columnStyle)
{
    const string RowColor = "#d94a4a";
    const string ColumnColor = "#4a90d9";

    var cell =
        $"<td style=\"border-top: 12px {rowStyle} {RowColor}; border-bottom: 12px {rowStyle} {RowColor}; " +
        $"border-left: 12px {columnStyle} {ColumnColor}; border-right: 12px {columnStyle} {ColumnColor}\"></td>";

    var row = $"<tr>{cell}{cell}</tr>";

    return "<td>" +
        $"<table class=\"ct\">{row}{row}</table>" +
        $"<div class=\"desc\">{desc}</div>" +
        $"<div class=\"css\">collapse, rows 12px {rowStyle} / columns 12px {columnStyle}</div>" +
        "</td>";
}

static string RoundedPatternPhaseComparisonSwatch() =>
    "<td>" +
    "<div class=\"phasepair\">" +
    "<div class=\"phasebox\" style=\"width:240px\"></div>" +
    "<div class=\"phasebox second\" style=\"width:245px\"></div>" +
    "</div>" +
    "<div class=\"desc\">one dot / two dots</div>" +
    "<div class=\"css\">same rounded border at width: 240px / 245px</div>" +
    "</td>";

/// <summary>A zero-content box whose four borders meet at its center - the classic "border triangle".</summary>
static string TriangleSwatch(string desc, string colors) =>
    "<td>" +
    $"<div style=\"width: 0; height: 0; border: 34px solid; border-color: {colors}; margin: 0 auto 3px\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">width/height: 0; border-color: {colors}</div>" +
    "</td>";

/// <summary>
/// A &lt;hr&gt; beside the zero-height &lt;div&gt; that is its exact equivalent. The rule IS a border, so
/// the pair has to be indistinguishable - and was not, until the rule stopped being painted by a
/// per-side path of its own that ignored border-style entirely.
/// </summary>
// The height the <div> paired against a rule declares, so that the two really are equivalent. `auto`
// rather than `0`: a rule's used content height is 0 and its box is its own borders and padding, and
// `height: auto` on an empty block is the one declaration that reproduces that under EITHER
// box-sizing. `height: 0` reads as the same thing and is not - under `box-sizing: border-box` it sizes
// the BORDER box to zero, so the div collapses to nothing and paints no band at all, leaving the pair
// looking mismatched for a reason that has nothing to do with what the swatch is demonstrating.
const string EquivalentDivHeight = "height: auto; ";

static string HrStyleSwatch(string desc, string inlineCss) =>
    "<td>" +
    "<div class=\"hrbox\">" +
    $"<hr style=\"{inlineCss}\">" +
    $"<div style=\"{EquivalentDivHeight}{inlineCss}\"></div>" +
    "</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">hr / div, {inlineCss}</div>" +
    "</td>";

/// <summary>
/// The same pairing inside a containing block with padding and a border of its own. The rule's basis
/// is that block's CONTENT width - its padding and border sit outside it - so the pair must still line
/// up, and must line up with the pairs in an unpadded cell at the same declared width.
/// </summary>
static string HrPaddedBoxSwatch(string desc, string inlineCss) =>
    "<td>" +
    "<div class=\"hrbox\" style=\"padding: 0 10px; border: 2px solid #ccc\">" +
    $"<hr style=\"{inlineCss}\">" +
    $"<div style=\"{EquivalentDivHeight}{inlineCss}\"></div>" +
    "</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">hr / div, {inlineCss}</div>" +
    "</td>";

/// <summary>
/// A &lt;hr&gt; on its own, for a case where the equivalent &lt;div&gt; would legitimately differ and so
/// must not be shown beside it - a rule carries the UA sheet's own <c>color: gray</c>, which is what
/// its border resolves <c>currentcolor</c> through whenever nothing is derived from a bevel.
/// <paramref name="inlineCss"/> of <c>null</c> means the bare <c>noshade</c> rule instead.
/// </summary>
static string HrAloneSwatch(string desc, string? inlineCss) =>
    "<td>" +
    "<div class=\"hrbox\">" +
    (inlineCss is null ? "<hr noshade>" : $"<hr style=\"{inlineCss}\">") +
    "</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">hr, {inlineCss ?? "noshade"}</div>" +
    "</td>";

/// <summary>
/// The one box in the bevel-base section that shades its own <c>currentcolor</c>: a table, which
/// Blink exempts from the substitution and PeachPDF follows. A real &lt;table&gt; with a cell rather
/// than a bare <c>display: table</c> div, so it has content to give it a height.
/// </summary>
static string TableBevelExemptionSwatch() =>
    "<td>" +
    "<table style=\"border: 16px inset; color: #d94a4a; background: #888; border-collapse: separate; " +
    "border-spacing: 0; width: 100%\"><tr><td style=\"height: 48px\"></td></tr></table>" +
    "<div class=\"desc\">table is exempt</div>" +
    "<div class=\"css\">table, border: 16px inset; color: #d94a4a</div>" +
    "</td>";

static string RadiusBorderSwatch(string desc, string style, string width, string radius) =>
    "<td>" +
    $"<div class=\"bsbox\" style=\"border: {width} {style} #4a90d9; border-radius: {radius}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border: {width} {style}; border-radius: {radius}</div>" +
    "</td>";

static string OriginSwatch(string desc, string inlineCss, string cssLabel = "") =>
    "<td>" +
    $"<div class=\"obox\" style=\"{inlineCss}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{(string.IsNullOrEmpty(cssLabel) ? inlineCss : cssLabel)}</div>" +
    "</td>";

static string Row(params string[] cells) =>
    $"<table class=\"sw\"><tr>{string.Join("", cells)}</tr></table>";

const string Css = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .box { height: 48px; border: 1px solid #000; margin-bottom: 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var html = "<!DOCTYPE html><html><head>" + Css + "</head><body>" +

    "<h1>CSS Gradient Test Page</h1>" +

    "<h2>1 — linear-gradient: Direction &amp; Angle</h2>" +
    Row(
        Swatch("default (to bottom)", "linear-gradient(red, blue)"),
        Swatch("to top", "linear-gradient(to top, red, blue)"),
        Swatch("to right", "linear-gradient(to right, red, blue)"),
        Swatch("45deg", "linear-gradient(45deg, red, blue)")
    ) +

    "<h2>2 — linear-gradient: Multi-Stop &amp; Positions</h2>" +
    Row(
        Swatch("3 stops", "linear-gradient(to right, red, yellow, blue)"),
        Swatch("abs lengths", "linear-gradient(to right, red 0, yellow 40px, blue 80px)"),
        Swatch("hard stop", "linear-gradient(to right, red 0 50%, blue 50% 100%)"),
        Swatch("color hint", "linear-gradient(to right, red, 30%, blue)")
    ) +

    "<h2>3 — linear-gradient: Alpha Transparency</h2>" +
    Row(
        Swatch("transparent → color", "linear-gradient(to right, rgba(255,0,0,0), red)"),
        Swatch("color → transparent", "linear-gradient(to right, rgba(0,128,255,1), rgba(0,128,255,0))"),
        Swatch("rgba() 80% opacity", "linear-gradient(to right, rgba(255,0,0,0.8), rgba(0,0,255,0.8))"),
        Swatch("multi-stop alpha", "linear-gradient(to right, red, rgba(255,255,0,0.5), rgba(0,0,255,0))")
    ) +

    "<h2>4 — repeating-linear-gradient</h2>" +
    Row(
        Swatch("stripes 20px", "repeating-linear-gradient(to right, red 0 10px, blue 10px 20px)"),
        Swatch("45deg stripes", "repeating-linear-gradient(45deg, red 0 8px, white 8px 16px)"),
        Swatch("fade repeat", "repeating-linear-gradient(to right, red 0, blue 30px)"),
        Swatch("full span = no repeat", "repeating-linear-gradient(to right, red, blue)")
    ) +

    "<h2>5 — radial-gradient: Basic</h2>" +
    Row(
        Swatch("default (ellipse center)", "radial-gradient(red, blue)"),
        Swatch("circle", "radial-gradient(circle, red, blue)"),
        Swatch("at 25% 25%", "radial-gradient(at 25% 25%, red, blue)"),
        Swatch("circle at 50% 25%", "radial-gradient(circle at 50% 25%, yellow, orange, red)")
    ) +

    "<h2>6 — radial-gradient: Size Keywords &amp; Explicit</h2>" +
    Row(
        Swatch("farthest-corner", "radial-gradient(farthest-corner at 30% 30%, red, blue)"),
        Swatch("closest-side", "radial-gradient(closest-side at 30% 30%, red, blue)"),
        Swatch("farthest-side", "radial-gradient(farthest-side at 30% 30%, red, blue)"),
        Swatch("explicit 30px", "radial-gradient(30px at center, red, blue)")
    ) +

    "<h2>7 — radial-gradient: Alpha</h2>" +
    Row(
        Swatch("transparent center", "radial-gradient(rgba(255,0,0,0), rgba(255,0,0,1))"),
        Swatch("spotlight", "radial-gradient(circle, rgba(255,255,255,0.9), rgba(255,255,255,0))"),
        Swatch("sunset", "radial-gradient(circle, #fff7e6, #ff6b35, #1a1a2e)"),
        Swatch("multi-stop alpha", "radial-gradient(circle, rgba(255,0,0,1), rgba(255,255,0,0.5), rgba(0,0,255,0))")
    ) +

    "<h2>8 — repeating-radial-gradient</h2>" +
    Row(
        Swatch("rings", "repeating-radial-gradient(circle, red 0 10px, blue 10px 20px)"),
        Swatch("fade rings", "repeating-radial-gradient(circle, red 0, blue 25px)"),
        Swatch("ellipse rings", "repeating-radial-gradient(red 0 8px, white 8px 16px)"),
        Swatch("with alpha", "repeating-radial-gradient(circle, rgba(255,0,0,0.8) 0 10px, rgba(0,0,255,0.8) 10px 20px)")
    ) +

    "<h2>9 — conic-gradient: Basic</h2>" +
    Row(
        Swatch("default", "conic-gradient(red, blue)"),
        Swatch("3 stops", "conic-gradient(red, yellow, blue)"),
        Swatch("from 90deg", "conic-gradient(from 90deg, red, blue)"),
        Swatch("at 25% 75%", "conic-gradient(at 25% 75%, red, green, blue)")
    ) +

    "<h2>10 — conic-gradient: Stop Positions</h2>" +
    Row(
        Swatch("angle stops", "conic-gradient(red 0deg, blue 180deg, green 360deg)"),
        Swatch("percent stops", "conic-gradient(red 0%, blue 50%, green 100%)"),
        Swatch("hard stop", "conic-gradient(red 0 90deg, blue 90deg 180deg, green 180deg 360deg)"),
        Swatch("pie chart", "conic-gradient(#e74c3c 0 25%, #3498db 25% 65%, #2ecc71 65% 100%)")
    ) +

    "<h2>11 — conic-gradient: Alpha &amp; From+At</h2>" +
    Row(
        Swatch("with alpha", "conic-gradient(rgba(255,0,0,0), red)"),
        Swatch("from 45deg at 30% 70%", "conic-gradient(from 45deg at 30% 70%, red, blue)"),
        Swatch("color wheel", "conic-gradient(red, yellow, lime, cyan, blue, magenta, red)"),
        Swatch("starburst", "conic-gradient(gold 0 10%, white 10% 20%, gold 20% 30%, white 30% 40%, gold 40% 50%, white 50% 60%, gold 60% 70%, white 70% 80%, gold 80% 90%, white 90% 100%)")
    ) +

    "<h2>12 — repeating-conic-gradient</h2>" +
    Row(
        Swatch("60deg tile", "repeating-conic-gradient(red 0deg 30deg, blue 30deg 60deg)"),
        Swatch("from 45deg", "repeating-conic-gradient(from 45deg, red 0deg, blue 60deg)"),
        Swatch("checkerboard-like", "repeating-conic-gradient(#000 0 25%, #fff 25% 50%)"),
        Swatch("narrow slice", "repeating-conic-gradient(red 0 5deg, blue 5deg 10deg)")
    ) +

    "<h2>13 — Color Space Interpolation: in oklab</h2>" +
    Row(
        Swatch("sRGB red→blue", "linear-gradient(to right, red, blue)"),
        Swatch("oklab red→blue", "linear-gradient(in oklab to right, red, blue)"),
        Swatch("oklab red→green", "linear-gradient(in oklab to right, red, green)"),
        Swatch("oklab red→yellow→blue", "linear-gradient(in oklab to right, red, yellow, blue)")
    ) +

    "<h2>14 — Color Space Interpolation: Polar (HSL, OKLch)</h2>" +
    Row(
        Swatch("hsl shorter red→blue", "linear-gradient(in hsl, red, blue)"),
        Swatch("hsl longer red→blue", "linear-gradient(in hsl longer hue, red, blue)"),
        Swatch("oklch shorter red→blue", "linear-gradient(in oklch, red, blue)"),
        Swatch("oklch longer red→blue", "linear-gradient(in oklch longer hue, red, blue)")
    ) +

    "<h2>15 — Color Space Interpolation: Lab, LCH, sRGB-linear</h2>" +
    Row(
        Swatch("lab red→blue", "linear-gradient(in lab to right, red, blue)"),
        Swatch("lch red→blue", "linear-gradient(in lch to right, red, blue)"),
        Swatch("srgb-linear red→blue", "linear-gradient(in srgb-linear to right, red, blue)"),
        Swatch("display-p3 red→blue", "linear-gradient(in display-p3 to right, red, blue)")
    ) +

    "<h2>16 — Color Space: Radial &amp; Conic</h2>" +
    Row(
        Swatch("radial oklab", "radial-gradient(in oklab circle, red, blue)"),
        Swatch("radial oklch", "radial-gradient(in oklch circle, red, blue)"),
        Swatch("conic oklab", "conic-gradient(in oklab, red, blue)"),
        Swatch("conic oklch longer", "conic-gradient(in oklch longer hue, red, blue)")
    ) +

    "</body></html>";

await SaveShowcaseAsync("gradients", "Backgrounds & Borders", "CSS Gradients",
    "linear-gradient, radial-gradient, and conic-gradient: directions, angles, multi-stop and hard-stop color lists, and CSS Color Level 4 interpolation spaces.",
    html, pdfConfig);

// --- Border-radius showcase ---

const string RadiusCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .rbox { height: 60px; background: steelblue; border: 2px solid #1a6b8a; margin-bottom: 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var radiusHtml = "<!DOCTYPE html><html><head>" + RadiusCss + "</head><body>" +

    "<h1>CSS border-radius Test Page</h1>" +

    "<h2>1 — Shorthand: 1–4 values</h2>" +
    Row(
        RadiusSwatch("1 value — all equal", "20px"),
        RadiusSwatch("2 values — opposite pairs", "10px 30px"),
        RadiusSwatch("3 values — TL / TR+BL / BR", "8px 20px 35px"),
        RadiusSwatch("4 values — each corner", "5px 15px 30px 45px")
    ) +

    "<h2>2 — Individual Longhands</h2>" +
    Row(
        RadiusSwatch("top-left only", "0", "border-top-left-radius: 30px; "),
        RadiusSwatch("bottom-right only", "0", "border-bottom-right-radius: 30px; "),
        RadiusSwatch("TL + BR set", "0", "border-top-left-radius: 25px; border-bottom-right-radius: 25px; "),
        RadiusSwatch("all 4 longhands", "0", "border-top-left-radius: 10px; border-top-right-radius: 20px; border-bottom-right-radius: 30px; border-bottom-left-radius: 15px; ")
    ) +

    "<h2>3 — Elliptical Corners (/ syntax)</h2>" +
    Row(
        RadiusSwatch("flat wide (40px / 10px)", "40px / 10px"),
        RadiusSwatch("tall narrow (10px / 40px)", "10px / 40px"),
        RadiusSwatch("uniform ellipse (30px / 20px)", "30px / 20px"),
        RadiusSwatch("asymmetric", "20px 0 / 0 20px")
    ) +

    "<h2>4 — Percentage Values</h2>" +
    Row(
        RadiusSwatch("50% — pill/ellipse", "50%"),
        RadiusSwatch("25% — quarter round", "25%"),
        RadiusSwatch("50% / 25%", "50% / 25%"),
        RadiusSwatch("25% / 50%", "25% / 50%")
    ) +

    "<h2>5 — Overlapping Radii Reduction</h2>" +
    "<p class=\"intro\">Each box is 80px wide. A radius larger than half the box is automatically scaled down.</p>" +
    Row(
        RadiusSwatch("60px on 80px box", "60px", "width: 80px; "),
        RadiusSwatch("100px (auto-capped)", "100px", "width: 80px; "),
        RadiusSwatch("50% on tall box", "50%", "width: 80px; height: 80px; "),
        RadiusSwatch("no overlap — 20px", "20px", "width: 80px; ")
    ) +

    "<h2>6 — Combined Styles</h2>" +
    Row(
        RadiusSwatch("solid border + bg", "15px"),
        RadiusSwatch("dashed border", "15px", "border-style: dashed; "),
        RadiusSwatch("dotted border", "15px", "border-style: dotted; "),
        RadiusSwatch("no border, bg only", "15px", "border: none; ")
    ) +

    "<h2>7 — Clipping to the Rounded Curve (overflow: hidden)</h2>" +
    "<p class=\"intro\">A rounded overflow:hidden box clips its descendants to the curve itself " +
    "(CSS Backgrounds and Borders Level 3 &#167;5.5), not just its padding-edge rectangle — a child " +
    "that fills the box must show rounded corners matching the parent's own, and the parent's own " +
    "border must never paint past its own rounded edge.</p>" +
    "<table class=\"sw\"><tr><td style=\"width:50%\">" +
    "<div style=\"display:flex;align-items:center;gap:8px;width:140px\">" +
    "<div style=\"flex:1;height:14px;border-radius:999px;background:#ffc487;overflow:hidden\">" +
    "<div style=\"height:100%;background:#3e7a4c;width:65%\"></div>" +
    "</div><span style=\"font-size:8pt\">65 / 35</span></div>" +
    "<div class=\"desc\">pill progress bar — flex child clipped to a 999px-radius parent</div>" +
    "</td><td style=\"width:50%\">" +
    "<div style=\"border:2px solid #1a6b8a;border-radius:14px;overflow:hidden;width:200px\">" +
    "<div style=\"padding:6px 10px;background:steelblue;color:#fff;font-size:8pt\">Card header</div>" +
    "<div style=\"display:grid;grid-template-columns:1fr 1fr\">" +
    "<div style=\"padding:6px 10px;border-right:1px solid #1a6b8a;border-top:1px solid #1a6b8a;font-size:7pt\">Cell A</div>" +
    "<div style=\"padding:6px 10px;border-top:1px solid #1a6b8a;font-size:7pt\">Cell B</div>" +
    "</div></div>" +
    "<div class=\"desc\">rounded bordered card — grid content and border confined to the curve</div>" +
    "</td></tr></table>" +

    "<h2>8 — Padding/Content-Edge Radius Reduction (thick border)</h2>" +
    "<p class=\"intro\">The padding edge's own radius is the border-box radius minus the border " +
    "thickness, clamped to zero (CSS Backgrounds and Borders Level 3 &#167;5.5) — for a thick border " +
    "relative to its radius, the inset curve must sit noticeably tighter than the outer border, not " +
    "bulge past it. Both boxes below use border: 6px solid; border-radius: 14px, so the inset curve's " +
    "effective radius is 14&#8722;6=8px.</p>" +
    "<table class=\"sw\"><tr><td style=\"width:50%\">" +
    "<div style=\"border:6px solid #1a6b8a;border-radius:14px;width:160px;height:90px;" +
    "background:linear-gradient(135deg,#ffb454,#e8663c);background-clip:padding-box;\"></div>" +
    "<div class=\"desc\">background-clip: padding-box — fill's curve follows the border's own inner edge</div>" +
    "<div class=\"css\">border: 6px solid; border-radius: 14px; background-clip: padding-box</div>" +
    "</td><td style=\"width:50%\">" +
    "<div style=\"border:6px solid #1a6b8a;border-radius:14px;width:160px;height:90px;overflow:hidden\">" +
    "<div style=\"width:100%;height:100%;background:linear-gradient(135deg,#5a9bd8,#2f5f8a)\"></div>" +
    "</div>" +
    "<div class=\"desc\">overflow: hidden — descendant fill clipped to the same reduced inner curve</div>" +
    "</td></tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("border_radius", "Backgrounds & Borders", "Border Radius",
    "border-radius from simple uniform rounding to per-corner and elliptical radii, on filled and bordered boxes, plus overflow:hidden clipping descendant content to the curve and background-clip's inset curves reduced by border width.",
    radiusHtml, pdfConfig);

// Renders the exact same document as the border_radius showcase above, but at a non-default
// PixelsPerInch (issue #812, reopened) - every rounded border stroke, background fill, and
// overflow-clip curve above is built from a PdfSharpAdapter.PixelsPerPoint-inflated layout-space
// rect/radii; RenderUtils.GetRoundRect and BordersDrawHandler's rounded contour builders must divide
// by PixelsPerPoint before building their paths, or the rounded geometry above renders too large
// and mis-positioned relative to everything else on the page. ShrinkToFit is deliberately left off here
// (unlike pdfConfig above) since it recomputes its own effective PixelsPerPoint from content
// measurement and would make the two renders an apples-to-oranges comparison rather than isolating
// the PixelsPerInch=96-vs-72 difference this showcase exists to demonstrate.
PdfGenerateConfig pdfConfig96 = new()
{
    PageSize = PageSize.A4,
    PageOrientation = PageOrientation.Portrait,
    PixelsPerInch = 96
};

await SaveShowcaseAsync("border_radius_96dpi", "Backgrounds & Borders", "Border Radius (96 DPI)",
    "The same border-radius coverage as the Border Radius showcase, rendered at PixelsPerInch=96 instead of the library's default 72 — confirms rounded borders, backgrounds, and overflow-clip curves render identically regardless of PixelsPerInch.",
    radiusHtml, pdfConfig96);

// --- Box-shadow showcase ---

const string ShadowCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0; background: #eef1f5 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 1.1em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
    table.sw { border-collapse: separate; border-spacing: 0; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 14px 8px 20px; vertical-align: top; width: 25%; text-align: center }
    .sbox { height: 46px; width: 70px; margin: 6px auto 8px; background: #fff; border-radius: 4px;
            display: flex; align-items: center; justify-content: center; color: #333; font-size: 7pt }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

string ShadowSwatch(string label, string shadow, string extra = "") =>
    "<td><div class=\"sbox\" style=\"box-shadow: " + shadow + "; " + extra + "\">box</div>" +
    "<div class=\"desc\">" + label + "</div>" +
    "<div class=\"css\">box-shadow: " + shadow + "</div></td>";

string ShadowRow(params string[] cells) => "<table class=\"sw\"><tr>" + string.Concat(cells) + "</tr></table>";

var shadowHtml = "<!DOCTYPE html><html><head>" + ShadowCss + "</head><body>" +

    "<h1>CSS box-shadow Test Page</h1>" +
    "<p class=\"intro\">Each 70&#215;46 white box carries one or more box-shadow layers. Offsets, blur, spread, color, inset, and multiple layers are exercised.</p>" +

    "<h2>1 — Drop Shadows (offset, no blur)</h2>" +
    ShadowRow(
        ShadowSwatch("down-right", "6px 6px 0 #888"),
        ShadowSwatch("up-left (negative)", "-6px -6px 0 #888"),
        ShadowSwatch("straight down", "0 6px 0 #b06"),
        ShadowSwatch("colored", "5px 5px 0 #0a7")
    ) +

    "<h2>2 — Blur</h2>" +
    ShadowRow(
        ShadowSwatch("soft, small (4px)", "3px 3px 4px rgba(0,0,0,.4)"),
        ShadowSwatch("soft, large (12px)", "4px 4px 12px rgba(0,0,0,.5)"),
        ShadowSwatch("ambient (no offset)", "0 0 12px rgba(0,0,0,.55)"),
        ShadowSwatch("lifted card", "0 8px 16px rgba(20,40,80,.35)")
    ) +

    "<h2>3 — Spread</h2>" +
    ShadowRow(
        ShadowSwatch("solid ring (+6px)", "0 0 0 6px #4a90d9"),
        ShadowSwatch("negative spread", "6px 6px 6px -2px rgba(0,0,0,.6)"),
        ShadowSwatch("glow (blur + spread)", "0 0 10px 4px rgba(240,160,0,.7)"),
        ShadowSwatch("halo", "0 0 0 3px #f0a, 0 0 0 6px #a0f")
    ) +

    "<h2>4 — Inset</h2>" +
    ShadowRow(
        ShadowSwatch("inset vignette", "inset 0 0 14px rgba(0,0,0,.55)", "background: #fd0"),
        ShadowSwatch("inset top-left", "inset 5px 5px 8px rgba(0,0,0,.45)", "background: #cde"),
        ShadowSwatch("inset ring", "inset 0 0 0 5px #4a90d9"),
        ShadowSwatch("pressed", "inset 0 3px 6px rgba(0,0,0,.4)", "background: #ddd")
    ) +

    "<h2>5 — Multiple Layers &amp; Rounded</h2>" +
    "<p class=\"intro\">The first-listed layer paints on top. box-shadow honors border-radius.</p>" +
    ShadowRow(
        ShadowSwatch("two-tone", "3px 3px 4px #d33, -3px -3px 4px #33d"),
        ShadowSwatch("elevation stack", "0 1px 2px rgba(0,0,0,.3), 0 6px 14px rgba(0,0,0,.25)"),
        ShadowSwatch("rounded + blur", "5px 6px 10px rgba(0,0,0,.45)", "border-radius: 22px"),
        ShadowSwatch("neumorphic", "6px 6px 12px #b9c2cf, -6px -6px 12px #ffffff", "background: #eef1f5; border-radius: 12px")
    ) +

    "<h2>6 — Inset Inside a Border &amp; calc() Lengths</h2>" +
    "<p class=\"intro\">An inset shadow is confined to the padding edge, whose corner radius is the border-radius minus the border width (a border at least as wide as the radius leaves it square). Offsets, blur and spread accept calc().</p>" +
    ShadowRow(
        ShadowSwatch("inset, radius 20 − border 6", "inset 0 0 8px 2px rgba(0,0,0,.6)", "background: #fd0; border: 6px solid #4a90d9; border-radius: 20px"),
        ShadowSwatch("inset, border &gt; radius", "inset 0 0 8px 2px rgba(0,0,0,.6)", "background: #fd0; border: 8px solid #4a90d9; border-radius: 4px"),
        ShadowSwatch("calc() lengths", "calc(2px + 4px) calc(2px + 4px) calc(3px * 2) rgba(0,0,0,.5)")
    ) +

    "</body></html>";

await SaveShowcaseAsync("box_shadow", "Backgrounds & Borders", "Box Shadow",
    "box-shadow: drop shadows, Gaussian blur, spread, inset (following the padding edge's rounded corners), colored and multiple layered shadows, with border-radius and calc() lengths.",
    shadowHtml, pdfConfig);

// --- border-image showcase ---

// A 12x12 "picture frame" texture: a 2px colored ring around a lighter center, synthesized with the
// real raster encoder (PeachImage) rather than a hand-picked minimal PNG - see MakeRasterDataUri's own
// comment above for why a hand-typed one isn't reliably decodable by PeachPDF's internal codec.
static string MakeBorderImageFrameDataUri()
{
    const int size = 12;
    const int ringWidth = 2;
    var ring = (R: (byte)139, G: (byte)69, B: (byte)19);   // saddle brown
    var center = (R: (byte)222, G: (byte)184, B: (byte)135); // burlywood

    var pixels = new byte[size * size * 4];
    for (var y = 0; y < size; y++)
    {
        for (var x = 0; x < size; x++)
        {
            var onRing = x < ringWidth || y < ringWidth || x >= size - ringWidth || y >= size - ringWidth;
            var (r, g, b) = onRing ? ring : center;
            var i = (y * size + x) * 4;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
        }
    }

    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Rgba32);
    pixels.CopyTo(image.GetPixelSpan());
    using var ms = new MemoryStream();
    image.Save(ms, "png");
    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
}
var borderImageFrameDataUri = MakeBorderImageFrameDataUri();

const string BorderImageCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    .row { display: flex; gap: 16px; margin-bottom: 4px }
    .card { width: 120px; height: 80px }
    .label { font-size: 7.5pt; font-family: "Courier New", monospace; color: #444; margin-bottom: 3px; text-align: center }
    .col { text-align: center }
    </style>
    """;

var borderImageHtml = "<!DOCTYPE html><html><head>" + BorderImageCss + "</head><body>" +

    "<h1>CSS Backgrounds &amp; Borders 3 border-image</h1>" +

    "<h2>1 — 9-slice from a raster frame texture</h2>" +
    "<p style=\"font-size:8pt;color:#666;margin:0 0 6px\">The 12&times;12 source (a 2px ring around a lighter center) is sliced 2/2/2/2, then each region is stretched or tiled to a 12pt border.</p>" +
    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"label\">stretch (default)</div>" +
    $"<div class=\"card\" style=\"border:12pt solid transparent;border-image-source:url({borderImageFrameDataUri});border-image-slice:2;border-image-width:12pt\"></div></div>" +
    "<div class=\"col\"><div class=\"label\">repeat</div>" +
    $"<div class=\"card\" style=\"border:12pt solid transparent;border-image-source:url({borderImageFrameDataUri});border-image-slice:2;border-image-width:12pt;border-image-repeat:repeat\"></div></div>" +
    "<div class=\"col\"><div class=\"label\">slice ... fill</div>" +
    $"<div class=\"card\" style=\"border:12pt solid transparent;border-image-source:url({borderImageFrameDataUri});border-image-slice:2 fill;border-image-width:12pt\"></div></div>" +
    "</div>" +

    "<h2>1b — round and space</h2>" +
    "<p style=\"font-size:8pt;color:#666;margin:0 0 6px\">round resizes each edge/center tile so a whole number of them fit exactly, with no partial tile at the corner; space keeps every tile at its natural size and inserts equal gaps between them so the first and last still touch the edges — both shown here against a card width the plain 12pt tile size (repeat, above) does not divide evenly.</p>" +
    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"label\">round</div>" +
    $"<div class=\"card\" style=\"width:130px;border:12pt solid transparent;border-image-source:url({borderImageFrameDataUri});border-image-slice:2 fill;border-image-width:12pt;border-image-repeat:round\"></div></div>" +
    "<div class=\"col\"><div class=\"label\">space</div>" +
    $"<div class=\"card\" style=\"width:130px;border:12pt solid transparent;border-image-source:url({borderImageFrameDataUri});border-image-slice:2 fill;border-image-width:12pt;border-image-repeat:space\"></div></div>" +
    "</div>" +

    "<h2>2 — border-image-outset</h2>" +
    "<p style=\"font-size:8pt;color:#666;margin:0 0 6px\">The painted frame extends past the border box without affecting layout - the dashed guide shows the untouched border box underneath.</p>" +
    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"label\">outset: 8pt</div>" +
    "<div style=\"width:120px;height:80px;border:1px dashed #999;display:flex;align-items:center;justify-content:center\">" +
    $"<div style=\"width:80px;height:40px;border:12pt solid transparent;border-image-source:url({borderImageFrameDataUri});border-image-slice:2;border-image-width:12pt;border-image-outset:8pt\"></div>" +
    "</div></div>" +
    "</div>" +

    "<h2>3 — a gradient source (no raster image needed)</h2>" +
    "<p style=\"font-size:8pt;color:#666;margin:0 0 6px\">border-image-source accepts any &lt;image&gt;, including a gradient - useful for a colored frame with no asset at all.</p>" +
    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"label\">linear-gradient, slice:1 stretch</div>" +
    "<div class=\"card\" style=\"border:10pt solid transparent;border-image-source:linear-gradient(135deg,#4a90d9,#f0a);border-image-slice:1;border-image-width:10pt\"></div></div>" +
    "<div class=\"col\"><div class=\"label\">conic-gradient, rounded corners</div>" +
    "<div class=\"card\" style=\"border:10pt solid transparent;border-radius:14pt;border-image-source:conic-gradient(red,orange,yellow,green,blue,violet,red);border-image-slice:1;border-image-width:10pt\"></div></div>" +
    "</div>" +

    "</body></html>";

await SaveShowcaseAsync("border_image", "Backgrounds & Borders", "Border Image",
    "border-image: the standard 9-slice algorithm (corners scaled, edges stretched/tiled per border-image-repeat — stretch, repeat, round or space — with an optional filled center) from a raster texture or any CSS <image> including gradients, plus border-image-outset extending the painted frame past the border box.",
    borderImageHtml, pdfConfig);

// --- box-decoration-break showcase ---

const string DecorationBreakCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10.5pt; margin: 1.1em 0 0.45em; color: #444 }
    p.intro { color: #555; margin: 0 0 0.8em; line-height: 1.45 }
    .pair { width: 100%; border-collapse: collapse }
    .pair td { width: 50%; vertical-align: top; padding: 0 6pt 0 0 }
    .label { font: bold 8pt Arial; color: #666; margin: 0 0 4pt }
    .css { font: 7.5pt "Courier New", monospace; color: #888; margin: 4pt 0 0 }
    .measure { width: 165pt; line-height: 2.4 }

    /* The canonical case: a highlighted inline that wraps. */
    .pill {
      background: linear-gradient(to right, #ffd08a, #f2709c);
      border-radius: 9pt;
      padding: 3pt 9pt;
      color: #3a2b1e;
    }
    .framed {
      background: #eaf4ff;
      border: 2pt solid #2b7cd3;
      border-radius: 8pt;
      padding: 2pt 8pt;
    }
    /* A hard (unblurred) offset shadow, so which edges carry one is unmistakable. */
    .shadowed { background: #fff2cc; box-shadow: 3pt 3pt 0 #b08d57; padding: 2pt 8pt }
    .clone { -webkit-box-decoration-break: clone; box-decoration-break: clone }

    /* Taller than any page band, so each one is guaranteed to cross a page boundary
       wherever it starts. The second gets a page to itself so both crossings are visible. */
    .band {
      border: 3pt solid #2b7cd3;
      border-radius: 10pt;
      background: #eaf4ff;
      padding: 8pt 10pt;
      height: 880pt;
      color: #24486b;
    }
    .band + .band { break-before: page }

    /* Section 5: enough short lines inside one inline that it runs off the bottom of a page
       and continues on the next, so the break it is cut by is a page boundary. */
    .onpage { break-before: page }
    .runon { width: 300pt; line-height: 2.4 }
    </style>
    """;

// The section-5 fixture's lines: short enough not to wrap, and enough of them that the inline runs off
// the bottom of its page and continues on the next, so the break cutting it is a page boundary.
var crossingPageLines = string.Join("<br>", Enumerable.Range(1, 100).Select(i => $"highlighted line {i}"));

var decorationBreakHtml = "<!DOCTYPE html><html><head>" + DecorationBreakCss + "</head><body>" +
    "<h1>box-decoration-break</h1>" +
    "<p class=\"intro\">When a break splits a box, <code>box-decoration-break</code> decides whether its border, " +
    "padding, background, corner radii and shadow belong to the box as a whole or to each piece. " +
    "<b>slice</b> (the initial value) renders the box as if it were never broken and then cuts it at the break; " +
    "<b>clone</b> wraps every piece independently. Both apply at line breaks and at page breaks.</p>" +

    "<h2>1 — An inline box wrapping across lines</h2>" +
    "<table class=\"pair\"><tr>" +
    "<td><p class=\"label\">slice — one continuous pill, cut at each wrap</p>" +
    "<div class=\"measure\">The <span class=\"pill\">rounded, gradient-filled highlight runs on across the wrap</span> " +
    "and only its true ends are rounded.</div>" +
    "<p class=\"css\">box-decoration-break: slice</p></td>" +
    "<td><p class=\"label\">clone — a complete pill on every line</p>" +
    "<div class=\"measure\">The <span class=\"pill clone\">rounded, gradient-filled highlight restarts on each line</span> " +
    "so every line is closed off.</div>" +
    "<p class=\"css\">box-decoration-break: clone</p></td>" +
    "</tr></table>" +

    "<h2>2 — Borders and padding at a line break</h2>" +
    "<table class=\"pair\"><tr>" +
    "<td><p class=\"label\">slice — no border or padding inserted at the break</p>" +
    "<div class=\"measure\">A <span class=\"framed\">framed inline whose box is opened on the first line and closed " +
    "on the last</span> one.</div>" +
    "<p class=\"css\">box-decoration-break: slice</p></td>" +
    "<td><p class=\"label\">clone — every line fully framed</p>" +
    "<div class=\"measure\">A <span class=\"framed clone\">framed inline whose box is opened and closed on every " +
    "line it occupies</span> here.</div>" +
    "<p class=\"css\">box-decoration-break: clone</p></td>" +
    "</tr></table>" +

    "<h2>3 — box-shadow at a broken edge</h2>" +
    "<table class=\"pair\"><tr>" +
    "<td><p class=\"label\">slice — no shadow along the broken edges</p>" +
    "<div class=\"measure\">This <span class=\"shadowed\">shadowed inline casts a shadow around the outside of the " +
    "whole box only</span> — nowhere else.</div>" +
    "<p class=\"css\">box-decoration-break: slice</p></td>" +
    "<td><p class=\"label\">clone — each line casts its own shadow</p>" +
    "<div class=\"measure\">This <span class=\"shadowed clone\">shadowed inline casts a separate shadow around each " +
    "of its own lines</span> — all four sides.</div>" +
    "<p class=\"css\">box-decoration-break: clone</p></td>" +
    "</tr></table>" +

    "<h2>4 — A block crossing a page break (see the following pages)</h2>" +
    "<p class=\"intro\">The same over-tall block twice, each crossing a page boundary. Under <code>slice</code> the " +
    "border simply runs off the bottom of one page and continues at the top of the next; under <code>clone</code> each " +
    "page's piece is closed off with its own rounded border.</p>" +
    "<div class=\"band\">slice — this box is drawn as one unbroken rounded frame and cut by the page edge, so it has " +
    "no bottom border here and no top border on the next page.</div>" +
    "<div class=\"band clone\">clone — this box is wrapped independently on each page, so it closes with its own " +
    "rounded bottom border at the page edge and opens with a new one overleaf.</div>" +

    "<h2 class=\"onpage\">5 — An inline box crossing a page break</h2>" +
    "<p class=\"intro\">The inline-axis counterpart of section 4, and the same distinction as section 2 — except that " +
    "the break here is a page boundary rather than a line wrap. Under <code>slice</code> the highlight is opened once, " +
    "on its first line, and the lines continuing overleaf carry no leading border or padding of their own — they " +
    "resume flush against the block's content edge. Under <code>clone</code> the fragment opening on the new page " +
    "opens with a fresh set, exactly as it would after a wrap. Each runs onto the following page.</p>" +
    "<p class=\"label\">slice — the continuation resumes flush at the content edge</p>" +
    "<div class=\"runon\"><span class=\"framed\">" + crossingPageLines + "</span></div>" +
    "<h2 class=\"onpage\">5b — the same inline, cloning</h2>" +
    "<p class=\"label\">clone — the continuation re-opens with its own frame</p>" +
    "<div class=\"runon\"><span class=\"framed clone\">" + crossingPageLines + "</span></div>" +

    "</body></html>";

await SaveShowcaseAsync("box_decoration_break", "Backgrounds & Borders", "Box Decoration Break",
    "box-decoration-break: slice vs clone at both line-box and page breaks — continuous sliced decorations against independently wrapped per-fragment ones, for backgrounds, gradients, border-radius, borders and box-shadow.",
    decorationBreakHtml, pdfConfig);

// --- background-origin + background-clip showcase ---

const string OriginCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .obox { height: 60px; border: 8px solid #333; padding: 12px; margin-bottom: 3px; box-sizing: border-box }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var originHtml = "<!DOCTYPE html><html><head>" + OriginCss + "</head><body>" +

    "<h1>CSS background-origin &amp; background-clip Test Page</h1>" +
    "<p class=\"intro\">Each box has border: 8px solid #333 and padding: 12px. Three regions: border-box (full), padding-box (inside border), content-box (inside padding).</p>" +

    "<h2>1 — background-origin: Solid Color</h2>" +
    "<p class=\"intro\">Solid colors fill the clip area regardless of origin — this verifies no rendering errors.</p>" +
    Row(
        OriginSwatch("default (padding-box)", "background-color: steelblue"),
        OriginSwatch("border-box", "background-color: steelblue; background-origin: border-box", "background-origin: border-box"),
        OriginSwatch("padding-box", "background-color: steelblue; background-origin: padding-box", "background-origin: padding-box"),
        OriginSwatch("content-box", "background-color: steelblue; background-origin: content-box", "background-origin: content-box")
    ) +

    "<h2>2 — background-origin: Linear Gradient</h2>" +
    "<p class=\"intro\">Gradient coordinate space shifts with origin. border-box spans the full box; content-box spans only the content area.</p>" +
    Row(
        OriginSwatch("default (padding-box)", "background: linear-gradient(to right, red, blue)"),
        OriginSwatch("border-box", "background: linear-gradient(to right, red, blue); background-origin: border-box", "background-origin: border-box"),
        OriginSwatch("padding-box", "background: linear-gradient(to right, red, blue); background-origin: padding-box", "background-origin: padding-box"),
        OriginSwatch("content-box", "background: linear-gradient(to right, red, blue); background-origin: content-box", "background-origin: content-box")
    ) +

    "<h2>3 — background-origin: Radial Gradient</h2>" +
    "<p class=\"intro\">Radial center and radius are computed from the origin rect. content-box produces a more compressed gradient.</p>" +
    Row(
        OriginSwatch("default (padding-box)", "background: radial-gradient(circle, yellow, navy)"),
        OriginSwatch("border-box", "background: radial-gradient(circle, yellow, navy); background-origin: border-box", "background-origin: border-box"),
        OriginSwatch("padding-box", "background: radial-gradient(circle, yellow, navy); background-origin: padding-box", "background-origin: padding-box"),
        OriginSwatch("content-box", "background: radial-gradient(circle, yellow, navy); background-origin: content-box", "background-origin: content-box")
    ) +

    "<h2>4 — background-clip: Solid Color</h2>" +
    "<p class=\"intro\">background-clip controls where the background is painted. padding-box: no color behind border. content-box: color only in content area.</p>" +
    Row(
        OriginSwatch("default (border-box)", "background-color: coral"),
        OriginSwatch("border-box", "background-color: coral; background-clip: border-box", "background-clip: border-box"),
        OriginSwatch("padding-box", "background-color: coral; background-clip: padding-box", "background-clip: padding-box"),
        OriginSwatch("content-box", "background-color: coral; background-clip: content-box", "background-clip: content-box")
    ) +

    "<h2>5 — background-clip: Linear Gradient</h2>" +
    "<p class=\"intro\">Gradient fills the origin area but is clipped to the clip area.</p>" +
    Row(
        OriginSwatch("default (border-box)", "background: linear-gradient(to right, orange, purple)"),
        OriginSwatch("border-box", "background: linear-gradient(to right, orange, purple); background-clip: border-box", "background-clip: border-box"),
        OriginSwatch("padding-box", "background: linear-gradient(to right, orange, purple); background-clip: padding-box", "background-clip: padding-box"),
        OriginSwatch("content-box", "background: linear-gradient(to right, orange, purple); background-clip: content-box", "background-clip: content-box")
    ) +

    "<h2>6 — background-origin + background-clip: Combinations</h2>" +
    Row(
        OriginSwatch("origin: border, clip: border", "background: linear-gradient(to right, teal, gold); background-origin: border-box; background-clip: border-box", "origin: border-box; clip: border-box"),
        OriginSwatch("origin: padding, clip: padding", "background: linear-gradient(to right, teal, gold); background-origin: padding-box; background-clip: padding-box", "origin: padding-box; clip: padding-box"),
        OriginSwatch("origin: content, clip: content", "background: linear-gradient(to right, teal, gold); background-origin: content-box; background-clip: content-box", "origin: content-box; clip: content-box"),
        OriginSwatch("origin: border, clip: padding", "background: linear-gradient(to right, teal, gold); background-origin: border-box; background-clip: padding-box", "origin: border-box; clip: padding-box")
    ) +

    "<h2>7 — background-origin + background-clip: More Combinations</h2>" +
    Row(
        OriginSwatch("origin: border, clip: content", "background: linear-gradient(to right, crimson, lime); background-origin: border-box; background-clip: content-box", "origin: border-box; clip: content-box"),
        OriginSwatch("origin: padding, clip: content", "background: linear-gradient(to right, crimson, lime); background-origin: padding-box; background-clip: content-box", "origin: padding-box; clip: content-box"),
        OriginSwatch("origin: content, clip: padding", "background: linear-gradient(to right, crimson, lime); background-origin: content-box; background-clip: padding-box", "origin: content-box; clip: padding-box"),
        OriginSwatch("origin: content, clip: border", "background: linear-gradient(to right, crimson, lime); background-origin: content-box; background-clip: border-box", "origin: content-box; clip: border-box")
    ) +

    "<h2>8 — Shorthand with Origin/Clip Tokens</h2>" +
    "<p class=\"intro\">A single box-model keyword in the shorthand sets both origin and clip; two keywords set origin then clip.</p>" +
    Row(
        OriginSwatch("single keyword — padding", "background: steelblue padding-box", "background: steelblue padding-box"),
        OriginSwatch("single keyword — content", "background: coral content-box", "background: coral content-box"),
        OriginSwatch("gradient padding-box content-box", "background: linear-gradient(to right, teal, gold) padding-box content-box", "linear-gradient padding-box content-box"),
        OriginSwatch("gradient border-box padding-box", "background: linear-gradient(to right, teal, gold) border-box padding-box", "linear-gradient border-box padding-box")
    ) +

    "<h2>9 — Radial Gradient + Clip Combinations</h2>" +
    Row(
        OriginSwatch("radial, clip: border-box", "background: radial-gradient(circle at center, yellow, navy); background-clip: border-box", "radial; clip: border-box"),
        OriginSwatch("radial, clip: padding-box", "background: radial-gradient(circle at center, yellow, navy); background-clip: padding-box", "radial; clip: padding-box"),
        OriginSwatch("radial, clip: content-box", "background: radial-gradient(circle at center, yellow, navy); background-clip: content-box", "radial; clip: content-box"),
        OriginSwatch("radial, origin+clip: content", "background: radial-gradient(circle at center, yellow, navy); background-origin: content-box; background-clip: content-box", "origin: content-box; clip: content-box")
    ) +

    "<h2>10 — Multi-Layer background-origin / background-clip</h2>" +
    "<p class=\"intro\">Comma-separated background-origin/background-clip values cycle per background-image layer, one value per layer, just like background-position/background-size.</p>" +
    Row(
        OriginSwatch("2 layers: content-box, border-box",
            "background-image: linear-gradient(to right, red, blue), linear-gradient(to bottom, green, yellow); background-origin: content-box, border-box; background-clip: content-box, border-box",
            "origin/clip: content-box, border-box"),
        OriginSwatch("2 layers: padding-box, content-box",
            "background-image: linear-gradient(to right, teal, gold), radial-gradient(circle, crimson, navy); background-origin: padding-box, content-box; background-clip: padding-box, content-box",
            "origin/clip: padding-box, content-box"),
        OriginSwatch("3 layers cycling 2 values",
            "background-image: linear-gradient(red, blue), linear-gradient(green, yellow), linear-gradient(orange, purple); background-origin: border-box, content-box; background-clip: border-box, content-box",
            "3 layers, origin/clip cycle: border-box, content-box, border-box"),
        OriginSwatch("color uses LAST clip entry",
            "background-color: coral; background-clip: border-box, content-box",
            "background-clip: border-box, content-box (no images — color clips to content-box, the last entry)")
    ) +

    "<h2>11 — Multi-Layer background-repeat</h2>" +
    "<p class=\"intro\">A comma-separated background-repeat value also cycles per layer — each background-image layer can repeat independently.</p>" +
    Row(
        OriginSwatch("layer 1: no-repeat, layer 2: repeat",
            "background-image: radial-gradient(circle, red, blue 70%), radial-gradient(circle, green, yellow 70%); background-size: 16px, 16px; background-repeat: no-repeat, repeat",
            "background-repeat: no-repeat, repeat"),
        OriginSwatch("layer 1: repeat-x, layer 2: repeat-y",
            "background-image: linear-gradient(to right, red, blue), linear-gradient(to bottom, green, yellow); background-size: 16px 16px, 16px 16px; background-repeat: repeat-x, repeat-y",
            "background-repeat: repeat-x, repeat-y")
    ) +

    "</body></html>";

await SaveShowcaseAsync("background_origin_clip", "Backgrounds & Borders", "Background Origin & Clip",
    "background-origin and background-clip controlling where a background paints relative to the border, padding, and content boxes.",
    originHtml, pdfConfig);

// --- background-clip: text showcase ---

const string backgroundClipTextHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page { size: a4; margin: 20mm }
    body { margin: 0; font-family: Arial, sans-serif; color: #222 }
    h1 { font-size: 52pt; font-weight: 800; margin: 0 0 0.2em; letter-spacing: -1px;
         background: linear-gradient(to right, #e11d48, #7c3aed, #2563eb);
         background-clip: text; -webkit-background-clip: text; color: transparent }
    h2 { font-size: 22pt; margin: 0.6em 0 0.2em;
         background: linear-gradient(135deg, #f59e0b, #ef4444);
         background-clip: text; -webkit-background-clip: text; color: transparent }
    p { font-size: 10.5pt; line-height: 1.5; max-width: 32em; color: #444 }
    p.note { font-size: 9pt; color: #777; font-style: italic }
    </style>
    </head>
    <body>
    <h1>Revenue Growth</h1>
    <h2>Q4 Highlights</h2>
    <p>
      The gradient above is clipped to the exact shape of the glyphs themselves
      (<code>background-clip: text</code>) instead of painting across the whole heading&#8217;s box - the
      standard technique for gradient-filled headings, paired here with <code>color: transparent</code>
      so only the background shows through the letterforms.
    </p>
    <p class="note">
      Unlike PeachPDF&#8217;s SVG gradient-text support, this paragraph and the headings above it all stay
      ordinary, selectable, extractable PDF text - try selecting this text in a PDF viewer. Only the
      background paint is clipped to glyph shape; the text itself is never converted to vector art.
    </p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("background_clip_text", "Backgrounds & Borders", "background-clip: text (Gradient Text)",
    "Clips a background gradient to the shape of the text itself, with the glyphs still selectable and extractable in the PDF.",
    backgroundClipTextHtml, pdfConfig);

// --- background-position + background-size showcase ---

const string PositionCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .obox { height: 60px; border: 1px solid #333; background-color: #eee; background-repeat: no-repeat; margin-bottom: 3px; box-sizing: border-box }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

const string Dot = "radial-gradient(circle, crimson, darkred)";

// Gradients have no intrinsic size/ratio (they're "generated images" per spec), so a two-value
// background-size (e.g. "20px 20px") is used throughout to get an actual 20x20 square marker -
// a single-value size like "20px" would set width=20px but fall back to the FULL container height
// for the auto height axis (there's no ratio to compute a proportional height from), per spec.
const string DotSize = "20px 20px";

// A small square (1:1 intrinsic ratio, viewBox 0 0 20 20) vector SVG, reused below as an SVG
// background-image/list-style-image url() source - unlike Dot above, this has a real intrinsic
// size/ratio, so cover/contain/auto resolve against it exactly like a raster image would.
var svgDotMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20"><rect width="20" height="20" fill="#c0392b"/><circle cx="10" cy="10" r="8" fill="#f1c40f"/></svg>""";
var svgDotDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svgDotMarkup));

var positionHtml = "<!DOCTYPE html><html><head>" + PositionCss + "</head><body>" +

    "<h1>CSS background-position &amp; background-size Test Page</h1>" +
    "<p class=\"intro\">Each box is 60px tall with a light gray fill; the red dot is a 20x20 background-image whose placement/size is what's under test.</p>" +

    "<h2>1 — background-position: Keywords</h2>" +
    Row(
        OriginSwatch("top left", $"background-image: {Dot}; background-size: {DotSize}; background-position: top left", "background-position: top left"),
        OriginSwatch("top right", $"background-image: {Dot}; background-size: {DotSize}; background-position: top right", "background-position: top right"),
        OriginSwatch("bottom left", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom left", "background-position: bottom left"),
        OriginSwatch("bottom right", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom right", "background-position: bottom right")
    ) +

    "<h2>2 — background-position: Center &amp; Reversed Keyword Order</h2>" +
    Row(
        OriginSwatch("center", $"background-image: {Dot}; background-size: {DotSize}; background-position: center", "background-position: center"),
        OriginSwatch("bottom center", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom center", "background-position: bottom center"),
        OriginSwatch("center right (reversed)", $"background-image: {Dot}; background-size: {DotSize}; background-position: center right", "background-position: center right"),
        OriginSwatch("right (single keyword)", $"background-image: {Dot}; background-size: {DotSize}; background-position: right", "background-position: right (Y implied center)")
    ) +

    "<h2>3 — background-position: Percentages &amp; Lengths</h2>" +
    Row(
        OriginSwatch("25% 75%", $"background-image: {Dot}; background-size: {DotSize}; background-position: 25% 75%", "background-position: 25% 75% (not centered)"),
        OriginSwatch("10px 10px", $"background-image: {Dot}; background-size: {DotSize}; background-position: 10px 10px", "background-position: 10px 10px"),
        OriginSwatch("calc(50% - 10px) center", $"background-image: {Dot}; background-size: {DotSize}; background-position: calc(50% - 10px) center", "background-position: calc(50% - 10px) center"),
        OriginSwatch("0 0 (top left)", $"background-image: {Dot}; background-size: {DotSize}; background-position: 0 0", "background-position: 0 0")
    ) +

    "<h2>4 — background-position: 4-Value Edge Offset Syntax</h2>" +
    Row(
        OriginSwatch("right 10px bottom 10px", $"background-image: {Dot}; background-size: {DotSize}; background-position: right 10px bottom 10px", "background-position: right 10px bottom 10px"),
        OriginSwatch("right 20px top", $"background-image: {Dot}; background-size: {DotSize}; background-position: right 20px top", "background-position: right 20px top"),
        OriginSwatch("left bottom 20px", $"background-image: {Dot}; background-size: {DotSize}; background-position: left bottom 20px", "background-position: left bottom 20px"),
        OriginSwatch("bottom 5px right 5px", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom 5px right 5px", "background-position: bottom 5px right 5px")
    ) +

    "<h2>5 — background-size: cover / contain / auto (no intrinsic ratio)</h2>" +
    "<p class=\"intro\">Gradients are \"generated images\" with no intrinsic size or ratio, so per spec cover/contain/auto all resolve identically to 100% 100% (full box) - contrasted here with an explicit stretched size.</p>" +
    Row(
        OriginSwatch("cover (= full box)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: cover", "background-size: cover"),
        OriginSwatch("contain (= full box)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: contain", "background-size: contain"),
        OriginSwatch("auto (= full box)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: auto", "background-size: auto"),
        OriginSwatch("100% 100% (same result)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: 100% 100%", "background-size: 100% 100%")
    ) +

    "<h2>6 — background-size: Explicit Lengths &amp; Percentages</h2>" +
    Row(
        OriginSwatch("40px 40px", $"background-image: {Dot}; background-repeat: no-repeat; background-position: center; background-size: 40px 40px", "background-size: 40px 40px"),
        OriginSwatch("50% 50%", $"background-image: {Dot}; background-repeat: no-repeat; background-position: center; background-size: 50% 50%", "background-size: 50% 50%"),
        OriginSwatch("20px (width only, height fills box)", $"background-image: {Dot}; background-repeat: no-repeat; background-position: top left; background-size: 20px", "background-size: 20px (no ratio - height defaults to full box)"),
        OriginSwatch("gradient background-size, tiled", "background-image: linear-gradient(to right, red, blue); background-repeat: repeat; background-size: 50%", "gradient background-size: 50% (tiles)")
    ) +

    "<h2>7 — Multi-Layer background-position / background-size</h2>" +
    "<p class=\"intro\">Comma-separated background-position/background-size values cycle per background-image layer, same as background-image itself.</p>" +
    Row(
        OriginSwatch("2 dots, different corners",
            $"background-image: {Dot}, {Dot}; background-repeat: no-repeat; background-size: {DotSize}, {DotSize}; background-position: top left, bottom right",
            "background-position: top left, bottom right"),
        OriginSwatch("2 dots, different sizes",
            $"background-image: {Dot}, {Dot}; background-repeat: no-repeat; background-position: center; background-size: 40px 40px, 15px 15px",
            "background-size: 40px 40px, 15px 15px (cycled per layer)"),
        OriginSwatch("3 layers cycling 2 positions",
            $"background-image: {Dot}, {Dot}, {Dot}; background-repeat: no-repeat; background-size: 16px 16px; background-position: top left, bottom right",
            "3 layers, position cycles: top left, bottom right, top left")
    ) +

    "<h2>8 — SVG background-image</h2>" +
    "<p class=\"intro\">A url() background-image source can now be an SVG - rendered as real vector content (a reusable Form XObject tile), not rasterized, exactly like &lt;img src=\"x.svg\"&gt; already was.</p>" +
    Row(
        OriginSwatch("basic (auto = SVG's own viewBox size)",
            $"background-image: url('{svgDotDataUri}'); background-repeat: no-repeat; background-position: center",
            "background-image: url(x.svg) (no background-size)"),
        OriginSwatch("background-size: cover",
            $"background-image: url('{svgDotDataUri}'); background-repeat: no-repeat; background-size: cover",
            "background-size: cover (uses the SVG's 1:1 intrinsic ratio)"),
        OriginSwatch("background-size: contain",
            $"background-image: url('{svgDotDataUri}'); background-repeat: no-repeat; background-size: contain",
            "background-size: contain"),
        OriginSwatch("background-repeat: repeat (tiled)",
            $"background-image: url('{svgDotDataUri}'); background-size: 20px 20px; background-repeat: repeat",
            "background-repeat: repeat (one vector tile, reused per copy)")
    ) +

    "</body></html>";

await SaveShowcaseAsync("background_position_size", "Backgrounds & Borders", "Background Position & Size",
    "background-position and background-size combinations: keywords, lengths, percentages, cover, and contain.",
    positionHtml, pdfConfig);

// --- list-style-image showcase ---

// A small vector SVG sized to match a marker box (roughly one font-height square) rather than the
// larger svgDotDataUri used above for the background-image showcase's 60px-tall boxes - reusing that
// larger one here would render at its full 20x20 intrinsic size (list-style-image has no analogue to
// background-size), overflowing the ~10pt marker box and clipping down to just its solid center.
var svgMarkerMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#c0392b"/><circle cx="5" cy="5" r="4" fill="#f1c40f"/></svg>""";
var svgMarkerDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svgMarkerMarkup));

static string ListSwatch(string desc, string listCss, string itemLabel = "Item") =>
    "<td>" +
    $"<ul style=\"margin: 0; padding-left: 2em; {listCss}\">" +
    $"<li>{itemLabel} one</li><li>{itemLabel} two</li><li>{itemLabel} three</li>" +
    "</ul>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{listCss}</div>" +
    "</td>";

const string ListCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var listHtml = "<!DOCTYPE html><html><head>" + ListCss + "</head><body>" +

    "<h1>CSS list-style-image Test Page</h1>" +

    "<h2>1 — URL image (graceful fallback when missing)</h2>" +
    "<p class=\"intro\">A missing URL should produce no marker and no crash. The list items are still indented normally.</p>" +
    Row(
        ListSwatch("missing URL", "list-style-image: url('nonexistent.png');"),
        ListSwatch("none (baseline)", "list-style-image: none;"),
        ListSwatch("disc (no image)", "list-style-type: disc;"),
        ListSwatch("decimal (no image)", "list-style-type: decimal;")
    ) +

    "<h2>2 — linear-gradient markers</h2>" +
    "<p class=\"intro\">Each list item marker is a gradient-filled square, sized to the current font height.</p>" +
    Row(
        ListSwatch("to right red→blue", "list-style-image: linear-gradient(to right, red, blue);"),
        ListSwatch("to bottom green→yellow", "list-style-image: linear-gradient(to bottom, green, yellow);"),
        ListSwatch("45deg multi-stop", "list-style-image: linear-gradient(45deg, red, yellow, blue);"),
        ListSwatch("hard-stop stripes", "list-style-image: linear-gradient(to right, red 50%, blue 50%);")
    ) +

    "<h2>3 — radial-gradient markers</h2>" +
    Row(
        ListSwatch("circle center", "list-style-image: radial-gradient(circle, white, navy);"),
        ListSwatch("ellipse default", "list-style-image: radial-gradient(red, blue);"),
        ListSwatch("at 30% 30%", "list-style-image: radial-gradient(circle at 30% 30%, yellow, orange, red);"),
        ListSwatch("closest-side", "list-style-image: radial-gradient(closest-side circle at 50% 50%, lime, teal);")
    ) +

    "<h2>4 — conic-gradient markers</h2>" +
    Row(
        ListSwatch("default sweep", "list-style-image: conic-gradient(red, blue);"),
        ListSwatch("pie chart", "list-style-image: conic-gradient(#e74c3c 0 25%, #3498db 25% 65%, #2ecc71 65% 100%);"),
        ListSwatch("from 90deg", "list-style-image: conic-gradient(from 90deg, red, yellow, blue);"),
        ListSwatch("color wheel", "list-style-image: conic-gradient(red, yellow, lime, cyan, blue, magenta, red);")
    ) +

    "<h2>5 — list-style shorthand with gradient</h2>" +
    "<p class=\"intro\">A single list-style shorthand value setting both position and image.</p>" +
    Row(
        ListSwatch("inside linear", "list-style: inside linear-gradient(to right, purple, orange);"),
        ListSwatch("inside radial", "list-style: inside radial-gradient(circle, gold, crimson);"),
        ListSwatch("inside conic", "list-style: inside conic-gradient(red 0 33%, blue 33% 66%, green 66% 100%);"),
        ListSwatch("outside linear", "list-style: outside linear-gradient(45deg, teal, pink);")
    ) +

    "<h2>6 — Gradient marker alongside typed bullets</h2>" +
    "<p class=\"intro\">Left column uses a gradient list-style-image; right uses list-style-type only. Neither should interfere.</p>" +
    Row(
        ListSwatch("gradient image", "list-style-image: linear-gradient(to right, red, blue);"),
        ListSwatch("disc (no image)", "list-style-type: disc; list-style-image: none;"),
        ListSwatch("circle (no image)", "list-style-type: circle; list-style-image: none;"),
        ListSwatch("square (no image)", "list-style-type: square; list-style-image: none;")
    ) +

    "<h2>7 — SVG list-style-image</h2>" +
    "<p class=\"intro\">A url() list-style-image source can now be an SVG - rendered as real vector content, not rasterized, same as the SVG background-image support above.</p>" +
    Row(
        ListSwatch("SVG url() image", $"list-style-image: url('{svgMarkerDataUri}');"),
        ListSwatch("shorthand: inside + SVG", $"list-style: inside url('{svgMarkerDataUri}');"),
        ListSwatch("shorthand: outside + SVG", $"list-style: outside url('{svgMarkerDataUri}');"),
        ListSwatch("missing SVG file (graceful fallback)", "list-style-image: url('nonexistent.svg');")
    ) +

    "</body></html>";

await SaveShowcaseAsync("list_style_image", "Lists & Generated Content", "List Style Image",
    "Custom list bullets supplied through list-style-image.",
    listHtml, pdfConfig);

// --- ::marker styling showcase ---

// ::marker rules can't be expressed via an inline style="..." attribute (pseudo-elements aren't
// targetable that way), so each swatch gets its own scoped <style> block instead - mirrors
// ContentSwatch's own approach below for the same reason. The scoping class goes on the list
// container (matching ListSwatch's own convention above), so the selector needs a descendant
// combinator ("li::marker", not "::marker" directly on the class) - ::marker's own compound-
// selector matching checks the non-pseudo part against the marker's *parent* (the <li>), not
// against whatever box the class happens to be on.
static string MarkerSwatch(string desc, string markerCss, string tag = "ul", string itemLabel = "Item") =>
    "<td>" +
    $"<style>.mk-{desc.GetHashCode() & 0x7FFFFFFF} li::marker {{ {markerCss} }}</style>" +
    $"<{tag} class=\"mk-{desc.GetHashCode() & 0x7FFFFFFF}\" style=\"margin: 0; padding-left: 2em;\">" +
    $"<li>{itemLabel} one</li><li>{itemLabel} two</li><li>{itemLabel} three</li>" +
    $"</{tag}>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">::marker {{ {markerCss} }}</div>" +
    "</td>";

var markerHtml = "<!DOCTYPE html><html><head>" + ListCss + "</head><body>" +

    "<h1>CSS ::marker Test Page</h1>" +

    "<h2>1 — color and font styling</h2>" +
    "<p class=\"intro\">::marker is a real, independently styled box now - its color/font are its own, not the list item's.</p>" +
    Row(
        MarkerSwatch("red marker, black text", "color: red;", "ol"),
        MarkerSwatch("large bold marker", "font-size: 13pt; font-weight: bold;", "ol"),
        MarkerSwatch("marker color vs. text color", "color: green;", "ul"),
        MarkerSwatch("both color and font-size", "color: #8e44ad; font-size: 13pt;", "ol")
    ) +

    "<h2>2 — content overrides</h2>" +
    "<p class=\"intro\">An explicit content value fully replaces the automatic bullet/number - no automatic \".\" suffix, so any spacing/punctuation must be in the string itself.</p>" +
    Row(
        MarkerSwatch("custom string", "content: \"→  \";", "ul"),
        MarkerSwatch("counter() override", "content: counter(list-item) \")  \";", "ol"),
        MarkerSwatch("content: none (suppressed)", "content: none;", "ul"),
        MarkerSwatch("content: normal (baseline)", "content: normal;", "ol")
    ) +

    "<h2>3 — direction</h2>" +
    "<p class=\"intro\">The marker's own direction is independent of the list item's own text direction.</p>" +
    Row(
        MarkerSwatch("marker: rtl, text: ltr", "direction: rtl;", "ol"),
        MarkerSwatch("marker: ltr (baseline)", "direction: ltr;", "ol")
    ) +

    "<h2>4 — list-style-position: inside vs outside</h2>" +
    "<p class=\"intro\">An \"inside\" marker is a real first inline child of the list item's own content, flowing (and wrapping) like ordinary text; an \"outside\" marker (the default) hangs to the left, never affecting the item's own line-wrap.</p>" +
    Row(
        "<td>" +
        "<ul style=\"margin: 0; padding-left: 2em; list-style-position: outside\">" +
        "<li>Outside position (default): this list item has enough text in it to wrap onto a second line, so the effect on the hanging indent is visible.</li>" +
        "</ul>" +
        "<div class=\"desc\">outside (default)</div>" +
        "<div class=\"css\">list-style-position: outside</div>" +
        "</td>",
        "<td>" +
        "<ul style=\"margin: 0; padding-left: 2em; list-style-position: inside\">" +
        "<li>Inside position: this list item has enough text in it to wrap onto a second line, so the marker's effect on where the wrapped line starts is visible.</li>" +
        "</ul>" +
        "<div class=\"desc\">inside</div>" +
        "<div class=\"css\">list-style-position: inside</div>" +
        "</td>",
        // The reserved width for an "inside" marker follows the marker's own (possibly overridden)
        // font, not the item's - this large marker font visibly pushes the wrapped line further right.
        "<td>" +
        "<style>.mk-inside-big li::marker { font-size: 20pt; }</style>" +
        "<ol class=\"mk-inside-big\" style=\"margin: 0; padding-left: 2em; list-style-position: inside\">" +
        "<li>Inside position with a much larger marker font-size: this item's text also wraps, showing the reserved width grow with it.</li>" +
        "</ol>" +
        "<div class=\"desc\">inside + large marker font-size</div>" +
        "<div class=\"css\">list-style-position: inside; ::marker { font-size: 20pt }</div>" +
        "</td>"
    ) +

    "<h2>5 — items whose content is block-level</h2>" +
    "<p class=\"intro\">A marker sits beside its item's principal block box whether the item's content is inline or block-level, so <code>&lt;li&gt;&lt;p&gt;…&lt;/p&gt;&lt;/li&gt;</code> is numbered exactly like its inline neighbour, and an item mixing the two is numbered once.</p>" +
    Row(
        "<td>" +
        "<ol style=\"margin: 0; padding-left: 2em\">" +
        "<li><p style=\"margin: 0\">A paragraph, so this item's content is block-level.</p></li>" +
        "<li>Ordinary inline content.</li>" +
        "<li><p style=\"margin: 0\">Another block-level item.</p></li>" +
        "</ol>" +
        "<div class=\"desc\">block-level item content</div>" +
        "<div class=\"css\">&lt;li&gt;&lt;p&gt;…&lt;/p&gt;&lt;/li&gt;</div>" +
        "</td>",
        "<td>" +
        "<ul style=\"margin: 0; padding-left: 2em\">" +
        "<li>Leading inline text<p style=\"margin: 0\">followed by a block.</p></li>" +
        "<li><div>A div, also block-level.</div></li>" +
        "</ul>" +
        "<div class=\"desc\">mixed inline and block content</div>" +
        "<div class=\"css\">&lt;li&gt;text&lt;p&gt;…&lt;/p&gt;&lt;/li&gt;</div>" +
        "</td>"
    ) +

    "<h2>6 — a marker whose item breaks across a column</h2>" +
    "<p class=\"intro\">A column is a fragmentainer like a page, so the same rule holds: an outside marker goes with the column its item <em>begins</em> in, not the one it happens to finish in. The middle item below runs past the end of the first column; its bullet stays beside its own first line, and it gets no second one where it resumes.</p>" +
    "<div style=\"column-count: 2; column-gap: 2em\">" +
    "<ul style=\"margin: 0; padding-left: 2em\">" +
    string.Join("", Enumerable.Range(1, 3).Select(i =>
        $"<li>Column item {i}, long enough that the list runs past the end of the first column. " +
        string.Join(" ", Enumerable.Range(0, 40).Select(w => $"word{w}")) +
        "</li>")) +
    "</ul>" +
    "</div>" +

    "<h2>7 — a marker whose item breaks across a page</h2>" +
    "<p class=\"intro\">An outside marker sits beside its item's first line, so it belongs to the page the item <em>starts</em> on even when the item's own text carries on onto the next one. The spacer below pushes the last item across the page boundary; its number must still appear, on the page its first line is on.</p>" +
    "<div style=\"height: 120pt\"></div>" +
    "<ol style=\"margin: 0; padding-left: 3em\">" +
    string.Join("", Enumerable.Range(1, 3).Select(i =>
        $"<li>Item {i} of a list long enough to be broken by the page boundary. " +
        string.Join(" ", Enumerable.Range(0, 90).Select(w => $"word{w}")) +
        "</li>")) +
    "</ol>" +

    "</body></html>";

await SaveShowcaseAsync("marker_styling", "Lists & Generated Content", "::marker Styling",
    "Styling list markers with the ::marker pseudo-element, including real per-item numbering via the list-item counter.",
    markerHtml, pdfConfig);

// --- list-style-type value coverage showcase ---

// Reuses SourceSans3 (already proven, via the font-variant-caps/bidi showcases above, to embed
// cleanly) rather than the module-level sourceSans3B64/notoHebrewB64 declared later in this script -
// those aren't in scope yet at this point in a top-level-statements file, which executes strictly in
// source order.
var listStyleTypeSourceSans3B64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")));
var listStyleTypeNotoHebrewB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansHebrewSubset.ttf")));

const string ListStyleTypeCss = """
    <style>
    @page { size: a4; margin: 15mm }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var listStyleTypeHtml = "<!DOCTYPE html><html><head>" +
    $"<style>@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{listStyleTypeSourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'Hebrew'; src: url('data:font/truetype;base64,{listStyleTypeNotoHebrewB64}') format('truetype'); }}" +
    "body { font: 8.5pt 'SS3', 'Hebrew', sans-serif; margin: 0 }</style>" +
    ListStyleTypeCss + "</head><body>" +

    "<h1>CSS list-style-type Value Coverage</h1>" +

    "<h2>1 — Symbolic markers (disclosure-open/-closed) and a literal &lt;string&gt; marker</h2>" +
    "<p class=\"intro\">disclosure-open/disclosure-closed are fixed, uncounted glyphs (every item gets " +
    "the same marker); a &lt;string&gt; value is literal marker text with no automatic suffix.</p>" +
    Row(
        ListSwatch("disclosure-open", "list-style-type: disclosure-open;"),
        ListSwatch("disclosure-closed", "list-style-type: disclosure-closed;"),
        // Single-quoted CSS string literal - ListSwatch wraps listCss in a double-quoted HTML style="..."
        // attribute, so a double-quoted CSS string here would prematurely close that attribute.
        ListSwatch("literal &lt;string&gt;", "list-style-type: '→ ';"),
        ListSwatch("none (baseline)", "list-style-type: none;")
    ) +

    "<h2>2 — Fixed styles, past their range</h2>" +
    "<p class=\"intro\">cjk-earthly-branch/cjk-heavenly-stem have a finite 12/10-symbol range; a " +
    "counter value beyond it falls back to plain decimal, per CSS Counter Styles Level 3.</p>" +
    Row(
        ListSwatch("cjk-earthly-branch (1-12)", "list-style-type: cjk-earthly-branch;"),
        ListSwatch("cjk-heavenly-stem (1-10)", "list-style-type: cjk-heavenly-stem;"),
        ListSwatch("cjk-decimal", "list-style-type: cjk-decimal;"),
        ListSwatch("ethiopic-numeric", "list-style-type: ethiopic-numeric;")
    ) +

    "<h2>3 — Hebrew: additive numbering, including the 15/16 override</h2>" +
    "<p class=\"intro\">15 and 16 are manually overridden to טו/טז instead of the " +
    "naive combination, which closely resembles the Tetragrammaton - list starts at item 14 to show " +
    "the transition, and now correctly reaches four digits (1000+) rather than silently dropping the " +
    "thousands place.</p>" +
    // font-family is set explicitly (Hebrew first) rather than relying on inherited per-codepoint
    // fallback through 'SS3' - avoids a PDF font-subsetting interaction with the many other special
    // glyphs (disclosure triangles, the string arrow) used earlier on this same page.
    "<ol style=\"margin: 0; padding-left: 2em; list-style-type: hebrew; font-family: 'Hebrew', 'SS3', sans-serif\" start=\"14\">" +
    string.Concat(Enumerable.Repeat("<li>Item</li>", 6)) +
    "</ol>" +
    "<div class=\"css\">list-style-type: hebrew; &lt;ol start=\"14\"&gt;</div>" +

    "<h2>4 — Additional numeric/alphabetic/fixed styles now supported</h2>" +
    "<p class=\"intro\">Every value below is verified by the automated test suite " +
    "(CommonUtilsTests/ListItemCounterIntegrationTests) against the CSS Counter Styles Level 3 spec " +
    "text. Rendering the actual script glyph (rather than this page's fallback font's notdef box) " +
    "needs a font covering that script registered via PdfGenerator.AddFontFromStream - see " +
    "docs/usage-examples.md#fonts - the same requirement any HTML renderer has for non-Latin text.</p>" +
    Row(
        ListSwatch("devanagari", "list-style-type: devanagari;"),
        ListSwatch("thai", "list-style-type: thai;"),
        ListSwatch("arabic-indic", "list-style-type: arabic-indic;"),
        ListSwatch("bengali", "list-style-type: bengali;")
    ) +
    Row(
        ListSwatch("lower-armenian", "list-style-type: lower-armenian;"),
        ListSwatch("upper-armenian", "list-style-type: upper-armenian;"),
        ListSwatch("hiragana-iroha", "list-style-type: hiragana-iroha;"),
        ListSwatch("katakana-iroha", "list-style-type: katakana-iroha;")
    ) +

    "</body></html>";

await SaveShowcaseAsync("list_style_type", "Lists & Generated Content", "List Style Type Coverage",
    "The full CSS Counter Styles Level 3 predefined-style coverage list-style-type now supports - numeric, fixed, symbolic, and additive systems, plus a literal <string> marker.",
    listStyleTypeHtml, pdfConfig);

// --- content image showcase ---

static string ContentSwatch(string desc, string contentValue, string pseudoElement = "before", string width = "40px", string height = "28px", string? cssLabel = null) =>
    "<td>" +
    $"<style>.ci-{pseudoElement}-{desc.GetHashCode() & 0x7FFFFFFF}::{ pseudoElement} {{ content: {contentValue}; display: inline-block; width: {width}; height: {height}; }}</style>" +
    $"<div class=\"ci-{pseudoElement}-{desc.GetHashCode() & 0x7FFFFFFF}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    // A data: URI content value can be thousands of unbroken base64 characters - showing it
    // verbatim here (as every other swatch does) isn't just unreadable, it's a real trigger for a
    // word-break:break-all measurement bug (a single unbreakable token that long inflates
    // ShrinkToFit's measured page width and squashes the whole page - see cssLabel callers below).
    $"<div class=\"css\">::{pseudoElement} {{ content: {cssLabel ?? contentValue} }}</div>" +
    "</td>";

// A hand-drawn vector peach (two overlapping gradient-filled circles form the characteristic
// cleft, plus a pair of small leaves) - used below to prove a url() content-image source can be
// an SVG, rendered as real vector content, on-brand for PeachPDF's own showcase.
var peachMarkup = """
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
      <defs>
        <radialGradient id="peachL" cx="30%" cy="30%" r="80%">
          <stop offset="0%" stop-color="#ffdca8"/>
          <stop offset="60%" stop-color="#ffab6b"/>
          <stop offset="100%" stop-color="#f4784a"/>
        </radialGradient>
        <radialGradient id="peachR" cx="35%" cy="30%" r="80%">
          <stop offset="0%" stop-color="#ffd0c2"/>
          <stop offset="60%" stop-color="#ff8f7d"/>
          <stop offset="100%" stop-color="#e85d4a"/>
        </radialGradient>
      </defs>
      <circle cx="12" cy="18" r="10" fill="url(#peachL)"/>
      <circle cx="20" cy="18" r="10" fill="url(#peachR)"/>
      <path d="M16 10 C14 5 10 3 7 4 C9 7 12 9 15 9 Z" fill="#5cb85c"/>
      <path d="M16 10 C18 5 22 3 25 4 C23 7 20 9 17 9 Z" fill="#4a9e4a"/>
    </svg>
    """;
var peachDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(peachMarkup));

// Regression: a <style> element nested in the document BODY (as every other swatch on this page
// uses, one per <td>) that defines a pseudo-element rule whose content resolves to a REAL loaded
// url() image (SVG or raster) previously corrupted ShrinkToFit's width measurement pass - a
// display:none box's content (the <style> tag's own hidden text, which is unbroken for thousands
// of base64 characters) was still being deep-scanned for the page's "longest word" during table
// column-width measurement. Now that display:none subtrees are correctly skipped, these rules are
// defined per-<td> like every other swatch on this page rather than hoisted into <head>.
static string PeachTd(string desc, string className, string peachDataUri, string elementHtml, string pseudoElement, string cssLabel, string extraStyle = "") =>
    "<td>" +
    $"<style>.{className}::{pseudoElement} {{ content: url('{peachDataUri}'); display: inline-block; width: 32px; height: 32px;{extraStyle} }}</style>" +
    elementHtml +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

static string ContentSwatchInline(string desc, string pseudoElement, string inlineCss, string? cssLabel = null) =>
    "<td>" +
    $"<style>.cci-{desc.GetHashCode() & 0x7FFFFFFF}::{pseudoElement} {{ {inlineCss} }}</style>" +
    $"<p class=\"cci-{desc.GetHashCode() & 0x7FFFFFFF}\">Text</p>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">::{pseudoElement} {{ {cssLabel ?? inlineCss} }}</div>" +
    "</td>";

const string ContentCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var contentHtml = "<!DOCTYPE html><html><head>" + ContentCss + "</head><body>" +

    "<h1>CSS content Image Test Page</h1>" +
    "<p class=\"intro\">Demonstrates url() and gradient functions in the CSS content property on ::before and ::after pseudo-elements. Image values require display: inline-block with explicit width/height.</p>" +

    "<h2>1 — ::before linear-gradient</h2>" +
    Row(
        ContentSwatch("to right red→blue", "linear-gradient(to right, red, blue)"),
        ContentSwatch("to bottom green→yellow", "linear-gradient(to bottom, green, yellow)"),
        ContentSwatch("45deg multi-stop", "linear-gradient(45deg, red, yellow, blue)"),
        ContentSwatch("hard-stop stripes", "linear-gradient(to right, red 50%, blue 50%)")
    ) +

    "<h2>2 — ::before radial-gradient</h2>" +
    Row(
        ContentSwatch("circle center", "radial-gradient(circle, white, navy)"),
        ContentSwatch("ellipse default", "radial-gradient(red, blue)"),
        ContentSwatch("at 30% 30%", "radial-gradient(circle at 30% 30%, yellow, orange, red)"),
        ContentSwatch("closest-side", "radial-gradient(closest-side circle at 50% 50%, lime, teal)")
    ) +

    "<h2>3 — ::before conic-gradient</h2>" +
    Row(
        ContentSwatch("default sweep", "conic-gradient(red, blue)"),
        ContentSwatch("pie chart", "conic-gradient(#e74c3c 0 25%, #3498db 25% 65%, #2ecc71 65% 100%)"),
        ContentSwatch("from 90deg", "conic-gradient(from 90deg, red, yellow, blue)"),
        ContentSwatch("color wheel", "conic-gradient(red, yellow, lime, cyan, blue, magenta, red)")
    ) +

    "<h2>4 — ::after gradients and URL fallback</h2>" +
    Row(
        ContentSwatch("::after linear", "linear-gradient(to bottom, purple, orange)", "after"),
        ContentSwatch("::after radial", "radial-gradient(circle, gold, crimson)", "after"),
        ContentSwatch("::after conic", "conic-gradient(red 0 33%, blue 33% 66%, green 66% 100%)", "after"),
        ContentSwatch("missing URL (no crash)", "url('nonexistent.png')")
    ) +

    "<h2>5 — Repeating variants</h2>" +
    Row(
        ContentSwatch("repeating-linear", "repeating-linear-gradient(45deg, red, blue 10px)"),
        ContentSwatch("repeating-radial", "repeating-radial-gradient(circle, red, blue 10px)"),
        ContentSwatch("repeating-conic", "repeating-conic-gradient(red 0 10deg, blue 10deg 20deg)"),
        ContentSwatch("linear baseline", "linear-gradient(to right, teal, pink)")
    ) +

    "<h2>6 — Mixed: text content regression and image side-by-side</h2>" +
    "<p class=\"intro\">Text content (string literals, counters) must still work correctly alongside the new image code path.</p>" +
    Row(
        ContentSwatchInline("text bullet", "before", "content: \"• \"; color: red;"),
        ContentSwatchInline("gradient + text", "before", "content: linear-gradient(to right, red, blue); display: inline-block; width: 40px; height: 20px;"),
        ContentSwatchInline("::after text", "after", "content: \" ★\"; color: orange;"),
        ContentSwatchInline("none (no output)", "before", "content: none;")
    ) +

    "<h2>7 — SVG url() content image</h2>" +
    "<p class=\"intro\">A url() content-image source can also be an SVG, rendered as real vector content (not rasterized) - same as background-image and list-style-image. Note the image paints at the SVG's own intrinsic viewBox size (32x32 here), independent of the box size reserved by display:inline-block's width/height.</p>" +
    Row(
        PeachTd("::before peach", "peach-before", peachDataUri, "<div class=\"peach-before\"></div>", "before", "::before { content: url('data:image/svg+xml;base64,…') }"),
        PeachTd("::after peach", "peach-after", peachDataUri, "<div class=\"peach-after\"></div>", "after", "::after { content: url('data:image/svg+xml;base64,…') }"),
        PeachTd("peach + text (::after)", "peach-inline", peachDataUri, "<p class=\"peach-inline\">Text</p>", "after", "::after { content: url('data:image/svg+xml;base64,…') }", " vertical-align: middle;"),
        ContentSwatch("missing SVG (no crash)", "url('nonexistent.svg')", "before", "32px", "32px")
    ) +

    "</body></html>";

await SaveShowcaseAsync("content_image", "Lists & Generated Content", "Generated Content Images",
    "Images inserted through the CSS content property on generated content.",
    contentHtml, pdfConfig);

// ─── quotes / open-quote / close-quote showcase ─────────────────────────────

var quotesHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    body { font: 12pt Georgia, serif; margin: 20px; }
    h2 { font: bold 14pt Arial, sans-serif; margin: 24px 0 8px; }
    p.intro { font: 10pt Arial, sans-serif; color: #555; }
    q { font-style: italic; }

    /* UA default: q::before { content: open-quote } / q::after { content: close-quote }
       needs no author CSS at all - PeachPDF's default stylesheet supplies it. */

    .custom-quotes {
      quotes: "\201C" "\201D" "\2018" "\2019"; /* “ ” ‘ ’ - selected by nesting depth */
    }
    .custom-quotes q::before { content: open-quote; }
    .custom-quotes q::after  { content: close-quote; }

    .no-quotes q { quotes: none; }
    .no-quotes q::before { content: open-quote; }
    .no-quotes q::after  { content: close-quote; }
    </style>
    </head>
    <body>

    <h2>1 — UA default (guillemets, no author CSS)</h2>
    <p class="intro">A bare &lt;q&gt; renders quote marks out of the box via PeachPDF's default stylesheet.</p>
    <p><q>The only thing we have to fear is fear itself.</q> — Franklin D. Roosevelt</p>

    <h2>2 — Nested quotes select a pair by depth (CSS 2.1 §12.3.1)</h2>
    <p class="intro">A custom <code>quotes</code> property with two pairs; the inner &lt;q&gt; automatically
    picks the second pair since it is one level deeper.</p>
    <p class="custom-quotes"><q>She said, <q>this is amazing</q>, and smiled.</q></p>

    <h2>3 — quotes: none suppresses the glyph but keeps tracking depth</h2>
    <p class="intro">open-quote/close-quote still resolve (so nesting depth stays correct for sibling
    content) but render no character.</p>
    <p class="no-quotes"><q>No visible marks around this sentence.</q></p>

    </body>
    </html>
    """;

await SaveShowcaseAsync("quotes", "Lists & Generated Content", "quotes & open-quote/close-quote",
    "The CSS 2.1 quotes property and the open-quote/close-quote/no-open-quote/no-close-quote " +
    "content keywords, including nesting-depth pair selection and the default q::before/::after UA styling.",
    quotesHtml, pdfConfig);

// ─── CSS Paged Media showcase ───────────────────────────────────────────────

var pagedMediaHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm 25mm 20mm;
      @top-left-corner { content: url('{{PEACH_DATA_URI}}'); }
      @top-left   { content: "Acme Corp"; font-size: 8pt; font-family: Arial; color: #555; }
      @top-center { content: "Annual Report 2025"; font-size: 8pt; font-family: Arial; font-weight: bold; }
      @top-right  { content: "Confidential"; font-size: 8pt; font-family: Arial; color: #cc0000; }
      @bottom-left   { content: "\A9 2025 Acme Corp"; font-size: 7pt; font-family: Arial; color: #888; }
      @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 8pt; font-family: Arial; }
      @bottom-right  { content: "Internal Use Only"; font-size: 7pt; font-family: Arial; color: #888; }
    }
    @page :first {
      @top-left   { content: none; }
      @top-center { content: none; }
      @top-right  { content: none; }
    }
    body { font: 10pt Arial, sans-serif; margin: 0; }
    h1 { font-size: 28pt; text-align: center; margin: 60pt 0 20pt; }
    h2 { font-size: 16pt; margin: 24pt 0 8pt; border-bottom: 1px solid #999; padding-bottom: 4pt; break-after: avoid }
    p  { margin: 0 0 8pt; line-height: 1.5; }
    .cover-subtitle { font-size: 14pt; text-align: center; color: #555; margin-bottom: 60pt; }
    .page-break { page-break-after: always; }
    </style>
    </head>
    <body>

    <!-- Cover page (page :first — no header) -->
    <div class="page-break">
      <h1>Annual Report 2025</h1>
      <p class="cover-subtitle">Acme Corporation — Confidential</p>
      <p style="text-align:center; color:#888; font-size:9pt;">
        This document demonstrates CSS Paged Media support in PeachPDF.<br/>
        The cover page has no running header; pages 2+ show header and footer.
      </p>
    </div>

    <!-- Page 2 -->
    <div class="page-break">
      <h2>Executive Summary</h2>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.</p>
      <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim.</p>
      <p>Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto beatae vitae dicta.</p>
      <p>Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit, sed quia consequuntur magni dolores eos qui ratione voluptatem sequi nesciunt. Neque porro quisquam est, qui dolorem ipsum.</p>
      <p>At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis praesentium voluptatum deleniti atque corrupti quos dolores et quas molestias excepturi sint occaecati cupiditate non provident.</p>
    </div>

    <!-- Page 3 -->
    <div>
      <h2>Financial Highlights</h2>
      <p>Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe eveniet ut et voluptates repudiandae sint et molestiae non recusandae.</p>
      <p>Itaque earum rerum hic tenetur a sapiente delectus, ut aut reiciendis voluptatibus maiores alias consequatur aut perferendis doloribus asperiores repellat.</p>
      <p>Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil impedit quo minus id quod maxime placeat facere possimus, omnis voluptas assumenda est, omnis dolor repellendus.</p>
      <p>Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse quam nihil molestiae consequatur, vel illum qui dolorem eum fugiat quo voluptas nulla pariatur.</p>
    </div>

    </body>
    </html>
    """.Replace("{{PEACH_DATA_URI}}", peachDataUri);

await SaveShowcaseAsync("paged_media", "Paged Media", "Paged Media",
    "@page rules with margin boxes, running headers and footers, and page counters, including a " +
    "url() logo image in a margin box (@top-left-corner).",
    pagedMediaHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Paged Media showcase — margin-box image alignment ─────────────────

// A margin box's image content follows the box's alignment exactly as text does (CSS Paged
// Media Level 3 §7.2): the default follows the box's position - @top-left flush left, @top-center
// centered, @top-right flush right - and an explicit text-align applies. The same peach logo is
// dropped into each of the three top boxes with no per-box width, so its horizontal placement is
// driven purely by alignment; the bottom row adds an explicit text-align: right override.
var marginImageAlignHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 22mm 20mm 22mm 20mm;
      @top-left    { content: url('{{PEACH_DATA_URI}}'); }
      @top-center  { content: url('{{PEACH_DATA_URI}}'); }
      @top-right   { content: url('{{PEACH_DATA_URI}}'); }
      @bottom-left { content: url('{{PEACH_DATA_URI}}'); text-align: right; }
      @bottom-right { content: "explicit text-align: right \2192"; font: 8pt Arial; color: #888; vertical-align: middle; }
    }
    body { font: 11pt Arial, sans-serif; color: #333; margin: 0; }
    h1 { font-size: 20pt; margin: 0 0 12pt; }
    p  { margin: 0 0 8pt; line-height: 1.6; }
    </style>
    </head>
    <body>
      <h1>Margin-box image alignment</h1>
      <p>The peach logo appears three times across the top margin: flush to the left in
      <code>@top-left</code>, centered in <code>@top-center</code>, and flush to the right in
      <code>@top-right</code> &mdash; each placed purely by the margin box's default alignment,
      with no explicit box width.</p>
      <p>In the bottom-left box the logo carries an explicit <code>text-align: right</code>, so it
      hugs the right edge of its box instead of its default left.</p>
    </body>
    </html>
    """.Replace("{{PEACH_DATA_URI}}", peachDataUri);

await SaveShowcaseAsync("margin_box_image_alignment", "Paged Media", "Margin-box Image Alignment",
    "An image in a @page margin box follows the box's alignment like text does: default from the " +
    "box position (@top-left/@top-center/@top-right) plus an explicit text-align override.",
    marginImageAlignHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Paged Media showcase — named strings (string-set / string()) ──────

var namedStringsHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm 25mm 20mm;
      @top-left   { content: string(chapter); font-size: 8pt; font-family: Arial; color: #333; font-style: italic; }
      @top-right  { content: string(section); font-size: 8pt; font-family: Arial; color: #555; }
      @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 8pt; font-family: Arial; }
    }
    body { font: 10pt Arial, sans-serif; margin: 0; }
    h1 { font-size: 20pt; margin: 0 0 12pt; string-set: chapter content(); }
    h2 { font-size: 13pt; margin: 18pt 0 6pt; string-set: section content(); break-after: avoid }
    p  { margin: 0 0 8pt; line-height: 1.5; }
    .page-break { page-break-after: always; }
    </style>
    </head>
    <body>

    <div class="page-break">
      <h1>Chapter 1: Introduction</h1>
      <h2>1.1 Background</h2>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. String-set captures the heading text and string() displays it in the top margin.</p>
      <p>Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. The chapter title appears top-left; the section title top-right.</p>
      <h2>1.2 Scope</h2>
      <p>Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.</p>
      <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.</p>
    </div>

    <div>
      <h1>Chapter 2: Methodology</h1>
      <h2>2.1 Approach</h2>
      <p>Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.</p>
      <p>At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis praesentium voluptatum deleniti.</p>
      <h2>2.2 Results</h2>
      <p>Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe eveniet ut et voluptates.</p>
      <p>Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil impedit quo minus id quod maxime.</p>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_named_strings", "Paged Media", "Named Strings",
    "Running headers populated from document content with string-set and string().",
    namedStringsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Paged Media showcase — running elements (position: running() / element()) ──

var runningElementsHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 30mm 20mm 25mm 20mm;
      @top-center { content: element(chapter-heading); }
      @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 8pt; font-family: Arial; color: #888; }
    }
    body { font: 10pt Arial, sans-serif; margin: 0; }
    h1 { font-size: 20pt; margin: 0 0 12pt; }
    h1.running {
      /* Removed from normal flow entirely - never appears here, only in @top-center. */
      position: running(chapter-heading);
      display: flex; align-items: center; gap: 8pt;
      font-size: 9pt; margin: 0;
    }
    h1.running .badge {
      background: #2563eb; color: #fff; font-weight: bold;
      border-radius: 3pt; padding: 2pt 7pt; font-size: 8pt;
    }
    h1.running .title { font-style: italic; color: #333; }
    p { margin: 0 0 8pt; line-height: 1.5; }
    .page-break { page-break-after: always; }
    </style>
    </head>
    <body>

    <div class="page-break">
      <h1 class="running"><span class="badge">Ch. 1</span><span class="title">Introduction</span></h1>
      <h1>Chapter 1: Introduction</h1>
      <p>Unlike string-set/string() (previous showcase), which captures plain text only, position: running()
      removes this whole heading from the flow and content: element() shows it in the margin box complete
      with its own formatting - the blue "Ch. 1" badge and italic title are real, laid-out elements, not
      captured strings.</p>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore
      et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.</p>
      <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla
      pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt.</p>
    </div>

    <div>
      <h1 class="running"><span class="badge">Ch. 2</span><span class="title">Methodology</span></h1>
      <h1>Chapter 2: Methodology</h1>
      <p>The running header updates to the new chapter's badge and title as soon as this heading is
      encountered - the same first/start/last/first-except selection rules string() already offers,
      applied to a whole element instead of a string.</p>
      <p>At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis praesentium voluptatum
      deleniti atque corrupti quos dolores et quas molestias excepturi sint occaecati.</p>
      <p>Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe eveniet ut et
      voluptates repudiandae sint et molestiae non recusandae itaque earum rerum hic tenetur.</p>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_running_elements", "Paged Media", "Running Elements",
    "Running headers with full formatting fidelity via position: running() and content: element() - unlike string-set/string(), the margin box shows a real, laid-out element (colors, badges, nested spans), not just captured text.",
    runningElementsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS GCPM showcase — footnotes (float: footnote) ──
// Demonstrates: multiple footnotes stacking on one page in document order, a page-boundary case
// where the dynamically-reserved footnote area shrinks how much ordinary flow content that page
// can hold, a break-inside:avoid paragraph correctly avoiding the reserved strip, per-page
// numbering reset, and author styling of ::footnote-call/::footnote-marker.
var footnotesHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm;
      @bottom-center { content: counter(page); font-size: 8pt; font-family: Arial; color: #888; }
      /* @footnote styles the note area itself - here, a heavier colored divider with extra
         room above and below it, instead of PeachPDF's default thin black rule. */
      @footnote {
        border-top: 2pt solid #2563eb;
        margin-top: 10pt;
        padding-top: 8pt;
      }
    }
    /* A named page for the continuous-numbering section: declaring counter-reset here at all is
       what replaces the UA sheet's own `counter-reset: footnote`, so nothing resets the counter and
       numbering runs on from the previous pages. height gives the note area a fixed band. */
    @page continuous {
      counter-reset: none;
      @footnote { height: 70pt; }
    }
    .continuous { page: continuous; }
    .cols { column-count: 2; column-gap: 24pt; column-fill: auto; height: 300pt; }
    .cols p { margin: 0 0 10pt; }
    body { font: 11pt Georgia, serif; margin: 0; color: #222; }
    h1 { font-size: 20pt; margin: 0 0 14pt; }
    p { line-height: 1.6; margin: 0 0 10pt; }
    .keep { break-inside: avoid; border-left: 3pt solid #2563eb; padding-left: 10pt; }
    .spacer { height: 640pt; }

    /* Author styling of the two footnote pseudo-elements - a colored, bracketed call and a
       bold, colored leading number in the note area, instead of the plain UA default. */
    ::footnote-call { color: #2563eb; font-weight: bold; }
    ::footnote-marker { color: #2563eb; font-weight: bold; }
    </style>
    </head>
    <body>

    <h1>Footnotes</h1>
    <p>float: footnote pulls an element out of normal flow entirely<span style="float:footnote">float: footnote is the only footnote float value css-gcpm-3 defines; inline-footnote is a PrinceXML extension, not a CSS feature.</span>,
    leaving a numbered in-flow reference behind and routing the element's own content to a note area
    at the bottom of the page the reference landed on<span style="float:footnote">The note area's height is reserved dynamically, based on how many footnotes actually land on a given page - not a fixed-height margin box.</span>.</p>

    <p>Because the reservation is dynamic, content already flowing down the page correctly stops
    above it even when a paragraph asks to stay together as one unit:</p>

    <div class="spacer"></div>

    <p class="keep">This paragraph carries break-inside: avoid. Once the footnote area above claims
    room at the foot of the page, this whole paragraph is kept together and, if it no longer fits,
    moves to the next page as a unit rather than splitting across the reserved strip<span style="float:footnote">A third footnote, to show the reservation composing across every footnote that lands on the same page.</span>.</p>

    <p>The footnote counter is a real, cascaded counter: the UA stylesheet declares
    @page { counter-reset: footnote } and @footnote { counter-increment: footnote }, so by default
    each page starts back at 1.</p>

    <div style="break-before: page;">
    <p>A fresh page, a fresh footnote<span style="float:footnote">This is footnote 1 again, not 4 - the UA stylesheet's own per-page counter-reset is what does that.</span>.</p>
    </div>

    <div style="break-before: page;" class="continuous">
    <h1>Continuous numbering, and a fixed note-area height</h1>
    <p>Declaring counter-reset yourself on an applicable @page replaces the UA declaration outright,
    so a set that never mentions the footnote counter - counter-reset: none here - leaves nothing to
    reset it and numbering simply runs on through the document. This page also declares
    @footnote { height: 70pt }, a fixed band for the bodies rather than one sized to them: the
    divider sits at the same place whatever lands below it, and the slack falls under the last
    note.</p>
    <p>These two notes carry on from wherever the counter already stood instead of restarting at
    1<span style="float:footnote">Numbered by the document-wide counter, because this page's own
    counter-reset never names it - the previous page ended at 1, so these are 2 and 3.</span>. The
    number reaching the page is a real counter value, so content: counter(footnote, lower-roman) on
    ::footnote-call renders it as a roman numeral, and counter-increment on @footnote changes the
    step<span style="float:footnote">Both the call and the marker resolve counter(footnote) to the
    live, pagination-resolved value.</span>.</p>
    </div>

    <div style="break-before: page;">
    <h1>A block-level source</h1>
    <p>The element carrying float: footnote can be block-level too. It is replaced by the numbered call, which
    is inline, so among block siblings the call gets a line of its own.</p>
    <div style="float:footnote">This note came from a &lt;div&gt; with float: footnote, so its call stands on its own line between the paragraphs.</div>
    <p>The paragraph after the source carries on below that call, and the note sits at the foot of the page like any other.</p>
    </div>

    <div style="break-before: page;">
    <h1>Column-scoped notes</h1>
    <p>float-reference: column (CSS Page Floats) routes a note to the bottom of the column its own
    reference landed in, rather than the bottom of the page. Each column gets its own divider, its own
    width and its own reserved strip; the notes are still numbered across the page, because
    float-reference decides placement and says nothing about counters.</p>
    <div class="cols">
    <p>The first column carries this note<span style="float:footnote; float-reference:column">Scoped to
    the first column, so it sits at that column's own foot.</span>, and the column's own content stops
    above the strip that note reserves rather than running into it.</p>
    <p>__COLUMN_FILLER__</p>
    <p>The second column carries its own<span style="float:footnote; float-reference:column">Scoped to
    the second column - a separate area, with its own divider, beside the first rather than below
    it.</span>, numbered 2 because numbering runs across the page.</p>
    </div>
    </div>

    <div style="break-before: page;">
    <h1>footnote-display: compact</h1>
    <p>footnote-display, set on the float: footnote source element itself, controls how a footnote's
    body stacks in the note area. compact places a short body inline, packed beside the previous one,
    and gives a body that needs more than one line its own full-width row instead:</p>
    <p>A short note<span style="float:footnote; footnote-display: compact;">Short.</span>, another
    short note<span style="float:footnote; footnote-display: compact;">Also short.</span>, then a much
    longer one<span style="float:footnote; footnote-display: compact;">This footnote's own text is
    deliberately long enough that it wraps onto more than one line at the page's full content width,
    so compact falls back to giving it a full-width row of its own rather than trying to pack it
    beside its neighbors.</span>, and a short one again<span style="float:footnote; footnote-display: compact;">Short once more.</span>.</p>
    </div>

    <div style="break-before: page;">
    <h1>footnote-policy: block and line</h1>
    <p>footnote-policy controls what happens when a page's note area can't hold everything that landed
    on it - here, because the note body itself is long enough that its note area alone would exceed
    this whole page's content band. block forces the whole paragraph carrying the call onto the next
    page; line forces only the specific line carrying the call, leaving earlier lines of the same
    paragraph on this page:</p>

    <p class="keep" id="policy-block">This paragraph's own footnote-policy is block<span
    style="float:footnote; footnote-policy: block;">__BLOCK_NOTE_FILLER__ Because
    footnote-policy: block is set, the whole paragraph carrying this call - not just this line - moves
    to the next page so the call and its note land together there instead.</span>, so once its note
    doesn't fit, the whole paragraph moves to the next page.</p>

    <p id="policy-line">This paragraph is long enough to wrap across several lines before reaching its
    own footnote reference, which is exactly the point: footnote-policy: line only pushes the specific
    line carrying the call<span style="float:footnote; footnote-policy: line;">__LINE_NOTE_FILLER__
    Because footnote-policy: line is set, only the line carrying this call (and whatever follows it in
    the same paragraph) moves onward - the lines before it, earlier in this same paragraph, stay right
    here.</span> onward, while every line before it - including the start of this very paragraph - stays
    exactly where it already was.</p>
    </div>

    </body>
    </html>
    """;

// The note bodies above need to genuinely exceed a whole A4 page's content band on their own (not
// just be long) to actually demonstrate footnote-policy forcing a break - a handful of sentences
// comfortably fits a page, so this repeats a filler sentence enough times to guarantee real overflow.
var footnoteOverflowFiller = string.Concat(Enumerable.Repeat(
    "This note's own body text is repeated enough times that its note area alone is taller than this whole page's content band. ",
    25));
// Enough prose to push the second reference into the second column, so the two column-scoped notes
// genuinely land in different columns rather than both in the first.
var columnFiller = string.Concat(Enumerable.Repeat(
    "Ordinary column prose, repeated to fill the first column so that what follows begins the second. ",
    12));

footnotesHtml = footnotesHtml
    .Replace("__BLOCK_NOTE_FILLER__", footnoteOverflowFiller)
    .Replace("__LINE_NOTE_FILLER__", footnoteOverflowFiller)
    .Replace("__COLUMN_FILLER__", columnFiller);

await SaveShowcaseAsync("paged_media_footnotes", "Paged Media", "Footnotes",
    "css-gcpm-3's float: footnote: a numbered in-flow reference, a note area whose height is reserved dynamically per page based on how many footnotes land there, break-inside: avoid content correctly kept clear of the reserved strip, an @footnote rule styling the note area's own divider and giving it a fixed height, a real cascaded footnote counter (continuous numbering via @page counter-reset), column-scoped areas via float-reference: column, footnote-display: compact packing short notes onto one row, and footnote-policy: block/line forcing a page break when a note doesn't fit.",
    footnotesHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Page Floats showcase — float: top/bottom/top-bottom/snap/inside/outside ──
// Demonstrates: a float: top figure landing flush at the true top of the page its source position
// falls on, with ordinary flow content starting below the reserved strip rather than overlapping it;
// a float: bottom callout landing flush at the true bottom, with flow content stopping above it;
// float: top-bottom falling back to the bottom edge once the top edge has no room left; and
// inside/outside resolving to opposite physical sides on a right-hand versus a left-hand page.
var pageFloatsHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm;
      @bottom-center { content: counter(page); font-size: 8pt; font-family: Arial; color: #888; }
    }
    body { font: 11pt Georgia, serif; margin: 0; color: #222; }
    h1 { font-size: 20pt; margin: 0 0 14pt; }
    p { line-height: 1.6; margin: 0 0 10pt; }
    .figure {
      float: top;
      border: 1pt solid #2563eb;
      background: #eff6ff;
      padding: 10pt;
      margin: 0 0 12pt;
      font-size: 9pt;
    }
    .callout {
      float: bottom;
      border: 1pt solid #b45309;
      background: #fffbeb;
      padding: 10pt;
      margin: 12pt 0 0;
      font-size: 9pt;
    }
    .side-note {
      width: 140pt;
      border: 1pt solid #15803d;
      background: #f0fdf4;
      padding: 8pt;
      font-size: 9pt;
      margin: 0 0 10pt;
    }
    </style>
    </head>
    <body>

    <h1>Page floats</h1>
    <p>css-page-floats extends float past the inline left/right edges to the page itself.
    float: top below pins this figure to the true top edge of whichever page its source position
    lands on - ordinary flow content, including this paragraph, starts below the reserved strip
    rather than overlapping it.</p>

    <div class="figure">float: top - pinned to this page's top edge, not to where it appears in the
    source order.</div>

    <p>Because the reservation is dynamic (the same mechanism float: footnote already uses for its
    note area), the figure's own height is measured once and fed back in, so the flow content below
    it always starts exactly where the figure ends.</p>

    <div class="callout">float: bottom - pinned to this page's bottom edge. Flow content above stops
    short of the reserved strip instead of running into it.</div>

    <div style="break-before: page;">
    <h1>top-bottom: falls back once the top has no room</h1>
    <p>float: top-bottom tries the top edge first; once an earlier float has already claimed the room
    there, a later one falls back to the bottom edge instead:</p>
    <div style="float:top; border:1pt solid #2563eb; background:#eff6ff; padding:8pt; height:480pt; font-size:9pt;">
    float: top - a tall figure that claims most of the page's top-reservable room.</div>
    <div style="float:top-bottom; border:1pt solid #7c3aed; background:#f5f3ff; padding:8pt; font-size:9pt;">
    float: top-bottom - with no room left at the top, this one lands at the bottom edge instead.</div>
    <p>The two floats above never overlap: the second one's own height was measured, found not to fit
    in what remained at the top, and reserved from the bottom edge instead.</p>
    </div>

    <div style="break-before: page;">
    <h1>inside / outside on a right-hand (recto) page</h1>
    <p>This is an odd-numbered page - the same "recto" side @page :right selects. inside floats to the
    edge nearest the spine (left, here); outside floats to the far edge (right):</p>
    <div class="side-note" style="float:inside;">float: inside - nearest the spine on this recto page.</div>
    <div class="side-note" style="float:outside;">float: outside - the far edge on this recto page.</div>
    <p style="clear:both;">Turning the page flips which physical side each one resolves to, since both
    are relative to the binding rather than a fixed left or right.</p>
    </div>

    <div style="break-before: page;">
    <h1>inside / outside on a left-hand (verso) page</h1>
    <p>This is an even-numbered page - inside and outside now resolve to the opposite physical sides
    from the recto page before it:</p>
    <div class="side-note" style="float:inside;">float: inside - now on the right, nearest the spine on
    this verso page.</div>
    <div class="side-note" style="float:outside;">float: outside - now on the left, the far edge on this
    verso page.</div>
    </div>

    <div style="break-before: page;">
    <h1>float-reference: column</h1>
    <p>Inside a two-column container a page float can name the column instead of the page. Only the
    column holding the float gives up room: the left column below keeps its full height and starts at
    the top of the band, while the right column starts under its own float-reference: column figure.</p>
    <div style="column-count: 2; column-gap: 24pt;">
      <div class="figure" style="float-reference: column; float: bottom; margin: 12pt 0 0;">float: bottom, float-reference: column - flush with the foot of the left column, which stops above it.</div>
      <p>Column one. This paragraph is anchored in the left column, so the callout belongs to that column and to no other: its strip is reserved at the left column's foot and the right column is unaffected by it.</p>
      <p>The left column keeps flowing down to the callout. Every line up to the callout is in the left column's own band.</p>
      <p>More left-column text, so that the flow reaches the right column: the container balances its columns, and the room each float takes is part of the balance.</p>
      <p>Still the left column, until the balance moves the flow across.</p>
      <div class="figure" style="float-reference: column; margin: 0 0 12pt;">float: top, float-reference: column - pinned to the top of the right column, whose text starts below it.</div>
      <p>Right column. This paragraph follows the second float in the source, so it is anchored in the right column and the figure sits at that column's top edge, not the page's.</p>
      <p>Right column. The left column beside it began at the top of the band and is untouched by this float.</p>
      <p>Right column. A little more text so that the two columns are of a similar length.</p>
    </div>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_page_floats", "Paged Media", "Page floats",
    "css-page-floats' float: top/bottom/top-bottom/snap/inside/outside: a float: top figure landing flush at the true top of its landing page with flow content starting below the reserved strip, a float: bottom callout landing flush at the true bottom with flow content stopping above it, float: top-bottom falling back to the bottom edge once the top edge has no room left, inside/outside resolving to opposite physical sides depending on whether the landing page is a right-hand (recto) or left-hand (verso) page, and float-reference: column pinning a float to the edge of the column its anchor sits in so only that column gives up room.",
    pageFloatsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── Scroll containers across page breaks ───────────────────────────────────
// An auto-height overflow: hidden/auto box has nothing to clip on paper, so it breaks between its lines
// like any block (css-break-3 §2 only permits treating it as monolithic), and so does one capped only by
// max-height. One with a fixed height stays whole. The DRAFT stamp is declared last but positioned against the first page's area, and is drawn there.
var scrollContainerCodeLines = string.Join("\n", Enumerable.Range(1, 34).Select(i =>
    $"{i,2}  " + (i % 5) switch
    {
        0 => "return total;",
        1 => "var total = 0;",
        2 => "foreach (var line in invoice.Lines)",
        3 => "    total += line.Quantity * line.UnitPrice;",
        _ => "// apply discounts and taxes per line",
    }));

var scrollContainersAcrossPagesHtml = $$"""
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: 105mm 148mm;
      margin: 12mm 10mm;
      @bottom-center { content: "Page " counter(page); font-size: 7pt; font-family: Arial; color: #888; }
    }
    body { font-family: Arial, sans-serif; font-size: 8.5pt; line-height: 1.35; margin: 0; color: #1f2937; }
    h1 { font-size: 12pt; margin: 0 0 6pt; padding-right: 48pt; }
    h2 { font-size: 10pt; margin: 10pt 0 4pt; }
    pre {
      overflow: auto;
      background: #f3f4f6;
      border: 0.75pt solid #9ca3af;
      padding: 6pt;
      font-size: 7.5pt;
      line-height: 1.3;
      margin: 0;
    }
    .panel {
      overflow: hidden;
      border: 0.75pt solid #2563eb;
      background: #eff6ff;
      padding: 4pt 8pt;
    }
    .panel p { margin: 0 0 4pt; }
    .capped {
      overflow: auto;
      height: 170pt;
      border: 0.75pt solid #b45309;
      background: #fffbeb;
      padding: 4pt 8pt;
    }
    .layout { overflow: hidden; border-top: 0.75pt solid #16a34a; padding-top: 4pt; }
    .menu { float: left; width: 62pt; background: #f0fdf4; border: 0.75pt solid #16a34a; padding: 4pt; }
    .article { margin-left: 74pt; }
    .article p { margin: 0 0 4pt; }
    .note { float: right; width: 110pt; margin: 0 0 4pt 8pt; background: #fff7ed; border: 0.75pt solid #ea580c; padding: 4pt; }
    .stamp {
      position: absolute;
      top: 0;
      right: 0;
      border: 1.5pt solid #dc2626;
      color: #dc2626;
      font-weight: bold;
      font-size: 9pt;
      padding: 2pt 6pt;
    }
    </style>
    </head>
    <body>
    <h1>Scroll containers across page breaks</h1>
    <p>A box with <code>overflow: auto</code> or <code>hidden</code> and no height of its own grows with its
    content, so on paper it has nothing to clip. It breaks between its lines like any other block, as it
    does when a browser prints it, instead of being sliced with a line lost at every page edge.</p>

    <h2>A code listing with overflow: auto</h2>
    <pre>{{scrollContainerCodeLines}}</pre>

    <h2>An overflow: hidden panel</h2>
    <div class="panel">
    <p>Every paragraph in this panel is drawn whole on one page or the next. The border and background
    are sliced at the page edge, as box-decoration-break: slice does for any block.</p>
    <p>Before this change the panel would have been laid out in one piece and cut into page-sized slices,
    and the line on each cut drawn on neither page.</p>
    <p>A wrapper holding absolutely positioned boxes or a multi-column, flex or grid layout still stays in
    one piece, because those parts cannot yet continue on the next page.</p>
    <p>Add break-inside: avoid to keep a short panel together instead.</p>
    <p>This is the case the clearfix idiom produces most often: a long, auto-height wrapper whose only job
    is to establish a new block formatting context, with ordinary paragraphs inside it.</p>
    <p>Its height grows with its content, so there is nothing it can clip in the block axis, and the page
    edge simply falls between two of its lines.</p>
    <p>The last paragraphs continue on the next page, still inside the same blue panel.</p>
    </div>

    <p>A box whose own height is fixed is different. With a height and no max-height it can scroll or
    clip what it holds, so CSS Fragmentation lets it be treated as monolithic content, like an image: it
    is never broken between its lines. Where it would straddle a page boundary, it is carried to the next
    page whole. The box below starts low enough on its page that it would straddle one. A box capped only
    by max-height breaks like the panel above, as a browser prints it, as long as its content fits under
    the cap.</p>

    <h2>A fixed-height box stays whole</h2>
    <div class="capped">
    <p>This box has overflow: auto and height: 170pt, so it is treated as monolithic: it moves whole
    to the next page when it does not fit where it starts, rather than breaking.</p>
    <p>It starts too close to the foot of the page for all of its paragraphs, so the whole box, border
    and all, has moved to the top of this page instead of leaving its first lines behind.</p>
    <p>An auto-height box in the same place would have broken between two of these paragraphs.</p>
    <p>To keep a short auto-height box together as well, give it break-inside: avoid.</p>
    </div>

    <h2>A floated menu inside a clearfix wrapper</h2>
    <div class="layout">
    <div class="menu"><b>Contents</b><br>Introduction<br>Method<br>Results<br>Discussion</div>
    <div class="article">
    <p>This wrapper has overflow: hidden only to contain the floated menu on its left, the way many web
    page layouts are built. The text column beside the menu is long enough to cross the page edge.</p>
    <p>The menu is laid out in one piece, so the wrapper can still break between the lines of the text
    column: every paragraph is drawn, on this page or the next, with none lost at the page edge.</p>
    <p>Before this change the whole wrapper was kept in one piece and cut into page-sized slices, and the
    line on each cut was drawn on neither page.</p>
    <p>Text beside a float is laid out against the float's final position, so the lines that wrap past the
    menu's foot use the full width of the column.</p>
    <p>A menu short enough to fit on one page is never split: if it would straddle the page edge it moves
    to the next page whole, as the note further down does.</p>
    <p>The text column continues here, on the next page, still inside the same wrapper, and ends it.</p>
    </div>
    </div>

    <h2>A float moves whole</h2>
    <p>The orange note below is floated to the right. It starts too close to the foot of its page to fit
    there, but it fits on one page, so it moves whole to the top of the next page instead of being split
    across the page edge. A float taller than a page is drawn across the pages instead, a slice on each.</p>
    <div class="note"><b>Note</b><br>This floated note has moved whole to the top of this page rather than
    being split across the page edge.</div>
    <p>The text after the note does not wait for it. It fills the rest of the note's first page at full
    width, and the text that reaches the next page wraps around the note there, until it passes the
    note's foot and uses the full width again. A browser printing the same page lays it out the same way.</p>
    <p>A later float is never placed above an earlier one, so a second note after this one would follow it
    onto the next page rather than stay behind. Nothing is drawn twice at the page boundary, and nothing
    is lost there.</p>

    <p>The DRAFT stamp at the top right of the first page is written at the very end of this document.
    It has no positioned ancestor, so it is placed against the first page's area and drawn there, and the
    paragraph after it is not moved by it.</p>
    <div class="stamp">DRAFT</div>
    <p>End of document.</p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("scroll_containers_across_pages", "Paged Media", "Scroll Containers Across Pages",
    "An auto-height overflow: auto code listing and an overflow: hidden panel breaking cleanly between their lines across page boundaries, a fixed-height overflow: auto box moving whole to the next page instead, a clearfix wrapper around a floated menu breaking between the lines of the text beside it, a floated note moving whole to the next page with the text wrapping around it there, and an absolutely positioned DRAFT stamp declared at the end of the document drawn on the first page.",
    scrollContainersAcrossPagesHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Content Module 3 showcase — target-counter()/target-text()/leader() ──
// The classic hand-authored table of contents: leader() fills the gap between a chapter
// title and its page number with a dotted rule, and target-counter(attr(href), page)
// resolves to the REAL page each chapter lands on after layout/pagination - not a stale,
// hand-typed number. The second TOC list below splits the leader and page number across
// two separate <a> elements rather than one, to demonstrate that leader() sees the whole
// line (every box on it), not just its own element's content - the fill still lines up
// correctly against text from a completely different sibling element.
var targetCounterHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm;
      @bottom-center { content: counter(page); font-size: 8pt; font-family: Arial; color: #888; }
    }
    body { font: 11pt Georgia, serif; margin: 0; color: #222; }
    h1 { font-size: 22pt; margin: 0 0 18pt; }
    h2 { font-size: 10pt; text-transform: uppercase; letter-spacing: 0.05em; color: #888; margin: 22pt 0 6pt; }
    p { line-height: 1.6; margin: 0 0 10pt; }
    p.note { font-size: 9pt; color: #888; }

    .toc { list-style: none; margin: 0; padding: 0; }
    .toc li { margin: 0 0 8pt; }
    .toc a { color: #222; text-decoration: none; }
    .toc a.entry::after {
      /* One declaration: leader and page number both live on the same <a>'s ::after. */
      content: leader(dotted) " " target-counter(attr(href), page);
      color: #888;
    }

    .toc-split li { display: block; }
    .toc-split .fill::after { content: leader(dotted); color: #888; }
    .toc-split .pagenum::after { content: target-counter(attr(href), page); color: #888; }

    .xref li { margin: 0 0 6pt; }
    .xref-label { color: #555; }
    .xref-content::after { content: target-text(attr(href)); font-weight: bold; }
    .xref-badge::after { content: target-text(attr(href), before); font-weight: bold; color: #2563eb; }
    .xref-mark::after { content: target-text(attr(href), after); font-weight: bold; color: #2563eb; }
    .xref-letter::after { content: target-text(attr(href), first-letter); font-weight: bold; color: #2563eb; }

    .chapter { break-before: page; }
    .chapter h1 { border-bottom: 2pt solid #2563eb; padding-bottom: 6pt; }
    #ch1-title::after { content: " (revised)"; }
    #ch3-title::before { content: "Featured: "; }
    </style>
    </head>
    <body>

    <h1>Table of Contents</h1>
    <ul class="toc">
      <li><a class="entry" href="#ch1">Chapter One: Introduction</a></li>
      <li><a class="entry" href="#ch2">Chapter Two: Methodology</a></li>
      <li><a class="entry" href="#ch3">Chapter Three: Results</a></li>
    </ul>

    <h2>Same idiom, split across two elements</h2>
    <p class="note">leader() distributes the line's remaining width across every leader on it,
    including a later sibling element's content it never itself declared - here the fill and the
    page number are two separate &lt;a&gt; elements, not one.</p>
    <ul class="toc toc-split">
      <li><a href="#ch1">Chapter One</a><a class="fill" href="#ch1"></a><a class="pagenum" href="#ch1"></a></li>
      <li><a href="#ch2">Chapter Two</a><a class="fill" href="#ch2"></a><a class="pagenum" href="#ch2"></a></li>
      <li><a href="#ch3">Chapter Three</a><a class="fill" href="#ch3"></a><a class="pagenum" href="#ch3"></a></li>
    </ul>

    <h2>Cross-references (target-text() modes)</h2>
    <p class="note">target-text(&lt;target&gt;[, content | before | after | first-letter]) pulls text from
    another element without retyping it: the default content mode copies the target's own text, before/after
    copy that element's generated ::before/::after content, and first-letter copies just its opening
    character. Chapter One's heading carries a generated "(revised)" suffix via ::after and Chapter Three's
    carries a "Featured:" badge via ::before - both pulled here purely through target-text(), not duplicated
    by hand.</p>
    <ul class="xref">
      <li><span class="xref-label">Chapter One is titled</span> "<a class="xref-content" href="#ch1-title"></a>"</li>
      <li><span class="xref-label">Chapter One's heading ends with</span> <a class="xref-mark" href="#ch1-title"></a></li>
      <li><span class="xref-label">Chapter Two's heading starts with the letter</span> <a class="xref-letter" href="#ch2-title"></a></li>
      <li><span class="xref-label">Chapter Three is flagged</span> <a class="xref-badge" href="#ch3-title"></a></li>
    </ul>

    <div class="chapter" id="ch1">
      <h1 id="ch1-title">Chapter One: Introduction</h1>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut
      labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris
      nisi ut aliquip ex ea commodo consequat.</p>
      <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla
      pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt
      mollit anim id est laborum.</p>
    </div>

    <div class="chapter" id="ch2">
      <h1 id="ch2-title">Chapter Two: Methodology</h1>
      <p>At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis praesentium
      voluptatum deleniti atque corrupti quos dolores et quas molestias excepturi sint occaecati
      cupiditate non provident.</p>
      <p>Similique sunt in culpa qui officia deserunt mollitia animi, id est laborum et dolorum fuga.
      Et harum quidem rerum facilis est et expedita distinctio.</p>
      <p>Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil impedit quo minus id quod
      maxime placeat facere possimus, omnis voluptas assumenda est omnis dolor repellendus.</p>
    </div>

    <div class="chapter" id="ch3">
      <h1 id="ch3-title">Chapter Three: Results</h1>
      <p>Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe eveniet ut
      et voluptates repudiandae sint et molestiae non recusandae itaque earum rerum hic tenetur a
      sapiente delectus.</p>
      <p>Ut aut reiciendis voluptatibus maiores alias consequatur aut perferendis doloribus asperiores
      repellat.</p>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("target_counter_toc", "Paged Media", "Table of Contents & Cross-References (target-counter / target-text / leader)",
    "A hand-authored table of contents with real, non-stale page numbers - leader(dotted) fills the gap and target-counter(attr(href), page) resolves the actual page each chapter lands on after pagination, including split across separate elements on the same line. A cross-reference list demonstrates all four target-text() modes: content, before, after, and first-letter.",
    targetCounterHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Per-page margin variation showcase ─────────────────────────────────────
// Per-page top/bottom margin overrides are layout-affecting (CSS Paged Media 3 page-box
// model): each page's own margins define its content band, so the flowing text genuinely
// breaks at different heights per page - the deep-margined first page holds visibly fewer
// paragraphs, mirrored :left/:right pages trade extra top space for extra bottom space,
// and the running furniture follows each page's own margins. The :first margins use
// relative units (% of the page width, em against the root font) - per-page rules resolve
// them against the same bases the base rule uses (#150), not just absolute lengths.
var perPageMarginsHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 20mm;
        @top-center { content: "Running Header"; font: bold 9pt Arial; }
        @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; }
    }
    @page :first {
        margin-top: 38%;      /* of the page width: ~80mm on A4 */
        margin-bottom: 10em;  /* against the root font size */
        @top-center { content: none; }
    }
    @page :left  { margin-top: 45mm; margin-bottom: 15mm; }
    @page :right { margin-top: 15mm; margin-bottom: 45mm; }
    body { font: 11pt Arial; }
    h1 { font-size: 22pt; text-align: center; margin: 0 0 4mm; }
    .note { text-align: center; font-size: 10pt; color: #555; }
    </style>
    </head>
    <body>
      <h1>Per-Page Margin Variation</h1>
      <p class="note">Every page's own @page margins define its content band: the first page's deep
      margins (declared in relative units - 38% of the page width on top, 10em on the bottom) fit
      only a few paragraphs, then :right pages (15mm top / 45mm bottom) and :left pages (45mm top /
      15mm bottom) alternate mirrored bands - watch where the text starts, where it breaks, and how
      many paragraphs fit on each page.</p>
    """ +
    string.Concat(Enumerable.Range(1, 60).Select(i =>
        $"<p>Paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>")) +
    """
    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_per_page_margins", "Paged Media", "Per-page Margins",
    "Layout-affecting per-page margins: each page's own @page margins define its content band, so text flows into visibly different-height pages (deep first page, mirrored :left/:right bands) - with relative units (%, em) supported in per-page rules too.",
    perPageMarginsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Per-page horizontal reflow showcase (#143) ─────────────────────────────
// Left/right per-page margins are layout-affecting too: each page's own margins define its
// content-box WIDTH, so main-column text re-wraps to that page's own measure (CSS Paged Media 3:
// "the edges of the page area act as a containing block for the layout that occurs between page
// breaks") - not merely shifted at paint time. This is the classic binding-gutter case: mirrored
// :left/:right margins put the wide gutter on the inside edge of each leaf, and the justified
// body text genuinely re-flows to each page's own (different) measure - the right margin of every
// line lands on that page's own content edge. A wider :first page shows the widest measure of all.
var perPageReflowHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 25mm 20mm;
        @bottom-center { content: "Page " counter(page); font: 8pt Arial; color: #888; }
    }
    /* Mirrored binding gutters: the wide margin sits on the inner (spine) edge of each leaf. */
    @page :right { margin-left: 18mm; margin-right: 48mm; }   /* odd pages */
    @page :left  { margin-left: 48mm; margin-right: 18mm; }   /* even pages */
    @page :first { margin-left: 12mm; margin-right: 55mm; }   /* widest measure of the set */
    body { font: 11pt Georgia, serif; text-align: justify; }
    h1 { font-size: 20pt; text-align: center; margin: 0 0 5mm; }
    .note { font-style: italic; color: #555; }
    </style>
    </head>
    <body>
      <h1>Per-Page Horizontal Reflow</h1>
      <p class="note">Each page below has a different left/right margin (mirrored binding gutters),
      so the justified body text re-wraps to that page's own measure - the right edge of every line
      aligns to the page's own content edge, wider or narrower, not a single fixed column.</p>
    """ +
    string.Concat(Enumerable.Range(1, 70).Select(i =>
        $"<p>Paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do " +
        "eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, " +
        "quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.</p>")) +
    """
    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_horizontal_reflow", "Paged Media", "Per-page Reflow",
    "Left/right per-page margins reflow content to each page's own width: mirrored :left/:right binding gutters re-wrap the justified body text to each page's own measure (CSS Paged Media page-area containing block), not just shift it.",
    perPageReflowHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Full-bleed page showcase ───────────────────────────────────────────────
// The headline capability behind layout-affecting per-page margins: a `margin: 0` first
// page whose content band is the entire physical sheet. The cover plate is sized to the
// full A4 sheet (210mm x 297mm) and paints to all four paper edges - corner registration
// marks prove every corner carries ink - while the forced break lands the second page back
// on ordinary 20mm margins with its running footer.
var fullBleedHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 20mm;
        @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; }
    }
    @page :first {
        margin: 0;
        @bottom-center { content: none; }
    }
    body { font: 11pt Arial; margin: 0; }
    /* Corner registration marks: ink in all four sheet corners proves true 4-edge bleed.
       Drawn as no-repeat background strips (two per corner) layered over the plate
       gradients, so every corner of the physical sheet demonstrably carries paint. */
    .cover {
      width: 210mm;
      height: 297mm;
      background-image:
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        radial-gradient(circle at 20% 15%, rgba(255, 255, 255, 0.18), transparent 45%),
        linear-gradient(155deg in oklch, #0f2b46, #1d5c8a 55%, #2e8bc0);
      background-position: top left, top left, top right, top right,
        bottom left, bottom left, bottom right, bottom right, center, center;
      background-size: 14mm 3mm, 3mm 14mm, 14mm 3mm, 3mm 14mm,
        14mm 3mm, 3mm 14mm, 14mm 3mm, 3mm 14mm, auto, auto;
      background-repeat: no-repeat;
      color: #f2f7fb; text-align: center;
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10mm;
      page-break-after: always;
    }
    .cover h1 { font-size: 30pt; letter-spacing: 6pt; word-spacing: 10pt; text-transform: uppercase; font-weight: normal; margin: 0; }
    .cover p { margin: 0; letter-spacing: 2pt; color: rgba(242, 247, 251, 0.75); }
    h2 { margin-top: 0; }
    </style>
    </head>
    <body>
    <div class="cover">
      <h1>Full Bleed</h1>
      <p>@page :first &#123; margin: 0 &#125; &mdash; the content band is the whole sheet</p>
      <p>Corner marks touch all four paper edges</p>
    </div>
    <h2>Back to ordinary margins</h2>
    <p>The cover's forced break lands this page on the base 20mm margins, with the running
    footer restored. Per-page top and bottom margins are layout-affecting: each page's own
    @page margins define its content band, so an edge-to-edge cover and a conventionally
    margined document coexist in one PDF.</p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("full_bleed", "Paged Media", "Full-Bleed Pages",
    "An edge-to-edge cover via @page :first { margin: 0 } - the first page's content band is the entire sheet (corner marks touch all four paper edges), followed by a normally margined page.",
    fullBleedHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Named pages showcase ────────────────────────────────────────────────────
var namedPagesHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 20mm;
        @bottom-center { content: "Page " counter(page); font: 8pt Arial; }
    }
    @page chapter {
        @top-right { content: "Chapter Section"; font: italic 8pt Arial; color: #335; }
    }
    body { font: 11pt Arial; }
    h1 { font-size: 18pt; border-bottom: 2pt solid #336; padding-bottom: 4pt; }
    </style>
    </head>
    <body>
    """ +
    string.Concat(Enumerable.Range(1, 3).Select(i =>
        $"<div style=\"page: chapter\">" +
        $"<h1>Chapter {i}: Section Title</h1>" +
        string.Concat(Enumerable.Range(1, 18).Select(j =>
            $"<p>Chapter {i}, paragraph {j}: Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
            "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>")) +
        "</div>")) +
    """
    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_named_pages", "Paged Media", "Named Pages",
    "Routing content to differently-styled pages with named @page rules and the page property.",
    namedPagesHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Mixed page orientation showcase (issue #143's own headline capability) ─────
// A named @page rule's own `size` gives that page a genuinely independent physical
// PDF page (its own /MediaBox) - so one document can mix portrait and landscape
// (or any other differently-sized) pages via `page: <name>`. Everything that makes
// this a real showcase rather than just a size swap: the body text on the landscape
// section genuinely RE-WRAPS to that page's own (much wider) measure rather than
// keeping the portrait column width (mid-fragment block/text rewrap, issue #143),
// and the fixed-position corner badge's own percentage `left`/`top`/`width` all
// resolve against each page's own area too, so it stays correctly placed and sized
// relative to the sheet it is actually on instead of drifting toward one edge on
// the narrower pages or shrinking away from the correct corner on the wider one.
//
// Deliberately two pages, not three: a third (reverted-to-portrait) page after the
// landscape section would exercise a genuine, already-tracked gap rather than this
// layer's own new work - a <table> anywhere inside a named-page section currently
// leaves that name "stuck" active for a later sibling section, even one that never
// touches the table engine itself (see the named-page-reversion-outside-block-flow
// accepted-gap note, issue #166).
var mixedOrientationHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 25mm 20mm;
        @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; color: #888; }
    }
    @page landscape {
        size: A4 landscape;
        margin: 18mm 20mm;
    }
    body { font: 11pt Georgia, serif; margin: 0; text-align: justify; }
    h1 { font-size: 20pt; text-align: center; margin: 0 0 5mm; font-family: Arial; }
    h2 { font-size: 15pt; margin: 0 0 4mm; font-family: Arial; color: #2e5c8a; }
    .note { font-style: italic; color: #555; }
    .badge {
      position: fixed;
      left: 82%;
      top: 4%;
      width: 15%;
      height: 14mm;
      background: linear-gradient(135deg, #2e8bc0, #1d5c8a);
      color: #f2f7fb;
      font: bold 8pt Arial;
      letter-spacing: 1pt;
      border-radius: 2mm;
      display: flex;
      align-items: center;
      justify-content: center;
      text-align: center;
    }
    table { width: 100%; border-collapse: collapse; margin-top: 4mm; }
    th, td { border: 0.5pt solid #999; padding: 2mm 3mm; font-size: 9pt; text-align: left; }
    th { background: #eef2f6; font-family: Arial; }
    tr:nth-child(even) td { background: #f8fafc; }
    </style>
    </head>
    <body>
    <div class="badge">Q3<br>REPORT</div>
    <h1>Quarterly Regional Report</h1>
    <p class="note">This cover page is ordinary A4 portrait. The corner badge (position: fixed,
    sized and placed in percentages) tracks each page's own area - watch how it lands in the same
    relative corner, at the same relative size, on the landscape page overleaf despite the sheet
    itself changing shape.</p>
    <p>Executive summary: regional performance improved across every territory this quarter, led by
    continued growth in the eastern corridor. The detail overleaf is routed to a landscape page for
    the width a full regional comparison needs - a single PDF document mixing page orientations,
    with every layer of layout (text reflow, fixed-position content, and the physical page geometry
    itself) agreeing about which page is which shape.</p>
    <div style="page: landscape; page-break-before: always">
      <h2>Regional Sales Detail &mdash; Landscape Page</h2>
      <p>This section is routed to the named <code>landscape</code> page, whose own @page rule sets
      <code>size: A4 landscape</code> with no change to the base font or column styling. Notice that
      this paragraph's own text reaches noticeably further across the sheet than the cover page's did
      overleaf - main-column content re-wraps to each page's own content-box width (CSS Paged Media:
      "the edges of the page area act as a containing block for the layout that occurs between page
      breaks"), so the wider landscape measure is genuine layout, not a stretched portrait column.</p>
      <table>
        <thead>
          <tr><th>Territory</th><th>Q1</th><th>Q2</th><th>Q3</th><th>YoY Growth</th><th>Top Segment</th><th>Units Shipped</th><th>Lead Analyst</th></tr>
        </thead>
        <tbody>
    """ +
    string.Concat(new[]
    {
        ("North America", "$4.2M", "$4.6M", "$5.1M", "+21%", "Enterprise", "128,400", "R. Alvarez"),
        ("EMEA", "$3.1M", "$3.4M", "$3.9M", "+18%", "Mid-market", "96,200", "L. Mensah"),
        ("APAC", "$2.8M", "$3.3M", "$4.0M", "+29%", "Consumer", "211,700", "K. Tanaka"),
        ("Latin America", "$1.4M", "$1.6M", "$1.9M", "+26%", "Consumer", "88,900", "P. Duarte"),
        ("Eastern Corridor", "$0.9M", "$1.5M", "$2.4M", "+62%", "Enterprise", "54,300", "S. Ibrahim"),
    }.Select(t =>
        $"<tr><td>{t.Item1}</td><td>{t.Item2}</td><td>{t.Item3}</td><td>{t.Item4}</td>" +
        $"<td>{t.Item5}</td><td>{t.Item6}</td><td>{t.Item7}</td><td>{t.Item8}</td></tr>")) +
    """
        </tbody>
      </table>
    </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_mixed_orientation", "Paged Media", "Mixed Page Orientation",
    "One PDF mixing portrait and landscape pages via a named @page rule's own size - the landscape section's body text genuinely re-wraps to its own wider measure, and a fixed-position corner badge (percentage left/top/width) stays correctly placed and sized on every page despite the sheet itself changing shape.",
    mixedOrientationHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Directional page breaks showcase ───────────────────────────────────────
// css-break-3 §3.1's left/right/recto/verso: each chapter opens on a right-hand
// page, so a chapter whose predecessor ended on one gets a blank verso page
// inserted ahead of it, and the closing break-after pads the book so the page
// after it would fall recto too. The folios alternate to the outer edge, which
// is what makes the left/right rhythm (and the inserted blanks) legible.
//
// Deliberately symmetric physical margins: a mirrored binding gutter is the more
// obvious demo, but per-page left/right margin overrides re-wrap each page to its
// own measure, which puts the document inside the bounded reflow loop where the
// page a break lands on can settle on different fixpoints between runs.
var directionalBreaksHtml =
    """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A5;
      margin: 18mm;
      @bottom-center { content: counter(page); font-size: 9pt; color: #888; }
    }
    @page :left {
      @bottom-center { content: none; }
      @bottom-left { content: counter(page); font-size: 9pt; color: #888; }
      @top-left { content: string(chapter); font-size: 8pt; color: #aaa; letter-spacing: 0.08em; }
    }
    @page :right {
      @bottom-center { content: none; }
      @bottom-right { content: counter(page); font-size: 9pt; color: #888; }
      @top-right { content: string(chapter); font-size: 8pt; color: #aaa; letter-spacing: 0.08em; }
    }
    @page :first { @top-left { content: none; } @top-right { content: none; } }
    body { font-family: Georgia, 'Times New Roman', serif; font-size: 10.5pt; line-height: 1.55; margin: 0; color: #222; }
    h1.cover { font-size: 26pt; text-align: center; margin: 40pt 0 6pt; letter-spacing: 0.02em; }
    p.cover-sub { text-align: center; color: #777; font-size: 11pt; margin: 0; }
    h2 { font-size: 15pt; margin: 0 0 14pt; color: #1864ab; }
    h2 .num { display: block; font-size: 8.5pt; letter-spacing: 0.18em; color: #aaa; margin-bottom: 4pt; }
    p { margin: 0 0 9pt; }
    section.chapter { break-before: recto; }
    section.end { break-after: recto; }
    /* The `section > heading` idiom: the break is declared on the *first child* of its container
       rather than on the container itself, so it has no preceding sibling of its own to be resolved
       against and is resolved against the wrapper's instead. */
    div.part > h2 { break-before: recto; }
    h2 { string-set: chapter content(); }
    </style>
    </head>
    <body>

    <h1 class="cover">The Recto Rule</h1>
    <p class="cover-sub">A short book that always opens its chapters on the right</p>

    """
    + string.Concat(Enumerable.Range(1, 3).Select(i =>
        $"<section class=\"chapter{(i == 3 ? " end" : "")}\">"
        + $"<h2><span class=\"num\">CHAPTER {i}</span>Of Pages and Their Sides</h2>"
        // The last chapter is deliberately short enough to end on a right-hand page, so its
        // closing break-after: recto has to pad the book with a final blank verso.
        + string.Concat(Enumerable.Range(1, new[] { 6, 8, 5 }[i - 1]).Select(j =>
            $"<p>Paragraph {j}. A chapter that must begin on a right-hand page will, when the page "
            + "before it is itself a right-hand page, leave the intervening left-hand page empty rather "
            + "than start on the wrong side. That empty page is a real page: it carries the running head "
            + "and its folio, and it counts toward the total.</p>"))
        + "</section>"))
    + """

    <div class="part">
    <h2><span class="num">COLOPHON</span>Where the Break Is Declared</h2>
    <p>This heading opens on a right-hand page like every chapter before it, but the rule that puts it
    there is written on the heading rather than on the division that contains it. A heading is the first
    thing in its division, so there is no earlier sibling for the break to fall after; the break point
    before it is the same break point as the one before the division, and it is resolved there.</p>
    <p>A break declared on the very first thing in a document is a different matter. Nothing precedes it,
    so there is nothing to break from, and no page is manufactured in front of it.</p>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_directional_breaks", "Paged Media", "Directional Page Breaks",
    "Chapters that always open on a right-hand page: break-before: recto inserts a blank verso page "
    + "when it needs one, and the outer-edge folios show the left/right alternation. The closing "
    + "colophon declares its break on the heading rather than the division, so it is resolved against "
    + "the division's own predecessor.",
    directionalBreaksHtml, new PdfGenerateConfig { PageSize = PageSize.A5 });

// ── Monolithic content showcase ────────────────────────────────────────────
var monolithicHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a5; margin: 14mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937; orphans: 1; widows: 1 }
    h1 { font-size: 13pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 1.2em }
    p { margin: 0 0 0.7em; line-height: 1.5 }
    .card {
      border: 1px solid #94a3b8; border-radius: 6px; padding: 10px 12px; margin: 0 0 0.9em;
      background: linear-gradient(160deg, #eff6ff, #dbeafe);
    }
    .card h2 { font-size: 10pt; margin: 0 0 0.3em; color: #1d4ed8 }
    .card p { margin: 0; font-size: 8.5pt }
    .clipped { overflow: hidden }
    .tag { font-size: 7pt; letter-spacing: .04em; text-transform: uppercase; color: #64748b }
    </style></head><body>
    <h1>Monolithic content at a page break</h1>
    <p class="intro">CSS Fragmentation Level 3 &sect;2 makes a scroll container &mdash; any box with
    <code>overflow</code> other than <code>visible</code> &mdash; monolithic: it may not be split, so where it
    would straddle a page boundary it moves to the next page whole. The two cards below are identical apart
    from that one declaration.</p>
    """
    + string.Concat(Enumerable.Range(1, 14).Select(i =>
        $"<p>Filler paragraph {i}. This body copy pushes the cards down the page so that each one meets the "
        + "page boundary rather than sitting comfortably inside a single page.</p>"))
    + """
    <div class="card">
      <span class="tag">overflow: visible</span>
      <h2>This card is split</h2>
      <p>Nothing forbids a break inside it, so the page boundary cuts straight through: its border and its
      gradient background end on one page and resume on the next.</p>
    </div>
    """
    + string.Concat(Enumerable.Range(15, 16).Select(i =>
        $"<p>Filler paragraph {i}. The same run of body copy again, so the second card meets the following "
        + "page boundary at the same place its twin met the first one.</p>"))
    + """
    <div class="card clipped">
      <span class="tag">overflow: hidden</span>
      <h2>This card moves whole</h2>
      <p>Being a scroll container makes it monolithic, so rather than being cut in half it is carried
      wholesale onto the next page, leaving the gap above it.</p>
    </div>
    """
    + string.Concat(Enumerable.Range(31, 4).Select(i =>
        $"<p>Trailing paragraph {i}, so the move above has visible content after it.</p>"))
    + """
    <h1 style="margin-top:1.4em">A relocated box is laid out again, not slid down</h1>
    <p class="intro">A box that had already begun flowing its own text across the boundary cannot simply be
    shifted: its later lines were laid out against the next page's top, so moving the box would carry that
    distance into it as blank space and leave it reporting height it does not use. The card below holds
    several lines and meets the boundary part-way through them.</p>
    """
    + string.Concat(Enumerable.Range(35, 10).Select(i =>
        $"<p>Filler paragraph {i}. This run positions the multi-line card so the page boundary falls "
        + "between its own lines rather than above it.</p>"))
    + """
    <div class="card clipped">
      <span class="tag">overflow: hidden &mdash; multi-line</span>
      <h2>Its lines stay evenly spaced</h2>
      <p>The page boundary falls part-way through this paragraph, so the box is relocated after some of its
      text had already been placed on the following page. Because it is laid out again at its destination
      rather than translated to it, the lines below run on at their normal spacing instead of opening up a
      band of blank space part-way down the card.</p>
      <p>A second paragraph, so the break has somewhere to fall between lines and the effect is visible
      rather than hidden by a lucky alignment of the page grid.</p>
    </div>
    """
    + string.Concat(Enumerable.Range(45, 3).Select(i =>
        $"<p>Trailing paragraph {i}.</p>"))
    + """
    <h1 style="margin-top:1.4em">The container travels with it</h1>
    <p class="intro">A break point before a container's first child <i>is</i> the break point before the
    container (&sect;3.1), so when the child moves the container moves with it. Left behind, the panel below
    would span the boundary and print an empty copy of its own border and background on the page its
    contents had just left.</p>
    """
    + string.Concat(Enumerable.Range(65, 8).Select(i =>
        $"<p>Filler paragraph {i}. This run puts the panel's own first child across the page boundary."
        + "</p>"))
    + """
    <div style="border:1px solid #a3a3a3; border-radius:6px; padding:8px 10px; background:#fafafa">
      <div class="card clipped">
        <span class="tag">first child &mdash; monolithic</span>
        <h2>The panel moves too</h2>
        <p>This card may not be split, so it starts on the next page. The panel around it is not what
        asked for the break, but the break point is the panel's own, so the panel opens on that page as
        well rather than being cut open on this one.</p>
      </div>
      <p style="font-size:8pt; margin:0.6em 0 0; color:#6b7280">A second child, so the panel is visibly
      more than the card it wraps.</p>
    </div>
    """
    + string.Concat(Enumerable.Range(75, 3).Select(i =>
        $"<p>Trailing paragraph {i}.</p>"))
    + """
    <h1>Inside a flex or grid container</h1>
    <p>A flex line and a grid row are break points too. The row below asks not to be broken, so it moves
    to the next page as a unit &mdash; every item in it, not only the one that asked, because the
    cross-axis alignment holds them together.</p>
    """
    + string.Concat(Enumerable.Range(55, 10).Select(i =>
        $"<p>Filler paragraph {i}. This run puts the row below at the foot of its page.</p>"))
    + """
    <div style="display:flex; gap:8pt; break-inside:avoid">
      <div class="card"><span class="tag">flex item</span><h2>One</h2>
        <p>Its line meets the page boundary, so the whole line moves.</p></div>
      <div class="card"><span class="tag">flex item</span><h2>Two</h2>
        <p>It moves with its neighbour rather than being left behind.</p></div>
    </div>
    <div style="display:grid; grid-template-columns:1fr 1fr; gap:8pt; break-inside:avoid; margin-top:10pt">
      <div class="card"><span class="tag">grid item</span><h2>Three</h2>
        <p>A grid row travels together for the same reason.</p></div>
      <div class="card"><span class="tag">grid item</span><h2>Four</h2>
        <p>An item spanning several rows travels with the first of them.</p></div>
    </div>

    <h1>The lines below a moved one follow it</h1>
    <p>A wrapping flex container whose middle line meets the page boundary. That line moves to the next
    page, and every line below it moves with it &mdash; a line does not stay where it was while the one
    above it leaves, and the container's own height grows by what its content travelled.</p>
    """
    + string.Concat(Enumerable.Range(85, 11).Select(i =>
        $"<p>Filler paragraph {i}. This run puts the second of the three lines below across the boundary.</p>"))
    + """
    <div style="display:flex; flex-wrap:wrap; gap:8pt; border:1px dashed #a3a3a3; padding:6pt">
    """
    + string.Concat(Enumerable.Range(1, 6).Select(i =>
        $"<div class=\"card\" style=\"width:38%; margin:0; break-inside:avoid\">"
        + $"<span class=\"tag\">line {(i + 1) / 2}</span>"
        + $"<h2>Item {i}</h2><p>Sized so three lines of two wrap inside the container.</p></div>"))
    + """
    </div>

    <h1>Either side of the break point may force it</h1>
    <p>&sect;3.1 states a forced break of the break <i>point</i>, not of a box: it is taken if the earlier
    item's <code>break-after</code> or the later item's <code>break-before</code> asks for one. The first
    line below declares <code>break-after: page</code>, so the line after it opens the next page even
    though nothing in that line asks for anything.</p>
    """
    + string.Concat(Enumerable.Range(96, 3).Select(i =>
        $"<p>Filler paragraph {i}. There is room left on this page, so nothing but the declared break "
        + "moves the second line off it.</p>"))
    + """
    <div style="display:flex; flex-wrap:wrap; gap:8pt; border:1px dashed #a3a3a3; padding:6pt">
      <div class="card" style="width:38%; margin:0; break-after:page">
        <span class="tag">break-after: page</span><h2>Item 1</h2>
        <p>This line stays where the flow put it &mdash; the break is after it, not before it.</p></div>
      <div class="card" style="width:38%; margin:0; break-after:page">
        <span class="tag">break-after: page</span><h2>Item 2</h2>
        <p>Its neighbour on the same line, declaring the same thing.</p></div>
      <div class="card" style="width:38%; margin:0">
        <span class="tag">no break value</span><h2>Item 3</h2>
        <p>Nothing here asks for a break, yet the line opens the next page: the break point above it was
        forced from the other side.</p></div>
      <div class="card" style="width:38%; margin:0">
        <span class="tag">no break value</span><h2>Item 4</h2>
        <p>It travels with its line, as a line always does.</p></div>
    </div>

    <h1>An avoided break takes the line above it too</h1>
    <p>&sect;3.1's other half. A break-avoidance value on either side of a break point forbids an
    unforced break there, and honouring it means moving the <i>earlier</i> line: the line below travels
    to the next page because it may not be cut, and the line chained to it by
    <code>break-after: avoid</code> follows rather than being stranded at the foot of the page its
    successor just left. This is the user-agent print sheet's <code>h1&ndash;h6 { break-after: avoid }</code>
    for a heading that happens to be a flex item.</p>
    """
    + string.Concat(Enumerable.Range(100, 11).Select(i =>
        $"<p>Filler paragraph {i}. This run puts the pair of lines below at the foot of their page."
        + "</p>"))
    + """
    <div style="display:flex; flex-wrap:wrap; gap:8pt; border:1px dashed #a3a3a3; padding:6pt">
      <div class="card" style="width:38%; margin:0; break-after:avoid">
        <span class="tag">break-after: avoid</span><h2>Heading line</h2>
        <p>Nothing here asks to move. It travels because the break point below it may not be broken.</p></div>
      <div class="card" style="width:38%; margin:0">
        <span class="tag">same line</span><h2>Its neighbour</h2>
        <p>A line moves as a unit, so this comes along.</p></div>
      <div class="card" style="width:38%; margin:0; break-inside:avoid">
        <span class="tag">break-inside: avoid</span><h2>Section body</h2>
        <p>This line meets the page boundary and may not be cut, so it opens the next page.</p></div>
      <div class="card" style="width:38%; margin:0; break-inside:avoid">
        <span class="tag">same line</span><h2>Its neighbour</h2>
        <p>The two lines arrive together, with the spacing between them preserved.</p></div>
    </div>

    <h1>Wrap-reverse reads down the page, not down the source</h1>
    <p><code>flex-wrap: wrap-reverse</code> reverses where the lines sit, so the first line in the source
    is the <i>last</i> one down the page. What follows a line that moves is the line physically below it,
    which here is the <i>earlier</i> one in the source; and the break point between the two is the one
    above it. Read the source order as though it ran down the page and the moved line is drawn over the
    top of a line that stayed where it was.</p>
    """
    + string.Concat(Enumerable.Range(120, 8).Select(i =>
        $"<p>Filler paragraph {i}. This run puts the container's upper line &mdash; the second of the two "
        + "in the source &mdash; across the page boundary.</p>"))
    + """
    <div style="display:flex; flex-wrap:wrap-reverse; gap:8pt; border:1px dashed #a3a3a3; padding:6pt">
      <div class="card" style="width:38%; margin:0; break-inside:avoid">
        <span class="tag">source line 1</span><h2>Item 1</h2>
        <p>Last down the page, though first in the source. It follows the line above it rather than
        staying behind when that line leaves.</p></div>
      <div class="card" style="width:38%; margin:0; break-inside:avoid">
        <span class="tag">source line 1</span><h2>Item 2</h2>
        <p>Its neighbour on the same line, travelling with it as a line always does.</p></div>
      <div class="card" style="width:38%; margin:0; break-inside:avoid">
        <span class="tag">source line 2</span><h2>Item 3</h2>
        <p>First down the page. This line meets the boundary and may not be cut, so it opens the next
        page.</p></div>
      <div class="card" style="width:38%; margin:0; break-inside:avoid">
        <span class="tag">source line 2</span><h2>Item 4</h2>
        <p>The two lines arrive in the order they are drawn in, one below the other.</p></div>
    </div>
    </body></html>
    """;

await SaveShowcaseAsync("paged_media_monolithic_content", "Paged Media", "Monolithic Content",
    "A box with overflow: hidden is a scroll container, which CSS Fragmentation §2 forbids breaking: it "
    + "moves to the next page whole instead of being cut in half by the page boundary. A flex line and a "
    + "grid row that ask not to be broken move as a unit for the same reason, and the lines below a "
    + "moved one follow it rather than staying put. A forced break is taken from either side of a break "
    + "point, so break-after on one line opens the next page for the line after it — and break-after: "
    + "avoid there moves the earlier line too, so a heading is not stranded on the page its section "
    + "body just left. Under flex-wrap: wrap-reverse all of this is read down the page rather than down "
    + "the source, which is the reverse order there.",
    monolithicHtml, new PdfGenerateConfig { PageSize = PageSize.A5 });

// ── orphans / widows showcase ──────────────────────────────────────────────
static string OrphansWidowsSection(string title, string intro, string paragraphStyle, int filler) =>
    $"<h1 style=\"break-before:page\">{title}</h1><p class=\"intro\">{intro}</p>"
    + string.Concat(Enumerable.Range(1, filler).Select(i =>
        $"<p class=\"filler\">Filler line {i} &mdash; body copy that positions the marked paragraph so its "
        + "own lines meet the page boundary.</p>"))
    + $"<p class=\"marked\" style=\"{paragraphStyle}\">"
    + string.Concat(Enumerable.Range(1, 9).Select(i =>
        $"Sentence {i} of the marked paragraph, long enough to occupy a line of its own at this measure. "))
    + "</p>";

var orphansWidowsHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a5; margin: 14mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 12pt; margin: 0 0 0.4em }
    p { margin: 0 0 0.6em; line-height: 1.6 }
    p.intro { color: #6b7280; font-size: 8pt; margin-bottom: 1em }
    p.filler { color: #9ca3af }
    p.marked { background: #eff6ff; border-left: 2pt solid #2563eb; padding: 2pt 6pt; color: #1e3a8a }
    </style></head><body>
    """
    + OrphansWidowsSection(
        "Without a minimum",
        "The marked paragraph below declares <code>orphans: 1; widows: 1</code>, so any split of it is "
        + "acceptable and the page boundary falls exactly where ordinary flow puts it.",
        "orphans:1; widows:1", filler: 13)
    + OrphansWidowsSection(
        "widows: four lines must follow",
        "The same paragraph in the same place, now declaring <code>orphans: 2; widows: 4</code>, so at "
        + "least four of its own lines have to fall after the break it is split at.",
        "orphans:2; widows:4", filler: 13)
    + "</body></html>";

await SaveShowcaseAsync("paged_media_orphans_widows", "Paged Media", "Orphans and Widows",
    "CSS Fragmentation §5.4's line minimums either side of a page break. widows moves the minimum number "
    + "of lines across the break rather than relocating the whole paragraph; orphans is decided at the "
    + "break point, so too few lines before it makes the break fall before the paragraph instead.",
    orphansWidowsHtml, new PdfGenerateConfig { PageSize = PageSize.A5 });

// ── keep-with-next showcase ────────────────────────────────────────────────
// A heading chained to the paragraph below it by break-after: avoid (the UA print default for h1-h6),
// positioned so the paragraph's own text breaks across the page boundary. The paragraph therefore
// completes on a *later* fragmentainer pass than the one that placed the heading, so the correction
// has to reach back into a fragmentainer the driver has already filled: the pass that placed the
// heading is re-entered and the group is laid out again at the top of the next page. Carried out by
// moving the group instead - which is all that was possible before - the paragraph arrives with the
// page gap still inside it, as blank space between two of its own lines.
static string KeepWithNextSection(double fillerHeight) =>
    "<h1 style=\"break-before:page\">Keep-with-next across a page boundary</h1>"
    + "<p class=\"intro\">The heading below declares nothing of its own: <code>h1&ndash;h6 { break-after: "
    + "avoid }</code> is the user-agent print default. Its section body asks not to be broken and starts "
    + $"a line above the page boundary, with {fillerHeight:0}pt of filler above it.</p>"
    + $"<div class=\"filler\" style=\"height:{fillerHeight:0}pt\">Filler</div>"
    + "<h2>A heading that must not be stranded</h2>"
    + "<p class=\"marked\">"
    + string.Concat(Enumerable.Range(1, 5).Select(i =>
        $"Sentence {i} of the section body, long enough to occupy a line of its own. "))
    + "</p>";

// The pass the driver goes back to is not always one that began at a page top. Here running text
// carries across the first page boundary, so the pass that places the first section's heading was
// itself entered part-way through that paragraph - and going back to it means continuing the
// paragraph a second time. Everything the discarded passes appended to it has to be undone first;
// left in place, the pass hands line boxes it already holds to CssLineBox.AssignRectanglesToBoxes a
// second time and the whole render fails with "An item with the same key has already been added".
static string ResumedPassSection(int leadSentences, int bodySentences) =>
    "<h1 style=\"break-before:page\">Going back to a pass that stopped mid-paragraph</h1>"
    + "<p class=\"intro\">The running text below breaks across the page boundary, so the pass that placed "
    + "the first heading under it started inside that paragraph rather than at a page top. Each heading "
    + "still arrives with its own section body.</p>"
    + "<p>"
    + string.Concat(Enumerable.Range(1, leadSentences).Select(i =>
        $"Sentence {i} of the running text that carries on across the page boundary, long enough to "
        + "occupy a line of its own at this measure. "))
    + "</p>"
    + string.Concat(Enumerable.Range(1, 2).Select(n =>
        $"<h2>Section {n}</h2><p class=\"marked\">"
        + string.Concat(Enumerable.Range(1, bodySentences).Select(i =>
            $"Sentence {i} of section {n}'s body, long enough to occupy a line of its own. "))
        + "</p>"));

var keepWithNextHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a5; margin: 14mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 12pt; margin: 0 0 0.4em }
    h2 { font-size: 10pt; margin: 0 0 0.4em; color: #1e3a8a }
    p { margin: 0 0 0.6em; line-height: 1.6 }
    p.intro { color: #6b7280; font-size: 8pt; margin-bottom: 1em }
    div.filler { color: #9ca3af; background: #f3f4f6; font-size: 8pt }
    p.marked { background: #eff6ff; border-left: 2pt solid #2563eb; padding: 2pt 6pt; color: #1e3a8a;
               width: 200pt; break-inside: avoid; orphans: 1; widows: 1 }
    </style></head><body>
    """
    + KeepWithNextSection(410)
    + ResumedPassSection(leadSentences: 40, bodySentences: 10)
    + "</body></html>";

await SaveShowcaseAsync("paged_media_keep_with_next", "Paged Media", "Keep With Next",
    "CSS Fragmentation §3.1's break-after: avoid, honored even where the heading was placed in a "
    + "fragmentainer the layout driver had already finished with.",
    keepWithNextHtml, new PdfGenerateConfig { PageSize = PageSize.A5 });

// ── table row break values showcase ────────────────────────────────────────
// A break point between two table rows is a class-A break point like any other (CSS Fragmentation
// §3.1), so a break value declared on a row - or on the row group the row begins or ends - is taken
// there. The table engine places its own rows and never routes them through the block-flow break
// machinery, so until it read those values a `break-before: page` on a <tr> did nothing at all and
// the table simply flowed on. The repeating <thead> is the point: taking the break through the
// engine's own row loop is what keeps the header repeating onto the page the break opens.
static string RowBreakRows(int from, int count, string firstRowStyle = "") =>
    string.Concat(Enumerable.Range(from, count).Select(i =>
        $"<tr{(i == from ? firstRowStyle : "")}>"
        + $"<td>{i:000}</td><td>Component {(char)('A' + (i % 7))}-{i}</td>"
        + $"<td>{(i % 3 == 0 ? "In stock" : i % 3 == 1 ? "On order" : "Discontinued")}</td>"
        + $"<td class=\"num\">{i * 137 % 900 + 12}</td></tr>"));

var tableRowBreaksHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a5; margin: 14mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 12pt; margin: 0 0 0.4em }
    p { margin: 0 0 0.8em; color: #6b7280; font-size: 8pt }
    table { width: 100%; border-collapse: collapse; font-size: 8pt }
    thead th { background: #1e3a8a; color: #fff; text-align: left; padding: 3pt 5pt; font-size: 7.5pt }
    td { padding: 2.5pt 5pt; border-bottom: 0.5pt solid #e5e7eb }
    td.num { text-align: right }
    tbody.section-2 > tr:first-child { break-before: page }
    tbody.section-2 > tr:first-child td { background: #eff6ff; color: #1e3a8a; font-weight: bold }
    </style></head><body>
    <h1>Break values between table rows</h1>
    <p>The highlighted row declares <code>break-before: page</code>. Its repeating header follows it
    onto the page the break opens, and the page before it ends wherever the break fell rather than
    where the rows ran out of room.</p>
    <table>
      <thead><tr><th>#</th><th>Part</th><th>Status</th><th>Qty</th></tr></thead>
      <tbody>
    """
    + RowBreakRows(1, 12)
    + """
      </tbody>
      <tbody class="section-2">
    """
    + RowBreakRows(101, 14)
    + """
      </tbody>
    </table>
    </body></html>
    """;

// ── a cell's own text across a page boundary ───────────────────────────────
// A line box is monolithic (CSS Fragmentation §4.1), so it belongs to exactly one page. A table
// cell's own text used to be exempt from word flow's boundary check, which left nothing to stop a
// line straddling the break - the emitter then claimed it on both pages and it painted sliced in
// half on each.
var tallCellHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 12mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    table { width: 100%; border-collapse: collapse }
    td { padding: 5pt 6pt; border: 0.75pt solid #94a3b8; line-height: 1.5; text-align: justify }
    </style></head><body>
    <h1>A cell with more text than a page holds</h1>
    <p class="intro">The cell's own text - no wrapper block - runs past the page boundary. Every line
    lands whole on one page or the other; none is cut through the middle.</p>
    <table><tr><td>
    """
    + string.Join(" ", Enumerable.Range(1, 240).Select(i => $"word{i}"))
    + """
    </td></tr></table>
    </body></html>
    """;

await SaveShowcaseAsync("paged_media_table_cell_lines", "Paged Media", "Table Cell Text Across Pages",
    "CSS Fragmentation §4.1: a line box is a monolithic break unit, so a table cell's own text breaks "
    + "between lines rather than through one.",
    tallCellHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

await SaveShowcaseAsync("paged_media_table_row_breaks", "Paged Media", "Table Row Break Values",
    "CSS Fragmentation §3.1's forced break values honored at the break point between two table rows, "
    + "with the table's repeating header carried onto the page the break opens.",
    tableRowBreaksHtml, new PdfGenerateConfig { PageSize = PageSize.A5 });

// ── a row that continues, cell by cell ─────────────────────────────────────
// css-tables-3 §6.1: a row whose cell runs out of room continues in the next fragmentainer, and its
// cells are fragmented independently (css-break-3 §2.1 parallel flows). The narrow cell finishes on
// the first page and is not drawn again; the wide one picks up exactly where it stopped. The repeating
// <thead> is carried onto each page the row continues onto, and css-tables-3 §6.2's "leave room" is what
// keeps the continuation beneath it rather than overlapped by it (issue #439). The repeating <tfoot> is
// the same rule read from the other end of the band: it closes every page the row reaches, with the room
// for it left at the band's foot so the continuation stops above it rather than under it (#493). Both
// groups are tinted, so an overlap would be visible rather than merely present. The narrow cell's *box*
// continues with the row's, so its background and borders run the full depth of every continuation with
// no content in them (#478) - which is what its tinted background is here to show.
var rowContinuationHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 12mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    table { width: 100%; border-collapse: collapse }
    th, td { border: 0.75pt solid #94a3b8; padding: 4pt 5pt; vertical-align: top }
    thead th { background: #e2e8f0; font-size: 8pt; text-align: left }
    tfoot td { background: #e2e8f0; font-size: 8pt; text-align: left }
    td.note { width: 30%; color: #6b7280; font-size: 8pt; background: #f1f5f9 }
    td.body { line-height: 1.5; text-align: justify }
    </style></head><body>
    <h1>One row, several pages</h1>
    <p class="intro">The right-hand cell holds more than a page of text, so the row continues onto the
    next page. The left-hand cell finished on the first one, so none of its content is drawn again - the
    cells of a row are fragmented independently. Its box still continues with the row, which is why its
    tint runs the full depth of every continuation.</p>
    <table>
      <thead><tr><th>Note</th><th>Clause</th></tr></thead>
      <tfoot><tr><td>Continues</td><td>Overleaf</td></tr></tfoot>
      <tbody><tr>
        <td class="note">Finishes on the first page.</td>
        <td class="body">
    """
    + string.Join(" ", Enumerable.Range(1, 260).Select(i => $"clause{i}"))
    + """
        </td>
      </tr></tbody>
    </table>
    </body></html>
    """;

await SaveShowcaseAsync("paged_media_table_row_continuation", "Paged Media", "Table Row Continuation",
    "css-tables-3 §6.1: a row whose cell runs out of room continues on the next page, and the cells of "
    + "that row are fragmented independently - a short cell finishes where it finishes, while its box "
    + "continues with the row, its tint and borders running the full depth of each continuation with no "
    + "content in it. The repeating header is carried onto every page the row reaches, above the "
    + "continuation rather than over it, and the repeating footer closes each of those pages below it - "
    + "css-tables-3 §6.2's \"leave room\" applied at both ends of the band.",
    rowContinuationHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

// ── a row taller than the page band ────────────────────────────────────────
// css-break-3 §4.3: a break moves content to the *next* fragmentainer, so a break target above the
// content it must follow is not a break at all. The first row here holds a block taller than the page's
// whole content band, so there is no page that could hold it and it is left where it is, overflowing
// across pages. What matters is the rows after it: they continue immediately below it, in the band its
// bottom actually landed in, rather than being placed back on a page it is still filling and painted
// over its content (issue #432).
//
// And the repeating <thead> is carried onto every page the table spans, including the ones the tall row
// merely overflows through - css-tables-3 §6.2 repeats a header "on each page spanned by a table", not on
// each page it broke on, and asks for room to be left for it (#509). Room on a page nothing breaks on can
// only come from slicing the row itself, which css-break-3 §4.3 allows in as many words: "the UA may also
// fragment the contents of monolithic elements by slicing the element's graphical representation". So the
// block is neither resized nor moved - each page draws it from a different origin, and the strips meet
// exactly, which is what makes the gradient continuous across the header on every page.
var tallRowHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 10mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    table { width: 100%; border-collapse: collapse }
    th, td { border: 0.75pt solid #94a3b8; padding: 4pt 5pt; vertical-align: top }
    thead th { background: #e2e8f0; font-size: 8pt; text-align: left }
    td.tall { padding: 0 }
    .chart { height: 620pt; background: linear-gradient(#dbeafe, #1d4ed8); padding: 5pt;
             color: #0f172a; font-size: 8pt }
    tr.after td { background: #fef3c7 }
    </style></head><body>
    <h1>A row taller than the page</h1>
    <p class="intro">The first row holds a 620pt block on a band of roughly 260pt, so it spans three
    pages on its own. The rows after it start below where it ends, not back inside it.</p>
    <table>
      <thead><tr><th>Figure</th></tr></thead>
      <tbody>
        <tr><td class="tall"><div class="chart">One block, taller than any page can hold.</div></td></tr>
        <tr class="after"><td>The row after it, immediately below.</td></tr>
        <tr class="after"><td>And the row after that.</td></tr>
        <tr class="after"><td>And one more, so the sequence is visible.</td></tr>
      </tbody>
    </table>
    </body></html>
    """;

await SaveShowcaseAsync("paged_media_table_tall_row", "Paged Media", "Table Row Taller Than a Page",
    "CSS Fragmentation §4.3: a table row whose cell holds a block taller than the page's content band "
    + "has no page that could hold it, so it is left where it is and overflows across pages - and the "
    + "rows after it continue immediately below where it ends, rather than being placed back on a page "
    + "it is still filling and drawn over its content. The repeating header is carried onto every page "
    + "the table spans, including the ones the row only overflows through: css-tables-3 6.2 asks for room "
    + "to be left for it there, and 4.3 allows a UA to slice the graphical representation of content it "
    + "cannot break, so the block resumes below the header on each page rather than running under it.",
    tallRowHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

// ── a rowspan crossing a page boundary ─────────────────────────────────────
// css-tables-3 §6.1: a cell is fragmented like anything else. The "Q1" cell spans three rows and the
// page boundary falls inside it, so its box closes at the foot of the page it began on and continues at
// the head of the next - rather than being one box stretched across the boundary and drawn straight
// through the page edge (issue #511). The row that *ends* the span is carried onto the next page whole,
// like any other row; it used to be the one case the row loop declined to move.
//
// Q4 adds a second shape, this time on the Note column itself: its rowspan cell's own content is long
// enough to overflow the page it opened on *by itself*, before the row that ends the span - two pages
// later - is even reached (issue #521). Before that fix the cell's box was left stretched across every
// page in between rather than genuinely continued - visible as the tint and border running past this
// page's own foot instead of closing there and reopening on the next.
//
// The tint and the border on the spanning cell are the whole point of the fixture: the change is
// entirely in where that cell's box begins and ends, and a cell with no background cannot show it.
var rowspanBreakHtml = """
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 10mm }
    body { font: 9pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    table { width: 100%; border-collapse: collapse }
    th, td { border: 0.75pt solid #94a3b8; padding: 4pt 5pt; vertical-align: top }
    thead th { background: #e2e8f0; font-size: 8pt; text-align: left }
    td.quarter { background: #cffafe; border-left: 2pt solid #0e7490; font-weight: bold;
                 vertical-align: middle; text-align: center }
    td.overflowing { background: #cffafe; border-left: 2pt solid #0e7490 }
    td.wide { height: 46pt }
    </style></head><body>
    <h1>A rowspan across a page break</h1>
    <p class="intro">Each quarter's cell spans its three months. The page boundary falls inside the
    second one, so that cell closes at the foot of the page and opens again at the head of the next.</p>
    <table>
      <thead><tr><th>Quarter</th><th>Month</th><th>Note</th></tr></thead>
      <tbody>
        <tr><td class="quarter" rowspan="3">Q1</td><td>January</td><td class="wide">Opening balance carried forward.</td></tr>
        <tr><td>February</td><td class="wide">Nothing remarkable.</td></tr>
        <tr><td>March</td><td class="wide">Quarter closes.</td></tr>
        <tr><td class="quarter" rowspan="3">Q2</td><td>April</td><td class="wide">The spanning cell starts here.</td></tr>
        <tr><td>May</td><td class="wide">And the page boundary falls below this row.</td></tr>
        <tr><td>June</td><td class="wide">So this row, which ends the span, is carried over whole.</td></tr>
        <tr><td class="quarter" rowspan="2">Q3</td><td>July</td><td class="wide">A third span, after the break.</td></tr>
        <tr><td>August</td><td class="wide">Closing the table.</td></tr>
        <tr><td>Q4</td><td>October</td><td class="overflowing" rowspan="3">
          This note is deliberately long, on purpose: long enough that the spanning cell's own content
          overflows the page it opened on before the row that ends the span is even reached. The cell's
          box now closes at the foot of every page it passes through and reopens at the head of the
          next, rather than one box stretched from where it opened all the way to where the span ends.
          Padding this out with enough further sentences to make that genuinely happen on an A6 page is
          the whole point of this paragraph, so here are several more: the quarterly close took longer
          than usual this year because two suppliers changed their invoicing format at the same time,
          and reconciling both against the ledger by hand consumed most of the first week alone. A third
          supplier's statement arrived a full month late, which pushed the final reconciliation past the
          date this note was meant to be filed by, and that in turn delayed the quarter's own closing
          meeting by several more days than anyone had planned for going into it.
        </td></tr>
        <tr><td class="wide">Q4</td><td class="wide">November</td></tr>
        <tr><td class="wide">Q4</td><td class="wide">December</td></tr>
      </tbody>
    </table>
    </body></html>
    """;

await SaveShowcaseAsync("paged_media_table_rowspan_break", "Paged Media", "Rowspan Across a Page Break",
    "css-tables-3 §6.1: a cell whose rowspan reaches out of the page it was placed in is fragmented "
    + "like any other content - its box closes at the foot of that page and continues at the head of the "
    + "next, its tint and borders running the full depth of each fragment. The row that ends the span is "
    + "carried onto the next page whole, like any other row, rather than being left straddling the "
    + "boundary and drawn cut through by it. This is what both other engines do: Gecko re-reflows the "
    + "spanning cell against the remaining space and continues it, and Blink treats every cell as its "
    + "own §2.1 parallel flow. Q4 adds the case where the cell's own content, not just its span, "
    + "overflows the page it opened on: it is fragmented across every page it passes through rather "
    + "than left stretched from where it opened to where the span ends (issue #521).",
    rowspanBreakHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

// ── whether a <thead> repeats at all ───────────────────────────────────────
// css-tables-3 §6.2 makes repetition conditional: the group repeats only where it carries an avoiding
// break-inside and where it costs under a quarter of the page (#494). The UA print stylesheet supplies
// `thead, tfoot { break-inside: avoid }`, so repetition is the default - and `break-inside: auto` is the
// author's way to turn it off, which is what the second table here does. Both tables hold the same rows
// on the same page, so the difference is the header and nothing else: the first repeats its header onto
// every page, the second prints its once and gives the pages after it wholly to the rows. The tint is
// what makes the absence legible rather than merely true.
var groupRepetitionHtml = """
    <!DOCTYPE html>
    <html><head><style>
      @page { size: a6; margin: 11mm }
      body { font: 8pt sans-serif; margin: 0 }
      h2 { font-size: 8.5pt; margin: 0 0 4pt }
      p.note { color: #6b7280; font-size: 7pt; margin: 0 0 6pt }
      table { width: 100%; border-collapse: collapse; margin-bottom: 12pt }
      th, td { border: 0.6pt solid #cbd5e1; padding: 2.5pt 4pt; text-align: left }
      thead th { background: #1e3a8a; color: #fff; font-size: 7.5pt }
      table.once thead { break-inside: auto }
      table.once thead th { background: #64748b }
      td.n { text-align: right; width: 22% }
    </style></head><body>
    <h2>Repeating header (the default)</h2>
    <p class="note">The UA stylesheet gives every &lt;thead&gt; break-inside: avoid, so §6.2 repeats it.</p>
    <table>
      <thead><tr><th>Ledger entry</th><th class="n">Amount</th></tr></thead>
      <tbody>{{REPEATROWS}}</tbody>
    </table>
    <h2>Header laid out once</h2>
    <p class="note">The same table with break-inside: auto on its &lt;thead&gt; - §6.2's opt-out.</p>
    <table class="once">
      <thead><tr><th>Ledger entry</th><th class="n">Amount</th></tr></thead>
      <tbody>{{ONCEROWS}}</tbody>
    </table>
    </body></html>
    """;

static string RepetitionRows(string prefix, int count) =>
    string.Concat(Enumerable.Range(1, count).Select(i =>
        $"<tr><td>{prefix} entry {i:D2}, recorded in sequence</td>"
        + $"<td class=\"n\">{(i * 137) % 900 + 100}.{(i * 29) % 100:D2}</td></tr>"));

groupRepetitionHtml = groupRepetitionHtml
    .Replace("{{REPEATROWS}}", RepetitionRows("Repeating", 26))
    .Replace("{{ONCEROWS}}", RepetitionRows("Single", 26));

await SaveShowcaseAsync("paged_media_table_group_repetition", "Paged Media", "Repeating a Header, or Not",
    "css-tables-3 §6.2 makes repeating a <thead>/<tfoot> conditional: the group repeats only where it "
    + "carries an avoiding break-inside and where doing so costs under a quarter of the page. The "
    + "user-agent print stylesheet supplies break-inside: avoid, so repetition is what a table gets by "
    + "default - and break-inside: auto is the author's way to turn it off, laying the group out once, in "
    + "flow, and giving the pages after it wholly to the rows.",
    groupRepetitionHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

// ── Margin box explicit sizing showcase ────────────────────────────────────
var marginBoxSizingHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 25mm 20mm;
        @top-left   { content: "Narrow Left"; width: 80pt; font: 8pt Arial; }
        @top-center { content: "Wide Center (auto)"; font: bold 8pt Arial; }
        @top-right  { content: "Right"; width: 60pt; font: 8pt Arial; }
        @bottom-left  { content: "© 2025"; font: 7pt Arial; color: #888; }
        @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; }
        @bottom-right { content: "Confidential"; width: 70pt; font: 7pt Arial; color: #c00; }
    }
    body { font: 11pt Arial; }
    </style>
    </head>
    <body>
      <h1>Margin Box Sizing</h1>
      <p>Top row: left=80pt fixed, center=auto (gets remaining space), right=60pt fixed.</p>
      <p>Bottom row: left and center auto, right=70pt fixed.</p>
    """ +
    string.Concat(Enumerable.Range(1, 30).Select(i =>
        $"<p>Line {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>")) +
    """
    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_margin_box_sizing", "Paged Media", "Margin Box Sizing",
    "Explicit width and height sizing of @page margin boxes.",
    marginBoxSizingHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── @page / margin-box background & border showcase (issue #1082, closes #943 and #1147) ──
var pageAndMarginBoxBackgroundHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 25mm 20mm;
        background-color: #eef2f7;
        @bottom-center {
            content: "Page " counter(page) " of " counter(pages);
            font: 8pt Arial;
            color: #fff;
            background-color: #2c3e50;
            border-top: 2pt solid #1a252f;
            padding: 4pt 0;
        }
    }
    @page :first {
        background-image: linear-gradient(to bottom, #1a2a6c, #2c3e91);
        background-size: cover;
        background-clip: content-box;
        border: 3pt solid #1a2a6c;
        padding: 10mm;
        @bottom-center { content: none; background-color: transparent; border-top: none; }
    }
    body { font: 11pt Arial; margin: 0; }
    h1 { color: #fff; font-size: 26pt; margin: 3in 0 0; text-align: center; }
    h2 { color: #1a2a6c; break-before: page; }
    </style>
    </head>
    <body>
      <h1>Annual Report</h1>
      <h2>Chapter One</h2>
    """ +
    string.Concat(Enumerable.Range(1, 30).Select(i =>
        $"<p>Line {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>")) +
    """
    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_page_and_margin_box_background", "Paged Media",
    "Page & Margin Box Background/Border",
    "css-page-3 §3.1's @page box background (a cover page tinted independently of the body pages via "
    + "@page :first, painted below the CSS2.1 canvas fill and the document's own content) plus a footer "
    + "margin box with its own background-color and border-top. The cover page also gives the page box "
    + "itself a border and padding (issue #1147), genuinely reserving layout space so the cover's own "
    + "heading sits inset from the border rather than under it, with background-clip: content-box "
    + "confining the cover gradient to inside the padding.",
    pageAndMarginBoxBackgroundHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Flexbox showcase ──────────────────────────────────────────────────

static string FItem(string label, string color, string extraCss = "") =>
    $"<div style=\"background:{color};color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;{extraCss}\">{label}</div>";

static string FContainer(string desc, string containerCss, string itemsHtml) =>
    $"<tr><td style=\"font:7pt Arial;color:#333;padding:2px 4px 2px 0;white-space:nowrap\">{desc}</td>" +
    $"<td style=\"padding:2px\"><div style=\"display:flex;border:1px solid #bbb;background:#f8f8f8;min-height:22px;{containerCss}\">{itemsHtml}</div></td>" +
    $"<td style=\"font:5.5pt Arial;color:#888;padding:2px 4px;word-break:break-all\">{containerCss}</td></tr>";

static string FSection(string title, string rows) =>
    $"<h2>{title}</h2>" +
    "<table style=\"width:100%;border-collapse:collapse;margin-bottom:6px\">" +
    "<col style=\"width:90px\"><col><col style=\"width:100px\">" +
    rows + "</table>";

static string FItems3(string extraCss = "") =>
    FItem("A", "#e74c3c", extraCss) + FItem("B", "#3498db", extraCss) + FItem("C", "#27ae60", extraCss);


const string FlexCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 9pt; margin: 0.7em 0 0.2em; padding-bottom: 2px; border-bottom: 1px solid #ccc; color: #333; break-after: avoid }
    </style>
    """;

var flexHtml = "<!DOCTYPE html><html><head>" + FlexCss + "</head><body>" +

    "<h1>CSS Flexbox Test Page</h1>" +

    FSection("1 — flex-direction",
        FContainer("row (default)", "flex-direction:row;gap:4px;", FItems3()) +
        FContainer("row-reverse", "flex-direction:row-reverse;gap:4px;", FItems3()) +
        FContainer("column", "flex-direction:column;gap:2px;width:60px;", FItems3()) +
        FContainer("column-reverse", "flex-direction:column-reverse;gap:2px;width:60px;", FItems3())
    ) +

    FSection("2 — justify-content (row, 240px container)",
        FContainer("flex-start", "justify-content:flex-start;width:240px;gap:4px;",   FItems3("width:50px;")) +
        FContainer("start",      "justify-content:start;width:240px;gap:4px;",         FItems3("width:50px;")) +
        FContainer("center",     "justify-content:center;width:240px;gap:4px;",        FItems3("width:50px;")) +
        FContainer("flex-end",   "justify-content:flex-end;width:240px;gap:4px;",      FItems3("width:50px;")) +
        FContainer("right",      "justify-content:right;width:240px;gap:4px;",         FItems3("width:50px;")) +
        FContainer("space-between","justify-content:space-between;width:240px;",       FItems3("width:50px;")) +
        FContainer("space-around", "justify-content:space-around;width:240px;",        FItems3("width:50px;")) +
        FContainer("space-evenly", "justify-content:space-evenly;width:240px;",        FItems3("width:50px;"))
    ) +

    FSection("3 — align-items (row, 80px container height)",
        FContainer("flex-start", "align-items:flex-start;height:80px;gap:4px;",  FItems3("width:50px;height:28px;")) +
        FContainer("self-start", "align-items:self-start;height:80px;gap:4px;",  FItems3("width:50px;height:28px;")) +
        FContainer("center",     "align-items:center;height:80px;gap:4px;",       FItems3("width:50px;height:28px;")) +
        FContainer("flex-end",   "align-items:flex-end;height:80px;gap:4px;",     FItems3("width:50px;height:28px;")) +
        FContainer("self-end",   "align-items:self-end;height:80px;gap:4px;",     FItems3("width:50px;height:28px;")) +
        FContainer("stretch",    "align-items:stretch;height:80px;gap:4px;",      FItems3("width:50px;")) +
        FContainer("baseline (varying font sizes)", "align-items:baseline;gap:4px;",
            FItem("A", "#e74c3c", "font-size:8pt;width:50px;") +
            FItem("B", "#3498db", "font-size:16pt;width:50px;") +
            FItem("C", "#27ae60", "font-size:24pt;width:50px;"))
    ) +

    FSection("4 — flex-grow",
        FContainer("grow:0,0,0 (none)", "gap:4px;",
            FItem("A grow:0", "#e74c3c", "flex-grow:0;width:50px;") +
            FItem("B grow:0", "#3498db", "flex-grow:0;width:50px;") +
            FItem("C grow:0", "#27ae60", "flex-grow:0;width:50px;")) +
        FContainer("grow:1,1,1 (equal)", "gap:4px;",
            FItem("A 1", "#e74c3c", "flex-grow:1;") +
            FItem("B 1", "#3498db", "flex-grow:1;") +
            FItem("C 1", "#27ae60", "flex-grow:1;")) +
        FContainer("grow:1,2,3 (ratio)", "gap:4px;",
            FItem("A 1", "#e74c3c", "flex-grow:1;") +
            FItem("B 2", "#3498db", "flex-grow:2;") +
            FItem("C 3", "#27ae60", "flex-grow:3;"))
    ) +

    FSection("5 — flex-shrink &amp; flex-basis",
        FContainer("shrink:1,1 basis:120px (overflows 240px)", "width:240px;",
            FItem("A 120", "#e74c3c", "flex-basis:120px;flex-shrink:1;") +
            FItem("B 120", "#3498db", "flex-basis:120px;flex-shrink:1;") +
            FItem("C 120", "#27ae60", "flex-basis:120px;flex-shrink:1;")) +
        FContainer("shrink:1,0 (C does not shrink)", "width:240px;",
            FItem("A shr:1", "#e74c3c", "flex-basis:120px;flex-shrink:1;") +
            FItem("B shr:1", "#3498db", "flex-basis:120px;flex-shrink:1;") +
            FItem("C shr:0", "#27ae60", "flex-basis:80px;flex-shrink:0;")) +
        FContainer("flex:1 shorthand", "gap:4px;",
            FItem("flex:1", "#e74c3c", "flex:1;") +
            FItem("flex:2", "#3498db", "flex:2;") +
            FItem("flex:none", "#27ae60", "flex:none;width:50px;"))
    ) +

    FSection("6 — flex-wrap",
        FContainer("nowrap (overflow)", "flex-wrap:nowrap;width:200px;",
            FItems3("width:90px;")) +
        FContainer("wrap", "flex-wrap:wrap;width:200px;gap:4px;",
            FItems3("width:90px;height:24px;")) +
        FContainer("wrap-reverse", "flex-wrap:wrap-reverse;width:200px;gap:4px;",
            FItems3("width:90px;height:24px;")) +
        FContainer("wrap-reverse, unequal line heights", "flex-wrap:wrap-reverse;width:200px;gap:4px;",
            FItem("A 12px", "#e74c3c", "width:90px;height:12px;") +
            FItem("B 40px", "#3498db", "width:90px;height:40px;") +
            FItem("C 24px", "#27ae60", "width:90px;height:24px;")) +
        // wrap-reverse swaps cross-start and cross-end inside a line as well as between them: A shares a
        // line with the taller B, so flex-start puts it against that line's bottom and flex-end against
        // its top - the mirror of what the same two values do without wrap-reverse. The items are narrow
        // enough (80px + FItem's 12px of padding = 92px, twice, inside 200px) for two to share a line;
        // one item per line has nowhere else to be and would show none of this.
        FContainer("wrap-reverse + align-items:flex-start", "flex-wrap:wrap-reverse;align-items:flex-start;width:200px;gap:4px;",
            FItem("A 12px", "#e74c3c", "width:80px;height:12px;") +
            FItem("B 40px", "#3498db", "width:80px;height:40px;") +
            FItem("C 24px", "#27ae60", "width:80px;height:24px;")) +
        FContainer("wrap-reverse + align-items:flex-end", "flex-wrap:wrap-reverse;align-items:flex-end;width:200px;gap:4px;",
            FItem("A 12px", "#e74c3c", "width:80px;height:12px;") +
            FItem("B 40px", "#3498db", "width:80px;height:40px;") +
            FItem("C 24px", "#27ae60", "width:80px;height:24px;")) +
        // align-content's initial value is `normal`, which behaves as `stretch`: with a definite
        // container height and free cross space, the two lines grow to fill it rather than packing at
        // the top and leaving the space below empty, which explicit align-content:flex-start still does
        // (issue #461).
        FContainer("wrap, height:120px, align-content unset (stretches)", "flex-wrap:wrap;width:200px;height:120px;gap:4px;",
            FItems3("width:90px;height:24px;")) +
        FContainer("wrap, height:120px, align-content:flex-start", "flex-wrap:wrap;align-content:flex-start;width:200px;height:120px;gap:4px;",
            FItems3("width:90px;height:24px;")) +
        FContainer("wrap, height:120px, align-content:start", "flex-wrap:wrap;align-content:start;width:200px;height:120px;gap:4px;",
            FItems3("width:90px;height:24px;")) +
        FContainer("wrap, height:120px, align-content:baseline (falls back to start)", "flex-wrap:wrap;align-content:baseline;width:200px;height:120px;gap:4px;",
            FItems3("width:90px;height:24px;"))
    ) +

    FSection("7 — align-self (overrides align-items)",
        FContainer("align-items:flex-start, item B → flex-end, item D → self-end",
            "align-items:flex-start;height:80px;gap:4px;",
            FItem("A start", "#e74c3c", "width:50px;height:28px;") +
            FItem("B end", "#3498db", "width:50px;height:28px;align-self:flex-end;") +
            FItem("C center", "#27ae60", "width:50px;height:28px;align-self:center;") +
            FItem("D self-end", "#8e44ad", "width:50px;height:28px;align-self:self-end;"))
    ) +

    FSection("7b — column cross-axis alignment (align-items / align-self)",
        FContainer("column, align-items:center", "flex-direction:column;align-items:center;width:200px;gap:4px;",
            FItem("centered", "#e74c3c") + FItem("also centered", "#3498db")) +
        FContainer("column, align-items:flex-end", "flex-direction:column;align-items:flex-end;width:200px;gap:4px;",
            FItem("right", "#e74c3c") + FItem("also right", "#3498db")) +
        FContainer("column, center + one self:flex-start", "flex-direction:column;align-items:center;width:200px;gap:4px;",
            FItem("centered", "#e74c3c") + FItem("self-start", "#27ae60", "align-self:flex-start;")) +
        FContainer("column, align-items:stretch (default → full width)", "flex-direction:column;align-items:stretch;width:200px;gap:4px;",
            FItem("full width", "#3498db"))
    ) +

    FSection("8 — order",
        FContainer("DOM order: A B C (order: 3 1 2)", "gap:4px;",
            FItem("A order:3", "#e74c3c", "order:3;width:50px;") +
            FItem("B order:1", "#3498db", "order:1;width:50px;") +
            FItem("C order:2", "#27ae60", "order:2;width:50px;"))
    ) +

    FSection("9 — Nested flex containers",
        "<tr><td colspan='3' style='padding:2px'>" +
        "<div style='display:flex;gap:8px;'>" +
        "  <div style='display:flex;flex-direction:column;gap:4px;flex:1;border:1px solid #bbb;padding:4px;background:#f8f8f8;'>" +
        "    <div style='background:#e74c3c;color:#fff;font:7pt Arial;padding:3px;text-align:center'>Col A row 1</div>" +
        "    <div style='background:#c0392b;color:#fff;font:7pt Arial;padding:3px;text-align:center'>Col A row 2</div>" +
        "  </div>" +
        "  <div style='display:flex;flex-direction:column;gap:4px;flex:2;border:1px solid #bbb;padding:4px;background:#f8f8f8;'>" +
        "    <div style='display:flex;gap:4px;'>" +
        "      <div style='background:#3498db;color:#fff;font:7pt Arial;padding:3px;flex:1;text-align:center'>B1</div>" +
        "      <div style='background:#2980b9;color:#fff;font:7pt Arial;padding:3px;flex:1;text-align:center'>B2</div>" +
        "    </div>" +
        "    <div style='background:#1abc9c;color:#fff;font:7pt Arial;padding:3px;text-align:center'>Col B row 2 (full width)</div>" +
        "  </div>" +
        "</div>" +
        "</td></tr>"
    ) +

    FSection("10 — inline-flex",
        "<tr><td colspan='3' style='padding:2px;font:8pt Arial'>" +
        "Text before " +
        "<span style='display:inline-flex;gap:3px;vertical-align:middle;border:1px solid #bbb;padding:2px;'>" +
        "  <span style='background:#e74c3c;color:#fff;font:6pt Arial;padding:2px 4px;'>R</span>" +
        "  <span style='background:#3498db;color:#fff;font:6pt Arial;padding:2px 4px;'>G</span>" +
        "  <span style='background:#27ae60;color:#fff;font:6pt Arial;padding:2px 4px;'>B</span>" +
        "</span>" +
        " text after — inline-flex sits in the text flow." +
        // A wrapping inline-flex holding block-level items: its children belong to the flex formatting
        // context inside it, not to the line the box itself sits on, so both rows stay in the box.
        "<div style='margin-top:5px'>Wrapping inline-flex with block-level items: " +
        "<span style='display:inline-flex;flex-wrap:wrap;width:120px;gap:2px;vertical-align:middle;border:1px solid #bbb;padding:2px;'>" +
        "  <div style='background:#e74c3c;color:#fff;font:6pt Arial;padding:2px;width:110px;'>row 1</div>" +
        "  <div style='background:#3498db;color:#fff;font:6pt Arial;padding:2px;width:110px;height:16px;'>row 2</div>" +
        "</span>" +
        " text after.</div>" +
        "</td></tr>"
    ) +

    FSection("11 — max-width / max-height clamping",
        FContainer("A max-width:80px, both grow:1", "gap:4px;",
            FItem("A", "#e74c3c", "flex-grow:1;max-width:80px;") +
            FItem("B", "#3498db", "flex-grow:1;")) +
        FContainer("column: A max-height:50px, both grow:1 (160px tall)", "flex-direction:column;height:160px;gap:4px;width:80px;",
            FItem("A", "#e74c3c", "flex-grow:1;max-height:50px;") +
            FItem("B", "#3498db", "flex-grow:1;"))
    ) +

    FSection("12 — Percentage flex-basis",
        FContainer("flex-basis:50% (240px container)", "width:240px;",
            FItem("A 50%", "#e74c3c", "flex-basis:50%;flex-grow:0;flex-shrink:0;")) +
        FContainer("column, auto height: flex-basis:50% falls back to content", "flex-direction:column;width:80px;",
            FItem("A 50%", "#e74c3c", "flex-basis:50%;"))
    ) +

    FSection("13 — flex-basis: content",
        FContainer("flex-basis:content ignores explicit width:150px", "gap:4px;",
            FItem("Hi", "#e74c3c", "flex-basis:content;width:150px;flex-grow:0;flex-shrink:0;"))
    ) +

    FSection("14 — Auto margins (main axis)",
        FContainer("margin-left:auto pushes item to the end", "width:240px;",
            FItem("A", "#e74c3c", "margin-left:auto;width:50px;")) +
        FContainer("margin:0 auto centers a single item", "width:240px;",
            FItem("A", "#e74c3c", "margin:0 auto;width:50px;")) +
        FContainer("second item margin-left:auto pushes items apart", "width:240px;",
            FItem("A", "#e74c3c", "width:50px;") +
            FItem("B", "#3498db", "margin-left:auto;width:50px;"))
    ) +

    // An auto margin on the cross axis absorbs the line's free cross space (Flexbox §8.1, §9.4 step 11):
    // it overrides align-self, and the item is not stretched. Each row pairs the auto-margined item (A)
    // with a plain sibling (B) so the difference is visible against a reference.
    FSection("14b — Auto margins (cross axis)",
        FContainer("row: margin-top:auto pushes A to the bottom", "height:70px;gap:4px;",
            FItem("A", "#e74c3c", "width:50px;height:24px;margin-top:auto;") +
            FItem("B", "#3498db", "width:50px;height:24px;")) +
        FContainer("row: margin:auto 0 centers A vertically", "height:70px;gap:4px;",
            FItem("A", "#e74c3c", "width:50px;height:24px;margin:auto 0;") +
            FItem("B", "#3498db", "width:50px;height:24px;")) +
        FContainer("row: margin-bottom:auto holds A at the top of its line under wrap-reverse", "height:70px;gap:4px;flex-wrap:wrap-reverse;align-items:flex-start;",
            FItem("A", "#e74c3c", "width:50px;height:24px;margin-bottom:auto;") +
            FItem("B", "#3498db", "width:50px;height:24px;")) +
        FContainer("column: margin-left:auto pushes A to the right", "flex-direction:column;width:240px;gap:2px;",
            FItem("A", "#e74c3c", "width:60px;margin-left:auto;") +
            FItem("B", "#3498db", "width:60px;")) +
        FContainer("column: margin:0 auto centers A", "flex-direction:column;width:240px;gap:2px;",
            FItem("A", "#e74c3c", "width:60px;margin:0 auto;") +
            FItem("B", "#3498db", "width:60px;")) +
        FContainer("column: an auto margin overrides align-self:stretch (A shrinks to its content)", "flex-direction:column;width:240px;gap:2px;",
            FItem("A", "#e74c3c", "margin-left:auto;align-self:stretch;") +
            FItem("B", "#3498db", "align-self:stretch;"))
    ) +

    FSection("15 — Replaced elements (img/svg) mixed with block siblings",
        "<tr><td colspan='3' style='padding:2px 4px 6px;font:7pt Arial;color:#555'>" +
        "A flex container mixing an inline-level replaced element (an &lt;img&gt; or inline &lt;svg&gt;) " +
        "with a block-level sibling wraps the replaced element in an anonymous box per CSS Flexbox §4 — " +
        "it must still be measured, positioned, and painted like any other flex item." +
        "</td></tr>" +
        "<tr><td style=\"font:7pt Arial;color:#333;padding:2px 4px 2px 0;white-space:nowrap\">img + block title</td>" +
        "<td style=\"padding:2px\"><div style=\"display:flex;align-items:center;gap:8px;border:1px solid #bbb;background:#f8f8f8;padding:4px;\">" +
        "<img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\" width=\"48\" height=\"48\" style=\"background:#e74c3c\" />" +
        "<div style=\"font:bold 9pt Arial;color:#222\">Account Statement</div>" +
        "</div></td>" +
        "<td style=\"font:5.5pt Arial;color:#888;padding:2px 4px\">img width=48 height=48</td></tr>" +
        "<tr><td style=\"font:7pt Arial;color:#333;padding:2px 4px 2px 0;white-space:nowrap\">inline svg + block title</td>" +
        "<td style=\"padding:2px\"><div style=\"display:flex;align-items:center;gap:8px;border:1px solid #bbb;background:#f8f8f8;padding:4px;\">" +
        "<svg width=\"32\" height=\"32\"><circle cx=\"16\" cy=\"16\" r=\"16\" fill=\"#27ae60\" /></svg>" +
        "<div style=\"font:bold 9pt Arial;color:#222\">Status: Active</div>" +
        "</div></td>" +
        "<td style=\"font:5.5pt Arial;color:#888;padding:2px 4px\">inline &lt;svg&gt;</td></tr>"
    ) +

    "</body></html>";

await SaveShowcaseAsync("flexbox", "Layout", "Flexbox",
    "Flexbox layout: direction, wrapping, justification (including start/right and self-start/self-end), alignment, gaps, auto margins on both axes, flexible item sizing, and replaced elements (img/svg) as flex items.",
    flexHtml, pdfConfig);

// ─── CSS Custom Properties (var()) showcase ─────────────────────────────────

static string VarSwatch(string desc, string boxCss, string valueLabel, string cssLabel) =>
    "<td>" +
    $"<div class=\"vbox\" style=\"{boxCss}\">Aa</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"val\">{valueLabel}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string VarCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .vbox { height: 44px; display: flex; align-items: center; justify-content: center; font: bold 9pt Arial; margin-bottom: 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .val { font-size: 6pt; font-family: monospace; color: #b8860b; font-weight: bold; margin-bottom: 2px; word-break: break-all }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    .card { border-radius: 8px; padding: 14px }
    .card h3 { margin: 0 0 6px; font-size: 11pt }
    .card p { margin: 0 0 8px; font-size: 8pt; line-height: 1.4 }
    .card button { border: none; border-radius: 4px; padding: 6px 14px; font: bold 8pt Arial }
    .cardval { font-size: 6pt; font-family: monospace; color: #b8860b; font-weight: bold; margin-bottom: 2px }
    </style>
    """;

var varHtml = "<!DOCTYPE html><html><head>" + VarCss + "</head><body>" +

    "<h1>CSS Custom Properties &amp; var() Test Page</h1>" +

    "<h2>1 — Basic Declaration &amp; Usage</h2>" +
    "<p class=\"intro\">--main-bg, --main-color and --main-border are declared once on the surrounding container and consumed via var() on each box.</p>" +
    "<div style=\"--main-bg: #2c3e50; --main-color: white; --main-border: 3px solid #1a252f;\">" +
    Row(
        VarSwatch("background + color + border", "background: var(--main-bg); color: var(--main-color); border: var(--main-border);", "--main-bg: #2c3e50 · --main-color: white · --main-border: 3px solid #1a252f", "background: var(--main-bg); color: var(--main-color); border: var(--main-border)"),
        VarSwatch("reused for border only", "border: var(--main-border); color: var(--main-bg); background: white;", "--main-border: 3px solid #1a252f", "border: var(--main-border)"),
        VarSwatch("reused for text color", "color: var(--main-bg); border: 1px solid #ccc; background: white;", "--main-bg: #2c3e50", "color: var(--main-bg)"),
        VarSwatch("literal (no var, for comparison)", "background: #2c3e50; color: white; border: 3px solid #1a252f;", "(no custom property used)", "background: #2c3e50 (literal)")
    ) +
    "</div>" +

    "<h2>2 — Fallback Values</h2>" +
    "<p class=\"intro\">Each box references a custom property that was never declared; the fallback (second argument to var()) is used instead.</p>" +
    Row(
        VarSwatch("color fallback", "background: var(--undefined-bg, #8e44ad); color: white;", "--undefined-bg: (not declared) → fallback #8e44ad", "background: var(--undefined-bg, #8e44ad)"),
        VarSwatch("length fallback", "background: #16a085; color: white; padding: var(--undefined-padding, 16px);", "--undefined-padding: (not declared) → fallback 16px", "padding: var(--undefined-padding, 16px)"),
        VarSwatch("nested fallback chain", "background: var(--undefined-a, var(--undefined-b, #d35400)); color: white;", "--undefined-a, --undefined-b: (not declared) → fallback #d35400", "var(--a, var(--b, #d35400))"),
        VarSwatch("no fallback (uses initial)", "background: var(--totally-undefined); border: 1px dashed #999;", "--totally-undefined: (not declared) → initial value", "background: var(--totally-undefined)")
    ) +

    "<h2>3 — Inheritance &amp; Local Override</h2>" +
    "<p class=\"intro\">--accent is declared once on the outer container. The second box overrides it locally; the override does not leak to its siblings.</p>" +
    "<div style=\"--accent: #2980b9;\">" +
    Row(
        VarSwatch("inherited (no override)", "background: var(--accent); color: white;", "--accent: #2980b9 (inherited)", "background: var(--accent) → inherited"),
        VarSwatch("local override", "--accent: #c0392b; background: var(--accent); color: white;", "--accent: #c0392b (local override)", "--accent: #c0392b (local)"),
        VarSwatch("sibling still inherits original", "background: var(--accent); color: white;", "--accent: #2980b9 (inherited, unaffected)", "background: var(--accent) → unaffected"),
        VarSwatch("--accent: unset (still inherits)", "--accent: unset; background: var(--accent); color: white;", "--accent: #2980b9 (via unset → inherit)", "--accent: unset; background: var(--accent)")
    ) +
    "</div>" +

    "<h2>4 — Cyclic References Resolve Safely</h2>" +
    "<p class=\"intro\">Per spec, a custom property that references itself (directly or through a chain) becomes invalid instead of looping forever; a fallback or the property's initial value is used.</p>" +
    Row(
        VarSwatch("direct cycle, with fallback", "--loop-a: var(--loop-b); --loop-b: var(--loop-a); background: var(--loop-a, #7f8c8d); color: white;", "--loop-a ↔ --loop-b: cyclic → invalid → fallback #7f8c8d", "--loop-a: var(--loop-b); --loop-b: var(--loop-a); background: var(--loop-a, #7f8c8d)"),
        VarSwatch("self-reference — always invalid", "--self: var(--self, #e74c3c); background: var(--self); border: 1px dashed #999;", "--self: self-referential → invalid (the fallback inside --self's OWN definition does not rescue it — matches Chrome/Firefox)", "--self: var(--self, #e74c3c); background: var(--self) (no fallback here)"),
        VarSwatch("one-directional chain (not cyclic)", "--chain-a: var(--chain-b); --chain-b: #27ae60; background: var(--chain-a); color: white;", "--chain-a → --chain-b: #27ae60 (resolved, not cyclic)", "--a: var(--b); --b: #27ae60 → resolves"),
        VarSwatch("multi-hop chain", "--x1: var(--x2); --x2: var(--x3); --x3: #f39c12; background: var(--x1); color: white;", "--x1 → --x2 → --x3: #f39c12 (resolved)", "--x1→--x2→--x3: #f39c12")
    ) +

    "<h2>5 — Real-World Example: Themeable Card Component</h2>" +
    "<p class=\"intro\">The same card markup and CSS rules render two different themes purely by changing custom property values on the wrapping element — no duplicated rules.</p>" +
    """
    <style>
    .card {
      --card-bg: white;
      --card-fg: #222;
      --card-accent: #2c3e50;
      --card-muted: #666;
      background: var(--card-bg);
      color: var(--card-fg);
      border: 1px solid var(--card-accent);
    }
    .card h3 { color: var(--card-accent); }
    .card p { color: var(--card-muted); }
    .card button { background: var(--card-accent); color: var(--card-bg); }
    </style>
    """ +
    "<table class=\"sw\"><tr>" +
    "<td style=\"width:50%\">" +
        "<div class=\"card\">" +
            "<h3>Light Theme</h3>" +
            "<p>This card uses the component's default custom property values.</p>" +
            "<button>Learn More</button>" +
        "</div>" +
        "<div class=\"cardval\">--card-bg: white · --card-fg: #222 · --card-accent: #2c3e50 · --card-muted: #666 (defaults)</div>" +
    "</td>" +
    "<td style=\"width:50%\">" +
        "<div class=\"card\" style=\"--card-bg: #1a1a2e; --card-fg: #eee; --card-accent: #e94560; --card-muted: #aaa;\">" +
            "<h3>Dark Theme</h3>" +
            "<p>Same markup and rules — only the custom property values differ.</p>" +
            "<button>Learn More</button>" +
        "</div>" +
        "<div class=\"cardval\">--card-bg: #1a1a2e · --card-fg: #eee · --card-accent: #e94560 · --card-muted: #aaa (overridden inline)</div>" +
    "</td>" +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("custom_properties", "CSS Values & Functions", "Custom Properties",
    "CSS custom properties resolved through var(), including fallbacks and cascading overrides.",
    varHtml, pdfConfig);

// --- CSS transform / transform-origin showcase ---

static string TransformSwatch(string desc, string transformCss, string extraCss = "") =>
    "<td>" +
    $"<div class=\"tbox\" style=\"transform: {transformCss};{extraCss}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">transform: {transformCss}</div>" +
    "</td>";

const string TransformCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; padding-bottom: 150px; vertical-align: top; width: 25%; text-align: center }
    .tbox { width: 100px; height: 56px; background: steelblue; border: 2px solid #1a6b8a; margin: 75px auto 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var transformHtml = "<!DOCTYPE html><html><head>" + TransformCss + "</head><body>" +

    "<h1>CSS transform / transform-origin Test Page</h1>" +

    "<h2>1 — Individual 2D Functions</h2>" +
    Row(
        TransformSwatch("translate", "translate(20px, 10px)"),
        TransformSwatch("scale", "scale(1.4)"),
        TransformSwatch("rotate", "rotate(25deg)"),
        TransformSwatch("skew", "skew(15deg, 5deg)")
    ) +

    "<h2>2 — Composition Order Matters</h2>" +
    "<p class=\"intro\">The same two functions, written in opposite order, produce visibly different results — the last-written function is applied first (closest to the element), the first-written function is applied last.</p>" +
    Row(
        TransformSwatch("translate then rotate — spins in place, then shifts", "translate(30px, 0) rotate(45deg)"),
        TransformSwatch("rotate then translate — orbits around the origin", "rotate(45deg) translate(30px, 0)"),
        TransformSwatch("scale then translate", "scale(1.3) translate(15px, 0)"),
        TransformSwatch("translate then scale", "translate(15px, 0) scale(1.3)")
    ) +

    "<h2>3 — transform-origin Pivot Point</h2>" +
    "<p class=\"intro\">The same rotate(45deg) pivoting around different origins.</p>" +
    Row(
        TransformSwatch("origin: center (default)", "rotate(45deg)"),
        TransformSwatch("origin: top left", "rotate(45deg)", "transform-origin: 0 0;"),
        TransformSwatch("origin: bottom right", "rotate(45deg)", "transform-origin: 100% 100%;"),
        TransformSwatch("origin: 25% 75%", "rotate(45deg)", "transform-origin: 25% 75%;")
    ) +

    "<h2>4 — matrix() Passthrough</h2>" +
    Row(
        TransformSwatch("identity matrix", "matrix(1, 0, 0, 1, 0, 0)"),
        TransformSwatch("translate via matrix", "matrix(1, 0, 0, 1, 20, 10)"),
        TransformSwatch("scale via matrix", "matrix(1.3, 0, 0, 1.3, 0, 0)"),
        TransformSwatch("skew via matrix", "matrix(1, 0.3, 0, 1, 0, 0)")
    ) +

    "<h2>5 — 3D Rotations (no perspective)</h2>" +
    "<p class=\"intro\">3D rotations project onto the flat page as an axis-aligned foreshortening (narrower/shorter), with no vanishing point - PeachPDF does not support perspective(), so there's never a tapered/trapezoidal look.</p>" +
    Row(
        TransformSwatch("rotateX(50deg)", "rotateX(50deg)"),
        TransformSwatch("rotateY(50deg)", "rotateY(50deg)"),
        TransformSwatch("rotate3d(1,1,0,45deg)", "rotate3d(1, 1, 0, 45deg)"),
        TransformSwatch("translateZ (no visible effect)", "translateZ(300px)")
    ) +

    "<h2>6 — Combined With Other Features</h2>" +
    Row(
        TransformSwatch("+ border-radius", "rotate(15deg)", "border-radius: 12px;"),
        TransformSwatch("+ gradient background", "rotate(-10deg) scale(1.2)", "background: linear-gradient(to right, #e74c3c, #3498db);"),
        "<td>" +
            "<div class=\"tbox\" style=\"transform: rotate(12deg); color: white; font-size: 7pt;\">Hello</div>" +
            "<div class=\"desc\">+ text content (whole subtree transforms)</div>" +
            "<div class=\"css\">transform: rotate(12deg)</div>" +
        "</td>",
        TransformSwatch("multi-function chain", "translate(10px, 0) rotate(20deg) scale(1.2)")
    ) +

    "<h2>7 — Radial Gradients Under a Transform</h2>" +
    "<p class=\"intro\">An <b>elliptical</b> radial gradient is the one gradient shape whose PDF shading is written as a unit circle plus a matrix, so it is also the one that has to carry the element's transform through that matrix. The first two swatches are the same gradient with and without a transform; if the transform were dropped, the second would lose its highlight and fill flat with the gradient's outer color.</p>" +
    Row(
        TransformSwatch("elliptical radial, no transform", "none", "background: radial-gradient(ellipse, #fff9c4, #f57f17);"),
        TransformSwatch("elliptical radial + rotate/scale", "rotate(-12deg) scale(1.25)", "background: radial-gradient(ellipse, #fff9c4, #f57f17);"),
        TransformSwatch("circular radial + rotate/scale", "rotate(-12deg) scale(1.25)", "background: radial-gradient(circle, #fff9c4, #f57f17);"),
        TransformSwatch("off-center ellipse + skew", "skew(12deg, 4deg)", "background: radial-gradient(ellipse at 30% 35%, #e0f7fa, #006064);")
    ) +

    "</body></html>";

await SaveShowcaseAsync("transform", "Graphics & Effects", "Transforms",
    "CSS transforms - translate, rotate, scale, skew - with transform-origin control.",
    transformHtml, pdfConfig);

// --- CSS calc() / min() / max() / clamp() showcase ---

static string CalcSwatch(string desc, string css) =>
    "<td>" +
    $"<div class=\"cbox\" style=\"{css}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{css}</div>" +
    "</td>";

const string CalcCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25%; text-align: center }
    .cbox { height: 40px; background: steelblue; border: 2px solid #1a6b8a; margin: 0 auto 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    .wrap-200 { width: 200px; border: 1px dashed #999; margin: 0 auto; padding: 4px }
    </style>
    """;

var calcHtml = "<!DOCTYPE html><html><head>" + CalcCss + "</head><body>" +

    "<h1>CSS calc() / min() / max() / clamp() Test Page</h1>" +

    "<h2>1 — Basic Arithmetic</h2>" +
    Row(
        CalcSwatch("addition", "width: calc(100px + 40px);"),
        CalcSwatch("subtraction", "width: calc(200px - 60px);"),
        CalcSwatch("multiplication", "width: calc(20px * 4);"),
        CalcSwatch("division", "width: calc(200px / 4);")
    ) +

    "<h2>2 — Mixed Units &amp; Percentages</h2>" +
    "<p class=\"intro\">calc(1em + 5px) resolves the em against the element's own font-size; calc(100% - 40px) resolves the percentage against the 200px dashed container below.</p>" +
    Row(
        "<td>" +
            "<div class=\"wrap-200\"><div class=\"cbox\" style=\"font-size: 16px; width: calc(1em + 5px);\"></div></div>" +
            "<div class=\"desc\">1em + 5px @ 16px font</div>" +
            "<div class=\"css\">width: calc(1em + 5px)</div>" +
        "</td>",
        "<td>" +
            "<div class=\"wrap-200\"><div class=\"cbox\" style=\"width: calc(100% - 40px);\"></div></div>" +
            "<div class=\"desc\">100% - 40px in a 200px container</div>" +
            "<div class=\"css\">width: calc(100% - 40px)</div>" +
        "</td>"
    ) +

    "<h2>3 — Nested calc() and Parentheses</h2>" +
    Row(
        CalcSwatch("nested calc()", "width: calc(calc(50px + 50px) * 2);"),
        CalcSwatch("parenthesized grouping", "width: calc((50px + 50px) * 2);")
    ) +

    "<h2>4 — margin / padding / border-radius / height</h2>" +
    Row(
        CalcSwatch("margin-left", "margin-left: calc(20px + 10px); width: 60px;"),
        CalcSwatch("padding (widens box)", "padding: calc(5px + 5px); width: 60px;"),
        CalcSwatch("border-radius", "border-radius: calc(10px + 10px); width: 80px;"),
        CalcSwatch("height", "height: calc(20px + 20px); width: 80px;")
    ) +

    "<h2>5 — Negative Result</h2>" +
    "<p class=\"intro\">PeachPDF doesn't clamp a negative calc() result to zero, matching how a plain negative length is already handled.</p>" +
    Row(
        CalcSwatch("calc(50px - 100px)", "width: calc(50px - 100px); height: 20px; border: 1px dashed red;")
    ) +

    "<h2>6 — min() / max() / clamp()</h2>" +
    Row(
        CalcSwatch("min(150px, 100px)", "width: min(150px, 100px);"),
        CalcSwatch("max(150px, 100px)", "width: max(150px, 100px);"),
        CalcSwatch("clamp(50px, 300px, 150px)", "width: clamp(50px, 300px, 150px);"),
        CalcSwatch("clamp(50px, 10px, 150px)", "width: clamp(50px, 10px, 150px);")
    ) +

    "<h2>7 — calc() Combined With a Custom Property</h2>" +
    Row(
        "<td>" +
            "<div class=\"wrap-200\"><div class=\"cbox\" style=\"--gap: 20px; width: calc(100% - var(--gap));\"></div></div>" +
            "<div class=\"desc\">calc() referencing a custom property</div>" +
            "<div class=\"css\">width: calc(100% - var(--gap))</div>" +
        "</td>"
    ) +

    "</body></html>";

await SaveShowcaseAsync("calc", "CSS Values & Functions", "calc() & Math Functions",
    "calc(), min(), max(), and clamp() expressions resolving against real box dimensions.",
    calcHtml, pdfConfig);

// ─── SVG showcase ────────────────────────────────────────────────────────────

static string SvgSwatch(string desc, string svgMarkup, string label) =>
    "<td>" +
    $"<div class=\"sbox\">{svgMarkup}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{label}</div>" +
    "</td>";

static string PeachSwatch(string title, string svgMarkup, string blurb) =>
    "<td style=\"width:50%\">" +
    "<div style=\"page-break-inside:avoid\">" +
    $"<div class=\"peach-box\">{svgMarkup}</div>" +
    $"<h3 style=\"margin:4px 0 2px;font-size:9pt;text-align:center\">{title}</h3>" +
    $"<p style=\"margin:0;font-size:7pt;color:#666;text-align:center\">{blurb}</p>" +
    "</div>" +
    "</td>";

const string SvgShowcaseCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25%; text-align: center }
    .sbox { height: 90px; border: 1px solid #ccc; margin-bottom: 3px; display: flex; align-items: center; justify-content: center; background: #fafafa }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all; font-family: monospace }
    .peach-box { border: 1px solid #e0c9a6; margin-bottom: 4px; background: #fffdf7; text-align: center; padding: 10px 0 }
    </style>
    """;

// Shared point lists (5-point star / pentagon) reused across several swatches.
const string StarPoints = "50,12 58.8,37.9 86.1,38.3 64.3,54.6 72.3,80.7 50,65 27.7,80.7 35.7,54.6 13.9,38.3 41.2,37.9";
const string PentagonPoints = "50,12 86,38 72,81 28,81 14,38";

// Shared between the "inline <svg>" and "<img src=data:...>" swatches below, to prove both
// rendering paths take the exact same markup. Namespaces are declared (harmless for the inline
// case, which doesn't need them) because the <img> path parses this as standalone XML via
// XDocument, which requires the "xlink" prefix to be declared before xlink:href can be used.
var parityMarkup =
    $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 100 100" width="80" height="80"><defs><polygon id="parityStar" points="{StarPoints}"/><linearGradient id="lgParity" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#a1c4fd"/><stop offset="1" stop-color="#c2e9fb"/></linearGradient><clipPath id="clipParity"><use xlink:href="#parityStar"/></clipPath></defs><g clip-path="url(#clipParity)"><polygon points="0,0 100,0 100,100 0,100" fill="url(#lgParity)"/></g></svg>""";
var parityDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(parityMarkup));

// Small synthesized raster PNG (8x8 colorful checker) for the <image> element showcase. A
// hand-picked minimal PNG isn't reliably decodable by the raster codec PeachPDF uses internally
// (PeachImage, a transitive dependency via the PeachPDF project reference) - writing one with the
// matching real encoder is.
static string MakeRasterDataUri()
{
    const int size = 8;
    var palette = new (byte R, byte G, byte B)[]
    {
        (231, 76, 60), (241, 196, 15), (46, 204, 113), (52, 152, 219)
    };
    var pixels = new byte[size * size * 4];
    for (var y = 0; y < size; y++)
    {
        for (var x = 0; x < size; x++)
        {
            var (r, g, b) = palette[(x + y) % palette.Length];
            var i = (y * size + x) * 4;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
        }
    }

    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Rgba32);
    pixels.CopyTo(image.GetPixelSpan());
    using var ms = new MemoryStream();
    image.Save(ms, "png");
    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
}
var rasterDataUri = MakeRasterDataUri();

// A data:image/svg+xml payload for <image href="...">, proving that path stays real vector content
// (never rasterized) - unlike the raster PNG swatch above.
var nestedVectorMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><defs><radialGradient id="nvg" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="50"><stop offset="0" stop-color="#fceabb"/><stop offset="1" stop-color="#f8b500"/></radialGradient></defs><circle cx="50" cy="50" r="45" fill="url(#nvg)"/></svg>""";
var nestedVectorDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(nestedVectorMarkup));

// SVG <style> works for both inline and standalone SVG (issues #159/#192) - matched through the full
// CSS engine (combinators, attribute/structural selectors, var(), calc()). These two data:image/svg+xml
// <img> payloads exercise the standalone path; the inline path is shown by the swatches below them.
var styleClassMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="80" height="80"><style>.hi{fill:#2980b9}.lo{fill:#bdc3c7}</style><rect x="10" y="10" width="35" height="80" class="lo"/><rect x="55" y="10" width="35" height="80" class="hi"/></svg>""";
var styleClassDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(styleClassMarkup));
var styleIdMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="80" height="80"><style>#target{fill:#c0392b}</style><circle id="target" cx="50" cy="50" r="35" fill="#bdc3c7"/></svg>""";
var styleIdDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(styleIdMarkup));

var svgHtml = "<!DOCTYPE html><html><head>" + SvgShowcaseCss + "</head><body>" +

    "<h1>SVG Test Page</h1>" +
    "<p class=\"intro\">PeachPDF renders SVG through its own vector scene graph, reusing the same PDF path/fill/stroke/gradient/clip primitives already used for CSS backgrounds and borders — SVG content is never rasterized to a bitmap. Full SVG 1.0 coverage is now supported (minus a handful of PDF-incompatible features such as animation, scripting, and filters): all basic shapes, fill-rule, stroke dash/cap/join, rotate()/skewX()/skewY() transforms, full preserveAspectRatio and nested viewports, objectBoundingBox/spreadMethod/currentColor gradients, the style cascade (style= and &lt;style&gt;), &lt;switch&gt;/&lt;a&gt; links, &lt;marker&gt;, &lt;pattern&gt;, &lt;mask&gt;, &lt;image&gt;, and &lt;text&gt;/&lt;tspan&gt;/&lt;tref&gt;. See <a href=\"https://github.com/jhaygood86/PeachPDF/blob/main/docs/supported-svg-features.md\">supported-svg-features.md</a> for the full compatibility matrix.</p>" +

    "<h2>1 — Path Primitives: Lines, Curves &amp; Arcs</h2>" +
    Row(
        SvgSwatch("straight lines (M/L/Z)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M50,10 L90,90 L10,90 Z" fill="#3498db"/></svg>""",
            "path d=\"M50,10 L90,90 L10,90 Z\""),
        SvgSwatch("cubic Bézier (C)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M10,80 C10,20 90,20 90,80" fill="none" stroke="#e74c3c" stroke-width="6"/></svg>""",
            "path d=\"M10,80 C10,20 90,20 90,80\""),
        SvgSwatch("elliptical arc (A)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M10,60 A40,40 0 0 1 90,60" fill="none" stroke="#27ae60" stroke-width="6"/></svg>""",
            "path d=\"M10,60 A40,40 0 0 1 90,60\""),
        SvgSwatch("multiple subpaths",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M25,10 L40,25 L25,40 L10,25 Z M75,60 L90,75 L75,90 L60,75 Z" fill="#9b59b6"/></svg>""",
            "one path, two \"M...Z\" subpaths")
    ) +

    "<h2>2 — circle &amp; polygon</h2>" +
    Row(
        SvgSwatch("circle",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#f39c12"/></svg>""",
            "circle cx=50 cy=50 r=35"),
        SvgSwatch("polygon (pentagon)",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="{PentagonPoints}" fill="#2ecc71"/></svg>""",
            "polygon points=\"...\""),
        SvgSwatch("overlapping circles + opacity",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="38" cy="50" r="30" fill="#3498db" opacity="0.7"/><circle cx="62" cy="50" r="30" fill="#e74c3c" opacity="0.7"/></svg>""",
            "two circles, opacity=\"0.7\" each"),
        SvgSwatch("polygon (5-point star)",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="{StarPoints}" fill="#f1c40f"/></svg>""",
            "polygon points=\"...\"")
    ) +

    "<h2>3 — Linear &amp; Radial Gradients</h2>" +
    Row(
        SvgSwatch("linearGradient, 2 stops",
            """<svg viewBox="0 0 100 100" width="80" height="60"><defs><linearGradient id="lg1" gradientUnits="userSpaceOnUse" x1="10" y1="50" x2="90" y2="50"><stop offset="0" stop-color="#ff5f6d"/><stop offset="1" stop-color="#ffc371"/></linearGradient></defs><polygon points="10,20 90,20 90,80 10,80" fill="url(#lg1)"/></svg>""",
            "linearGradient x1/y1/x2/y2, 2 stops"),
        SvgSwatch("linearGradient, 3 stops",
            """<svg viewBox="0 0 100 100" width="80" height="60"><defs><linearGradient id="lg2" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#00c6ff"/><stop offset="0.5" stop-color="#8e54e9"/><stop offset="1" stop-color="#eb3941"/></linearGradient></defs><polygon points="10,20 90,20 90,80 10,80" fill="url(#lg2)"/></svg>""",
            "diagonal, 3 stops"),
        SvgSwatch("radialGradient, centered",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="rg1" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40"><stop offset="0" stop-color="#fff9c4"/><stop offset="1" stop-color="#f57f17"/></radialGradient></defs><circle cx="50" cy="50" r="40" fill="url(#rg1)"/></svg>""",
            "radialGradient cx/cy/r"),
        SvgSwatch("radialGradient + gradientTransform",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="rg2" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40" gradientTransform="matrix(1 0 0 0.5 0 25)"><stop offset="0" stop-color="#e0f7fa"/><stop offset="1" stop-color="#006064"/></radialGradient></defs><circle cx="50" cy="50" r="40" fill="url(#rg2)"/></svg>""",
            "gradientTransform squishes the radial into an ellipse")
    ) +

    "<h2>4 — Stroke Properties</h2>" +
    Row(
        SvgSwatch("stroke only, default miterlimit",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,80 L50,15 L80,80 Z" fill="none" stroke="#2c3e50" stroke-width="4" stroke-miterlimit="10"/></svg>""",
            "stroke-width=4 stroke-miterlimit=10"),
        SvgSwatch("thick stroke, low miterlimit",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,80 L50,15 L80,80 Z" fill="none" stroke="#2c3e50" stroke-width="10" stroke-miterlimit="1"/></svg>""",
            "stroke-width=10 stroke-miterlimit=1"),
        SvgSwatch("fill + stroke combined",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#1abc9c" stroke="#0e6655" stroke-width="6"/></svg>""",
            "fill and stroke on the same shape"),
        SvgSwatch("stroke-only star",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="{StarPoints}" fill="none" stroke="#c0392b" stroke-width="3"/></svg>""",
            "fill=\"none\" stroke=\"#c0392b\"")
    ) +
    Row(
        SvgSwatch("gradient stroke (rounded rect + ellipse)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="gs1" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#c0392b"/><stop offset="1" stop-color="#2980b9"/></linearGradient></defs><rect x="10" y="12" width="80" height="34" rx="8" fill="none" stroke="url(#gs1)" stroke-width="6"/><ellipse cx="50" cy="72" rx="38" ry="20" fill="none" stroke="url(#gs1)" stroke-width="6"/></svg>""",
            "stroke=\"url(#gradient)\" on a rounded rect and an ellipse - a gradient stroke sharing a page with solid strokes")
    ) +
    // A transparent (fades-to-alpha-0) gradient stroke realizes a PDF soft mask; that mask must be
    // torn down after the stroke so it doesn't bleed onto later paint - an opaque gradient stroke or
    // a gradient stroke in a following panel - which would otherwise be masked away (issue #135).
    Row(
        SvgSwatch("transparent gradient stroke beside opaque",
            """<svg viewBox="0 0 210 90" width="150" height="64"><defs><linearGradient id="gtr" gradientUnits="userSpaceOnUse" x1="10" y1="0" x2="90" y2="0"><stop offset="0" stop-color="#ff0080"/><stop offset="1" stop-color="#0080ff" stop-opacity="0"/></linearGradient><linearGradient id="gop" gradientUnits="userSpaceOnUse" x1="120" y1="0" x2="200" y2="0"><stop offset="0" stop-color="#00b050"/><stop offset="1" stop-color="#c08000"/></linearGradient></defs><rect x="10" y="15" width="80" height="60" fill="none" stroke="url(#gtr)" stroke-width="10"/><rect x="120" y="15" width="80" height="60" fill="none" stroke="url(#gop)" stroke-width="10"/></svg>""",
            "left fades to transparent; the opaque green-gold frame must stay fully visible"),
        SvgSwatch("gradient stroke inside padded wrapper",
            """<div style="padding:8px 6px"><svg viewBox="0 0 100 100" width="64" height="64"><defs><linearGradient id="gp" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#8e44ad"/><stop offset="1" stop-color="#16a085" stop-opacity="0.2"/></linearGradient></defs><rect x="14" y="14" width="72" height="72" rx="10" fill="none" stroke="url(#gp)" stroke-width="9"/></svg></div>""",
            "wrapper padding no longer drops the stroke"),
        SvgSwatch("gradient stroke inside rounded bordered wrapper",
            """<div style="border-radius:12px;border:2px solid #333;padding:6px"><svg viewBox="0 0 100 100" width="60" height="60"><defs><linearGradient id="gb" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#e67e22"/><stop offset="1" stop-color="#2980b9" stop-opacity="0.2"/></linearGradient></defs><rect x="14" y="14" width="72" height="72" rx="10" fill="none" stroke="url(#gb)" stroke-width="9"/></svg></div>""",
            "border-radius + border wrapper no longer drops the stroke"),
        SvgSwatch("two gradient-stroked panels in a row",
            """<svg viewBox="0 0 100 100" width="40" height="64"><defs><linearGradient id="ga" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#c0392b"/><stop offset="1" stop-color="#c0392b" stop-opacity="0"/></linearGradient></defs><circle cx="50" cy="50" r="34" fill="none" stroke="url(#ga)" stroke-width="10"/></svg><svg viewBox="0 0 100 100" width="40" height="64"><defs><linearGradient id="gc" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#27ae60"/><stop offset="1" stop-color="#2980b9"/></linearGradient></defs><circle cx="50" cy="50" r="34" fill="none" stroke="url(#gc)" stroke-width="10"/></svg>""",
            "a transparent gradient ring never masks a later one on the same page")
    ) +

    "<h2>5 — Group Opacity &amp; Transforms</h2>" +
    Row(
        SvgSwatch("nested group opacity (0.7 × 0.7)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="10,10 90,10 90,90 10,90" fill="#f1c40f"/><g opacity="0.7"><g opacity="0.7"><circle cx="50" cy="50" r="35" fill="#2980b9"/></g></g></svg>""",
            "g opacity=\"0.7\" > g opacity=\"0.7\""),
        SvgSwatch("group transform: translate + scale",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="translate(15,15) scale(0.6)"><path d="M50,10 L90,90 L10,90 Z" fill="#8e44ad"/></g></svg>""",
            "transform=\"translate(15,15) scale(0.6)\""),
        SvgSwatch("horizontal mirror via scale(-1,1)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="scale(-1,1) translate(-100,0)"><path d="M20,80 L60,20 L90,80 Z" fill="#16a085"/></g></svg>""",
            "transform=\"scale(-1,1) translate(-100,0)\""),
        SvgSwatch("use + per-instance transform",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="dot2" cx="50" cy="50" r="15" fill="#9b59b6"/></defs><use xlink:href="#dot2" transform="scale(0.6)"/><use xlink:href="#dot2" transform="translate(40,20) scale(0.8)"/></svg>""",
            "two &lt;use&gt; of the same &lt;circle&gt;, each transformed")
    ) +
    Row(
        SvgSwatch("group opacity over overlapping text",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g opacity="0.5"><text x="6" y="58" font-size="42" fill="#e74c3c">AV</text><text x="24" y="74" font-size="42" fill="#2980b9">VA</text></g></svg>""",
            "g opacity=\"0.5\" over two overlapping &lt;text&gt; runs — no double-darkening at the overlap"),
        SvgSwatch("use opacity of a container",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><g id="pair"><rect x="12" y="12" width="52" height="52" fill="#e67e22"/><rect x="36" y="36" width="52" height="52" fill="#8e44ad"/></g></defs><use xlink:href="#pair" opacity="0.5"/></svg>""",
            "use opacity=\"0.5\" of a &lt;g&gt; of overlapping rects — composited once"),
        SvgSwatch("nested svg opacity",
            """<svg viewBox="0 0 100 100" width="80" height="80"><svg x="8" y="8" width="84" height="84" opacity="0.5"><circle cx="34" cy="34" r="28" fill="#16a085"/><circle cx="58" cy="58" r="28" fill="#c0392b"/></svg></svg>""",
            "nested &lt;svg opacity=\"0.5\"&gt; with overlapping circles")
    ) +

    "<h2>6 — clipPath + use</h2>" +
    "<p class=\"intro\">clip-path references a &lt;clipPath&gt; that itself contains a &lt;use&gt; of a shape defined once in &lt;defs&gt; — the same pattern used by the peach illustrations below. A clip shape's own <code>transform</code> (directly, via <code>&lt;use transform&gt;</code>, or via a wrapping <code>&lt;g&gt;</code>) is applied to the clip geometry, so one shape can be reused at several positions. With <code>clipPathUnits=\"objectBoundingBox\"</code> the 0..1 clip geometry auto-scales to the referencing element's own bounding box.</p>" +
    Row(
        SvgSwatch("gradient clipped to a circle",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="clipCircle1" cx="50" cy="50" r="35"/><linearGradient id="lg3" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#ff9a9e"/><stop offset="1" stop-color="#fecfef"/></linearGradient><clipPath id="clip1"><use xlink:href="#clipCircle1"/></clipPath></defs><g clip-path="url(#clip1)"><polygon points="0,0 100,0 100,100 0,100" fill="url(#lg3)"/></g></svg>""",
            "clipPath > use > circle"),
        SvgSwatch("gradient clipped to a star",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><polygon id="clipStar" points="{StarPoints}"/><linearGradient id="lg4" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#f6d365"/><stop offset="1" stop-color="#fda085"/></linearGradient><clipPath id="clip2"><use xlink:href="#clipStar"/></clipPath></defs><g clip-path="url(#clip2)"><polygon points="0,0 100,0 100,100 0,100" fill="url(#lg4)"/></g></svg>""",
            "clipPath > use > polygon"),
        SvgSwatch("use for simple repetition",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="dot" cx="0" cy="0" r="12" fill="#e67e22"/></defs><use xlink:href="#dot" x="30" y="50"/><use xlink:href="#dot" x="70" y="50"/></svg>""",
            "use xlink:href=\"#dot\" x=.. y=.."),
        SvgSwatch("one clip shape reused at 4 positions",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><rect id="clipTile" x="0" y="0" width="34" height="34" rx="7"/><linearGradient id="lgReuse" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="100" y2="100"><stop offset="0" stop-color="#43e97b"/><stop offset="1" stop-color="#38f9d7"/></linearGradient><clipPath id="clipReuse"><use xlink:href="#clipTile" transform="translate(8,8)"/><use xlink:href="#clipTile" transform="translate(58,8)"/><use xlink:href="#clipTile" transform="translate(8,58)"/><use xlink:href="#clipTile" transform="translate(58,58)"/></clipPath></defs><g clip-path="url(#clipReuse)"><polygon points="0,0 100,0 100,100 0,100" fill="url(#lgReuse)"/></g></svg>""",
            "clipPath > use transform=\"translate(..)\" ×4")
    ) +
    Row(
        SvgSwatch("clip + group opacity combined",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="clipCircle2" cx="50" cy="50" r="35"/><clipPath id="clip3"><use xlink:href="#clipCircle2"/></clipPath></defs><polygon points="0,0 100,0 100,100 0,100" fill="#34495e"/><g clip-path="url(#clip3)" opacity="0.8"><polygon points="0,0 100,0 100,100 0,100" fill="#e74c3c"/></g></svg>""",
            "g clip-path=\"url(#clip3)\" opacity=\"0.8\""),
        SvgSwatch("objectBoundingBox: clip auto-scales to the element",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><clipPath id="obbDiamond" clipPathUnits="objectBoundingBox"><polygon points="0.5,0 1,0.5 0.5,1 0,0.5"/></clipPath><linearGradient id="lgObb" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#8e2de2"/><stop offset="1" stop-color="#4a00e0"/></linearGradient></defs><rect x="15" y="15" width="70" height="70" fill="url(#lgObb)" clip-path="url(#obbDiamond)"/></svg>""",
            "clipPathUnits=\"objectBoundingBox\" (0..1 diamond)")
    ) +

    "<h2>7 — Basic Shapes: rect, ellipse, line &amp; polyline</h2>" +
    Row(
        SvgSwatch("rect with rounded corners",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="15" y="15" width="70" height="70" rx="15" fill="#3498db"/></svg>""",
            "rect x y width height rx=15"),
        SvgSwatch("ellipse (percentage rx)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><ellipse cx="50" cy="50" rx="35%" ry="25" fill="#e67e22"/></svg>""",
            "ellipse rx=\"35%\" (percentage length)"),
        SvgSwatch("line, round linecap",
            """<svg viewBox="0 0 100 100" width="80" height="80"><line x1="10" y1="90" x2="90" y2="10" stroke="#c0392b" stroke-width="8" stroke-linecap="round"/></svg>""",
            "line x1 y1 x2 y2 stroke-linecap=\"round\""),
        SvgSwatch("polyline (open, unclosed)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><polyline points="10,80 30,20 50,70 70,15 90,60" fill="none" stroke="#16a085" stroke-width="4" stroke-linejoin="round"/></svg>""",
            "polyline points=\"...\" fill=\"none\"")
    ) +

    "<h2>8 — Stroke: Dash Arrays, Caps &amp; Joins</h2>" +
    Row(
        SvgSwatch("stroke-dasharray + dashoffset",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M10,50 L90,50" stroke="#8e44ad" stroke-width="8" stroke-dasharray="14,8" stroke-dashoffset="4" fill="none"/></svg>""",
            "stroke-dasharray=\"14,8\" stroke-dashoffset=\"4\""),
        SvgSwatch("stroke-linecap: butt/round/square",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g stroke-width="12"><line x1="20" y1="15" x2="20" y2="85" stroke="#2c3e50" stroke-linecap="butt"/><line x1="50" y1="15" x2="50" y2="85" stroke="#2980b9" stroke-linecap="round"/><line x1="80" y1="15" x2="80" y2="85" stroke="#c0392b" stroke-linecap="square"/></g></svg>""",
            "stroke-linecap: butt, round, square"),
        SvgSwatch("stroke-linejoin: miter/round/bevel",
            """<svg viewBox="0 0 140 100" width="90" height="64"><g fill="none" stroke-width="8"><path d="M5,75 L25,25 L45,75" stroke="#2c3e50" stroke-linejoin="miter"/><path d="M50,75 L70,25 L90,75" stroke="#2980b9" stroke-linejoin="round"/><path d="M95,75 L115,25 L135,75" stroke="#c0392b" stroke-linejoin="bevel"/></g></svg>""",
            "stroke-linejoin: miter, round, bevel"),
        SvgSwatch("dashed rounded-rect border",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="15" y="15" width="70" height="70" rx="12" fill="none" stroke="#16a085" stroke-width="5" stroke-dasharray="6,4" stroke-linecap="round" stroke-linejoin="round"/></svg>""",
            "dasharray + linecap + linejoin combined")
    ) +

    "<h2>9 — Fill Rule &amp; Opacity</h2>" +
    Row(
        SvgSwatch("fill-rule=\"nonzero\" (default)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,20 L80,20 L80,80 L20,80 Z M35,35 L65,35 L65,65 L35,65 Z" fill="#8e44ad" fill-rule="nonzero"/></svg>""",
            "same-direction inner square: solid (no hole)"),
        SvgSwatch("fill-rule=\"evenodd\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,20 L80,20 L80,80 L20,80 Z M35,35 L65,35 L65,65 L35,65 Z" fill="#8e44ad" fill-rule="evenodd"/></svg>""",
            "identical path, evenodd: donut (hole visible)"),
        SvgSwatch("fill-opacity, stroke fully opaque",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#e74c3c" fill-opacity="0.4" stroke="#c0392b" stroke-width="6"/></svg>""",
            "fill-opacity=\"0.4\" (stroke unaffected)"),
        SvgSwatch("stroke-opacity, fill fully opaque",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#e74c3c" stroke="#2c3e50" stroke-width="10" stroke-opacity="0.4"/></svg>""",
            "stroke-opacity=\"0.4\" (fill unaffected)")
    ) +
    Row(
        SvgSwatch("invalid fill-rule inherits, doesn't reset to nonzero",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g fill-rule="evenodd"><path d="M20,20 L80,20 L80,80 L20,80 Z M35,35 L65,35 L65,65 L35,65 Z" fill="#8e44ad" fill-rule="not-a-rule"/></g></svg>""",
            "g fill-rule=\"evenodd\" > path fill-rule=\"not-a-rule\": still a donut"),
        SvgSwatch("invalid stroke-linecap/linejoin inherit from group",
            """<svg viewBox="0 0 140 100" width="90" height="64"><g fill="none" stroke="#2980b9" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"><path d="M20,75 L45,25 L70,75" stroke-linecap="not-a-cap" stroke-linejoin="not-a-join"/></g></svg>""",
            "group's round cap/join survive an invalid override")
    ) +
    Row(
        SvgSwatch("invalid fill/stroke inherit from group",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g fill="#8e44ad" stroke="#2c3e50" stroke-width="4"><circle cx="50" cy="50" r="35" fill="not-a-color" stroke="not-a-color"/></g></svg>""",
            "g fill/stroke set > circle fill/stroke=\"not-a-color\": still purple with dark outline"),
        SvgSwatch("invalid fill-opacity/stroke-opacity inherit from group",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g fill-opacity="0.4" stroke-opacity="0.4"><circle cx="50" cy="50" r="35" fill="#e74c3c" fill-opacity="not-a-number" stroke="#2c3e50" stroke-width="10" stroke-opacity="not-a-number"/></g></svg>""",
            "group's 0.4 fill/stroke opacity survive an invalid override, not reset to 1.0")
    ) +

    "<h2>10 — Transforms: rotate() &amp; skew()</h2>" +
    Row(
        SvgSwatch("rotate(angle, cx, cy)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="rotate(30,50,50)"><rect x="25" y="35" width="50" height="30" fill="#3498db"/></g></svg>""",
            "transform=\"rotate(30,50,50)\""),
        SvgSwatch("rotate(-angle, cx, cy)",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><g transform="rotate(-45,50,50)"><polygon points="{PentagonPoints}" fill="#e67e22"/></g></svg>""",
            "transform=\"rotate(-45,50,50)\""),
        SvgSwatch("skewX()",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="skewX(20)"><rect x="15" y="25" width="50" height="50" fill="#16a085"/></g></svg>""",
            "transform=\"skewX(20)\""),
        SvgSwatch("skewY()",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="skewY(-20)"><rect x="25" y="15" width="50" height="50" fill="#9b59b6"/></g></svg>""",
            "transform=\"skewY(-20)\"")
    ) +

    "<h2>11 — Advanced Gradients: objectBoundingBox, spreadMethod &amp; Radial Focus</h2>" +
    Row(
        SvgSwatch("objectBoundingBox (default)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="obbGrad" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#43cea2"/><stop offset="1" stop-color="#185a9d"/></linearGradient></defs><rect x="15" y="30" width="70" height="40" fill="url(#obbGrad)"/></svg>""",
            "no gradientUnits: x1/x2 are 0..1 fractions of the rect's own box"),
        SvgSwatch("spreadMethod=\"repeat\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="spreadRepeat" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="20" y2="0" spreadMethod="repeat"><stop offset="0" stop-color="#ff9966"/><stop offset="1" stop-color="#ff5e62"/></linearGradient></defs><rect x="10" y="10" width="80" height="80" fill="url(#spreadRepeat)"/></svg>""",
            "narrow x1..x2 range tiles across the rect"),
        SvgSwatch("spreadMethod=\"reflect\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="spreadReflect" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="20" y2="0" spreadMethod="reflect"><stop offset="0" stop-color="#00c9ff"/><stop offset="1" stop-color="#92fe9d"/></linearGradient></defs><rect x="10" y="10" width="80" height="80" fill="url(#spreadReflect)"/></svg>""",
            "same idea, mirrored at each repeat"),
        SvgSwatch("radial fx/fy off-center highlight",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="fxfy" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40" fx="30" fy="30"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#34495e"/></radialGradient></defs><circle cx="50" cy="50" r="40" fill="url(#fxfy)"/></svg>""",
            "fx/fy offset from cx/cy: sphere-shading highlight")
    ) +

    "<h2>12 — preserveAspectRatio &amp; Nested Viewports</h2>" +
    Row(
        SvgSwatch("default: xMidYMid meet",
            """<svg viewBox="0 0 200 100" width="80" height="80"><rect x="0" y="0" width="100" height="100" fill="#3498db"/><rect x="100" y="0" width="100" height="100" fill="#e74c3c"/></svg>""",
            "2:1 viewBox into a square box: letterboxed"),
        SvgSwatch("xMidYMid slice",
            """<svg viewBox="0 0 200 100" width="80" height="80" preserveAspectRatio="xMidYMid slice"><rect x="0" y="0" width="100" height="100" fill="#3498db"/><rect x="100" y="0" width="100" height="100" fill="#e74c3c"/></svg>""",
            "same viewBox, slice: cropped, fills the box"),
        SvgSwatch("preserveAspectRatio=\"none\"",
            """<svg viewBox="0 0 200 100" width="80" height="80" preserveAspectRatio="none"><rect x="0" y="0" width="100" height="100" fill="#3498db"/><rect x="100" y="0" width="100" height="100" fill="#e74c3c"/></svg>""",
            "stretched independently per axis: distorted"),
        SvgSwatch("nested &lt;svg&gt;, own viewport",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="0" y="0" width="100" height="100" fill="#ecf0f1"/><svg x="15" y="15" width="70" height="70" viewBox="0 0 10 10"><circle cx="5" cy="5" r="5" fill="#8e44ad"/></svg></svg>""",
            "nested svg establishes its own coordinate system")
    ) +

    "<h2>13 — Style Cascade &amp; currentColor</h2>" +
    Row(
        SvgSwatch("style= overrides presentation attribute",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="red" style="fill:#2ecc71"/></svg>""",
            "fill=\"red\" style=\"fill:#2ecc71\": style wins"),
        SvgSwatch("currentColor",
            """<span style="color:#e67e22"><svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="currentColor"/></svg></span>""",
            "fill=\"currentColor\" resolves the ancestor's CSS color"),
        "<td>" +
            $"<div class=\"sbox\"><img src=\"{styleClassDataUri}\" width=\"80\" height=\"80\"/></div>" +
            "<div class=\"desc\">&lt;style&gt; class selector (standalone)</div>" +
            "<div class=\"css\">.hi/.lo rules, via &lt;img&gt;</div>" +
        "</td>",
        "<td>" +
            $"<div class=\"sbox\"><img src=\"{styleIdDataUri}\" width=\"80\" height=\"80\"/></div>" +
            "<div class=\"desc\">&lt;style&gt; id selector (standalone)</div>" +
            "<div class=\"css\">#target rule, via &lt;img&gt;</div>" +
        "</td>"
    ) +
    Row(
        SvgSwatch("inline &lt;svg&gt;&lt;style&gt;",
            """<svg viewBox="0 0 100 100" width="80" height="80"><style>.hi{fill:#2980b9}.lo{fill:#bdc3c7}</style><rect x="10" y="10" width="35" height="80" class="lo"/><rect x="55" y="10" width="35" height="80" class="hi"/></svg>""",
            "a &lt;style&gt; nested in an inline &lt;svg&gt; now applies (issue #159)"),
        SvgSwatch("HTML &lt;style&gt; cascades into SVG",
            """<style>svg.svgCascadeDemo circle{fill:#8e44ad}</style><svg class="svgCascadeDemo" viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35"/></svg>""",
            "a document-level rule matching SVG shapes applies (SVG 2 §6)"),
        // Both of these <style> rules are scoped through a class on their own <svg>, exactly as the
        // "HTML <style> cascades into SVG" swatch beside them is. A <style> element inside an inline
        // <svg> is NOT scoped to that <svg> - in an HTML document it is a document-level stylesheet
        // like any other (SVG 2 §6), which browsers agree on. Written as the bare type selectors
        // "circle" and "g rect", they therefore matched every <circle>/<g> <rect> on this page and
        // repainted the gradient swatches above (and both peach illustrations) a flat #d35400 -
        // correct cascading, but it silently destroyed what the rest of the page is here to show.
        SvgSwatch("combinator selector",
            """<svg class="combinatorDemo" viewBox="0 0 100 100" width="80" height="80"><style>.combinatorDemo g rect{fill:#16a085}</style><g><rect x="20" y="20" width="60" height="60"/></g></svg>""",
            "\"g rect\" descendant combinator, via the full selector engine"),
        SvgSwatch("var() custom property",
            """<svg class="varDemo" viewBox="0 0 100 100" width="80" height="80"><style>:root{--c:#d35400}.varDemo circle{fill:var(--c)}</style><circle cx="50" cy="50" r="35"/></svg>""",
            "fill:var(--c) resolves through the CSS cascade")
    ) +

    "<h2>14 — &lt;switch&gt; &amp; &lt;a&gt; Links</h2>" +
    Row(
        SvgSwatch("switch: first child wins",
            """<svg viewBox="0 0 100 100" width="80" height="80"><switch><rect x="20" y="20" width="60" height="60" fill="#e74c3c"/><circle cx="50" cy="50" r="35" fill="#3498db"/></switch></svg>""",
            "no requiredFeatures evaluation: always shows child 1"),
        SvgSwatch("switch: skips an unbuildable child",
            """<svg viewBox="0 0 100 100" width="80" height="80"><switch><metadata>ignored</metadata><circle cx="50" cy="50" r="35" fill="#27ae60"/></switch></svg>""",
            "&lt;metadata&gt; isn't renderable, so child 2 is used"),
        SvgSwatch("&lt;a href&gt;: real PDF link",
            """<svg viewBox="0 0 100 100" width="80" height="80"><a href="https://github.com/jhaygood86/PeachPDF"><circle cx="50" cy="50" r="35" fill="#2980b9" stroke="#1a5276" stroke-width="3"/></a></svg>""",
            "becomes a clickable PDF link annotation"),
        SvgSwatch("&lt;a&gt; with no href",
            """<svg viewBox="0 0 100 100" width="80" height="80"><a><rect x="20" y="20" width="60" height="60" fill="#7f8c8d"/></a></svg>""",
            "renders children normally, just isn't a link")
    ) +

    "<h2>15 — Markers</h2>" +
    Row(
        SvgSwatch("marker-end, orient=\"auto\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="arrow1" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill="#c0392b"/></marker></defs><path d="M10,80 Q50,10 90,80" fill="none" stroke="#c0392b" stroke-width="3" marker-end="url(#arrow1)"/></svg>""",
            "marker rotates to follow the path's tangent"),
        SvgSwatch("marker-start/mid/end on every vertex",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="dot1" markerWidth="6" markerHeight="6" refX="3" refY="3" markerUnits="userSpaceOnUse"><circle cx="3" cy="3" r="3" fill="#2980b9"/></marker></defs><polyline points="10,80 30,20 50,70 70,15 90,60" fill="none" stroke="#95a5a6" stroke-width="2" marker-start="url(#dot1)" marker-mid="url(#dot1)" marker-end="url(#dot1)"/></svg>""",
            "one marker def, placed at start/mid/end vertices"),
        SvgSwatch("markerUnits=\"strokeWidth\" (default)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="mScale" markerWidth="4" markerHeight="4" refX="2" refY="2" markerUnits="strokeWidth"><circle cx="2" cy="2" r="2" fill="#8e44ad"/></marker></defs><line x1="15" y1="30" x2="85" y2="30" stroke="#8e44ad" stroke-width="2" marker-end="url(#mScale)"/><line x1="15" y1="70" x2="85" y2="70" stroke="#8e44ad" stroke-width="10" marker-end="url(#mScale)"/></svg>""",
            "same marker, scales with each line's own stroke-width"),
        SvgSwatch("orient: fixed angle",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="tri" markerWidth="8" markerHeight="8" refX="4" refY="4" orient="45"><path d="M0,0 L8,0 L4,8 Z" fill="#16a085"/></marker></defs><polyline points="15,15 85,15 15,85 85,85" fill="none" stroke="none" marker-start="url(#tri)" marker-mid="url(#tri)" marker-end="url(#tri)"/></svg>""",
            "orient=\"45\": same fixed rotation at every vertex")
    ) +

    "<h2>16 — Pattern Fill</h2>" +
    Row(
        SvgSwatch("checkerboard",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="checker" patternUnits="userSpaceOnUse" width="20" height="20"><rect width="10" height="10" fill="#ecf0f1"/><rect x="10" width="10" height="10" fill="#95a5a6"/><rect y="10" width="10" height="10" fill="#95a5a6"/><rect x="10" y="10" width="10" height="10" fill="#ecf0f1"/></pattern></defs><rect x="10" y="10" width="80" height="80" fill="url(#checker)"/></svg>""",
            "pattern of 4 &lt;rect&gt; tiles, patternUnits=\"userSpaceOnUse\""),
        SvgSwatch("polka dots",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="dots" patternUnits="userSpaceOnUse" width="16" height="16"><circle cx="8" cy="8" r="4" fill="#e67e22"/></pattern></defs><circle cx="50" cy="50" r="40" fill="url(#dots)"/></svg>""",
            "dot pattern clipped to the circle's own geometry"),
        SvgSwatch("patternTransform=\"rotate(45)\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="stripes" patternUnits="userSpaceOnUse" width="12" height="12" patternTransform="rotate(45)"><rect width="6" height="12" fill="#2ecc71"/></pattern></defs><rect x="10" y="10" width="80" height="80" fill="url(#stripes)"/></svg>""",
            "stripe tile rotated 45° via patternTransform"),
        SvgSwatch("pattern filling a star shape",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="grid" patternUnits="userSpaceOnUse" width="10" height="10"><rect width="10" height="10" fill="#fdebd0"/><rect width="10" height="2" fill="#e67e22"/><rect width="2" height="10" fill="#e67e22"/></pattern></defs><polygon points="{StarPoints}" fill="url(#grid)"/></svg>""",
            "pattern respects the star's own fill geometry"),
        SvgSwatch("pattern content inherits from &lt;svg&gt;",
            """<svg viewBox="0 0 100 100" width="80" height="80" fill="#e67e22" stroke="#2c3e50" stroke-width="2"><defs><pattern id="inheritDots" patternUnits="userSpaceOnUse" width="20" height="20"><circle cx="10" cy="10" r="6"/></pattern></defs><rect x="10" y="10" width="80" height="80" fill="url(#inheritDots)"/></svg>""",
            "the unstyled circle inherits fill and stroke from the root &lt;svg&gt;, not from the rect that references the pattern")
    ) +

    "<h2>17 — Mask</h2>" +
    Row(
        SvgSwatch("linear-gradient luminance fade",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="fadeGrad" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#000000"/></linearGradient><mask id="fadeMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect x="0" y="0" width="100" height="100" fill="url(#fadeGrad)"/></mask></defs><rect x="0" y="0" width="100" height="100" fill="#f1c40f"/><rect x="0" y="0" width="100" height="100" fill="#8e44ad" mask="url(#fadeMask)"/></svg>""",
            "gradient mask fades the purple rect over a yellow backdrop"),
        SvgSwatch("radial vignette",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="vignetteGrad" gradientUnits="objectBoundingBox" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#000000"/></radialGradient><mask id="vignetteMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect x="0" y="0" width="100" height="100" fill="url(#vignetteGrad)"/></mask></defs><rect x="0" y="0" width="100" height="100" fill="#c0392b" mask="url(#vignetteMask)"/></svg>""",
            "radial mask: spotlight/vignette fade"),
        SvgSwatch("vector shape as mask",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><mask id="starMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><polygon points="{StarPoints}" fill="#ffffff"/></mask></defs><rect x="0" y="0" width="100" height="100" fill="#16a085" mask="url(#starMask)"/></svg>""",
            "a shape, not just a gradient, as the mask's luminance"),
        SvgSwatch("&lt;text&gt; as mask content",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="textGrad" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ff9966"/><stop offset="1" stop-color="#ff5e62"/></linearGradient><mask id="textMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><text x="6" y="65" font-size="46" font-weight="bold" fill="#ffffff">PDF</text></mask></defs><rect x="0" y="0" width="100" height="100" fill="url(#textGrad)" mask="url(#textMask)"/></svg>""",
            "gradient shows only through the letter shapes"),
        SvgSwatch("mask content inherits fill",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><g fill="#ffffff"><mask id="inheritMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><circle cx="50" cy="50" r="40"/></mask></g></defs><rect x="0" y="0" width="100" height="100" fill="#8e44ad" mask="url(#inheritMask)"/></svg>""",
            "the mask's unstyled circle inherits fill=white from the &lt;g&gt; it is defined in, so the rect shows through")
    ) +

    "<h2>18 — &lt;image&gt; Element</h2>" +
    Row(
        SvgSwatch("raster data:image/png",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><image x="10" y="10" width="80" height="80" href="{rasterDataUri}"/></svg>""",
            "a real embedded raster image XObject"),
        SvgSwatch("vector data:image/svg+xml",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><image x="0" y="0" width="100" height="100" href="{nestedVectorDataUri}"/></svg>""",
            "stays real vector content, never rasterized"),
        SvgSwatch("preserveAspectRatio=\"none\" on &lt;image&gt;",
            $"""<svg viewBox="0 0 100 60" width="80" height="48"><image x="0" y="0" width="100" height="60" preserveAspectRatio="none" href="{rasterDataUri}"/></svg>""",
            "raster image stretched non-uniformly to fit its box"),
        SvgSwatch("&lt;image&gt; with clip-path",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="imgClip" cx="50" cy="50" r="40"/><clipPath id="clipImg"><use xlink:href="#imgClip"/></clipPath></defs><image x="5" y="5" width="90" height="90" href="{rasterDataUri}" clip-path="url(#clipImg)"/></svg>""",
            "clip-path applies to &lt;image&gt; like any other element")
    ) +

    "<h2>19 — Text, tspan &amp; tref</h2>" +
    Row(
        SvgSwatch("&lt;text x y fill font-size&gt;",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="0" y="0" width="100" height="100" fill="#2c3e50"/><text x="10" y="55" font-size="20" fill="#ecf0f1">Peach</text></svg>""",
            "plain positioned text, baseline at (x, y)"),
        SvgSwatch("text-anchor: start/middle/end",
            """<svg viewBox="0 0 100 100" width="80" height="80"><line x1="50" y1="5" x2="50" y2="95" stroke="#bdc3c7" stroke-dasharray="3,3"/><text x="50" y="30" font-size="12" fill="#2980b9" text-anchor="start">start</text><text x="50" y="55" font-size="12" fill="#c0392b" text-anchor="middle">middle</text><text x="50" y="80" font-size="12" fill="#27ae60" text-anchor="end">end</text></svg>""",
            "all three anchored at x=50 (dashed guideline)"),
        SvgSwatch("&lt;tspan&gt; restyles mid-run",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#fdf2e9"/><text x="8" y="45" font-size="14" fill="#2c3e50">Hello <tspan fill="#c0392b" font-weight="bold">World</tspan></text></svg>""",
            "tspan flows right after \"Hello \", own fill+weight"),
        SvgSwatch("&lt;tspan x y&gt;: new line + font variety",
            """<svg viewBox="0 0 100 100" width="80" height="80"><text x="10"><tspan x="10" y="30" font-size="16" font-weight="bold" fill="#2980b9">Bold</tspan><tspan x="10" y="55" font-size="14" font-style="italic" fill="#c0392b">Italic</tspan><tspan x="10" y="80" font-size="12" fill="#16a085">Regular</tspan></text></svg>""",
            "own x/y starts a new line; size/weight/style vary")
    ) +
    Row(
        SvgSwatch("font-* inherited from an ancestor &lt;g&gt;",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#eafaf1"/><g font-family="serif" font-size="16" font-weight="bold" fill="#16a085"><text x="8" y="40">Peach</text><text x="8" y="72" font-size="1.5em">1.5em</text></g></svg>""",
            "text inherits family/size/weight from &lt;g&gt;; 1.5em = 24"),
        SvgSwatch("per-character x list + rotate",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#fef9e7"/><text x="6 22 38 54 70" y="55" rotate="0 12 -8 16 -12" font-size="22" fill="#c0392b">Peach</text></svg>""",
            "each glyph its own x + rotation (SVG §10.4)"),
        SvgSwatch("nested &lt;tspan&gt; along a &lt;textPath&gt;",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><path id="tp" d="M8,72 Q50,8 92,72"/></defs><path d="M8,72 Q50,8 92,72" fill="none" stroke="#d5dbdb"/><text font-size="13" fill="#8e44ad"><textPath href="#tp">Pe<tspan fill="#c0392b" font-weight="bold">ach</tspan></textPath></text></svg>""",
            "restyled tspan laid along the curve too")
    ) +
    Row(
        SvgSwatch("letter-spacing &amp; word-spacing",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#fdf2e9"/><text x="6" y="35" font-size="13" letter-spacing="2" fill="#2c3e50">Spaced Out</text><text x="6" y="65" font-size="13" word-spacing="10" fill="#8e44ad">Word by word</text></svg>""",
            "both properties reach the same GSUB/GPOS pipeline HTML text uses"),
        SvgSwatch("text-transform",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#eafaf1"/><text x="6" y="30" font-size="12" text-transform="uppercase" fill="#16a085">shout</text><text x="6" y="55" font-size="12" text-transform="capitalize" fill="#2980b9">each word</text><text x="6" y="80" font-size="12" text-transform="lowercase" fill="#c0392b">QUIET</text></svg>""",
            "uppercase / capitalize / lowercase"),
        SvgSwatch("font-variant-caps: small-caps",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#fef9e7"/><text x="6" y="55" font-size="18" font-variant-caps="small-caps" fill="#8e44ad">Small Caps</text></svg>""",
            "real GSUB smcp/c2sc substitution, gated on font support"),
        SvgSwatch("text-decoration-line/-style/-color",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#2c3e50"/><text x="8" y="35" font-size="14" fill="#ecf0f1" text-decoration-line="underline">Underlined</text><text x="8" y="65" font-size="14" fill="#ecf0f1" text-decoration-line="overline line-through underline" text-decoration-color="#e74c3c" text-decoration-style="dashed">Decorated</text></svg>""",
            "own value only (not inherited), flows across a plain descendant tspan")
    ) +

    "<h2>20 — Inline &lt;svg&gt; vs &lt;img src=\"data:image/svg+xml\"&gt;</h2>" +
    "<p class=\"intro\">The identical SVG markup rendered two ways: embedded directly in the HTML, and encoded as a base64 data: URI on an &lt;img&gt; tag. Both go through the same vector renderer.</p>" +
    "<table class=\"sw\"><tr>" +
    SvgSwatch("inline &lt;svg&gt;", parityMarkup, "&lt;svg&gt;...&lt;/svg&gt; inline in the HTML body") +
    "<td>" +
        $"<div class=\"sbox\"><img src=\"{parityDataUri}\" width=\"80\" height=\"80\"/></div>" +
        "<div class=\"desc\">&lt;img src=\"data:...\"&gt;</div>" +
        "<div class=\"css\">same markup, base64 data: URI</div>" +
    "</td>" +
    "</tr></table>" +

    "<h2>21 — Peach Showcase</h2>" +
    "<p class=\"intro\">Two original peach illustrations, built entirely from the elements above: cubic-curve paths, radial gradients for shading, and a clipPath + use for the cross-section.</p>" +
    "<table class=\"sw\"><tr>" +
    PeachSwatch("Whole Peach",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <radialGradient id="peachBody" gradientUnits="userSpaceOnUse" cx="80" cy="70" r="150">
              <stop offset="0" stop-color="#fff3b0"/>
              <stop offset="0.35" stop-color="#ffb347"/>
              <stop offset="0.7" stop-color="#ff6f61"/>
              <stop offset="1" stop-color="#d1495b"/>
            </radialGradient>
            <radialGradient id="cheekBlush" gradientUnits="userSpaceOnUse" cx="140" cy="130" r="55">
              <stop offset="0" stop-color="#ff4d6d" stop-opacity="0.55"/>
              <stop offset="1" stop-color="#ff4d6d" stop-opacity="0"/>
            </radialGradient>
          </defs>
          <path d="M100,82 C90,60 65,45 40,55 C10,67 3,112 18,146 C33,178 66,195 100,195 C134,195 167,178 182,146 C197,112 190,67 160,55 C135,45 110,60 100,82 Z" fill="url(#peachBody)"/>
          <circle cx="140" cy="130" r="55" fill="url(#cheekBlush)"/>
          <path d="M100,55 C112,35 140,28 155,40 C143,55 118,60 100,55 Z" fill="#4caf50"/>
          <path d="M100,55 L96,35" fill="none" stroke="#6d4c30" stroke-width="6"/>
        </svg>
        """,
        "path (M/C/Z) body + leaf + stroked stem, radialGradient shading, radial blush with alpha stops") +
    PeachSwatch("Peach Slice",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <circle id="sliceOuter" cx="100" cy="100" r="85"/>
            <radialGradient id="fleshGradient" gradientUnits="userSpaceOnUse" cx="100" cy="90" r="110">
              <stop offset="0" stop-color="#fff8e1"/>
              <stop offset="0.5" stop-color="#ffb74d"/>
              <stop offset="1" stop-color="#e65100"/>
            </radialGradient>
            <clipPath id="sliceClip">
              <use xlink:href="#sliceOuter"/>
            </clipPath>
          </defs>
          <g clip-path="url(#sliceClip)">
            <polygon points="0,0 200,0 200,200 0,200" fill="url(#fleshGradient)"/>
            <g opacity="0.35">
              <path d="M100,100 L100,20" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L170,60" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L170,140" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L100,180" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L30,140" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L30,60" fill="none" stroke="#e65100" stroke-width="2"/>
            </g>
          </g>
          <path d="M100,75 C120,75 135,90 135,108 C135,128 118,142 100,142 C82,142 65,128 65,108 C65,90 80,75 100,75 Z" fill="#8d5524" stroke="#5d3a1a" stroke-width="2"/>
          <use xlink:href="#sliceOuter" fill="none" stroke="#c0392b" stroke-width="6"/>
        </svg>
        """,
        "clipPath + use round the flesh, opacity-grouped striations, pit path, use for the skin outline") +
    "</tr></table>" +
    "<table class=\"sw\"><tr>" +
    PeachSwatch("Peach Branch &amp; Blossoms",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <marker id="blossom" markerWidth="8" markerHeight="8" refX="4" refY="4" markerUnits="userSpaceOnUse">
              <circle cx="4" cy="4" r="3.5" fill="#ffd1e3" stroke="#ff8fab" stroke-width="0.6"/>
            </marker>
            <marker id="leafTip" markerWidth="10" markerHeight="10" refX="9" refY="5" orient="auto">
              <path d="M0,5 C3,0 7,0 10,5 C7,10 3,10 0,5 Z" fill="#4caf50"/>
            </marker>
          </defs>
          <path d="M30,170 C50,130 70,90 110,55" fill="none" stroke="#6d4c30" stroke-width="6" stroke-linecap="round"
                marker-mid="url(#blossom)" marker-end="url(#leafTip)"/>
          <polyline points="55,140 75,120 95,100 110,55" fill="none" stroke="none" marker-mid="url(#blossom)"/>
          <text x="100" y="192" font-size="13" font-style="italic" fill="#6d4c30" text-anchor="middle">Prunus persica</text>
        </svg>
        """,
        "&lt;marker&gt; blossoms/leaf tip along a curved branch, italic &lt;text&gt; caption") +
    PeachSwatch("Peach Basket",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <pattern id="weave" patternUnits="userSpaceOnUse" width="14" height="14" patternTransform="rotate(20)">
              <rect width="14" height="14" fill="#c98a4b"/>
              <rect width="14" height="6" fill="#a86b34"/>
            </pattern>
            <radialGradient id="basketPeach" gradientUnits="userSpaceOnUse" cx="0" cy="-15" r="45">
              <stop offset="0" stop-color="#fff3b0"/>
              <stop offset="0.5" stop-color="#ffb347"/>
              <stop offset="1" stop-color="#d1495b"/>
            </radialGradient>
            <linearGradient id="basketShadow" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stop-color="#000000"/>
              <stop offset="1" stop-color="#000000" stop-opacity="0"/>
            </linearGradient>
            <mask id="basketShadowMask" maskUnits="objectBoundingBox" x="0" y="0" width="1" height="1">
              <rect x="20" y="150" width="160" height="30" fill="url(#basketShadow)"/>
            </mask>
          </defs>
          <ellipse cx="100" cy="165" rx="80" ry="15" fill="#000000" opacity="0.25" mask="url(#basketShadowMask)"/>
          <rect x="30" y="110" width="140" height="70" rx="10" fill="url(#weave)" stroke="#7a4a1e" stroke-width="3"/>
          <circle cx="70" cy="105" r="28" fill="url(#basketPeach)" transform="translate(0,0) rotate(-8,70,105)"/>
          <circle cx="120" cy="100" r="30" fill="url(#basketPeach)"/>
          <circle cx="150" cy="115" r="24" fill="url(#basketPeach)" transform="rotate(10,150,115)"/>
        </svg>
        """,
        "pattern-woven basket, rotate() on individual peaches, gradient drop-shadow via mask") +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("svg", "Graphics & Effects", "SVG",
    "Inline and embedded SVG rendered as true vector PDF content: shapes, paths, gradients, patterns, masks, and text.",
    svgHtml, pdfConfig);

// --- SVG Form XObject reuse showcase ---

// Three pieces of SVG artwork: a `position: fixed` logo and a border-image repeated on every one of
// twelve pages (so the border is invoked many times per page too, not once), plus a small icon
// referenced by four SEPARATE <img> elements on one page - demonstrating the two distinct ways this
// reuse pays off: one element repainted across pages, and separate elements sharing one source. Each
// is rendered into ONE document-local Form XObject and invoked wherever it appears, so the file carries
// three copies of the artwork rather than one per placement - the saving this showcase exists to make
// visible. Measured by rendering this same document with the form cache switched off: 28 Form XObjects
// instead of 3, and 157,352 bytes instead of 129,640 - about 18% larger.
const string SvgFormReuseBorderSource =
    "data:image/svg+xml,%3Csvg%20xmlns%3D'http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg'%20width%3D'120'%20height%3D'120'%20viewBox%3D'0%200%20120%20120'%20preserveAspectRatio%3D'none'%3E%3Crect%20width%3D'120'%20height%3D'120'%20fill%3D'%23fde5d5'%2F%3E%3Crect%20x%3D'48'%20y%3D'48'%20width%3D'24'%20height%3D'24'%20fill%3D'%23fffaf6'%2F%3E%3Cg%20fill%3D'%23c95e58'%3E%3Ccircle%20cx%3D'24'%20cy%3D'24'%20r%3D'13'%2F%3E%3Ccircle%20cx%3D'96'%20cy%3D'24'%20r%3D'13'%2F%3E%3Ccircle%20cx%3D'24'%20cy%3D'96'%20r%3D'13'%2F%3E%3Ccircle%20cx%3D'96'%20cy%3D'96'%20r%3D'13'%2F%3E%3Cpath%20d%3D'M60%2010L74%2024%2060%2038%2046%2024Z%20M60%2082L74%2096%2060%20110%2046%2096Z%20M10%2060L24%2046%2038%2060%2024%2074Z%20M82%2060L96%2046%20110%2060%2096%2074Z'%2F%3E%3C%2Fg%3E%3Cg%20fill%3D'%23fff6e9'%3E%3Ccircle%20cx%3D'24'%20cy%3D'24'%20r%3D'5'%2F%3E%3Ccircle%20cx%3D'96'%20cy%3D'24'%20r%3D'5'%2F%3E%3Ccircle%20cx%3D'24'%20cy%3D'96'%20r%3D'5'%2F%3E%3Ccircle%20cx%3D'96'%20cy%3D'96'%20r%3D'5'%2F%3E%3C%2Fg%3E%3C%2Fsvg%3E";

// A third, independent SVG source referenced by four SEPARATE <img> elements (not one element
// repainted across pages, like the logo and border above) - demonstrating that separate elements
// resolving to the same standalone SVG source share one parsed document and therefore one Form
// XObject too, once instead of once per <img>.
const string SvgFormReuseIconSource =
    "data:image/svg+xml,%3Csvg%20xmlns%3D'http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg'%20viewBox%3D'0%200%2024%2024'%3E%3Cpath%20fill%3D'%23a64246'%20d%3D'M12%202L14.9%209.1%2022%2012%2014.9%2014.9%2012%2022%209.1%2014.9%202%2012%209.1%209.1Z'%2F%3E%3C%2Fsvg%3E";

var svgFormReuseSheetTitles = new[]
{
    "Overview", "Line items", "Delivery notes", "Quality checks", "Regional figures", "Customer notes",
    "Inventory", "Timeline", "Forecast", "Appendix A", "Appendix B", "Final page"
};

var svgFormReuseHtml =
    "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
    "<style>" +
    "@page { size: A4; margin: 38px 48px 42px }" +
    "body { margin: 0; color: #543b38; font: 14px Arial, sans-serif }" +
    ".running-logo { position: fixed; top: 0; left: 0; width: 365px; height: 81px }" +
    ".logo-rule { position: fixed; top: 96px; left: 0; right: 0; height: 2px; background: #e8b9a4 }" +
    ".border-panel {" +
    "  position: fixed; top: 112px; left: 0; width: 100%; height: 168px;" +
    "  box-sizing: border-box; border: 16px solid transparent;" +
    $"  border-image-source: url(\"{SvgFormReuseBorderSource}\");" +
    "  border-image-slice: 40%; border-image-width: 16px; border-image-repeat: repeat;" +
    "  background: #fffaf6; padding: 20px 24px }" +
    ".border-panel h2 { margin: 0 0 8px; color: #a64246; font-size: 19px }" +
    ".border-panel p { margin: 0; line-height: 1.45 }" +
    ".sheet { padding-top: 300px }" +
    ".sheet + .sheet { break-before: page }" +
    ".sheet-number { color: #a64246; font-size: 11px; font-weight: bold; letter-spacing: 2px }" +
    "h1 { margin: 9px 0 18px; font-size: 25px }" +
    ".sheet p { max-width: 570px; line-height: 1.65; margin: 0 0 16px }" +
    ".sample-row { margin-top: 30px; border-top: 1px solid #e8b9a4; padding-top: 11px }" +
    ".sample-row strong { float: right; color: #a64246 }" +
    ".icon-row { margin: 4px 0 18px }" +
    ".icon-row img { width: 22px; height: 22px; margin-right: 10px }" +
    "</style></head><body>" +

    // One parsed SvgDocument - the project's own peach mark, from docs/assets/img/peach.svg, plus the
    // wordmark - painted on every output page from a single Form XObject.
    """
    <svg class="running-logo" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 110" width="500" height="110" aria-label="PeachPDF logo">
      <defs>
        <radialGradient id="peach-body" cx="35%" cy="28%" r="85%">
          <stop offset="0%" stop-color="#ffc487"/>
          <stop offset="45%" stop-color="#ff9c66"/>
          <stop offset="100%" stop-color="#f4566a"/>
        </radialGradient>
        <linearGradient id="peach-leaf" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stop-color="#7bc47f"/>
          <stop offset="100%" stop-color="#3e7a4c"/>
        </linearGradient>
      </defs>
      <!-- docs/assets/img/peach.svg, the real project logo, in its own 32-unit space. -->
      <g transform="translate(8 7) scale(2.95)">
        <path fill="url(#peach-body)" d="M16 8.5 C14.8 6.9 12.9 6 10.9 6 C5.9 6 3 10.2 3 15.1 C3 22.3 8.8 29 16 29 C23.2 29 29 22.3 29 15.1 C29 10.2 26.1 6 21.1 6 C19.1 6 17.2 6.9 16 8.5 Z"/>
        <path fill="none" stroke="#000000" stroke-opacity="0.2" stroke-width="1.6" stroke-linecap="round" d="M16 9.5 C14.6 13.5 13.9 19.5 14.6 26"/>
        <path fill="none" stroke="#8a5a3b" stroke-width="2" stroke-linecap="round" d="M16 8.2 C16 6.6 16.6 5.2 17.8 4.2"/>
        <path fill="url(#peach-leaf)" d="M17.5 6.5 C18.5 3.5 21.5 1.8 24.8 2.2 C24.9 5.6 22.6 8.4 19.3 8.9 C18.2 9 17.3 8.3 17.5 6.5 Z"/>
      </g>
      <text x="122" y="53" font-family="Arial" font-size="43" font-weight="bold" fill="#9f3f45">PeachPDF</text>
      <text x="124" y="78" font-family="Arial" font-size="14" fill="#9a756c">VECTOR ARTWORK REUSED ACROSS PAGES</text>
    </svg>
    """ +
    "<div class=\"logo-rule\"></div>" +

    // One div, repainted on every page; its border-image source is a genuine repeating SVG frame, so
    // the same form is invoked once per corner, once per edge tile, twelve times over.
    "<div class=\"border-panel\">" +
    "<h2>One SVG border image, twelve pages</h2>" +
    "<p>The four corners are fixed; the diamond motif repeats along each edge. Both this frame and the " +
    "logo above are drawn into one document-local Form XObject each and invoked for every slice and " +
    "every page, so the PDF stores the artwork twice rather than twenty-four times - a copy per page " +
    "would make this file around 40% larger.</p>" +
    "</div>" +

    string.Concat(svgFormReuseSheetTitles.Select((title, index) =>
        "<section class=\"sheet\">" +
        $"<div class=\"sheet-number\">PAGE {index + 1:00} / 12</div>" +
        $"<h1>{title}</h1>" +
        (index == 0
            ? "<div class=\"icon-row\">" +
              string.Concat(Enumerable.Repeat(SvgFormReuseIconSource, 4)
                  .Select(src => $"<img src=\"{src}\" alt=\"\" width=\"22\" height=\"22\"/>")) +
              "</div>" +
              "<p>The four marks above are four separate &lt;img&gt; elements, not one element repeated - " +
              "each resolves the same SVG source, so they share one Form XObject between them too.</p>"
            : "") +
        "<p>This page repeats the same fixed vector logo and the same SVG border-image. Nothing about " +
        "the artwork changes from sheet to sheet, which is exactly the case a per-page copy would pay " +
        "for twelve times over.</p>" +
        "<p>Reuse is keyed on the parsed SVG document and the size it is painted at, so the twelve " +
        "pages share one form per artwork - and the border-image's own corners and edge tiles, all " +
        "drawn at the same size, share it too.</p>" +
        $"<div class=\"sample-row\"><span>Reference</span><strong>PEACH-{index + 1:000}</strong></div>" +
        "</section>")) +

    "</body></html>";

await SaveShowcaseAsync("svg_form_reuse", "Graphics & Effects", "SVG Form XObject Reuse",
    "The same SVG logo and SVG border-image on all twelve pages, plus a small icon referenced by four separate <img> elements on one page: each is stored once as a document-local Form XObject and invoked wherever it appears, rather than written into the PDF again per page or per element - three copies of the artwork instead of twenty-eight, and a file around 18% smaller.",
    svgFormReuseHtml, pdfConfig);

// --- advanced SVG text showcase (gradient/pattern fill, stroke, textPath) ---

static string TextPanel(string desc, string svg) =>
    "<td>" +
    $"<div class=\"stage\">{svg}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    "</td>";

const string SvgTextCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 33.33% }
    .stage { background: #fafafa; border: 1px solid #ccc; text-align: center }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    </style>
    """;

var svgTextAdvancedHtml = "<!DOCTYPE html><html><head>" + SvgTextCss + "</head><body>" +

    "<h1>SVG text: gradient/pattern fill, stroke &amp; textPath</h1>" +
    "<p class=\"intro\">A gradient/pattern <b>fill</b> or any <b>stroke</b> on &lt;text&gt; outlines each glyph to a vector path and paints it with the same brush/pen machinery shapes use; a &lt;textPath&gt; lays glyphs along a referenced &lt;path&gt;. Plain solid text keeps the fast, selectable text-show path. (docs/supported-svg-features.md)</p>" +

    "<h2>1 — Gradient &amp; pattern fill</h2>" +
    "<table class=\"sw\"><tr>" +
    TextPanel("linear-gradient fill",
        """
        <svg viewBox="0 0 200 70" width="190" height="66">
          <defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#e11"/><stop offset="0.5" stop-color="#b0e"/><stop offset="1" stop-color="#14e"/></linearGradient></defs>
          <text x="8" y="48" font-size="42" font-weight="bold" fill="url(#lg)">Peach</text>
        </svg>
        """) +
    TextPanel("radial-gradient fill",
        """
        <svg viewBox="0 0 200 70" width="190" height="66">
          <defs><radialGradient id="rg"><stop offset="0" stop-color="#ffd23f"/><stop offset="1" stop-color="#ee4266"/></radialGradient></defs>
          <text x="8" y="48" font-size="42" font-weight="bold" fill="url(#rg)">Glow</text>
        </svg>
        """) +
    TextPanel("tiled pattern fill",
        """
        <svg viewBox="0 0 200 70" width="190" height="66">
          <defs><pattern id="dots" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="10" height="10" fill="#2c3e50"/><circle cx="5" cy="5" r="2.5" fill="#f1c40f"/></pattern></defs>
          <text x="8" y="48" font-size="42" font-weight="bold" fill="url(#dots)">Dots</text>
        </svg>
        """) +
    "</tr></table>" +

    "<h2>2 — Stroke on text</h2>" +
    "<table class=\"sw\"><tr>" +
    TextPanel("fill + contrasting stroke",
        """
        <svg viewBox="0 0 200 70" width="190" height="66">
          <text x="8" y="50" font-size="44" font-weight="bold" fill="#ffd23f" stroke="#2c3e50" stroke-width="2">Bold</text>
        </svg>
        """) +
    TextPanel("outline only (fill:none)",
        """
        <svg viewBox="0 0 200 70" width="190" height="66">
          <text x="8" y="50" font-size="44" font-weight="bold" fill="none" stroke="#16a085" stroke-width="1.5">Line</text>
        </svg>
        """) +
    TextPanel("gradient fill + stroke",
        """
        <svg viewBox="0 0 200 70" width="190" height="66">
          <defs><linearGradient id="gs" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ff9966"/><stop offset="1" stop-color="#ff5e62"/></linearGradient></defs>
          <text x="8" y="50" font-size="44" font-weight="bold" fill="url(#gs)" stroke="#7a1c1c" stroke-width="1">Warm</text>
        </svg>
        """) +
    "</tr></table>" +

    "<h2>3 — Text on a path (&lt;textPath&gt;)</h2>" +
    "<p class=\"intro\">Each glyph is placed at its own distance along the path and rotated to the tangent there; text past the path end is dropped. startOffset and text-anchor shift the run's start along the path.</p>" +
    "<table class=\"sw\"><tr>" +
    TextPanel("along a wavy curve, gradient",
        """
        <svg viewBox="0 0 200 110" width="190" height="104">
          <defs>
            <path id="wave" d="M10,70 C50,20 100,110 140,60 S 190,30 195,60" fill="none"/>
            <linearGradient id="wg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#e11"/><stop offset="1" stop-color="#14e"/></linearGradient>
          </defs>
          <use href="#wave" stroke="#ddd" stroke-width="1"/>
          <text font-size="16" font-weight="bold" fill="url(#wg)"><textPath href="#wave">Following a curve!</textPath></text>
        </svg>
        """) +
    TextPanel("around a circle",
        """
        <svg viewBox="0 0 200 110" width="190" height="104">
          <defs><path id="ring" d="M100,95 m-45,0 a45,45 0 1 1 90,0 a45,45 0 1 1 -90,0" fill="none"/></defs>
          <use href="#ring" stroke="#eee" stroke-width="1"/>
          <text font-size="13" font-weight="bold" fill="#16a085"><textPath href="#ring" startOffset="5%">Around and around we go...</textPath></text>
        </svg>
        """) +
    TextPanel("centered on the path (text-anchor)",
        """
        <svg viewBox="0 0 200 110" width="190" height="104">
          <defs><path id="arc" d="M15,90 Q100,10 185,90" fill="none"/></defs>
          <use href="#arc" stroke="#ddd" stroke-width="1"/>
          <text font-size="15" font-weight="bold" fill="#8e44ad" text-anchor="middle"><textPath href="#arc" startOffset="50%">Centered</textPath></text>
        </svg>
        """) +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("svg_text_advanced", "Graphics & Effects", "SVG Text: Gradient, Stroke & Path",
    "SVG text with gradient/pattern fill, stroke, and glyphs laid along a path (textPath) - all rendered as real vector PDF content.",
    svgTextAdvancedHtml, pdfConfig);

// --- SVG vertical writing-mode text showcase ---

// A subset of Noto Sans JP (see assets/fonts/NotoSansJPSubset.LICENSE.txt) covering the CJK/Latin
// characters this showcase uses, so mixed text-orientation renders real upright glyphs rather than
// .notdef boxes.
var svgWritingModeCjkFontB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJPSubset.ttf")));

var svgWritingModeHtml = "<!DOCTYPE html><html><head>" + SvgTextCss + "</head><body>" +

    "<h1>SVG &lt;text&gt;: writing-mode &amp; text-orientation</h1>" +
    "<p class=\"intro\">writing-mode: vertical-rl/vertical-lr on an SVG &lt;text&gt; root advances the pen down the column instead of along a line; text-orientation classifies each glyph (mixed, the default) or forces one answer for every glyph (upright/sideways) - CJK reads upright, Latin/digits rotate 90°, the same Unicode Vertical_Orientation classification the HTML pipeline uses. (docs/supported-svg-features.md)</p>" +

    "<h2>1 — text-orientation: mixed, upright &amp; sideways</h2>" +
    $"<defs><style>@font-face {{ font-family: 'CJK'; src: url('data:font/truetype;base64,{svgWritingModeCjkFontB64}') format('truetype'); }}</style></defs>" +
    "<table class=\"sw\"><tr>" +
    TextPanel("mixed (default): CJK upright, Latin/digits rotated",
        """
        <svg viewBox="0 0 100 130" width="90" height="117">
          <rect x="1" y="1" width="30" height="128" fill="none" stroke="#1a6b8a" stroke-width="2"/>
          <text x="16" y="14" font-family="CJK" font-size="11" writing-mode="vertical-rl" fill="#2c3e50">縦書きPDF24</text>
        </svg>
        """) +
    TextPanel("upright: every glyph upright",
        """
        <svg viewBox="0 0 100 130" width="90" height="117">
          <rect x="1" y="1" width="30" height="128" fill="none" stroke="#1a6b8a" stroke-width="2"/>
          <text x="16" y="14" font-family="CJK" font-size="11" writing-mode="vertical-rl" text-orientation="upright" fill="#2c3e50">縦書きPDF24</text>
        </svg>
        """) +
    TextPanel("sideways: every glyph rotated",
        """
        <svg viewBox="0 0 100 130" width="90" height="117">
          <rect x="1" y="1" width="30" height="128" fill="none" stroke="#1a6b8a" stroke-width="2"/>
          <text x="16" y="14" font-family="CJK" font-size="11" writing-mode="vertical-rl" text-orientation="sideways" fill="#2c3e50">縦書きPDF24</text>
        </svg>
        """) +
    "</tr></table>" +

    "<h2>2 — vertical-rl vs vertical-lr, and a rotated fill gradient</h2>" +
    "<table class=\"sw\"><tr>" +
    TextPanel("vertical-rl",
        """
        <svg viewBox="0 0 100 130" width="90" height="117">
          <defs><style>@font-face { font-family: 'CJK2'; src: url('data:font/truetype;base64,FONTPLACEHOLDER') format('truetype'); }</style></defs>
          <text x="50" y="14" font-family="CJK2" font-size="12" writing-mode="vertical-rl" fill="#8e44ad">縦書き</text>
        </svg>
        """.Replace("FONTPLACEHOLDER", svgWritingModeCjkFontB64)) +
    TextPanel("vertical-lr",
        """
        <svg viewBox="0 0 100 130" width="90" height="117">
          <defs><style>@font-face { font-family: 'CJK3'; src: url('data:font/truetype;base64,FONTPLACEHOLDER') format('truetype'); }</style></defs>
          <text x="50" y="14" font-family="CJK3" font-size="12" writing-mode="vertical-lr" fill="#16a085">縦書き</text>
        </svg>
        """.Replace("FONTPLACEHOLDER", svgWritingModeCjkFontB64)) +
    TextPanel("gradient fill on a rotated (Latin) run",
        """
        <svg viewBox="0 0 100 130" width="90" height="117">
          <defs><linearGradient id="vwg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e11"/><stop offset="1" stop-color="#14e"/></linearGradient></defs>
          <text x="50" y="14" font-size="18" font-weight="bold" writing-mode="vertical-rl" text-orientation="sideways" fill="url(#vwg)">PEACH</text>
        </svg>
        """) +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("svg_vertical_text", "Graphics & Effects", "SVG Text: Vertical Writing Mode",
    "SVG <text> under writing-mode: vertical-rl/vertical-lr with real per-character text-orientation - CJK glyphs upright, Latin/digits rotated, composing with gradient fill.",
    svgWritingModeHtml, pdfConfig);

// --- opacity showcase ---

static string OpacitySwatch(string desc, string bodyHtml, string cssLabel) =>
    "<td>" +
    $"<div class=\"stage\">{bodyHtml}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string OpacityCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .stage { height: 90px; background: repeating-linear-gradient(45deg, #eee 0 8px, #fff 8px 16px); border: 1px solid #999; position: relative; overflow: hidden }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var opacityHtml = "<!DOCTYPE html><html><head>" + OpacityCss + "</head><body>" +

    "<h1>CSS opacity &amp; SVG group opacity Test Page</h1>" +
    "<p class=\"intro\">Each stage has a checkerboard-ish backdrop so translucency is visible. This exercises the isolated-transparency-group implementation (docs/html-css-support.md#opacity, docs/supported-svg-features.md).</p>" +

    "<h2>1 — Basic translucent box</h2>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("opacity: 1 (baseline)",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;\"></div>",
        "opacity: 1") +
    OpacitySwatch("opacity: 0.7",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;opacity:0.7;\"></div>",
        "opacity: 0.7") +
    OpacitySwatch("opacity: 0.3",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;opacity:0.3;\"></div>",
        "opacity: 0.3") +
    OpacitySwatch("opacity: 0 (invisible)",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;opacity:0;\"></div>",
        "opacity: 0") +
    "</tr></table>" +

    "<h2>2 — Overlapping children under one parent opacity (double-blend proof)</h2>" +
    "<p class=\"intro\">The overlap should look like a single flat blend, not a darker double-blend - this is the isolated transparency-group fix in action.</p>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("two opaque overlapping boxes (baseline)",
        "<div style=\"position:absolute;left:10px;top:15px;width:50px;height:50px;background:#e74c3c;\"></div>" +
        "<div style=\"position:absolute;left:35px;top:35px;width:50px;height:50px;background:#3498db;\"></div>",
        "no opacity - opaque overlap") +
    OpacitySwatch("parent opacity:0.5, opaque children",
        "<div style=\"position:absolute;left:0;top:0;width:100%;height:100%;opacity:0.5;\">" +
        "<div style=\"position:absolute;left:10px;top:15px;width:50px;height:50px;background:#e74c3c;\"></div>" +
        "<div style=\"position:absolute;left:35px;top:35px;width:50px;height:50px;background:#3498db;\"></div>" +
        "</div>",
        "parent opacity: 0.5 (children opaque)") +
    OpacitySwatch("nested opacity compounding",
        "<div style=\"position:absolute;left:0;top:0;width:100%;height:100%;opacity:0.6;\">" +
        "<div style=\"position:absolute;left:10px;top:15px;width:70px;height:60px;background:#e74c3c;opacity:0.6;\"></div>" +
        "</div>",
        "parent 0.6 &times; child 0.6 = 0.36 effective") +
    OpacitySwatch("opacity + transform combined",
        "<div style=\"position:absolute;left:20px;top:20px;width:60px;height:50px;background:#8e44ad;opacity:0.5;transform:rotate(15deg);\"></div>",
        "opacity: 0.5; transform: rotate(15deg)") +
    "</tr></table>" +

    "<h2>3 — Opacity over images and gradients</h2>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("opacity on a box containing a raster &lt;img&gt;",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;opacity:0.5;\">" +
        "<img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\" width=\"70\" height=\"70\" />" +
        "</div>",
        "opacity: 0.5 (img child)") +
    OpacitySwatch("opacity on a gradient background",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:linear-gradient(to right,#e74c3c,#3498db);opacity:0.5;\"></div>",
        "opacity: 0.5 (gradient background)") +
    "</tr></table>" +

    "<h2>4 — SVG &lt;g opacity&gt; (the SVG equivalent)</h2>" +
    "<p class=\"intro\">SVG group opacity now uses the same isolated-transparency-group compositing as CSS opacity - the overlap below should also look like a single flat blend.</p>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("&lt;g opacity=\"0.5\"&gt; with overlapping shapes",
        """
        <svg viewBox="0 0 100 100" width="90" height="90">
          <g opacity="0.5">
            <rect x="10" y="15" width="50" height="50" fill="#e74c3c"/>
            <rect x="35" y="35" width="50" height="50" fill="#3498db"/>
          </g>
        </svg>
        """,
        "&lt;g opacity=\"0.5\"&gt;, two overlapping rects") +
    OpacitySwatch("leaf fill-opacity vs group opacity",
        """
        <svg viewBox="0 0 100 100" width="90" height="90">
          <g opacity="0.6">
            <rect x="10" y="10" width="80" height="80" fill="#8e44ad" fill-opacity="0.5"/>
          </g>
        </svg>
        """,
        "group opacity 0.6 &times; leaf fill-opacity 0.5") +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("opacity", "Graphics & Effects", "Opacity",
    "Element opacity composited as real group transparency over text, images, and nested content.",
    opacityHtml, pdfConfig);

// --- stacking context showcase ---
//
// These three cases were all previously silently broken by the stacking-context algorithm only
// recognizing position+z-index (not opacity/transform), plus two pre-existing paint-order bugs it
// had: (1) any box establishing its own stacking context - including plain position:relative;
// z-index siblings - was dropped and never painted at all; (2) an out-of-flow stacking-context
// descendant nested a few plain wrapper divs deep painted during the wrong z-index layer's timing.
// See docs/html-css-support.md#stacking-context.

static string StackingSwatch(string desc, string bodyHtml, string cssLabel) =>
    "<td>" +
    $"<div class=\"stage\">{bodyHtml}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string StackingCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 33% }
    .stage { height: 130px; border: 1px solid #999; position: relative; }
    .chip { position: absolute; width: 90px; height: 90px; color: #fff; font-size: 8pt;
            text-align: center; line-height: 90px; }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var stackingHtml = "<!DOCTYPE html><html><head>" + StackingCss + "</head><body>" +

    "<h1>Stacking Context Test Page</h1>" +
    "<p class=\"intro\">Exercises the CSS stacking-context algorithm (MDN: Positioned layout &gt; Stacking context) - z-index ordering, opacity/transform establishing a stacking context, and out-of-flow content escaping a plain wrapper to compete at its true enclosing stacking context.</p>" +

    "<h2>1 &mdash; z-index siblings, negative and positive</h2>" +
    "<table class=\"sw\"><tr>" +
    StackingSwatch("both siblings paint, in z-index order",
        "<div class=\"chip\" style=\"position:relative;left:10px;top:10px;z-index:1;background:#c0392b;\">z: 1 (back)</div>" +
        "<div class=\"chip\" style=\"position:relative;left:55px;top:-55px;z-index:2;background:#2980b9;\">z: 2 (front)</div>",
        "position:relative;z-index:1 / z-index:2") +
    "</tr></table>" +

    "<h2>2 &mdash; opacity establishes a stacking context</h2>" +
    "<table class=\"sw\"><tr>" +
    StackingSwatch("absolutely-positioned child fades with its opacity parent",
        "<div style=\"position:absolute;left:5px;top:15px;width:180px;height:100px;opacity:0.5;background:#27ae60;\">" +
        "<div class=\"chip\" style=\"left:50px;top:15px;background:#e67e22;\">abs child</div>" +
        "</div>",
        "opacity:0.5 parent, position:absolute child (no z-index)") +
    StackingSwatch("transform establishes a stacking context",
        "<div style=\"position:absolute;left:5px;top:15px;width:180px;height:100px;transform:rotate(6deg);background:#27ae60;\">" +
        "<div class=\"chip\" style=\"left:50px;top:15px;background:#e67e22;\">abs child</div>" +
        "</div>",
        "transform:rotate(6deg) parent, position:absolute child (no z-index)") +
    "</tr></table>" +

    "<h2>3 &mdash; escaping a non-stacking-context wrapper</h2>" +
    "<p class=\"intro\">The nested box's own z-index:-1 competes against its true enclosing stacking context (the page), not just its immediate position:absolute parent (which has no z-index of its own, so does not itself establish a stacking context) - it must paint behind the sibling, not be trapped painting whenever the wrapper happens to.</p>" +
    "<table class=\"sw\"><tr>" +
    StackingSwatch("z-index:-1 box nested in a plain absolute wrapper",
        "<div class=\"chip\" style=\"position:relative;left:10px;top:10px;z-index:0;background:#8e44ad;\">sibling z: 0</div>" +
        "<div style=\"position:absolute;left:70px;top:10px;width:90px;height:90px;\">" +
        "<div class=\"chip\" style=\"position:relative;z-index:-1;background:#7f4020;\">nested z: -1</div>" +
        "</div>",
        "position:absolute wrapper (no z-index) &gt; position:relative;z-index:-1") +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("stacking_context", "Graphics & Effects", "Stacking Contexts",
    "z-index and stacking contexts: paint order across positioned, floated, and transformed boxes.",
    stackingHtml, pdfConfig);

// --- text-transform showcase ---

static string TextTransformSwatch(string desc, string text, string cssValue) =>
    "<td>" +
    $"<div class=\"ttbox\" style=\"text-transform: {cssValue}\">{text}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">text-transform: {cssValue}</div>" +
    "</td>";

static string TextTransformSwatchHtml(string desc, string bodyHtml, string cssLabel) =>
    "<td>" +
    $"<div class=\"ttbox\">{bodyHtml}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string TextTransformCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .ttbox { font-size: 11pt; border: 1px solid #999; background: #f7f7f7; padding: 8px; margin-bottom: 3px; min-height: 1.4em }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var textTransformHtml = "<!DOCTYPE html><html><head>" + TextTransformCss + "</head><body>" +

    "<h1>CSS text-transform Test Page</h1>" +

    "<h2>1 — Core Keywords</h2>" +
    Row(
        TextTransformSwatch("none (baseline)", "Hello World", "none"),
        TextTransformSwatch("uppercase", "hello world", "uppercase"),
        TextTransformSwatch("lowercase", "HELLO WORLD", "lowercase"),
        TextTransformSwatch("capitalize", "hello world", "capitalize")
    ) +

    "<h2>2 — Capitalize: Word-Boundary Edge Cases</h2>" +
    "<p class=\"intro\">Capitalize only uppercases the first letter of each whitespace-delimited word - a hyphenated compound stays one word, not three separately-capitalized fragments.</p>" +
    Row(
        TextTransformSwatch("hyphenated compound", "editor-in-chief", "capitalize"),
        TextTransformSwatch("already mixed case", "hELLO wORLD", "capitalize"),
        TextTransformSwatch("leading punctuation", "&#39;twas the night", "capitalize"),
        TextTransformSwatch("multiple spaces", "one   two   three", "capitalize")
    ) +

    "<h2>3 — Inheritance</h2>" +
    "<p class=\"intro\">text-transform is an inherited property - a child inline element picks it up from its parent unless it sets its own value.</p>" +
    Row(
        TextTransformSwatchHtml("child inherits parent's uppercase",
            "<div style=\"text-transform: uppercase\">parent <span>child inherits</span></div>",
            "div { text-transform: uppercase } span (no override)"),
        TextTransformSwatchHtml("child overrides to none",
            "<div style=\"text-transform: uppercase\">parent <span style=\"text-transform: none\">child overrides</span></div>",
            "span { text-transform: none }"),
        TextTransformSwatchHtml("child overrides to capitalize",
            "<div style=\"text-transform: lowercase\">PARENT <span style=\"text-transform: capitalize\">child override</span></div>",
            "div { text-transform: lowercase } span { text-transform: capitalize }"),
        TextTransformSwatchHtml("baseline, no transform anywhere",
            "<div>no transform <span>plain child</span></div>",
            "(none set)")
    ) +

    "<h2>4 — Combined With Other Text Properties</h2>" +
    Row(
        TextTransformSwatchHtml("uppercase + bold", "<div style=\"text-transform: uppercase; font-weight: bold\">important notice</div>", "text-transform: uppercase; font-weight: bold"),
        TextTransformSwatchHtml("capitalize + centered", "<div style=\"text-transform: capitalize; text-align: center\">quarterly report</div>", "text-transform: capitalize; text-align: center"),
        TextTransformSwatchHtml("uppercase + underline", "<div style=\"text-transform: uppercase; text-decoration: underline\">click here</div>", "text-transform: uppercase; text-decoration: underline"),
        TextTransformSwatchHtml("capitalize list marker text", "<ul style=\"margin:0;padding-left:1.2em;text-transform: capitalize\"><li>first item</li><li>second item</li></ul>", "ul { text-transform: capitalize }")
    ) +

    "<h2>5 — Long-Form Paragraph (Layout Correctness)</h2>" +
    "<p class=\"intro\">Confirms word-wrapping and width measurement operate on the transformed text, not the original - the paragraph below wraps the same as untransformed text of equal length would.</p>" +
    Row(
        TextTransformSwatchHtml("uppercase paragraph, narrow column",
            "<p style=\"text-transform: uppercase; width: 140px; margin: 0;\">the quick brown fox jumps over the lazy dog near the riverbank at dawn.</p>",
            "text-transform: uppercase; width: 140px"),
        TextTransformSwatchHtml("capitalize paragraph, narrow column",
            "<p style=\"text-transform: capitalize; width: 140px; margin: 0;\">the quick brown fox jumps over the lazy dog near the riverbank at dawn.</p>",
            "text-transform: capitalize; width: 140px")
    ) +

    "</body></html>";

await SaveShowcaseAsync("text_transform", "Typography & Text", "text-transform",
    "text-transform variants - uppercase, lowercase, capitalize - applied at render time.",
    textTransformHtml, pdfConfig);

// --- CSS Multi-column Layout showcase ---

static string McSection(string title, string bodyHtml) =>
    $"<h2>{title}</h2>{bodyHtml}";

static string McEntry(string term, string body) =>
    $"<p><b>{term}</b> — {body}</p>";

static string McEntries(int count, string prefix = "entry") =>
    string.Concat(Enumerable.Range(1, count).Select(i =>
        McEntry($"{prefix}-{i}", $"a short definition for {prefix} number {i}, just long enough to wrap onto a second line in a narrow column.")));

// The same content in the same box twice, differing only in the break-inside value, so the effect of
// the value is the whole of the difference between the two halves.
static string AvoidColumnPanel(string label, string breakInside) =>
    "<div class=\"frame\"><div class=\"label\">" + label + "</div>" +
    "<div class=\"mc\" style=\"columns:2;column-gap:20px;column-rule:0.5pt solid #000\">" +
    McEntries(1, "lead") +
    "<div style=\"" + breakInside + "background:#fdf3e3;border:0.5pt solid #d68910;padding:3pt;margin:0\">" +
    "<b>Panel</b> &mdash; a block with a border and a background of its own, holding enough text that it " +
    "cannot finish in the column it starts in: four lines against the two the first column has left for " +
    "it once the entry above has taken its share of the height." +
    "</div></div></div>";

// The same heading and panel in the same box twice, differing only in the heading's break-after, so
// whether the heading travels with the content it introduces is the whole of the difference.
//
// `column-fill: auto` rather than the default, and deliberately: a balanced column's height is derived
// from the content, and the arithmetic then rules the pull out everywhere. "The heading fits column one"
// is `filler + heading <= target` and "the run fits the destination" is `heading + panel <= target`,
// which together need `target >= filler + 2*heading + panel` - more than the whole content, where
// balancing sets the target to a fraction of it. A page-bounded column has a height of its own, which is
// also what the real case looks like: a chapter heading arriving near the foot of a full column.
static string KeepWithNextPanel(string label, string breakAfter) =>
    "<div class=\"frame\"><div class=\"label\">" + label + "</div>" +
    "<div class=\"mc\" style=\"columns:2;column-gap:20px;column-fill:auto;column-rule:0.5pt solid #000\">" +
    McEntries(26, "lead") +
    // Stated on both halves, because the UA print sheet already gives every heading
    // `page-break-after: avoid` - which is exactly what makes this the ordinary case, and what makes
    // "auto" rather than the absence of a declaration the control.
    "<h3 style=\"font-size:9pt;margin:0 0 0.3em;break-after:" + breakAfter + "\">Section heading</h3>" +
    "<div style=\"height:200pt;background:#fdf3e3;border:0.5pt solid #d68910;padding:3pt;margin:0\">" +
    "<b>Panel</b> &mdash; the content this heading introduces. It is taller than the room left below the " +
    "heading, so it starts the next column; whether the heading comes with it is the question." +
    "</div></div></div>";

// The same paragraph in the same box twice, differing only in box-decoration-break. A vertical
// gradient and a large border-radius are the two decorations defined over the whole box, so where
// each one starts and where it rounds is the whole of the difference between the two halves.
static string DecorationBreakPanel(string label, string decorationBreak) =>
    "<div class=\"frame\"><div class=\"label\">" + label + "</div>" +
    // An entry above the panel, as in section 8: it takes part of the first column's balanced share, so
    // the panel below it cannot finish there and splits at the boundary.
    "<div class=\"mc\" style=\"columns:2;column-gap:20px\">" +
    McEntries(1, "lead") +
    "<p style=\"box-decoration-break:" + decorationBreak + ";" +
    "background:linear-gradient(to bottom,#2980b9,#ecf0f1);border:1pt solid #1b4f72;" +
    "border-radius:10pt;padding:4pt;margin:0;text-align:justify\">" +
    string.Join(" ", Enumerable.Range(1, 16).Select(i =>
        $"clause&nbsp;{i} of a paragraph long enough to leave the column it starts in,")) +
    " and to finish in the next one.</p></div></div>";

// The same wrapper in the same box twice, differing only in box-decoration-break. Where the previous
// panel asks what a *whole-box* decoration is measured against, this one asks the plainer question of
// §6.2: whether the wrapper's own block-start border and padding are re-inserted at the column
// boundary. Under `slice` they are not, so the continuation begins flush with the column's content top;
// under `clone` they are, so it begins below a re-opened border and padding. The content is blocks
// rather than text, which is exactly the axis the block path had never asked the property about - it
// read the containing block's content edge either way, so both halves used to render as the `clone` one
// does, but with no border drawn in the room it left.
//
// The entries sit one level further down, inside a plain <div>, deliberately. That inner block is the
// box that *continues* into the second column, so it is placed by CssBox.ResumeInTheNextFragmentainer
// while its entries are placed by ColumnTopForTheChildThisFillBeginsAt - two different sites that have
// to agree. Correcting only the second leaves the inner block's own fragment starting below the entries
// it contains, which paints its border and background outside their own content; with the wrapper's
// decorations on the outer <section> and the entries one level in, that shows up here rather than only
// in the unit tests.
static string ContinuationDecorationPanel(string label, string decorationBreak) =>
    "<div class=\"frame\"><div class=\"label\">" + label + "</div>" +
    "<div class=\"mc\" style=\"columns:2;column-gap:20px;column-rule:0.5pt solid #000\">" +
    McEntries(1, "lead") +
    "<section style=\"box-decoration-break:" + decorationBreak + ";background:#eef9f0;" +
    "border:1pt solid #27ae60;padding:8pt 3pt 3pt;margin:0\">" +
    "<div style=\"margin:0;background:#f4ecf7;border:0.5pt solid #7d3c98\">" +
    McEntries(10, "nested") +
    "</div></section></div></div>";

const string MulticolCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    .mc p { margin: 0 0 0.4em; text-align: justify }
    .frame { border: 1px solid #bbb; padding: 6px; margin-bottom: 8px; background: #fafafa }
    .label { font-size: 6.5pt; color: #666; margin-bottom: 3px }
    </style>
    """;

var multicolHtml = "<!DOCTYPE html><html><head>" + MulticolCss + "</head><body>" +

    "<h1>CSS Multi-column Layout Test Page</h1>" +
    "<p class=\"intro\">Each frame below is one multi-column container. Compare column count/width/rule placement against the label.</p>" +

    McSection("1 &mdash; column-count: 2, short entries",
        "<div class=\"frame\"><div class=\"label\">columns: 2; column-rule: 0.5pt solid #000</div>" +
        "<div class=\"mc\" style=\"columns:2;column-rule:0.5pt solid #000\">" + McEntries(10) + "</div></div>") +

    McSection("2 &mdash; column-count: 3, colored rule",
        "<div class=\"frame\"><div class=\"label\">column-count: 3; column-rule: 2px dashed #c0392b; column-gap: 20px</div>" +
        "<div class=\"mc\" style=\"column-count:3;column-rule:2px dashed #c0392b;column-gap:20px\">" + McEntries(9) + "</div></div>") +

    McSection("3 &mdash; column-width (auto column count)",
        "<div class=\"frame\"><div class=\"label\">column-width: 140px; column-rule: 1px dotted #2980b9 (as many columns as fit)</div>" +
        "<div class=\"mc\" style=\"column-width:140px;column-rule:1px dotted #2980b9\">" + McEntries(8) + "</div></div>") +

    McSection("4 &mdash; page-spanning columns",
        "<div class=\"frame\"><div class=\"label\">columns: 2; enough entries to overflow onto a second page, still 2 columns wide there</div>" +
        "<div class=\"mc\" style=\"columns:2;column-rule:0.5pt solid #000\">" + McEntries(40) + "</div></div>") +

    McSection("5 &mdash; dictionary-style (mirrors css4.pub's Icelandic dictionary)",
        "<div class=\"frame\"><div class=\"label\">columns: 2; column-rule: 0.2pt solid black; text-align: justify; large centered heading as first child</div>" +
        "<div class=\"mc\" style=\"columns:2;column-rule:0.2pt solid black\">" +
        "<h2 style=\"font-size:28pt;text-align:center;margin:0 0 0.2em;border:none\">A</h2>" +
        McEntries(14, "word") + "</div></div>") +

    // One long paragraph continuing from one column into the next, with a background and border so
    // both fragments' own decoration areas are visible: a column is a fragmentainer that differs
    // from its neighbours in the inline axis, so each fragment carries geometry of its own.
    McSection("6 &mdash; a paragraph continuing across a column break",
        "<div class=\"frame\"><div class=\"label\">columns: 2; one paragraph balanced across both columns, split at the boundary - background and border on each fragment</div>" +
        "<div class=\"mc\" style=\"columns:2;column-gap:20px\">" +
        "<p style=\"background:#eef4fb;border:0.5pt solid #2980b9;padding:3pt;margin:0;text-align:justify\">" +
        string.Join(" ", Enumerable.Range(1, 14).Select(i =>
            $"clause&nbsp;{i} of one paragraph that keeps flowing until the column runs out,")) +
        " and then stops.</p></div></div>") +

    // column-fill: balance searches for a column height, and where its first estimate leaves a tail
    // over it grows the target and fills again. On a container that has *resumed* onto a later page
    // that second attempt has to undo what the first placed - including the geometry of the child it
    // is continuing, without touching the fragment the earlier page already holds.
    McSection("7 &mdash; a re-balanced fill on a resumed page",
        "<div class=\"frame\"><div class=\"label\">columns: 3; entries tall enough that the balance search retries, in a container that resumes onto the next page</div>" +
        "<div class=\"mc\" style=\"columns:3;column-rule:0.5pt solid #7f8c8d;column-gap:14px\">" +
        McEntries(60, "term") + "</div></div>") +

    // The two break values that name the column fragmentation context. `break-before: column` puts the
    // heading at the top of the next column with the first column left short; `break-inside:
    // avoid-column` keeps a panel whole rather than letting it split at the boundary the way the
    // paragraph in section 6 does. Neither has any effect outside a multi-column container, and neither
    // may disturb pagination: `avoid-page` on the same panel would not keep it together here.
    McSection("8 &mdash; forced column breaks and avoid-column",
        "<div class=\"frame\"><div class=\"label\">columns: 2; column-fill: auto; h3 { break-before: column } on the second heading &mdash; the first column is left short</div>" +
        "<div class=\"mc\" style=\"columns:2;column-gap:20px;column-fill:auto;column-rule:0.5pt solid #000\">" +
        "<h3 style=\"font-size:9pt;margin:0 0 0.3em\">Kept in column one</h3>" +
        McEntries(2, "before") +
        "<h3 style=\"font-size:9pt;margin:0 0 0.3em;break-before:column\">Forced into column two</h3>" +
        McEntries(2, "after") +
        "</div></div>" +
        AvoidColumnPanel("break-inside: auto &mdash; the panel splits at the column boundary, its border and background cut in two", "") +
        AvoidColumnPanel("break-inside: avoid-column &mdash; the same panel, moved whole to the next column", "break-inside:avoid-column;")) +

    // box-decoration-break at a column boundary, for the decorations that are defined over the *whole*
    // box rather than edge by edge. `slice` renders the box with no breaks present and cuts it
    // afterwards, so the gradient runs once from the top of the first fragment to the bottom of the last
    // and the corners round only at the box's true ends; `clone` wraps each fragment, so both restart in
    // every column. The two panels hold the same text in the same box and differ only in the value.
    McSection("9 &mdash; whole-box decorations at a column break",
        DecorationBreakPanel("box-decoration-break: slice (initial) &mdash; one gradient and one pair of rounded ends across both fragments", "slice") +
        DecorationBreakPanel("box-decoration-break: clone &mdash; the same panel, each fragment decorated in full", "clone")) +

    // §3.1's keep-with-next chain at a column boundary. A run's members are moved on the page grid, and
    // a column has no lower coordinate to move them to - every column of a container begins at the same
    // one - so the break is stated before the head of the run instead and the next column's fill lays
    // the whole run out there. The two panels differ only in the heading's break-after value.
    McSection("10 &mdash; keep-with-next at a column boundary",
        KeepWithNextPanel("break-after: auto &mdash; the heading is left at the foot of the column its content has left", "auto") +
        KeepWithNextPanel("break-after: avoid &mdash; the same heading, travelling with its content into the next column", "avoid")) +

    // §3.1's forced *page* break, inside a container that fragments its own content. It names the page
    // grid, and no column of this container is on another page - so it escapes the container rather than
    // being answered with a column of its own, and the entries after it open the next page.
    McSection("11 &mdash; a forced page break inside a multi-column container",
        "<div class=\"frame\"><div class=\"label\">columns: 2; break-before: page on the second heading &mdash; the entries after it start a new page, not a new column</div>" +
        "<div class=\"mc\" style=\"columns:2;column-gap:20px;column-rule:0.5pt solid #000\">" +
        "<h3 style=\"font-size:9pt;margin:0 0 0.3em\">Before the break</h3>" +
        McEntries(4, "kept") +
        "<h3 style=\"font-size:9pt;margin:0 0 0.3em;break-before:page\">After the break, on a page of its own</h3>" +
        McEntries(4, "moved") +
        "</div></div>") +

    // A break that falls below the container's own child loop. The entries are wrapped in a <section> of
    // their own, so the boundary is between two of *its* descendants rather than between two of the
    // container's - and the record has to travel up through the wrapper before the columns engine can read
    // it. Left alone, the wrapper neither moved nor broke: it kept flowing past the column band and past
    // the page.
    //
    // The two panels differ only in box-decoration-break, and the difference is at the *head* of the
    // continuation column (§6.2): `slice` is one box cut at the boundary, so the wrapper's own top border
    // and padding belong to the fragment it started in and the second column begins flush at the column
    // top; `clone` wraps each fragment, so the second column re-opens with both above it.
    McSection("12 &mdash; a break below the container's own child",
        ContinuationDecorationPanel(
            "box-decoration-break: slice (initial) &mdash; the continuation begins flush at the column top, with no border or padding re-inserted at the break",
            "slice") +
        ContinuationDecorationPanel(
            "box-decoration-break: clone &mdash; the same wrapper, each fragment re-opening with its own top border and padding",
            "clone")) +

    // §3's column-span: all. The heading breaks the column flow into two independently-balanced runs -
    // each of its own 6 entries splits 3-and-3 across the two columns, not 6-and-6 as if the heading
    // were an ordinary column item in the middle of one 12-entry flow. column-rule draws within each
    // run but never through the heading itself.
    McSection("13 &mdash; column-span: all",
        "<div class=\"frame\"><div class=\"label\">columns: 2; column-rule: 0.5pt solid #000; a heading with column-span: all splits the flow into two runs</div>" +
        "<div class=\"mc\" style=\"columns:2;column-gap:20px;column-rule:0.5pt solid #000\">" +
        McEntries(6, "before") +
        "<h2 style=\"column-span:all;background:#fdf3e3;border:0.5pt solid #d68910;padding:3pt;margin:0.3em 0\">" +
        "A heading spanning both columns" +
        "</h2>" +
        McEntries(6, "after") +
        "</div></div>") +

    // css-multicol-1 §2: a float belongs to the column box it appears in, and the container contains its
    // floats. The right float sits at the right edge of the first column, not of the container, and the
    // second column's lines beside its rows are not narrowed by it; the last container holds nothing but
    // floats, which used to be left at zero size.
    McSection("14 &mdash; floats inside columns",
        "<div class=\"frame\"><div class=\"label\">columns: 2; a float: right at the top of the first column &mdash; it stays in its column and shortens only that column's lines</div>" +
        "<div class=\"mc\" style=\"columns:2;column-gap:20px;column-rule:0.5pt solid #000\">" +
        "<p style=\"margin:0;text-align:left\"><span style=\"float:right;width:40pt;height:30pt;background:#d68910;margin:0 0 4pt 6pt\"></span>" +
        "Text that wraps around the float in the first column and then carries on into the second column, where the lines run at the full column width because the float is not beside them. " +
        "More text to fill the columns so that the second column is clearly visible and its lines can be compared with the first.</p>" +
        "</div></div>" +
        "<div class=\"frame\"><div class=\"label\">columns: 2; a container of nothing but floats &mdash; laid out in the first column and contained by the box</div>" +
        "<div class=\"mc\" style=\"columns:2;column-gap:20px;border:0.5pt solid #000\">" +
        "<div style=\"float:left;width:60pt;height:24pt;background:#2e86c1\"></div>" +
        "<div style=\"float:left;width:40pt;height:24pt;background:#27ae60\"></div>" +
        "<div style=\"float:left;width:200pt;height:24pt;background:#d68910\"></div>" +
        "</div></div>") +

    "</body></html>";

await SaveShowcaseAsync("multicol", "Layout", "Multi-column Layout",
    "CSS multi-column layout: column counts, widths, gaps, and column rules.",
    multicolHtml, pdfConfig);

// ── item content fragmentation for grid, flex rows, and flex columns (issues #517/#526) ────
// Before this, a flex or grid item's own content was translated into place as one already-measured
// piece: fine as long as it fit the fragmentainer it landed in, but a page boundary falling through an
// item's text either lost the remainder or drew the same line on both pages. Each engine's items now
// commit their content for real, live against the fragmentainer they finally sit in - so a row, line or
// item that runs out of room continues its remaining text on the next page, the same way an ordinary
// paragraph already did.
static string FragmentationCard(string label, string body) =>
    $"<div class=\"card\"><h3>{label}</h3><div class=\"body\">{body}</div></div>";

static string LoremRows(int count, string startAt) =>
    string.Concat(Enumerable.Range(1, count).Select(i =>
        $"<p>{startAt} entry {i}. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod " +
        "tempor incididunt ut labore et dolore magna aliqua.</p>"));

var gridFragmentationHtml = $$"""
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 10mm }
    body { font: 8.5pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    .grid { display: grid; grid-template-columns: 1fr; row-gap: 6pt }
    .card { border: 0.75pt solid #94a3b8; border-radius: 3pt; padding: 5pt 6pt }
    .card h3 { font-size: 8.5pt; margin: 0 0 3pt; color: #1d4ed8 }
    .card .body p { margin: 0 0 4pt; line-height: 1.35 }
    </style></head><body>
    <h1>Grid row content across a page break</h1>
    <p class="intro">Each row here is one grid item with several paragraphs of text - more than fits one
    page. Earlier rows finish where they finish; the row the boundary falls through continues its
    remaining paragraphs on the next page instead of losing them or drawing the same line twice.</p>
    <div class="grid">
    {{FragmentationCard("Row 1", LoremRows(1, "Row 1"))}}
    {{FragmentationCard("Row 2 &mdash; long enough to cross the page boundary", LoremRows(5, "Row 2"))}}
    {{FragmentationCard("Row 3", LoremRows(1, "Row 3"))}}
    </div>
    </body></html>
    """;

await SaveShowcaseAsync("css_grid_fragmentation", "Layout", "Grid Item Content Across Pages",
    "A grid row's own content now fragments across a page break instead of being lost or duplicated: "
    + "CssLayoutEngineGrid's commit pass lays each row's items out for real once they sit at their final "
    + "position, live against the fragmentainer they land in - the same content-fragmentation guarantee "
    + "flex and ordinary block flow already had (issues #517/#526).",
    gridFragmentationHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

var flexMultilineFragmentationHtml = $$"""
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 10mm }
    body { font: 8.5pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    .flex { display: flex; flex-wrap: wrap; row-gap: 6pt }
    .card { flex: 0 0 100%; border: 0.75pt solid #94a3b8; border-radius: 3pt; padding: 5pt 6pt }
    .card h3 { font-size: 8.5pt; margin: 0 0 3pt; color: #1d4ed8 }
    .card .body p { margin: 0 0 4pt; line-height: 1.35 }
    </style></head><body>
    <h1>Flex line content across a page break, wrapped</h1>
    <p class="intro">width:100% keeps this <code>flex-wrap: wrap</code> row to one item per line, so each
    card below is its own line. The commit pass walks every line, not only the first, so a later line's
    own text still continues on the next page rather than only ever the single-line case working.</p>
    <div class="flex">
    {{FragmentationCard("Line 1", LoremRows(1, "Line 1"))}}
    {{FragmentationCard("Line 2 &mdash; long enough to cross the page boundary", LoremRows(5, "Line 2"))}}
    {{FragmentationCard("Line 3", LoremRows(1, "Line 3"))}}
    </div>
    </body></html>
    """;

await SaveShowcaseAsync("flex_multiline_fragmentation", "Layout", "Flex Line Content Across Pages",
    "A wrapped flex row container's own item content now fragments across a page break for every line, "
    + "not only a single-line container: CssLayoutEngineFlex's commit pass walks every line in one pass, "
    + "committing as many as fit before a later line's content continues on the next page (issues "
    + "#517/#526).",
    flexMultilineFragmentationHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

var flexColumnFragmentationHtml = $$"""
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 10mm }
    body { font: 8.5pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    .col { display: flex; flex-direction: column; row-gap: 6pt }
    .card { border: 0.75pt solid #94a3b8; border-radius: 3pt; padding: 5pt 6pt }
    .card h3 { font-size: 8.5pt; margin: 0 0 3pt; color: #1d4ed8 }
    .card .body p { margin: 0 0 4pt; line-height: 1.35 }
    </style></head><body>
    <h1>Flex column item content across a page break</h1>
    <p class="intro">A <code>flex-direction: column</code> container's items are a sequential flow, unlike
    a row's parallel lines - each item is walked and committed in turn, so a later item's own content
    still continues on the next page when an earlier item already filled it.</p>
    <div class="col">
    {{FragmentationCard("Item 1", LoremRows(1, "Item 1"))}}
    {{FragmentationCard("Item 2 &mdash; long enough to cross the page boundary", LoremRows(5, "Item 2"))}}
    {{FragmentationCard("Item 3", LoremRows(1, "Item 3"))}}
    </div>
    </body></html>
    """;

await SaveShowcaseAsync("flex_column_fragmentation", "Layout", "Flex Column Item Content Across Pages",
    "A flex-direction:column container's items are a sequential flow, and each one's own content now "
    + "fragments across a page break the same way a row-direction line's items already did: "
    + "CssLayoutEngineFlex walks a column-direction line's items in turn, committing each one's content "
    + "live and continuing a later item's remaining content on the next page (issues #517/#526).",
    flexColumnFragmentationHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

var flexColumnBreakPointsHtml = $$"""
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 10mm }
    body { font: 8.5pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    .col { display: flex; flex-direction: column; row-gap: 6pt }
    .card { border: 0.75pt solid #94a3b8; border-radius: 3pt; padding: 5pt 6pt }
    .card h3 { font-size: 8.5pt; margin: 0 0 3pt; color: #1d4ed8 }
    .card .body p { margin: 0 0 4pt; line-height: 1.35 }
    .forced { break-before: page }
    </style></head><body>
    <h1>Break points between items of a column-direction flex line</h1>
    <p class="intro">The second card below declares <code>break-before: page</code>. A column-direction
    line's items are stacked in the block axis, so this is a real break point between two items of the
    same line - it now moves that item (and anything after it in the line) onto the next page instead of
    being read only from the container's very first child.</p>
    <div class="col">
    {{FragmentationCard("Item 1 &mdash; stays on this page", LoremRows(1, "Item 1"))}}
    <div class="card forced"><h3>Item 2 &mdash; break-before: page</h3><div class="body">{{LoremRows(1, "Item 2")}}</div></div>
    {{FragmentationCard("Item 3 &mdash; follows item 2", LoremRows(1, "Item 3"))}}
    </div>
    </body></html>
    """;

await SaveShowcaseAsync("flex_column_break_points", "Layout", "Break Points Between Column Flex Items",
    "A break-before/break-after/break-inside:avoid value between two items of the same "
    + "flex-direction:column line is now honored, moving just that item (and whatever follows it in the "
    + "line) rather than being read only from the container's own first child - each line of a wrapping "
    + "column container relocates independently of the others (issue #455).",
    flexColumnBreakPointsHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

// A footer flex item anchored via margin-top:auto (the same pattern the invoice showcase above
// uses) whose own committed height - pinned once, up front, by the item-content commit pass - falls
// short of the paragraphs it actually holds. The item's own box has no direct text of its own (its
// content lives entirely on its <p> children), which used to mean its continuation on page 2 lost its
// background and padding entirely, even though the paragraphs themselves kept flowing there correctly.
var flexItemBackgroundHtml = $$"""
    <!DOCTYPE html>
    <html><head><style>
    @page { size: a6; margin: 10mm }
    body { font: 8.5pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 11pt; margin: 0 0 0.4em }
    p.intro { color: #6b7280; font-size: 8pt; margin: 0 0 0.8em }
    .col { display: flex; flex-direction: column; height: 100mm }
    .filler { background: #eef2ff; border: 0.75pt solid #94a3b8; border-radius: 3pt; padding: 5pt 6pt }
    footer { margin-top: auto; height: 12mm; background: #cde3d6; border: 0.75pt solid #1d6a52; border-radius: 3pt; padding: 5pt 6pt }
    footer p { margin: 0 0 3pt; line-height: 1.35 }
    </style></head><body>
    <h1>A flex item's background across the page it overflows onto</h1>
    <p class="intro">The green footer below is a flex-direction:column item anchored to the bottom of
    its container via margin-top:auto - the same pattern the invoice showcase uses. Its own committed
    height (12mm) is pinned once, up front, but its paragraphs need more room than that, so they spill
    onto the next page. The footer's background and border now continue there instead of stopping after
    page 1.</p>
    <div class="col">
    <div class="filler">Filler content above the footer.</div>
    <footer>
    {{LoremRows(8, "Footer note")}}
    </footer>
    </div>
    </body></html>
    """;

await SaveShowcaseAsync("flex_item_pinned_height_background", "Layout", "Flex Item Background Past Its Pinned Height",
    "A flex-direction:column item whose own content is entirely block-level children (no direct text on "
    + "the item itself) and whose committed content-box size falls short of what it actually holds now "
    + "keeps its background and border on every page its content spills onto, not only the first - "
    + "FragmentEmitter.BuildDraft previously had a decoration rectangle only for a box with no content at "
    + "all in a fragmentainer (a pure continuation shell) or one whose own bounds already reached it, "
    + "missing the case where a page-grid box's item-content-commit-pinned bounds fall short of the "
    + "children it genuinely holds there (issue #569).",
    flexItemBackgroundHtml, new PdfGenerateConfig { PageSize = PageSize.A6 });

// --- overflow-wrap emergency line-breaking showcase ---
var overflowWrapHtml = """
<!DOCTYPE html><html lang="en"><head><style>
    @page { size: A4; margin: 28pt }
    body { font-family: Arial, sans-serif; color: #222 }
    h1 { font-size: 18pt; margin: 0 0 5pt }
    .intro { color: #555; font-size: 9pt; margin: 0 0 14pt }
    .row { display: flex; gap: 10pt; align-items: flex-start }
    .card { width: 112pt; padding: 7pt; border: 1pt solid #bbb; background: #fff8dc }
    .card h2 { font-size: 9pt; margin: 0 0 5pt; color: #8a5700 }
    .card p { width: 100%; margin: 0; font-size: 8pt; line-height: 1.35 }
    .anywhere { overflow-wrap: anywhere }
    .break-word { overflow-wrap: break-word }
    .alias { word-wrap: break-word }
    .intrinsic { margin-top: 18pt; display: grid; grid-template-columns: min-content min-content; gap: 12pt }
    .chip { padding: 5pt; border: 1pt solid #999; background: #eef6ff; font-size: 8pt }
</style></head><body>
<h1>overflow-wrap</h1>
<p class="intro">Emergency wrapping only breaks an otherwise-unbreakable token. The normal opportunity before the long lake name wins first; once the word is alone on a line, it can split at a grapheme boundary without adding a hyphen.</p>
<div class="row">
  <div class="card"><h2>normal</h2><p>Lake Chargoggagoggmanchauggagoggchaubunagungamaugg is in Massachusetts.</p></div>
  <div class="card"><h2>anywhere</h2><p class="anywhere">Lake Chargoggagoggmanchauggagoggchaubunagungamaugg is in Massachusetts.</p></div>
  <div class="card"><h2>break-word</h2><p class="break-word">Lake Chargoggagoggmanchauggagoggchaubunagungamaugg is in Massachusetts.</p></div>
  <div class="card"><h2>word-wrap alias</h2><p class="alias">Lake Chargoggagoggmanchauggagoggchaubunagungamaugg is in Massachusetts.</p></div>
</div>
<h2 style="font-size:11pt;margin:18pt 0 5pt">Min-content sizing</h2>
<p class="intro">anywhere contributes its grapheme opportunities to min-content; break-word deliberately keeps the whole word as its min-content width.</p>
<div class="intrinsic">
  <div class="chip anywhere">anywhere: Chargoggagoggmanchauggagoggchaubunagungamaugg</div>
  <div class="chip break-word">break-word: Chargoggagoggmanchauggagoggchaubunagungamaugg</div>
</div>
</body></html>
""";

await SaveShowcaseAsync("overflow_wrap", "Typography & Text", "overflow-wrap",
    "CSS Text emergency wrapping with overflow-wrap:anywhere, overflow-wrap:break-word, the legacy word-wrap alias, and their distinct min-content sizing behavior.",
    overflowWrapHtml, pdfConfig);

// --- hyphens: auto multi-language showcase ---
// Document language is a whole-container setting (<html lang>, see CssBox/HtmlContainerInt), so
// each language gets its own small document rather than one page per language like the other
// showcases above.

const string HyphenationCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    .col { width: 90px; border: 1px solid #bbb; padding: 6px; hyphens: auto; text-align: justify; float: left; margin-right: 10px }
    </style>
    """;

// tag: the document's <html lang>. words: a few long words known to have real hyphenation
// points in that language's pattern set, forced to wrap in a narrow column so any hyphenation
// is actually exercised (a real fix for the ASCII-only alphabet gate bug — see
// HyphenationEngine's Unicode-letter check — would have shipped correct-looking pattern data
// that silently never activated for non-Latin scripts like Russian below).
(string Tag, string Title, string[] Words)[] hyphenationShowcases =
[
    ("en-US", "English (en-US)", ["antidisestablishmentarianism", "internationalization", "hyphenation"]),
    ("de-DE", "German (de-DE, reformed orthography default)", ["Rechtsschutzversicherungsgesellschaften", "Konstitution", "Donaudampfschifffahrt"]),
    ("fr", "French (fr)", ["anticonstitutionnellement", "extraordinairement"]),
    ("ru", "Russian (ru, Cyrillic script)", ["предпринимательство", "информационный", "образовательного"])
];

foreach (var (tag, title, words) in hyphenationShowcases)
{
    var wordsHtml = string.Concat(words.Select(w => $"<p>{w}</p>"));
    var hyphenationHtml = $"<!DOCTYPE html><html lang=\"{tag}\">" +
        "<head>" + HyphenationCss + "</head><body>" +
        $"<h1>hyphens: auto — {title}</h1>" +
        "<p class=\"intro\">Each narrow column below forces these long words to wrap; hyphens:auto should split them at real language-appropriate break points instead of just overflowing/wrapping whole.</p>" +
        $"<div class=\"col\">{wordsHtml}</div>" +
        "</body></html>";

    var fileTag = tag.ToLowerInvariant().Replace("-", "_");
    await SaveShowcaseAsync($"hyphenation_{fileTag}", "Typography & Text", $"Hyphenation \u2014 {title}",
        "Automatic hyphenation (hyphens: auto) splitting long words at real, language-appropriate break points in a narrow justified column.",
        hyphenationHtml, pdfConfig);
}

// --- CSS Text 4 hyphenation control properties showcase ---
// Same narrow-justified-column setup as the hyphens:auto showcase above, but each column overrides
// a different one of the five CSS Text 4 hyphenation control properties so the effect of each is
// visible side by side against the first (default-behavior) column.

var hyphenateControlsHtml = "<!DOCTYPE html><html lang=\"en-US\"><head>" +
    """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    .col { width: 95px; border: 1px solid #bbb; padding: 6px; hyphens: auto; text-align: justify; float: left; margin-right: 10px }
    .col h3 { font-size: 7.5pt; margin: 0 0 4px; color: #1d4ed8 }
    .words p { margin: 0 0 6px }
    .prose p { margin: 0 }
    .default { }
    .character { hyphenate-character: "\2192" }
    .limit-chars { hyphenate-limit-chars: 5 6 6 }
    .limit-zone { hyphenate-limit-zone: 90% }
    .lines { width: 110px; font-size: 10px }
    .limit-lines { hyphenate-limit-lines: 1 }
    </style>
    """ +
    "</head><body>" +
    "<h1>Hyphenation control properties (CSS Text 4)</h1>" +
    "<p class=\"intro\">The first two pairs of columns are the same long words; hyphenate-character swaps the glyph and hyphenate-limit-chars moves the break point by widening the before/after minimums. The middle pair is running prose, where hyphenate-limit-zone accepts a larger ragged gap rather than hyphenating the line's last word. The last pair repeats one long word, where hyphenate-limit-lines caps how many consecutive lines may end in a hyphen.</p>" +
    "<div class=\"col words default\"><h3>hyphens: auto</h3><p>internationalization</p><p>antidisestablishmentarianism</p><p>uncharacteristically</p></div>" +
    "<div class=\"col words character\"><h3>hyphenate-character: \"→\"</h3><p>internationalization</p><p>antidisestablishmentarianism</p><p>uncharacteristically</p></div>" +
    "<div class=\"col words limit-chars\"><h3>hyphenate-limit-chars: 5 6 6</h3><p>internationalization</p><p>antidisestablishmentarianism</p><p>uncharacteristically</p></div>" +
    "<div class=\"col prose default\"><h3>hyphenate-limit-zone: 0 (default)</h3><p>This report documents extraordinarily comprehensive internationalization efforts across every department.</p></div>" +
    "<div class=\"col prose limit-zone\"><h3>hyphenate-limit-zone: 90%</h3><p>This report documents extraordinarily comprehensive internationalization efforts across every department.</p></div>" +
    "<div class=\"col prose lines default\"><h3>hyphenate-limit-lines: no-limit (default)</h3><p>antidisestablishmentarianism antidisestablishmentarianism antidisestablishmentarianism antidisestablishmentarianism</p></div>" +
    "<div class=\"col prose lines limit-lines\"><h3>hyphenate-limit-lines: 1</h3><p>antidisestablishmentarianism antidisestablishmentarianism antidisestablishmentarianism antidisestablishmentarianism</p></div>" +
    "</body></html>";

await SaveShowcaseAsync("hyphenation_controls", "Typography & Text", "Hyphenation Control Properties",
    "CSS Text 4's hyphenate-character, hyphenate-limit-chars, hyphenate-limit-zone and hyphenate-limit-lines "
    + "shown side by side against default hyphens:auto behavior: a custom hyphen glyph, wider before/after "
    + "character minimums moving where a word breaks, a wide limit-zone accepting a larger ragged gap at a "
    + "line's end rather than hyphenating its last word, and a limit-lines of 1 stopping two hyphenated "
    + "lines in a row (issue #713).",
    hyphenateControlsHtml, pdfConfig);

// --- CSS Text 4 hyphenate-limit-last showcase ---
// hyphenate-limit-last only has anything to show at a real fragmentainer break, so unlike the other four
// controls above (compact side-by-side columns on one page) this needs two full pages each: a filler
// div pushes the narrow, hyphenating column to end exactly two lines before the page boundary, so its
// last line is a real candidate for the property to act on. The filler height was found empirically
// (dumping CssBox.LineBoxes per page against this exact @page/font/width/line-height combination), not
// hand-derived - this repo's convention for pixel-exact fragmentation fixtures.
var hyphenateLimitLastFillerWords = string.Join(' ', Enumerable.Repeat("x antidisestablishmentarianism", 8));

var hyphenateLimitLastHtml = "<!DOCTYPE html><html lang=\"en-US\"><head>" +
    """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    h3 { font-size: 7.5pt; margin: 0 0 4px; color: #1d4ed8 }
    .col { width: 90px; border: 1px solid #bbb; padding: 6px; font-size: 10px; line-height: 20pt; hyphens: auto }
    .filler { height: 625pt }
    </style>
    """ +
    "</head><body>" +
    "<h1>hyphenate-limit-last (CSS Text 4)</h1>" +
    "<p class=\"intro\">The filler below pushes this narrow, hyphenating column to end two lines before the " +
    "page boundary — under the default none its last line still ends in a hyphen; under always that hyphen " +
    "is undone and the whole word moves to the top of the next page instead.</p>" +
    "<div><h3>hyphenate-limit-last: none (default)</h3>" +
    "<div class=\"filler\"></div>" +
    "<div class=\"col\">" + hyphenateLimitLastFillerWords + "</div></div>" +
    "<div style=\"break-before:page\"><h3>hyphenate-limit-last: always</h3>" +
    "<div class=\"filler\"></div>" +
    "<div class=\"col\" style=\"hyphenate-limit-last:always\">" +
    hyphenateLimitLastFillerWords + "</div></div>" +
    "</body></html>";

await SaveShowcaseAsync("hyphenate_limit_last", "Typography & Text", "hyphenate-limit-last",
    "CSS Text 4's hyphenate-limit-last preventing a hyphen from ending the last line before a page break: "
    + "the default none lets it hyphenate right up to the boundary, always moves the whole word onto the "
    + "following page instead.",
    hyphenateLimitLastHtml, pdfConfig);

// --- Tagged PDF (PDF/UA) showcase ---
// Tags are invisible in a normal page render, so this showcase's value is manual inspection of
// the structure tree (e.g. Acrobat's Tags panel, or another PDF/UA-aware checker) rather than
// visual comparison - it's included anyway per this repo's convention that a new visible
// capability gets a showcase, since several real rendering bugs elsewhere have only ever been
// caught by someone actually opening a showcase's output (see CLAUDE.md's Testing conventions).

const string TaggedPdfCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 10pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 16pt }
    h2 { font-size: 11pt; margin-top: 1.2em; break-after: avoid }
    table { border-collapse: collapse; width: 100% }
    td, th { border: 0.5pt solid #999; padding: 4px; text-align: left }
    .pull-quote { border-left: 3px solid #999; padding-left: 8px; color: #555; margin: 0.6em 0 }
    </style>
    """;

// A 1x1 transparent PNG - same fixture used by TaggedPdfStructureTreeTests.
const string TaggedPdfTinyPngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

var taggedPdfHtml = "<!DOCTYPE html><html lang=\"en-US\"><head>" + TaggedPdfCss + "</head><body>" +

    "<h1>Tagged PDF (PDF/UA) Showcase</h1>" +
    "<p>This document exercises PeachPDF's optional tagged-PDF output (<code>PdfGenerateConfig.EnableTaggedPdf</code>): headings, a paragraph, lists, a table, an image with alt text, a link, HTML5 sectioning elements, and both an explicit <code>-peachpdf-pdf-tag-type</code> override and a <code>none</code> suppression.</p>" +

    "<section>" +
    "<h2>Lists</h2>" +
    "<ul><li>First item</li><li>Second item</li></ul>" +
    "<ol><li>Step one</li><li>Step two</li></ol>" +
    "</section>" +

    "<section>" +
    "<h2>Table</h2>" +
    "<table><thead><tr><th>Name</th><th>Role</th></tr></thead>" +
    "<tbody>" +
    "<tr><td>Ada Lovelace</td><td>Mathematician</td></tr>" +
    "<tr><td>Alan Turing</td><td>Computer Scientist</td></tr>" +
    "</tbody></table>" +
    "</section>" +

    "<section>" +
    "<h2>Image and link</h2>" +
    $"<img src=\"data:image/png;base64,{TaggedPdfTinyPngBase64}\" alt=\"A small red square\" width=\"16\" height=\"16\" />" +
    "<p><a href=\"https://github.com/jhaygood86/PeachPDF\">PeachPDF on GitHub</a> - the underlying Link annotation is cross-referenced with its /Link structure element in both directions (/OBJR and /StructParent).</p>" +
    "</section>" +

    "<section>" +
    "<h2>-peachpdf-pdf-tag-type overrides</h2>" +
    "<div class=\"pull-quote\" style=\"-peachpdf-pdf-tag-type: BlockQuote\">This &lt;div&gt; is promoted to /BlockQuote via an explicit -peachpdf-pdf-tag-type override.</div>" +
    "<div style=\"-peachpdf-pdf-tag-type: none\"><p>This paragraph's wrapper &lt;div&gt; is tagged none, so it is invisible in the structure tree - the &lt;p&gt; below attaches directly to the nearest tagged ancestor instead.</p></div>" +
    "</section>" +

    "<blockquote>A real &lt;blockquote&gt;, tagged /BlockQuote by the default stylesheet (no override needed).</blockquote>" +

    "</body></html>";

var taggedPdfConfig = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
await SaveShowcaseAsync("tagged_pdf", "Standards & Accessibility", "Tagged PDF (PDF/UA)",
    "Accessible, tagged PDF output: a logical structure tree with headings, lists, tables, alt text, links, and tag-type overrides.",
    taggedPdfHtml, taggedPdfConfig);

// --- PDF/A Conformance showcase ---
// PDF/A output is otherwise visually identical to ordinary output - its value is in the file's own
// object structure (an /OutputIntents ICC profile, an XMP /Metadata stream with pdfaid:part/
// conformance, and - since this uses the accessible PdfA2A level - the same tagged structure tree as
// the Tagged PDF showcase above) rather than anything a reader sees on the page, but it's included
// per this repo's showcase convention anyway.

const string PdfACss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 10pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 16pt }
    p.intro { color: #555 }
    </style>
    """;

var pdfAHtml = "<!DOCTYPE html><html lang=\"en-US\"><head><title>PDF/A Conformance Showcase</title>" + PdfACss + "</head><body>" +
    "<h1>PDF/A Conformance Showcase</h1>" +
    "<p class=\"intro\">This document is generated with <code>PdfGenerateConfig.PdfAConformance = PdfAConformance.PdfA2A</code> - the accessible level of PDF/A-2. " +
    "It carries an embedded sRGB ICC output intent, an XMP metadata stream with <code>pdfaid:part</code>/<code>pdfaid:conformance</code>, and - because \"A\" levels build on tagged-PDF output - the same logical structure tree the Tagged PDF showcase demonstrates.</p>" +
    $"<img src=\"data:image/png;base64,{TaggedPdfTinyPngBase64}\" alt=\"A small red square\" width=\"16\" height=\"16\" />" +
    "<p>PDF/A-1 (not shown here) forbids PDF transparency groups entirely - PeachPDF rejects a document that uses CSS/SVG opacity, an SVG mask, or a semi-transparent gradient stop under that level rather than silently producing a non-conformant file. PDF/A-2 and PDF/A-3, used here, permit transparency normally.</p>" +
    "</body></html>";

var pdfAConfig = new PdfGenerateConfig
{
    PageSize = PageSize.A4,
    PdfAConformance = PdfAConformance.PdfA2A,
    Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
};
await SaveShowcaseAsync("pdf_a_conformance", "Standards & Accessibility", "PDF/A Conformance",
    "Archival PDF/A output: an embedded sRGB ICC output intent, XMP metadata with pdfaid:part/conformance, and (at the accessible \"A\" level shown here) a tagged structure tree.",
    pdfAHtml, pdfAConfig);

// --- PDF/A-3 Attachments showcase ---
// PDF/A-3 is the PDF/A part that allows embedding arbitrary files, which PeachPDF exposes as
// PdfGenerateConfig.Attachments. Like the PDF/A showcase above, what matters is in the file's structure -
// each file is indexed both in the catalog's /AF array and in the /Names /EmbeddedFiles tree (open the PDF
// in a viewer and look at its attachments panel) - so the page just shows the report the files belong to.

const string PdfA3AttachmentsCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 10pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 16pt }
    p.intro { color: #555 }
    table { border-collapse: collapse; margin: 10pt 0 }
    th, td { border: 1px solid #999; padding: 4pt 10pt; text-align: right }
    th:first-child, td:first-child { text-align: left }
    </style>
    """;

var pdfA3AttachmentsHtml = "<!DOCTYPE html><html lang=\"en-US\"><head><title>Quarterly Sales Report</title>" + PdfA3AttachmentsCss + "</head><body>" +
    "<h1>Quarterly Sales Report</h1>" +
    "<p class=\"intro\">This PDF/A-3 document carries two files: <code>q3-sales.csv</code>, the data behind the table below, and <code>notes.txt</code>, a supplementary note. " +
    "Each is added through <code>PdfGenerateConfig.Attachments</code> with its own MIME type and <code>AFRelationship</code>, and is listed in the viewer's attachments panel.</p>" +
    "<table><tr><th>Region</th><th>Units</th><th>Revenue</th></tr>" +
    "<tr><td>North</td><td>1,204</td><td>$48,160</td></tr>" +
    "<tr><td>South</td><td>987</td><td>$39,480</td></tr>" +
    "<tr><td>East</td><td>1,533</td><td>$61,320</td></tr>" +
    "<tr><td>West</td><td>1,076</td><td>$43,040</td></tr></table>" +
    "</body></html>";

var pdfA3AttachmentsConfig = new PdfGenerateConfig
{
    PageSize = PageSize.A4,
    PdfAConformance = PdfAConformance.PdfA3B,
    Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
};
pdfA3AttachmentsConfig.Attachments.Add(new PdfAttachment
{
    FileName = "q3-sales.csv",
    MimeType = "text/csv",
    // The table above is derived from this file, and holds nothing the table does not.
    Relationship = PdfAttachmentRelationship.Data,
    Description = "The numbers behind the report's table",
    Data = Encoding.UTF8.GetBytes("Region,Units,Revenue\nNorth,1204,48160\nSouth,987,39480\nEast,1533,61320\nWest,1076,43040\n"),
});
pdfA3AttachmentsConfig.Attachments.Add(new PdfAttachment
{
    FileName = "notes.txt",
    MimeType = "text/plain",
    Relationship = PdfAttachmentRelationship.Supplement,
    Description = "A note that adds to the report",
    Data = Encoding.UTF8.GetBytes("East led the quarter; South is expected to recover once the new store opens.\n"),
});
await SaveShowcaseAsync("pdf_a3_attachments", "Standards & Accessibility", "PDF/A-3 Attachments",
    "Embedded files in a PDF/A-3 document, added through PdfGenerateConfig.Attachments: a CSV holding the report's data and a text note, each with its own MIME type and AFRelationship, indexed in both /AF and /Names /EmbeddedFiles.",
    pdfA3AttachmentsHtml, pdfA3AttachmentsConfig);

// --- ZUGFeRD / Factur-X e-invoice showcases ---
// Built with the declarative API, and with the invoice XML generated by the open-source ZUGFeRD-csharp
// library. The C# shown on the site is Showcases/ZugferdInvoiceShowcase.cs itself - the very file compiled
// into this harness (it is copied next to the executable) - so what a reader sees is what actually ran.
// The two showcases share everything but the profile: EN 16931 (a company-to-company invoice, embedded as
// factur-x.xml) and XRECHNUNG (an invoice to a German public body, embedded as xrechnung.xml).

var zugferdInvoiceSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Showcases", "ZugferdInvoiceShowcase.cs"));

await SaveDeclarativeShowcaseAsync("zugferd_factur_x_invoice", "Standards & Accessibility", "ZUGFeRD / Factur-X Invoice",
    "A hybrid e-invoice: a readable invoice laid out with the declarative API, plus the same invoice as EN 16931 Cross Industry Invoice XML - generated by the open-source ZUGFeRD-csharp library and embedded by PdfGenerateConfig.FacturX in a PDF/A-3 file, with the Factur-X XMP metadata and extension schema.",
    zugferdInvoiceSource,
    gen => ZugferdInvoiceShowcase.BuildAsync(gen, xRechnung: false));

await SaveDeclarativeShowcaseAsync("zugferd_xrechnung_invoice", "Standards & Accessibility", "ZUGFeRD XRechnung Invoice",
    "The same hybrid e-invoice in the German XRECHNUNG profile, for an invoice to a public body: the XML is embedded as xrechnung.xml and the buyer reference (Leitweg-ID) is carried both in the XML and on the page.",
    zugferdInvoiceSource,
    gen => ZugferdInvoiceShowcase.BuildAsync(gen, xRechnung: true));

// --- CMYK Colors showcase ---
// device-cmyk() colors are carried through natively (never approximated to sRGB) and reach the PDF as
// real DeviceCMYK fill/stroke operators - PDF's own mixed-color-space document support means the RGB
// swatches alongside them stay ordinary DeviceRGB, unaffected. Rasterize through both PDFium and MuPDF
// per this repo's paint-verification convention - a content-stream substring check alone isn't proof
// the operators are correctly positioned/composited.

const string CmykColorsCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 16pt }
    .row { display: flex; gap: 12pt; margin-bottom: 12pt }
    .swatch { width: 90pt; height: 60pt; display: flex; align-items: center; justify-content: center;
              color: white; font-size: 9pt; text-align: center; border-radius: 4pt }
    .label { font-size: 8pt; color: #555; margin-top: 4pt }
    .col { display: flex; flex-direction: column; align-items: center }
    </style>
    """;

var cmykColorsHtml = "<!DOCTYPE html><html><head><title>CMYK Colors Showcase</title>" + CmykColorsCss + "</head><body>" +
    "<h1>CMYK Colors</h1>" +
    "<p>Each pair below is the same ink recipe: the left swatch is authored with <code>device-cmyk()</code> and reaches the PDF as a real <code>DeviceCMYK</code> operator; the right swatch is the equivalent RGB color, staying ordinary <code>DeviceRGB</code> on the very same page - PDF's own mixed-color-space document support, not a whole-document conversion.</p>" +

    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: device-cmyk(0 1 1 0)\">device-cmyk(0 1 1 0)</div><div class=\"label\">\"Print red\"</div></div>" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: rgb(237, 28, 36)\">rgb(237, 28, 36)</div><div class=\"label\">RGB equivalent</div></div>" +
    "</div>" +

    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: device-cmyk(1 0 0 0)\">device-cmyk(1 0 0 0)</div><div class=\"label\">Process cyan</div></div>" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: device-cmyk(0 1 0 0)\">device-cmyk(0 1 0 0)</div><div class=\"label\">Process magenta</div></div>" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: device-cmyk(0 0 1 0)\">device-cmyk(0 0 1 0)</div><div class=\"label\">Process yellow</div></div>" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: device-cmyk(0 0 0 1)\">device-cmyk(0 0 0 1)</div><div class=\"label\">Process key (black)</div></div>" +
    "</div>" +

    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: white; color: device-cmyk(0.75 0.68 0.67 0.90); border: 3pt solid device-cmyk(1 0 0 0); font-weight: bold\">CMYK text + border</div><div class=\"label\">color/border-color: device-cmyk()</div></div>" +
    "</div>" +

    "<p>A gradient whose stops are all <code>device-cmyk()</code> interpolates directly in C/M/Y/K space (issue #1090's follow-up), the same way an all-RGB gradient interpolates in RGB space - written as a real <code>/DeviceCMYK</code> shading, not approximated.</p>" +

    "<div class=\"row\">" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: linear-gradient(to right, device-cmyk(0 1 1 0), device-cmyk(1 0 0 0))\">&nbsp;</div><div class=\"label\">linear-gradient(), all-CMYK stops</div></div>" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: radial-gradient(device-cmyk(0 0 1 0), device-cmyk(1 0 0 0))\">&nbsp;</div><div class=\"label\">radial-gradient(), all-CMYK stops</div></div>" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: conic-gradient(device-cmyk(0 1 1 0), device-cmyk(0 1 0 0), device-cmyk(1 0 0 0), device-cmyk(0 1 1 0))\">&nbsp;</div><div class=\"label\">conic-gradient(), all-CMYK stops</div></div>" +
    "<div class=\"col\"><div class=\"swatch\" style=\"background: color-mix(in srgb, device-cmyk(0 1 1 0), device-cmyk(1 0 0 0))\">&nbsp;</div><div class=\"label\">color-mix(), two CMYK operands</div></div>" +
    "</div>" +

    "</body></html>";

var cmykColorsConfig = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
await SaveShowcaseAsync("cmyk_colors", "Standards & Accessibility", "CMYK Colors",
    "CSS device-cmyk() colors, carried through natively as real DeviceCMYK PDF operators (including gradients and color-mix() between two CMYK colors), coexisting on the same page as ordinary RGB colors.",
    cmykColorsHtml, cmykColorsConfig);

// --- PDF/X-1a Conformance showcase ---
// Like PDF/A above, structural value (a CMYK OutputIntent, and PeachPDF's rejection of both live
// transparency and chromatic RGB under X1a) more than visual - but the TrueBlack/RichBlack achromatic
// -RGB conversion (the un-set default text color surviving X1a's CMYK-only restriction) is genuinely
// visible, so this one is worth a render too.

const string PdfXCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 10pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 16pt }
    p.intro { color: #555 }
    </style>
    """;

var pdfXHtml = "<!DOCTYPE html><html><head><title>PDF/X-1a Conformance Showcase</title>" + PdfXCss + "</head><body>" +
    "<h1 style=\"color: device-cmyk(0 0 0 1)\">PDF/X-1a Conformance Showcase</h1>" +
    "<p class=\"intro\">This document is generated with <code>PdfGenerateConfig.PdfXConformance = PdfXConformance.X1a</code> - the strictest PDF/X level, requiring CMYK-only content and forbidding live transparency. It carries a caller-supplied CMYK ICC <code>OutputIntent</code>.</p>" +
    "<p>The heading above and this paragraph's un-set default text color are both plain RGB in the source HTML - X1a converts an <em>achromatic</em> (gray/black) RGB color to CMYK deterministically via <code>ColorOptions.BlackGeneration</code> (<code>UseTrueBlack</code> here) rather than rejecting it, since that has an exact, lossless ink mapping unlike an arbitrary hue.</p>" +
    "<p style=\"color: device-cmyk(0 0.81 0.94 0); font-weight: bold\">A genuinely chromatic color must be authored with device-cmyk() under X1a - color: orange here would throw at generation time instead.</p>" +
    "</body></html>";

var pdfXConfig = new PdfGenerateConfig
{
    PageSize = PageSize.A4,
    PdfXConformance = PdfXConformance.X1a,
    Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
    ColorOptions = new ColorOptions
    {
        OutputIntentProfile = ShowcaseCmykIccProfile.Bytes,
        OutputIntentIdentifier = "Showcase CMYK Profile",
    },
};
await SaveShowcaseAsync("pdf_x1a_conformance", "Standards & Accessibility", "PDF/X-1a Conformance",
    "Print-production PDF/X-1a output: a caller-supplied CMYK ICC output intent, CMYK-only content, and the achromatic-RGB TrueBlack conversion that keeps un-set default text colors working under the CMYK-only restriction.",
    pdfXHtml, pdfXConfig);

// --- PDF Bookmarks (Outline) showcase ---
// Like tagged PDF above, the outline is invisible in a normal page render - its value is manual
// inspection of the reader's bookmark/outline sidebar (e.g. Acrobat's or a browser PDF viewer's
// panel) rather than visual comparison, but included anyway per this repo's showcase convention.

const string BookmarksCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { counter-reset: chapter; font: 10pt Arial, sans-serif; margin: 0 }
    h1.chapter { counter-increment: chapter; font-size: 16pt; break-before: page }
    h1.chapter:first-of-type { break-before: avoid }
    h1.cover { font-size: 20pt; break-before: avoid }
    h2 { font-size: 12pt; margin-top: 1.2em }
    p.intro { color: #555 }
    .toc-entry { display: block; margin: 0.2em 0 }
    </style>
    """;

var bookmarksHtml = "<!DOCTYPE html><html><head>" + BookmarksCss + "</head><body>" +

    "<h1 class=\"cover\" style=\"bookmark-level: none\">Cover Page</h1>" +
    "<p class=\"intro\">This heading is excluded from the outline (bookmark-level: none) even though h1 defaults to bookmark-level: 1 - it's just a cover page, not a real chapter.</p>" +
    "<a class=\"toc-entry\" href=\"#chapter2\" style=\"bookmark-level: 1; bookmark-label: '→ Jump straight to Chapter 2'; -peachpdf-bookmark-target: attr(href)\">Jump straight to Chapter 2</a>" +

    "<h1 class=\"chapter\" style=\"bookmark-label: 'Chapter ' counter(chapter) ': Introduction'\">Introduction</h1>" +
    "<p class=\"intro\">This chapter's bookmark label is generated from a counter, not its own heading text (bookmark-label: 'Chapter ' counter(chapter) ': Introduction').</p>" +
    "<h2>Background</h2><p>Ordinary h2 bookmark (default bookmark-level: 2, label defaults to content(text) - the heading's own rendered text).</p>" +
    "<h2 style=\"bookmark-state: closed\">Appendix Notes (collapsed by default)</h2><p>This h2's bookmark starts collapsed in the reader's outline panel (bookmark-state: closed) - expand it to see its own nested sub-bookmark.</p>" +
    "<h3>Appendix Note A</h3><p>Nested under the collapsed Appendix Notes bookmark - stays hidden until that entry is expanded.</p>" +

    "<h1 class=\"chapter\" id=\"chapter2\">Chapter Two</h1>" +
    "<p class=\"intro\">This is where the \"Jump straight to Chapter 2\" bookmark on the cover page actually points, via -peachpdf-bookmark-target: attr(href) reading its own href.</p>" +

    "</body></html>";

await SaveShowcaseAsync("bookmarks", "Standards & Accessibility", "PDF Bookmarks (Outline)",
    "Automatic PDF outline generation from headings, with full CSS control via bookmark-level/bookmark-label/bookmark-state and -peachpdf-bookmark-target.",
    bookmarksHtml, pdfConfig);

// --- border-style showcase ---

const string BorderStyleCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .bsbox { height: 48px; background: #eee; margin-bottom: 3px }
    .phasepair { position: relative; height: 38px; margin-bottom: 3px }
    .phasebox { position: absolute; left: 0; top: 0; height: 48px; border: 14px #d94a4a; border-style: double dotted inset outset; border-radius: 28px; background: #eee; transform: scale(.5); transform-origin: top left }
    .phasebox.second { left: 140px }
    .wbox { height: 16px; background: #eee; margin-bottom: 1px }
    table.ct { border-collapse: collapse; margin: 0 0 4px }
    table.ct td { width: 26px; height: 14px; padding: 0; background: #eee }
    table.cs { border-collapse: separate; border-spacing: 0 }
    .hrbox { height: 58px } .hrbox hr, .hrbox div { margin: 0 0 12px }
    .wlabel { font-size: 6pt; color: #888; margin-bottom: 4px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var borderStyleHtml = "<!DOCTYPE html><html><head>" + BorderStyleCss + "</head><body>" +

    "<h1>CSS1 border-style Test Page</h1>" +

    "<h2>All CSS1 border-style keywords</h2>" +
    Row(
        BorderStyleSwatch("none", "none"),
        BorderStyleSwatch("hidden", "hidden"),
        BorderStyleSwatch("solid", "solid"),
        BorderStyleSwatch("dotted", "dotted")
    ) +
    Row(
        BorderStyleSwatch("dashed", "dashed"),
        BorderStyleSwatch("double", "double"),
        BorderStyleSwatch("groove", "groove"),
        BorderStyleSwatch("ridge", "ridge")
    ) +
    Row(
        BorderStyleSwatch("inset", "inset"),
        BorderStyleSwatch("outset", "outset")
    ) +

    // The dot/dash period is fitted to each edge so the run starts and ends flush with the corner
    // rather than being cut off part-way through - which means the spacing shifts as the box changes
    // size, and an edge's horizontal and vertical runs can land on slightly different gaps. Showing
    // the same style at several widths is the only way to see that actually happening.
    "<h2>Dot/dash spacing adapts to each edge</h2>" +
    Row(
        BorderFitSwatch("dotted, 100%", "dotted", "100%"),
        BorderFitSwatch("dotted, 85%", "dotted", "85%"),
        BorderFitSwatch("dotted, 70%", "dotted", "70%"),
        BorderFitSwatch("dotted, 55%", "dotted", "55%")
    ) +
    Row(
        BorderFitSwatch("dashed, 100%", "dashed", "100%"),
        BorderFitSwatch("dashed, 85%", "dashed", "85%"),
        BorderFitSwatch("dashed, 70%", "dashed", "70%"),
        BorderFitSwatch("dashed, 55%", "dashed", "55%")
    ) +

    // A beveled style shades one pair of sides darker and the other lighter, so the same keyword reads
    // differently per edge - and a color too dark to darken visibly lightens both faces instead rather
    // than disappearing into itself. These have to DECLARE black to reach that arm: an undeclared
    // border-color never arrives as black, for the reason the section after next demonstrates.
    "<h2>Bevel shading, including colors too dark to darken</h2>" +
    Row(
        BorderColorSwatch("inset, black", "inset", "#000"),
        BorderColorSwatch("outset, black", "outset", "#000"),
        BorderColorSwatch("groove, black", "groove", "#000"),
        BorderColorSwatch("ridge, black", "ridge", "#000")
    ) +

    // The mirror case at the other end: a color too light to lighten visibly keeps the declared color
    // on its lit pair instead of clipping to white, so the bevel still reads as two distinct faces.
    // These sit on a mid-grey fill because a near-white bevel is invisible against the usual #eee.
    "<h2>...and colors too light to lighten</h2>" +
    Row(
        SideSwatch("inset, near-white", "border: 16px inset #f0f0f0; background: #888"),
        SideSwatch("outset, near-white", "border: 16px outset #f0f0f0; background: #888"),
        SideSwatch("groove, near-white", "border: 16px groove #f0f0f0; background: #888"),
        SideSwatch("ridge, white", "border: 16px ridge #fff; background: #888")
    ) +

    // A bevel whose color is currentColor does not shade `color` at all - it shades a fixed light
    // grey, so all three of the first row's boxes paint the same two faces however different their
    // text colors are. That is what an unstyled <hr> and a bare `border: inset` look like in a
    // browser, and it is only visible as a showcase: every one of these parses and lays out
    // identically whichever base is used, so nothing but the painted bytes tells them apart.
    "<h2>A bevel with no declared color</h2>" +
    Row(
        SideSwatch("no color, color: red", "border: 16px inset; color: #d94a4a; background: #888"),
        SideSwatch("no color, color: black", "border: 16px inset; color: #000; background: #888"),
        SideSwatch("no color, color: white", "border: 16px inset; color: #fff; background: #888"),
        // ...and the same grey named explicitly is a different border, because now there IS a
        // declared color to shade. The one pair here that must NOT match.
        SideSwatch("declared #808080", "border: 16px inset #808080; background: #888")
    ) +
    Row(
        SideSwatch("outset, no color", "border: 16px outset; color: #d94a4a; background: #888"),
        SideSwatch("groove, no color", "border: 16px groove; color: #d94a4a; background: #888"),
        // A table display type is the one exemption: it does shade its own currentColor, so this one
        // comes out red where the identical declaration on a div above comes out grey.
        TableBevelExemptionSwatch(),
        // An outline is never substituted either, whatever its style - so this one is red too.
        SideSwatch("outline is not substituted", "outline: 16px inset; color: #d94a4a; background: #888; margin: 16px")
    ) +

    // Each style scales differently: double needs 3px before its three bands are a whole unit each,
    // and a dot/dash pattern's period is a multiple of the width, so a thin edge carries many more of
    // them. An edge too short to hold two dashes degenerates to solid.
    "<h2>The same style at several border widths</h2>" +
    Row(
        WidthSwatch("dotted", [1, 2, 4, 8, 16]),
        WidthSwatch("dashed", [1, 2, 4, 8, 16]),
        WidthSwatch("double", [1, 2, 4, 8, 16]),
        WidthSwatch("groove", [1, 2, 4, 8, 16])
    ) +

    // A corner's mitre runs from the border box's outer corner to its inner corner, so with unequal
    // widths it is not a 45 degree cut - and each band of a double/groove/ridge edge has to follow that
    // same diagonal. Mismatched colors are what make the seam visible at all.
    "<h2>Different width, style and color per side</h2>" +
    Row(
        SideSwatch("mixed widths", "border: solid #4a90d9; border-width: 4px 24px 12px 8px"),
        SideSwatch("mixed colors", "border: 16px solid; border-color: #d94a4a #4ad98a #4a90d9 #d9c74a"),
        SideSwatch("mixed styles", "border: 14px #4a90d9; border-style: solid dashed double dotted"),
        SideSwatch("mixed everything",
            "border-color: #d94a4a #4ad98a #4a90d9 #d9c74a; border-style: double solid groove dashed; border-width: 18px 6px 14px 10px")
    ) +

    // A collapsed table's grid lines are not any one box's edges - each is shared by the boxes on
    // either side of it - so a bevelled line shows BOTH faces, one per half, on every line alike:
    // interior lines and the outermost ones, whichever cell's declaration won them. That is why the
    // collapsed table above each separate one below reads as engraved rather than flat, and why its
    // inset is the same two bands as its ridge. Shading every segment as if it were a top/left edge
    // instead left the collapsed model unable to tell inset or outset apart from solid; groove and
    // ridge already painted two bands and are unchanged, which is what the four together show.
    "<h2>The four bevels on a collapsed table (collapse above, separate below)</h2>" +
    Row(
        CollapsedTableSwatch("inset", "inset"),
        CollapsedTableSwatch("outset", "outset"),
        CollapsedTableSwatch("groove", "groove"),
        CollapsedTableSwatch("ridge", "ridge")
    ) +

    // Where two collapsed grid lines cross, one of them paints the whole square they share - never
    // both, and never split along a diagonal. Giving the rows and columns different colours is what
    // makes that legible: the row line takes every crossing except along the table's own first row
    // line, where the column lines take it unless they are the table's first column line. The four
    // corners are crossings too, which is why they are filled rather than notched - each run used to
    // stop at the perpendicular line's centre from both directions at once. The bevels repeat the
    // fixture because a wrongly-owned joint there bands across instead of down, which reads as a
    // broken frame rather than as an off colour.
    "<h2>Which grid line paints a crossing, and the corners that used to go unpainted</h2>" +
    Row(
        CollapsedJointSwatch("equal: rows take all but the first row line", "solid", "solid"),
        CollapsedJointSwatch("double columns outrank solid rows, and take every crossing", "solid", "double"),
        CollapsedJointSwatch("inset", "inset", "inset"),
        CollapsedJointSwatch("outset", "outset", "outset")
    ) +

    // A horizontal rule is an ordinary box whose border IS the rule, so every border-style applies to
    // it exactly as it does to the zero-height div paired beneath each one here. The two are drawn by
    // the same code and must be indistinguishable; the rule used to be painted by a path of its own
    // that filled each side with the declared color flat, so every bevelled, patterned and double rule
    // below rendered as a solid slab.
    "<h2>The same styles on a horizontal rule (hr above, equivalent div below)</h2>" +
    Row(
        HrStyleSwatch("inset", "border: 4px inset #808080"),
        HrStyleSwatch("outset", "border: 4px outset #808080"),
        HrStyleSwatch("groove", "border: 6px groove #808080"),
        HrStyleSwatch("ridge", "border: 6px ridge #808080")
    ) +
    Row(
        HrStyleSwatch("double", "border: 6px double #4a90d9"),
        HrStyleSwatch("dashed", "border: 3px dashed #d94a4a"),
        HrStyleSwatch("dotted", "border: 3px dotted #4ad98a"),
        HrStyleSwatch("solid", "border: 2px solid #4a90d9")
    ) +

    // The same rules with no colour declared at all, which is how the UA sheet leaves a rule and how
    // most authors restyle one. A bevelled rule derives its two greys from the fixed base, so it still
    // pairs exactly with its equivalent div - neither has a declared colour and both take that base.
    "<h2>...and with no declared colour (hr above, equivalent div below)</h2>" +
    Row(
        HrStyleSwatch("inset, as the UA sheet leaves it", "border-style: inset; border-width: 4px"),
        HrStyleSwatch("outset", "border-style: outset; border-width: 4px"),
        HrStyleSwatch("groove", "border-style: groove; border-width: 6px"),
        HrStyleSwatch("ridge", "border-style: ridge; border-width: 6px")
    ) +

    // A FLAT rule with no declared colour is deliberately shown alone, not paired: it resolves
    // currentColor through `color`, and the UA sheet gives a rule its own `color: gray` that a div
    // does not have - so the pair legitimately differs here, and pairing them would read as a defect.
    // These used to paint the near-invisible #eee that the bevel base was declared as.
    "<h2>A flat rule with no declared colour takes the UA sheet's gray</h2>" +
    Row(
        HrAloneSwatch("solid", "border-style: solid; border-width: 2px"),
        HrAloneSwatch("dashed", "border-style: dashed; border-width: 3px"),
        HrAloneSwatch("dotted", "border-style: dotted; border-width: 3px"),
        HrAloneSwatch("noshade attribute", null)
    ) +

    // A percentage width on a rule resolves against its containing block's CONTENT width, with the
    // rule's own borders and padding outside that basis - so each pair below still lines up exactly,
    // at every width. It did not: the rule used to resolve against a basis already reduced by its own
    // borders, so every one of these came out narrower than its div by twice the border width, and
    // the whole section had to avoid declaring a width at all to stay honest.
    "<h2>A percentage width on a rule (hr above, equivalent div below)</h2>" +
    Row(
        HrStyleSwatch("100%", "width: 100%; border: 4px inset #808080"),
        HrStyleSwatch("75%", "width: 75%; border: 4px inset #808080"),
        HrStyleSwatch("50%", "width: 50%; border: 4px inset #808080"),
        HrStyleSwatch("25%", "width: 25%; border: 4px inset #808080")
    ) +
    Row(
        // A margin shifts the rule without shrinking the basis...
        HrStyleSwatch("50%, margin-left", "width: 50%; margin-left: 20px; border: 4px solid #4a90d9"),
        // ...padding sits outside it in the same way border does...
        HrStyleSwatch("50%, padding", "width: 50%; padding: 0 10px; border: 4px solid #4a90d9"),
        // ...and border-box makes the percentage the border box instead, borders inside it.
        HrStyleSwatch("50%, border-box", "width: 50%; box-sizing: border-box; border: 6px solid #4a90d9"),
        // auto is the one case that is NOT the basis: it is what is left of it after the rule's own
        // edges, which is why an unstyled rule's border box spans its container exactly.
        HrStyleSwatch("auto, with padding", "padding: 0 10px; border: 4px solid #4a90d9")
    ) +
    Row(
        // What "the rule's own edges" means depends on box-sizing: under border-box they are already
        // inside the width, so auto takes out nothing but the margins and the rule spans the cell.
        // Subtracting them literally instead left these two short by exactly that padding and border.
        HrStyleSwatch("auto, border-box + padding", "box-sizing: border-box; padding: 0 10px; border: 4px solid #4a90d9"),
        HrStyleSwatch("auto, border-box", "box-sizing: border-box; border: 6px solid #4a90d9"),
        // ...and the basis is measured from the containing block's CONTENT width, so these two match
        // the same declarations in an unpadded cell above, inset by the cell's own 10px of padding.
        HrPaddedBoxSwatch("50% in a padded box", "width: 50%; border: 4px solid #4a90d9"),
        HrPaddedBoxSwatch("auto in a padded box", "border: 4px solid #4a90d9")
    ) +

    // The classic zero-content "border triangle": four mitred trapezoids meeting at the box's center.
    // It only works if every corner really is cut on the diagonal, so it is the sharpest test there is
    // for the mitre - and the same trick Acid2's nose relies on.
    "<h2>Mitred corners, seen directly</h2>" +
    Row(
        TriangleSwatch("all four sides", "#d94a4a #4ad98a #4a90d9 #d9c74a"),
        TriangleSwatch("one side visible", "transparent transparent #4a90d9 transparent"),
        SideSwatch("thick vs thin", "border: solid #4a90d9; border-width: 30px 2px 30px 2px"),
        SideSwatch("double, uneven", "border: double #4a90d9; border-width: 9px 24px 15px 30px")
    ) +

    // A border whose four sides agree is painted as one continuous outline, so its corners stay seamless
    // and a dot/dash period can be fitted to the whole perimeter. double is two such outlines at the
    // thirds; groove/ridge are two rounded half-width bands whose side colors meet midway through each
    // corner.
    "<h2>Rounded corners</h2>" +
    Row(
        RadiusBorderSwatch("solid", "solid", "16px", "24px"),
        RadiusBorderSwatch("dotted", "dotted", "16px", "24px"),
        RadiusBorderSwatch("dashed", "dashed", "16px", "24px"),
        RadiusBorderSwatch("double", "double", "16px", "24px")
    ) +
    Row(
        RadiusBorderSwatch("solid, pill", "solid", "10px", "999px"),
        RadiusBorderSwatch("dotted, pill", "dotted", "10px", "999px"),
        RadiusBorderSwatch("double, pill", "double", "12px", "999px"),
        RadiusBorderSwatch("groove", "groove", "16px", "24px")
    ) +
    Row(
        RadiusBorderSwatch("solid, elliptical", "solid", "12px", "40px / 20px"),
        RadiusBorderSwatch("double, elliptical", "double", "15px", "40px / 20px"),
        RadiusBorderSwatch("dashed, pill", "dashed", "10px", "999px"),
        RadiusBorderSwatch("ridge", "ridge", "16px", "24px")
    ) +

    // The corner transition follows the ratio of the adjoining widths, so the wider edge owns more
    // of the curve. Opening border_style.html in Chrome provides a direct comparison of the same
    // non-uniform two-band bevel.
    "<h2>Non-uniform rounded bevel</h2>" +
    Row(
        SideSwatch("groove, mixed width and color",
            "border-style: groove; border-width: 18px 6px 14px 10px; border-color: #d94a4a #4ad98a #4a90d9 #d9c74a; border-radius: 24px")
    ) +

    // Every rounded side uses the same width-ratio corner split even when its style differs. This is
    // especially visible where a two-band bevel meets a solid fill or a clipped patterned stroke.
    "<h2>Mixed rounded styles</h2>" +
    Row(
        SideSwatch("groove / solid / ridge / dashed",
            "border: 18px #4a90d9; border-style: groove solid ridge dashed; border-radius: 36px"),
        // Chrome fits the dotted side against the complete rounded centerline. A two-pixel width
        // change shifts that global phase across the top-right transition.
        RoundedPatternPhaseComparisonSwatch()
    ) +

    "</body></html>";

await SaveShowcaseAsync("border_style", "Backgrounds & Borders", "Border Styles",
    "The full set of CSS border styles, from solid and dashed to groove, ridge, inset, and outset.",
    borderStyleHtml, pdfConfig);

// --- block inline placement showcase ---

// One row per declaration, each shown in an ltr and an rtl container of the same width. The container is a
// plain grey band and the box a coloured bar, so where the box lands against the band's two edges is the
// whole picture: which edge a narrower box sits against, which side an over-constrained box overflows, and
// where an auto margin sends it.
static string PlacementRow(string label, string boxCss) =>
    $"<div class=\"row\"><div class=\"label\">{label}</div>" +
    $"<div class=\"band\" style=\"direction: ltr\"><div class=\"bar\" style=\"{boxCss}\"></div></div>" +
    $"<div class=\"band\" style=\"direction: rtl\"><div class=\"bar\" style=\"{boxCss}\"></div></div></div>";

var blockPlacementHtml = """
    <!DOCTYPE html><html><head><style>
    @page { size: a4; margin: 15mm }
    body { font-family: Arial, sans-serif; font-size: 11px; margin: 0 }
    h1 { font-size: 15px; margin: 0 0 4px }
    p.note { margin: 0 0 10px; color: #555 }
    .head, .row { display: flex; gap: 8px; align-items: center; margin-bottom: 6px }
    .head div { font-weight: bold; color: #333 }
    .label { width: 190px; font-family: monospace; font-size: 10px; color: #333 }
    .head .col, .band { width: 160px }
    .head .col + .col, .band + .band { margin-left: 82px }
    .band { background: #ddd; border-left: 2px solid #999; border-right: 2px solid #999; box-sizing: content-box }
    .bar { height: 12px; background: #e8804a; margin: 0 }
    </style></head><body>
    <h1>Where a narrow block sits: ltr and rtl</h1>
    <p class="note">The grey band is the containing block. Each bar is a block-level box with the declaration on
    the left. An rtl block end-aligns; an over-wide one overflows toward the start edge; a single auto margin
    takes all of the slack.</p>
    <div class="head"><div class="label">declaration</div><div class="col">direction: ltr</div><div class="col">direction: rtl</div></div>
    """
    + PlacementRow("width: 64px", "width: 64px")
    + PlacementRow("width: 64px; margin: 0 auto", "width: 64px; margin: 0 auto")
    + PlacementRow("margin-left: auto", "width: 64px; margin-left: auto; margin-right: 0")
    + PlacementRow("margin-right: auto", "width: 64px; margin-left: 0; margin-right: auto")
    + PlacementRow("margin-left: auto; margin-right: 30px", "width: 64px; margin-left: auto; margin-right: 30px")
    + PlacementRow("margin-right: 30px", "width: 64px; margin-right: 30px")
    + PlacementRow("width: 120%; margin-left: 20px", "width: 120%; margin-left: 20px")
    + PlacementRow("max-width: 64px (auto width)", "max-width: 64px")
    + "</body></html>";

await SaveShowcaseAsync("block_inline_placement", "Layout", "Block Placement in ltr and rtl",
    "Where a narrower block-level box sits in a left-to-right and a right-to-left container, including auto margins and overflow.",
    blockPlacementHtml, pdfConfig);

// --- positioned inline showcase ---

// An absolutely positioned box sitting among inline text stays out of the line: the text around it reads on
// as one line, and a positioned inline around it is its containing block, formed from that inline's first and
// last line fragments.
var positionedCartSvg = Convert.ToBase64String(Encoding.UTF8.GetBytes(
    "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'>" +
    "<path d='M3 5h4l3 16h15l3-11H8M11 27h1m11 0h1' fill='none' stroke='white' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'/>" +
    "</svg>"));
var positionedInlineHtml = $$"""
    <!DOCTYPE html><html><head><style>
    @page { size: a4; margin: 15mm }
    body { font-family: Arial, sans-serif; font-size: 12pt; line-height: 1.6; margin: 0; color: #222 }
    h1 { font-size: 16pt; margin: 0 0 4pt }
    h2 { font-size: 12pt; margin: 18pt 0 6pt; color: #1a6b8a }
    p.note { margin: 0 0 8pt; color: #555; font-size: 10pt }
    .term { position: relative; background: #fdf1d6; padding: 0 2pt; border-bottom: 1.5pt solid #e0a42a }
    .tag { position: absolute; left: 0; bottom: 100%; margin-bottom: 2pt; white-space: nowrap; font-size: 7pt;
           line-height: 1; padding: 2pt 4pt; background: #1a6b8a; color: #fff; border-radius: 3pt }
    .corner { position: absolute; right: -5pt; top: -5pt; width: 9pt; height: 9pt; border-radius: 50%; background: #d94a4a }
    .wrap { width: 250pt; padding: 8pt; background: #f4f4f4 }
    .span-box { position: relative; background: rgba(74,144,217,.18) }
    .pin { position: absolute; width: 6pt; height: 6pt; background: #d94a4a }
    .ring { outline: 2pt solid #d94a4a; outline-offset: 2pt }
    .aside { position: absolute; top: 0; right: 0; width: 70pt; font-size: 8pt; line-height: 1.2; color: #777 }
    .cart-row { position: relative; width: 500px; height: 42px; overflow: hidden; background: #193b49 }
    .cart-nav { float: left; width: 440px; height: 42px; box-sizing: border-box;
                padding: 9px 0 0 16px; color: white; font-size: 11px }
    .cart { position: absolute; top: 5px; right: 14px }
    .cart a { display: inline-block; width: 32px; height: 32px;
              background: #227f71 url('data:image/svg+xml;base64,{{positionedCartSvg}}') center/26px no-repeat }
    .cart-badge { position: absolute; top: -4px; right: -6px; min-width: 14px; height: 14px;
                  border-radius: 7px; background: #e65a3d; color: white; font: bold 10px Arial;
                  text-align: center; line-height: 14px }
    </style></head><body>
    <h1>Positioned boxes and inline content</h1>
    <p class="note">An absolutely positioned box among inline content is taken out of the line, so the text around it
    carries on as if it were not there. When its nearest positioned ancestor is an inline, that inline is its
    containing block.</p>

    <h2>Anchored to a word</h2>
    <p style="margin-top: 20pt">The contract renews on the
    <span class="term">anniversary date<span class="tag">see clause 4.2</span></span> unless either party gives
    <span class="term">written notice<span class="corner"></span></span> ninety days before it.</p>

    <h2>An inline that wraps</h2>
    <p class="note">The pins sit at the top-left of the inline's first line and the bottom-right of its last line: the
    corners of the containing block the two fragments form.</p>
    <div class="wrap">Text before the
    <span class="span-box">positioned inline, long enough to wrap onto a second line
    <span class="pin" style="left: -3pt; top: -3pt"></span><span class="pin" style="right: -3pt; bottom: -3pt"></span></span>
    and text after it.</div>

    <h2>An inline holding only the positioned box</h2>
    <p class="note">Each badge's inline wrapper has no text of its own, so it is a zero-width box between the words,
    and the badge hangs from that point.</p>
    <p style="margin-top: 20pt">Status: shipped<span style="position: relative"><span class="tag">new</span></span>
    and reviewed<span style="position: relative"><span class="tag">2 comments</span></span> this week.</p>

    <h2>An empty inline with a larger font</h2>
    <p class="note">The wrapper holds no text, but it is still an inline box on the line, so its 28pt font makes the
    line taller and the text sits lower on the shared baseline. The pins mark the top-left and bottom-left of the
    wrapper's content area, which now lies inside the grey line.</p>
    <p style="background: #e8e8e8; line-height: 1.2">Before the badge<span style="position: relative; font-size: 28pt"><span class="pin" style="left: 0; top: 0"></span><span class="pin" style="left: 0; bottom: 0"></span></span> and after it.</p>

    <h2>The line is not broken</h2>
    <div style="position: relative; width: 300pt; padding-right: 80pt">The words after a positioned note
    <span class="aside">A note placed at the top right of the paragraph.</span>continue on the same line,
    and an outlined span holding one <span class="ring">keeps its outline<span class="aside" style="top: 44pt">A second note.</span></span>.</div>

    <h2>An independent positioned box after a float</h2>
    <p class="note">The navigation float must not shift the cart icon or badge outside the dark bar.</p>
    <div class="cart-row"><div class="cart-nav">Shop &nbsp; Products &nbsp; About</div><div class="cart"><a><span class="cart-badge">6</span></a></div></div>
    </body></html>
    """;

await SaveShowcaseAsync("positioned_inline", "Layout", "Positioned Boxes and Inline Content",
    "Absolutely positioned boxes among inline text, anchored to wrapped inlines, and isolated from a preceding float.",
    positionedInlineHtml, pdfConfig);

// --- horizontal rule attributes showcase ---

// One row per <hr>, each in a fixed-width band so where the rule sits between the band's edges is the whole
// picture: the default 0.5em margins, `align` (which maps to margins, not text-align), `size` (the rule's
// TOTAL height, so size=3 is a 3px rule and size=1 a single hairline), and `color`/`noshade` filling a thick
// rule rather than leaving a white gap between two coloured lines.
static string RuleRow(string label, string ruleHtml) =>
    $"<div class=\"row\"><div class=\"label\">{label}</div><div class=\"band\">{ruleHtml}</div></div>";

var hrAttributesHtml = """
    <!DOCTYPE html><html><head><style>
    @page { size: a4; margin: 15mm }
    body { font-family: Arial, sans-serif; font-size: 11px; margin: 0 }
    h1 { font-size: 15px; margin: 0 0 4px }
    p.note { margin: 0 0 10px; color: #555 }
    .row { display: flex; gap: 10px; align-items: center; margin-bottom: 4px }
    .label { width: 210px; font-family: monospace; font-size: 10px; color: #333 }
    .band { width: 300px; background: #f0f0f0; border-left: 2px solid #999; border-right: 2px solid #999 }
    .band p { margin: 0; font-size: 9px; color: #777 }
    </style></head><body>
    <h1>Horizontal rule attributes and margins</h1>
    <p class="note">Each rule sits in a grey band. A rule has 0.5em above and below by default, and centres when it
    is narrower than the band; <code>align</code> moves it by changing those margins, and an author margin overrides both.</p>
    """
    + RuleRow("default", "<hr>")
    + RuleRow("width=\"50%\"", "<hr width=\"50%\">")
    + RuleRow("width=\"50%\" align=\"left\"", "<hr width=\"50%\" align=\"left\">")
    + RuleRow("width=\"50%\" align=\"center\"", "<hr width=\"50%\" align=\"center\">")
    + RuleRow("width=\"50%\" align=\"right\"", "<hr width=\"50%\" align=\"right\">")
    + RuleRow("align=\"right\" + margin: 0", "<hr width=\"50%\" align=\"right\" style=\"margin: 0\">")
    + RuleRow("hr { margin: 0 } between text", "<p>above</p><hr style=\"margin: 0\"><p>below</p>")
    + RuleRow("hr default between text", "<p>above</p><hr><p>below</p>")
    + RuleRow("size=\"1\"", "<hr size=\"1\">")
    + RuleRow("size=\"3\"", "<hr size=\"3\">")
    + RuleRow("size=\"10\"", "<hr size=\"10\">")
    + RuleRow("size=\"10\" noshade", "<hr size=\"10\" noshade>")
    + RuleRow("size=\"10\" color=\"red\"", "<hr size=\"10\" color=\"red\">")
    + RuleRow("size=\"10\" color=\"#2a7\" width=\"50%\" align=\"right\"", "<hr size=\"10\" color=\"#2a7\" width=\"50%\" align=\"right\">")
    + "</body></html>";

await SaveShowcaseAsync("hr_attributes", "Layout", "Horizontal Rule Attributes",
    "Horizontal rules with their default margins, align, size, color and noshade attributes, and an author margin overriding them.",
    hrAttributesHtml, pdfConfig);

// --- outline showcase ---

const string OutlineCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .obox { height: 40px; background: #eee; margin: 12px 8px; }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

static string OutlineStyleSwatch(string desc, string style) =>
    "<td>" +
    $"<div class=\"obox\" style=\"outline: 8px {style} #4a90d9\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">outline: 8px {style} #4a90d9</div>" +
    "</td>";

static string RoundedOutlineStyleSwatch(string desc, string style) =>
    "<td>" +
    $"<div class=\"obox\" style=\"border-radius: 16px; outline: 8px {style} #4a90d9\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border-radius: 16px; outline: 8px {style} #4a90d9</div>" +
    "</td>";

static string AsymmetricRoundedOutlineSwatch(string desc, string radiusProperty) =>
    "<td>" +
    $"<div class=\"obox\" style=\"border: 3px solid #d94a4a; {radiusProperty}: 24px; outline: 8px solid #4a90d9; outline-offset: 4px\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{radiusProperty}: 24px; outline: 8px solid</div>" +
    "</td>";

static string BorderAndOutlineSwatch(
    string desc, string borderStyle, string outlineStyle, bool rounded = false)
{
    var radius = rounded ? "; border-radius: 16px" : "";
    var css = $"border: 6px {borderStyle} #d94a4a; outline: 6px {outlineStyle} #4a90d9{radius}";
    return "<td>" +
           $"<div class=\"obox\" style=\"{css}\"></div>" +
           $"<div class=\"desc\">{desc}</div>" +
           $"<div class=\"css\">{css}</div>" +
           "</td>";
}

var outlineHtml = "<!DOCTYPE html><html><head>" + OutlineCss + "</head><body>" +

    "<h1>CSS outline Test Page</h1>" +
    "<p class=\"intro\">outline never affects box sizing - it paints a ring entirely outside the border edge, offset by outline-offset, and can visually overlap surrounding content without shifting it.</p>" +

    "<h2>outline-style keywords</h2>" +
    Row(
        OutlineStyleSwatch("solid", "solid"),
        OutlineStyleSwatch("dashed", "dashed"),
        OutlineStyleSwatch("dotted", "dotted"),
        OutlineStyleSwatch("double", "double")
    ) +
    Row(
        OutlineStyleSwatch("groove", "groove"),
        OutlineStyleSwatch("ridge", "ridge"),
        OutlineStyleSwatch("inset", "inset"),
        OutlineStyleSwatch("outset", "outset")
    ) +
    Row(
        "<td><div class=\"obox\" style=\"outline: 8px auto #4a90d9\"></div>" +
        "<div class=\"desc\">auto (UA ring: solid, 2px, width ignored)</div>" +
        "<div class=\"css\">outline: 8px auto #4a90d9</div></td>" +
        "<td><div class=\"obox\" style=\"outline: 1px auto #4a90d9\"></div>" +
        "<div class=\"desc\">auto, 1px declared - same ring</div>" +
        "<div class=\"css\">outline: 1px auto #4a90d9</div></td>" +
        "<td><div class=\"obox\" style=\"outline: 8px solid #4a90d9\"></div>" +
        "<div class=\"desc\">8px solid, for comparison</div>" +
        "<div class=\"css\">outline: 8px solid #4a90d9</div></td>"
    ) +

    "<h2>Translucent patterned corners</h2>" +
    "<p class=\"intro\">Corner dots and dashes have the same opacity as the straight runs, including on a wrapped inline outline.</p>" +
    Row(
        "<td><div class=\"obox\" style=\"outline:8px dashed rgba(74,144,217,.5)\"></div>" +
        "<div class=\"desc\">dashed outline</div></td>",
        "<td><div class=\"obox\" style=\"outline:8px dotted rgba(74,144,217,.5)\"></div>" +
        "<div class=\"desc\">dotted outline</div></td>",
        "<td><div class=\"obox\" style=\"border:8px dashed rgba(74,144,217,.5)\"></div>" +
        "<div class=\"desc\">dashed border</div></td>",
        "<td><div style=\"height:40px;margin:12px 8px;font:12px Arial\">" +
        "<span style=\"outline:6px dashed rgba(74,144,217,.5)\">Alpha<br>Beta<br>Gamma</span></div>" +
        "<div class=\"desc\">wrapped inline outline</div></td>"
    ) +

    "<h2>border-radius following</h2>" +
    Row(
        RoundedOutlineStyleSwatch("rounded solid", "solid"),
        RoundedOutlineStyleSwatch("rounded dashed", "dashed"),
        RoundedOutlineStyleSwatch("rounded double", "double"),
        RoundedOutlineStyleSwatch("rounded groove", "groove")
    ) +
    Row(
        RoundedOutlineStyleSwatch("rounded dotted", "dotted"),
        RoundedOutlineStyleSwatch("rounded ridge", "ridge"),
        RoundedOutlineStyleSwatch("rounded inset", "inset"),
        RoundedOutlineStyleSwatch("rounded outset", "outset")
    ) +

    "<h2>asymmetric border-radius following</h2>" +
    "<p class=\"intro\">The red border has exactly one rounded corner. The blue outline should follow that corner while its other three corners remain square.</p>" +
    Row(
        AsymmetricRoundedOutlineSwatch("top-left only", "border-top-left-radius"),
        AsymmetricRoundedOutlineSwatch("top-right only", "border-top-right-radius"),
        AsymmetricRoundedOutlineSwatch("bottom-right only", "border-bottom-right-radius"),
        AsymmetricRoundedOutlineSwatch("bottom-left only", "border-bottom-left-radius")
    ) +

    "<h2>wrapped inline outline</h2>" +
    "<p class=\"intro\">An outline on an inline element that wraps across lines is drawn as one connected shape around all of them, matching Chromium: each line's rectangle is expanded by outline-offset, the rectangles are unioned, and the outline traces the boundary of the region they cover - running around its concave corners rather than closing across each wrap. Whether the lines merge into one piece depends only on whether those expanded rectangles <b>touch</b>; a border is not a special case, it just makes each line's box taller, and line spacing or outline-offset each cause the overlap on their own. Border itself keeps the sliced geometry either way, staying open at internal line breaks as box-decoration-break's slice value (which does not govern outline at all) requires. Every outline style follows the merged shape: a dash pattern is fitted along each of its edges, and a bevel takes each edge's light or dark face from the direction that edge runs in - the rule that still works once the lines have merged and a per-side one no longer does.</p>" +

    "<div style=\"display:flex; gap:26px; margin:12px; font-size:12pt; line-height:1.3\">" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">lines apart - separate pieces</div>" +
    "<div class=\"css\">line-height: 2</div>" +
    "<span style=\"border-radius:12px; outline:6px solid #4a90d9; line-height:2\">Alpha<br>Beta<br>Gamma</span></div>" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">closer lines touch - one shape, no border involved</div>" +
    "<div class=\"css\">line-height: 1.2</div>" +
    "<span style=\"border-radius:12px; outline:6px solid #4a90d9; line-height:1.2\">Alpha<br>Beta<br>Gamma</span></div>" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">same wide spacing, merged by offset alone</div>" +
    "<div class=\"css\">line-height: 2; outline-offset: 8px</div>" +
    "<span style=\"border-radius:12px; outline:6px solid #4a90d9; outline-offset:8px; line-height:2\">Alpha<br>Beta<br>Gamma</span></div>" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">border + outline: outline merges, border stays open</div>" +
    "<div class=\"css\">border: 6px; outline: 6px</div>" +
    "<span style=\"border-radius:12px; border:6px solid #d94a4a; outline:6px solid #4a90d9; line-height:2\">Alpha<br>Beta<br>Gamma</span></div></div>" +

    "<p class=\"intro\">The patterned and bevelled styles follow that same merged shape rather than restarting at each line: the dashes turn the step between two lines, and the bevel keeps one light source across the whole shape instead of relighting every line.</p>" +
    "<div style=\"display:flex; gap:26px; margin:12px; font-size:12pt; line-height:1.3\">" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">dashed follows the merged shape too</div>" +
    "<div class=\"css\">outline: 6px dashed</div>" +
    "<span style=\"border-radius:12px; border:6px solid #d94a4a; outline:6px dashed #4a90d9; line-height:2\">Alpha<br>Beta<br>Gamma</span></div>" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">dotted turns the step's corners</div>" +
    "<div class=\"css\">outline: 6px dotted</div>" +
    "<span style=\"border:6px solid #d94a4a; outline:6px dotted #4a90d9; line-height:2\">Alpha<br>Beta<br>Gamma</span></div>" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">bevel lit per edge direction, not per line</div>" +
    "<div class=\"css\">outline: 8px groove</div>" +
    "<span style=\"border-radius:12px; border:6px solid #d94a4a; outline:8px groove #4a90d9; line-height:2\">Alpha<br>Beta<br>Gamma</span></div>" +
    "<div style=\"width:150px; line-height:1.3\"><div class=\"desc\">rounded corners shaded from the edges they join</div>" +
    "<div class=\"css\">border-radius: 12px; outline: 8px inset</div>" +
    "<span style=\"border-radius:12px; border:6px solid #d94a4a; outline:8px inset #4a90d9; line-height:2\">Alpha<br>Beta<br>Gamma</span></div></div>" +

    "<h2>border + outline together</h2>" +
    "<p class=\"intro\">The red border occupies the box edge; the blue outline is a separate ring outside it. Rounded samples show both contours following the same border-radius.</p>" +
    Row(
        BorderAndOutlineSwatch("solid border + solid outline", "solid", "solid"),
        BorderAndOutlineSwatch("double border + dashed outline", "double", "dashed"),
        BorderAndOutlineSwatch("groove border + double outline", "groove", "double"),
        BorderAndOutlineSwatch("inset border + outset outline", "inset", "outset")
    ) +
    Row(
        BorderAndOutlineSwatch("rounded solid + solid", "solid", "solid", rounded: true),
        BorderAndOutlineSwatch("rounded double + dashed", "double", "dashed", rounded: true),
        BorderAndOutlineSwatch("rounded groove + double", "groove", "double", rounded: true),
        BorderAndOutlineSwatch("rounded inset + outset", "inset", "outset", rounded: true)
    ) +

    "<h2>outline-offset</h2>" +
    "<p class=\"intro\">A negative offset pulls the outline back over the border and padding; a positive offset pushes it further away.</p>" +
    Row(
        "<td><div class=\"obox\" style=\"outline: 6px solid #d94a4a; outline-offset: -10px; border: 3px solid #333\"></div>" +
        "<div class=\"desc\">outline-offset: -10px</div>" +
        "<div class=\"css\">outline: 6px solid #d94a4a; outline-offset: -10px</div></td>" +
        "<td><div class=\"obox\" style=\"outline: 6px solid #d94a4a; border: 3px solid #333\"></div>" +
        "<div class=\"desc\">outline-offset: 0 (default)</div>" +
        "<div class=\"css\">outline: 6px solid #d94a4a; border: 3px solid #333</div></td>" +
        "<td><div class=\"obox\" style=\"outline: 6px solid #d94a4a; outline-offset: 10px; border: 3px solid #333\"></div>" +
        "<div class=\"desc\">outline-offset: 10px</div>" +
        "<div class=\"css\">outline: 6px solid #d94a4a; outline-offset: 10px</div></td>" +
        "<td><div class=\"obox\" style=\"width: 30px; height: 30px; outline: 6px solid #d94a4a; outline-offset: -40px; border: 3px solid #333\"></div>" +
        "<div class=\"desc\">large negative offset stays visible</div>" +
        "<div class=\"css\">width: 30px; height: 30px; outline: 6px solid #d94a4a; outline-offset: -40px</div></td>"
    ) +

    "<h2>outline-color: invert</h2>" +
    "<p class=\"intro\">invert performs a true per-pixel color inversion of whatever is underneath the outline (via a PDF blend mode), not an approximation - it reads correctly over any background.</p>" +
    Row(
        "<td><div class=\"obox\" style=\"background: #d94a4a; outline: 10px solid invert; outline-offset: -14px\"></div>" +
        "<div class=\"desc\">over a red background</div>" +
        "<div class=\"css\">outline: 10px solid invert; outline-offset: -14px</div></td>" +
        "<td><div class=\"obox\" style=\"background: #2e7d32; outline: 10px solid invert; outline-offset: -14px\"></div>" +
        "<div class=\"desc\">over a green background</div>" +
        "<div class=\"css\">outline: 10px solid invert; outline-offset: -14px</div></td>" +
        "<td><div class=\"obox\" style=\"background: linear-gradient(to right, #222, #eee); outline: 10px solid invert; outline-offset: -14px\"></div>" +
        "<div class=\"desc\">over a gradient</div>" +
        "<div class=\"css\">outline: 10px solid invert; outline-offset: -14px</div></td>"
    ) +

    "<h2>Layout-neutral: outline never shifts surrounding content</h2>" +
    "<p class=\"intro\">This box's outline is wider than its own padding, so it visually overlaps its siblings - but every box below sits exactly where it would if the outline were removed.</p>" +
    "<div style=\"background:#eee\">before</div>" +
    "<div style=\"background:#fff2cc; outline: 20px solid #d94a4a; outline-offset: 6px; margin: 4px 0\">outlined (20px, offset 6px)</div>" +
    "<div style=\"background:#eee\">after</div>" +

    "</body></html>";

await SaveShowcaseAsync("outline", "Backgrounds & Borders", "Outline",
    "outline / outline-color / outline-style / outline-width / outline-offset: a layout-neutral ring drawn outside the border edge, including a true per-pixel color inversion for outline-color: invert.",
    outlineHtml, pdfConfig);

// --- vertical-align showcase ---

static string VerticalAlignSwatch(string desc, string va) =>
    "<td>" +
    "<p class=\"vabox\">" +
    "<span class=\"tall\">TALL</span> " +
    $"<span class=\"target\" style=\"vertical-align:{va}\">aligned</span>" +
    "</p>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">vertical-align: {va}</div>" +
    "</td>";

const string VerticalAlignCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .vabox { border: 1px solid #999; margin-bottom: 3px; white-space: nowrap }
    .tall { font-size: 30pt }
    .target { font-size: 10pt; background: #ffe58a }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var verticalAlignHtml = "<!DOCTYPE html><html><head>" + VerticalAlignCss + "</head><body>" +

    "<h1>CSS1 vertical-align Test Page</h1>" +
    "<p class=\"intro\" style=\"font-size:7pt;color:#555\">Each swatch shows a 30pt \"TALL\" span establishing the line's height, with a highlighted 10pt \"aligned\" span positioned per the labeled value.</p>" +

    "<h2>All CSS1 vertical-align keywords</h2>" +
    Row(
        VerticalAlignSwatch("baseline (default)", "baseline"),
        VerticalAlignSwatch("sub", "sub"),
        VerticalAlignSwatch("super", "super"),
        VerticalAlignSwatch("top", "top")
    ) +
    Row(
        VerticalAlignSwatch("middle", "middle"),
        VerticalAlignSwatch("bottom", "bottom"),
        VerticalAlignSwatch("text-top", "text-top"),
        VerticalAlignSwatch("text-bottom", "text-bottom")
    ) +

    "<h2>Length and percentage (CSS 2.1 §10.8.1): raises/lowers from the box's own baseline</h2>" +
    Row(
        VerticalAlignSwatch("length, +8pt (raised)", "8pt"),
        VerticalAlignSwatch("length, -8pt (lowered)", "-8pt"),
        VerticalAlignSwatch("percentage, 50% (of the span's own line-height)", "50%"),
        VerticalAlignSwatch("percentage, -50%", "-50%")
    ) +

    "</body></html>";

await SaveShowcaseAsync("vertical_align", "Typography & Text", "Vertical Align",
    "vertical-align behaviors for inline content, from baseline and middle to explicit offsets, including its length and percentage forms.",
    verticalAlignHtml, pdfConfig);

// --- atomic inline (inline-block) declared-width showcase (CSS 2.1 §10.3.9) ---

const string AtomicInlineWidthCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0; color: #1a1a1a }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 1.1em 0 0.4em; padding-bottom: 2px;
         border-bottom: 1px solid #bbb; break-after: avoid }
    p.intro { font-size: 8pt; color: #555; margin: 0 0 0.4em; max-width: 470pt }
    .row { margin-bottom: 4pt }
    .caption { font-size: 7.5pt; color: #666; margin-bottom: 2pt }
    .box { display: inline-block; width: 90pt; padding: 3pt 5pt;
           border: 1pt solid #1d6fa5; background: #e8f4fb }
    .key { display: inline-block; width: 120pt; padding: 2pt 5pt;
           background: #f1f1f1; border-left: 3pt solid #1d6fa5 }
    .tick { display: inline-block; width: 7pt; padding: 3pt 0 4pt;
            border: 1pt solid #444; background: #fff }
    .ticked { background: #1d6fa5; border-color: #1d6fa5 }
    .sized { display: inline-block; width: 120pt; padding: 3pt 6pt;
             border: 2pt solid #b8960f; background: #fdf6dd }
    .borderbox { box-sizing: border-box }
    .rule { border-top: 1px dashed #c33; width: 120pt; margin: 2pt 0 6pt }
    .percent-row { width: 400pt; border-right: 1px dashed #c33; background: #fafafa }
    .percent { display: inline-block; width: 50%; padding: 3pt 0;
               border: 1pt solid #6b4fa1; background: #eee8fa }
    .wrapbox { display: inline-block; width: 120pt; padding: 3pt 5pt;
               border: 1pt solid #1d6fa5; background: #e8f4fb }
    .boxh { display: inline-block; height: 50pt; padding: 3pt 5pt; margin-right: 6pt;
            border: 1pt solid #1d6fa5; background: #e8f4fb; vertical-align: top }
    .boxh-min { display: inline-block; min-height: 50pt; padding: 3pt 5pt; margin-right: 6pt;
                border: 1pt solid #b8960f; background: #fdf6dd; vertical-align: top }
    .boxh-narrow { width: 90pt }
    </style>
    """;

var atomicInlineWidthHtml = "<!DOCTYPE html><html><head>" + AtomicInlineWidthCss + "</head><body>" +

    "<h1>Atomic inline sizing</h1>" +
    "<p class=\"intro\">CSS 2.1 &sect;10.3.9 hands a non-replaced <code>inline-block</code> to shrink-to-fit " +
    "only when its <code>width</code> is <code>auto</code>. An explicit width is used as declared, and it " +
    "sizes the box itself &mdash; not merely the room the line reserves for it &mdash; so the background, " +
    "border and overflow clip are painted at that width whether or not the content fills it. CSS 2.1 " +
    "&sect;10.6.3 does the same for a non-auto <code>height</code>/<code>min-height</code> on the block " +
    "axis.</p>" +

    "<h2>The same declared width, whatever is inside</h2>" +
    "<div class=\"caption\">Three 90pt boxes: a full one, a one-character one, and an empty one. All " +
    "three paint the same width and push what follows them to the same place. Only their heights differ, " +
    "since an empty box has no content to be as tall as.</div>" +
    "<div class=\"row\"><span class=\"box\">filled right up</span><span class=\"box\">x</span>" +
    "<span class=\"box\"></span>|&nbsp;text after</div>" +

    "<h2>Fixed-width labels line their values up</h2>" +
    "<div class=\"caption\">The classic use: a label column whose width comes from the declaration rather " +
    "than from the longest word in it.</div>" +
    "<div class=\"row\"><span class=\"key\">Invoice</span> PP&ndash;2026&ndash;0417</div>" +
    "<div class=\"row\"><span class=\"key\">Issued</span> 15 September 2026</div>" +
    "<div class=\"row\"><span class=\"key\">Payment terms</span> Net 30</div>" +

    "<h2>An empty box is still a box</h2>" +
    "<div class=\"caption\">A checkbox glyph has no content at all, so a flow that measured only its words " +
    "gave it no room and nothing to paint.</div>" +
    "<div class=\"row\"><span class=\"tick\"></span> Artwork approved" +
    "&nbsp;&nbsp;<span class=\"tick ticked\"></span> Proof signed off" +
    "&nbsp;&nbsp;<span class=\"tick\"></span> Sent to press</div>" +

    "<h2>box-sizing decides what the width covers</h2>" +
    "<div class=\"caption\">Both boxes declare <code>width: 120pt</code> with 6pt of padding and a 2pt " +
    "border either side. Under <code>content-box</code> the declared width is the content area, so the box " +
    "paints 136pt wide; under <code>border-box</code> it is the whole box, and 120pt is all of it. The " +
    "dashed rule below each is exactly 120pt.</div>" +
    "<div class=\"row\"><span class=\"sized\">content-box</span><div class=\"rule\"></div></div>" +
    "<div class=\"row\"><span class=\"sized borderbox\">border-box</span><div class=\"rule\"></div></div>" +

    "<h2>Percentage widths use the containing block</h2>" +
    "<div class=\"caption\">The pale row is 400pt wide. Its inline-block declares " +
    "<code>width: 50%</code>, so the purple box is exactly 200pt wide and the following text starts " +
    "halfway across the row.</div>" +
    "<div class=\"row percent-row\"><span class=\"percent\">50% inline-block</span>|&nbsp;text after</div>" +

    "<h2>Content too wide for the box wraps inside it</h2>" +
    "<div class=\"caption\">An inline-block whose content cannot fit on one line inside it is a box of " +
    "its own: it breaks its lines at its own width and takes one place on the line holding it, with " +
    "its last line's baseline on that line's baseline. A word that cannot be broken at all still " +
    "overflows, without moving what follows the box.</div>" +
    "<div class=\"row\">before <span class=\"wrapbox\">This inline-block holds enough words to wrap " +
    "across several lines of its own rather than across the lines of the block around it.</span> " +
    "after Agy</div>" +
    "<div class=\"caption\">The same 120pt box holding one unbreakable word.</div>" +
    "<div class=\"row\">before <span class=\"wrapbox\">Unbreakableextremelylongword</span> after Agy</div>" +

    "<h2>A declared height sizes the box the same way (CSS 2.1 §10.6.3)</h2>" +
    "<div class=\"caption\">Three 50pt-tall boxes: a full one, a one-character one, and an empty one. All " +
    "three paint the same height, whether or not their content fills it — an empty box included, the same " +
    "gap a declared width closed above.</div>" +
    "<div class=\"row\"><span class=\"boxh\">filled right up</span><span class=\"boxh\">x</span>" +
    "<span class=\"boxh\"></span>text after</div>" +

    "<h2>min-height is a floor, not an override</h2>" +
    "<div class=\"caption\">The gold box declares <code>min-height: 50pt</code> with enough wrapped text " +
    "that its natural content is already taller than that — so it keeps its natural (taller) height " +
    "rather than being shrunk to 50pt, unlike the blue <code>height: 50pt</code> box beside it.</div>" +
    "<div class=\"row\"><span class=\"boxh boxh-narrow\">x</span>" +
    "<span class=\"boxh-min boxh-narrow\">min-height is only a floor, so content taller than it keeps its own height</span></div>" +

    "</body></html>";

await SaveShowcaseAsync("atomic_inline_width", "Layout", "Atomic Inline Sizing",
    "CSS 2.1 §10.3.9/§10.6.3: a declared width, height, or min-height on a display: inline-block sizes the box itself — fixed-width labels, empty checkbox glyphs, percentage sizing, box-sizing, and the block axis.",
    atomicInlineWidthHtml, pdfConfig);

// --- display: contents showcase (CSS Display 3 §2.5) ---
const string DisplayContentsCss = """
    <style>
    @page { size: A4; margin: 60pt 40pt 40pt 40pt;
            @top-center { content: "Chapter: " string(chapter); font: 10pt sans-serif; color: #7a3b16 } }
    body { display: contents; background: #fbf5ea; font-family: sans-serif; font-size: 11pt; color: #2a2a2a }
    h1 { font-size: 20pt; margin: 0 0 8pt 0; color: #7a3b16 }
    h2 { font-size: 13pt; margin: 14pt 0 4pt 0; color: #7a3b16 }
    p, .caption { margin: 0 0 6pt 0; line-height: 1.35 }
    .caption { color: #555; font-size: 9.5pt }
    .row { display: flex; gap: 6pt; width: 420pt; padding: 6pt; background: #fff; border: 1pt solid #d8c7a8; margin-bottom: 6pt }
    .row > .item, .item { width: 90pt; height: 28pt; background: #e9a15a; color: #fff; text-align: center; line-height: 28pt }
    .wrap { display: contents; border: 4pt solid crimson; background: crimson; padding: 30pt; margin: 30pt }
    table { border-collapse: collapse; margin-bottom: 6pt }
    td { border: 1pt solid #b89460; padding: 3pt 8pt; background: #fff }
    .contents-row { display: contents }
    .flag::before { content: "[!] "; color: #b12b2b }
    .flag { display: contents }
    .ruled { border-bottom: 1pt dashed #b89460; padding-bottom: 6pt }
    section.chap { display: contents }
    </style>
    """;

var displayContentsHtml = "<!DOCTYPE html><html><head>" + DisplayContentsCss + "</head><body>" +

    "<section class=\"chap\" style=\"string-set: chapter 'One'\">" +
    "<h1>display: contents</h1>" +
    "<p>The element generates no box: its children take part in its parent's formatting context as if it were " +
    "not there. Everything that is not the box &mdash; its <code>id</code>, its links, its tagged-PDF " +
    "structure, its <code>string-set</code> &mdash; still belongs to the element.</p>" +

    "<h2>Flex items through a wrapper</h2>" +
    "<div class=\"caption\">The crimson wrapper below has a 4pt border, padding, margin and a background, and " +
    "<code>display: contents</code>: none of it is drawn. Its three children are the flex items of the row, " +
    "spaced by the 6pt gap exactly as if they were direct children.</div>" +
    "<div class=\"row\"><div class=\"wrap\"><div class=\"item\">one</div><div class=\"item\">two</div></div>" +
    "<div class=\"item\">three</div></div>" +

    "<h2>Table cells through a row</h2>" +
    "<div class=\"caption\">Each <code>&lt;tr&gt;</code> is <code>display: contents</code>, so the cells are wrapped " +
    "in one anonymous row: all four sit on one line.</div>" +
    "<table><tbody><tr class=\"contents-row\"><td>a1</td><td>a2</td></tr>" +
    "<tr class=\"contents-row\"><td>b1</td><td>b2</td></tr></tbody></table>" +

    "<h2>Inline content and generated content</h2>" +
    "<p class=\"ruled\">This sentence has a <span class=\"flag\">flagged phrase whose span is contents, with its own " +
    "::before still generated</span> in the middle of the line, and it wraps like any other text.</p>" +

    "<h2>The body itself</h2>" +
    "<p>This page's <code>&lt;body&gt;</code> is <code>display: contents</code> with a cream background: it " +
    "generates no box, and the background still fills the whole page canvas.</p>" +
    "</section>" +

    "<section class=\"chap\" style=\"string-set: chapter 'Two'\">" +
    "<h1 style=\"break-before: page\">Still the same element</h1>" +
    "<p>The running header above changed from &ldquo;One&rdquo; to &ldquo;Two&rdquo;: each chapter is a " +
    "<code>&lt;section style=\"display: contents\"&gt;</code> whose <code>string-set</code> is assigned where its " +
    "content begins. The page break is on the <code>&lt;h1&gt;</code> inside it: a <code>break-before</code> on the " +
    "<code>display: contents</code> element itself would have no box to break before.</p>" +
    "</section>" +

    "</body></html>";

await SaveShowcaseAsync("display_contents", "Layout", "display: contents",
    "CSS Display 3 §2.5: an element with display: contents generates no box — its children become flex items, table cells and inline content of its parent — while its id, string-set, links and the body's canvas background still work.",
    displayContentsHtml, pdfConfig);

// --- line-height: normal showcase (CSS 2.1 §10.8.1) ---

const string NormalLineHeightCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font-size: 12pt; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    p.intro { font-family: Arial, sans-serif; color: #444; margin: 0 0 1em; max-width: 500pt }
    .sample { border: 1px solid #ccc; background: #fafafa; padding: 0; margin-bottom: 10pt; max-width: 380pt }
    .sample .label { font-family: Arial, sans-serif; font-size: 7pt; font-weight: bold; color: #444; background: #eee; padding: 3pt 6pt; border-bottom: 1px solid #ccc }
    .sample .text { font-size: 24pt; line-height: normal; margin: 0; padding: 0 6pt; background: #e8f0fe }
    .sample.serif .text { font-family: Georgia, 'Times New Roman', serif }
    .sample.sans .text { font-family: Arial, Helvetica, sans-serif }
    .sample.mono .text { font-family: 'Courier New', monospace }
    </style>
    """;

var normalLineHeightHtml = "<!DOCTYPE html><html><head>" + NormalLineHeightCss + "</head><body>" +

    "<h1>line-height: normal</h1>" +
    "<p class=\"intro\">Each box below sets only <code>font-size: 24pt; line-height: normal</code> - no explicit " +
    "line-height. The used value is resolved from that font's own ascent/descent/line-gap metrics (CSS 2.1 " +
    "&sect;10.8.1), matching how browsers do it, so it varies by font rather than being a flat multiplier of " +
    "font-size. The shaded background is exactly one line box tall, so its height relative to the glyphs " +
    "shows each font's own leading.</p>" +

    "<div class=\"sample serif\"><div class=\"label\">serif</div><div class=\"text\">Aligny jpqg</div></div>" +
    "<div class=\"sample sans\"><div class=\"label\">sans-serif</div><div class=\"text\">Aligny jpqg</div></div>" +
    "<div class=\"sample mono\"><div class=\"label\">monospace</div><div class=\"text\">Aligny jpqg</div></div>" +

    "</body></html>";

await SaveShowcaseAsync("line_height_normal", "Typography & Text", "line-height: normal",
    "line-height: normal resolved from each font's own ascent/descent/line-gap metrics, matching browser behavior, rather than a flat 1.2× font-size.",
    normalLineHeightHtml, pdfConfig);

// --- declared line-height showcase (CSS 2.1 §10.8/§10.8.1) ---
// The companion to the "normal" showcase above: what a DECLARED line-height does, including the two
// cases the line box's height used to be computed wrongly for - a line-height shorter than the font's
// own height (which must win anyway, letting the glyphs overflow), and the strut a block contributes
// to a line whose content all comes from a shorter-line-height inline descendant.

const string DeclaredLineHeightCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font-size: 12pt; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 1em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p.intro { font-family: Arial, sans-serif; color: #444; margin: 0 0 1em; max-width: 500pt }
    .sample { border: 1px solid #ccc; margin-bottom: 8pt; max-width: 380pt }
    .sample .label { font-family: Arial, sans-serif; font-size: 7pt; font-weight: bold; color: #444; background: #eee; padding: 3pt 6pt; border-bottom: 1px solid #ccc }
    .sample .text { font-family: Georgia, 'Times New Roman', serif; font-size: 24pt; padding: 0 6pt; background: #e8f0fe; margin: 0 }
    .stack .text { font-size: 12pt; max-width: 300pt }
    </style>
    """;

var declaredLineHeightHtml = "<!DOCTYPE html><html><head>" + DeclaredLineHeightCss + "</head><body>" +

    "<h1>Declared line-height</h1>" +
    "<p class=\"intro\">The shaded band behind each sample is exactly as tall as the line boxes it holds - " +
    "one line box in the single-line samples, and one per line in the wrapped ones further down. A declared " +
    "<code>line-height</code> is the line box's height whether it is larger or <em>smaller</em> than the " +
    "font's own ascent+descent (CSS 2.1 &sect;10.8): when it is smaller the leading is negative and the " +
    "glyphs deliberately overflow the band rather than the band growing to fit them.</p>" +

    "<h2>One line, 24pt Georgia</h2>" +
    "<div class=\"sample\"><div class=\"label\">line-height: 0.75 (shorter than the font - glyphs overflow the band)</div>" +
    "<div class=\"text\" style=\"line-height: 0.75\">Aligny jpqg</div></div>" +
    "<div class=\"sample\"><div class=\"label\">line-height: 1 (exactly the font-size)</div>" +
    "<div class=\"text\" style=\"line-height: 1\">Aligny jpqg</div></div>" +
    "<div class=\"sample\"><div class=\"label\">line-height: 1.5</div>" +
    "<div class=\"text\" style=\"line-height: 1.5\">Aligny jpqg</div></div>" +
    "<div class=\"sample\"><div class=\"label\">line-height: 36pt (absolute length)</div>" +
    "<div class=\"text\" style=\"line-height: 36pt\">Aligny jpqg</div></div>" +

    "<h2>Several lines - every line box stacks by the declared height</h2>" +
    "<div class=\"sample stack\"><div class=\"label\">line-height: 0.8 (lines pack tighter than the glyphs)</div>" +
    "<div class=\"text\" style=\"line-height: 0.8\">The quick brown fox jumps over the lazy dog, and then jumps back over it again for good measure.</div></div>" +
    "<div class=\"sample stack\"><div class=\"label\">line-height: 2</div>" +
    "<div class=\"text\" style=\"line-height: 2\">The quick brown fox jumps over the lazy dog, and then jumps back over it again for good measure.</div></div>" +

    "<h2>The strut</h2>" +
    "<p class=\"intro\">Every line box is also tall enough for the block's own font and line-height - the " +
    "<em>strut</em> (&sect;10.8.1) - even when all of the line's actual content comes from an inline that " +
    "declares a shorter one. Both bands below are the block's 30pt, not the inline's.</p>" +
    "<div class=\"sample\"><div class=\"label\">block line-height: 30pt, inline font: 6pt/6pt</div>" +
    "<div class=\"text\" style=\"line-height: 30pt\"><span style=\"font: 6pt/6pt Arial, sans-serif\">a small inline on a tall block's line</span></div></div>" +
    "<div class=\"sample\"><div class=\"label\">block line-height: 30pt, inline line-height: 48pt (the taller inline wins instead)</div>" +
    "<div class=\"text\" style=\"line-height: 30pt\"><span style=\"line-height: 48pt\">Aligny</span></div></div>" +

    "</body></html>";

await SaveShowcaseAsync("line_height_declared", "Typography & Text", "Declared line-height",
    "A declared line-height sets the line box height in both directions - including shorter than the font, where the glyphs overflow - plus the block's strut on every line.",
    declaredLineHeightHtml, pdfConfig);

// --- baseline alignment / half-leading showcase (CSS 2.1 §10.8.1) ---
// The third of the line-box trio, after "normal" and "declared" above: where a line's content sits
// INSIDE the line box those two size. Every sample here looked different before baseline alignment
// existed - the engine placed every inline box flush with its line's top, which is indistinguishable
// from baseline alignment only while every font on the line is one size.

const string BaselineCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 12pt Georgia, 'Times New Roman', serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 1em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p.intro { font-family: Arial, sans-serif; font-size: 9pt; color: #444; margin: 0 0 0.8em; max-width: 500pt }
    .sample { border: 1px solid #ccc; margin-bottom: 8pt; max-width: 420pt }
    .sample .label { font-family: Arial, sans-serif; font-size: 7pt; font-weight: bold; color: #444; background: #eee; padding: 3pt 6pt; border-bottom: 1px solid #ccc }
    .sample .text { padding: 0 6pt; background: #e8f0fe; margin: 0 }
    ol.mk { margin: 0; padding-left: 40pt; background: #e8f0fe; max-width: 420pt }
    ol.big li::marker { font-size: 22pt; font-weight: bold; color: #c0392b }
    .ib { display: inline-block; border: 1px solid #8e44ad; background: #f4ecf7; padding: 2pt 4pt }
    </style>
    """;

var baselineBadgeUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
    """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 60 60"><rect width="60" height="60" rx="6" fill="#27ae60"/><circle cx="30" cy="30" r="14" fill="#ecf0f1"/></svg>"""));

var baselineHtml = "<!DOCTYPE html><html><head>" + BaselineCss + "</head><body>" +

    "<h1>One line, one baseline</h1>" +
    "<p class=\"intro\">Every inline box on a line hangs its own glyphs from a single shared baseline, and " +
    "the <strong>leading</strong> - a box's <code>line-height</code> less its font's own height - is split " +
    "in half above and below that content area (CSS 2.1 &sect;10.8.1). The shaded band behind each sample " +
    "is exactly one line box tall, so it shows where the line box itself begins and ends.</p>" +

    "<h2>1 &mdash; Mixed font sizes share a baseline, not a top edge</h2>" +
    "<div class=\"sample\"><div class=\"label\">10pt text around a 30pt span - every run rests on one baseline</div>" +
    "<div class=\"text\" style=\"font-size: 10pt\">before <span style=\"font-size: 30pt\">Big Ag</span> after</div></div>" +
    "<div class=\"sample\"><div class=\"label\">three sizes at once: 8pt, 18pt, 30pt</div>" +
    "<div class=\"text\" style=\"font-size: 8pt\">small <span style=\"font-size: 18pt\">medium</span> " +
    "<span style=\"font-size: 30pt\">large</span> small</div></div>" +

    "<h2>2 &mdash; A tall line-height centres the text, it does not hang it from the top</h2>" +
    "<p class=\"intro\">Half of the leading goes above the glyphs and half below, so the text sits in the " +
    "middle of its band. The band itself is the declared <code>line-height</code> either way.</p>" +
    "<div class=\"sample\"><div class=\"label\">font-size: 14pt; line-height: 1 (leading ~0 - the band hugs the glyphs)</div>" +
    "<div class=\"text\" style=\"font-size: 14pt; line-height: 1\">Aligny jpqg</div></div>" +
    "<div class=\"sample\"><div class=\"label\">font-size: 14pt; line-height: 2.5 (the glyphs sit centred in the band)</div>" +
    "<div class=\"text\" style=\"font-size: 14pt; line-height: 2.5\">Aligny jpqg</div></div>" +
    "<div class=\"sample\"><div class=\"label\">the same, wrapped - every line is centred in its own band</div>" +
    "<div class=\"text\" style=\"font-size: 11pt; line-height: 2.5; width: 260pt\">The quick brown fox " +
    "jumps over the lazy dog and then jumps back again.</div></div>" +

    "<h2>3 &mdash; A line can be taller than every line-height on it</h2>" +
    "<p class=\"intro\">The two sides of a line box are maximised independently: the box reaching highest " +
    "above the baseline need not be the one reaching lowest below it. Here the 26pt text sets the ascent " +
    "side and the small span's tall <code>line-height</code> sets the descent side, so the band is taller " +
    "than either box's own <code>line-height</code>.</p>" +
    "<div class=\"sample\"><div class=\"label\">block 26pt/26pt containing a span of 8pt/40pt</div>" +
    "<div class=\"text\" style=\"font-size: 26pt; line-height: 26pt\">Ascent" +
    "<span style=\"font-size: 8pt; line-height: 40pt\"> and a deep small span</span></div></div>" +

    "<h2>4 &mdash; An outside ::marker sits on its item's first baseline</h2>" +
    "<p class=\"intro\">A <code>::marker</code> in a larger font than its item keeps its own baseline on the " +
    "item's, rather than hanging its digits below the text they number - and the item's first line grows to " +
    "hold it. css-lists-3 &sect;3.5 leaves both expressly undefined; this follows what browsers do.</p>" +
    "<div class=\"sample\"><div class=\"label\">10pt items, ::marker { font-size: 22pt } - markers and text share a baseline</div>" +
    "<ol class=\"mk big\" style=\"font-size: 10pt\"><li>First item</li><li>Second item</li><li>Third item</li></ol></div>" +
    "<div class=\"sample\"><div class=\"label\">the same list with no ::marker override, for comparison</div>" +
    "<ol class=\"mk\" style=\"font-size: 10pt\"><li>First item</li><li>Second item</li><li>Third item</li></ol></div>" +

    "<h2>5 &mdash; An atomic inline sits on the baseline too</h2>" +
    "<p class=\"intro\">An image, an inline <code>&lt;svg&gt;</code>, MathML, a form control or an " +
    "<code>inline-block</code> is aligned by its own box rather than by font metrics: its " +
    "<strong>bottom margin edge</strong> rests on the line's baseline, and the line grows above the " +
    "baseline to hold it. An <code>inline-block</code> with visible overflow uses its own last line's " +
    "baseline instead, so its text lines up with the text beside it.</p>" +
    "<div class=\"sample\"><div class=\"label\">10pt text around a 24pt image - the image's bottom is on the text baseline</div>" +
    "<div class=\"text\" style=\"font-size: 10pt\">before <img src=\"" + baselineBadgeUri +
    "\" style=\"width: 24pt; height: 24pt\"> after Agy</div></div>" +
    "<div class=\"sample\"><div class=\"label\">the same image with margin-bottom: 6pt - the margin edge, not the image, meets the baseline</div>" +
    "<div class=\"text\" style=\"font-size: 10pt\">before <img src=\"" + baselineBadgeUri +
    "\" style=\"width: 24pt; height: 24pt; margin-bottom: 6pt\"> after Agy</div></div>" +
    "<div class=\"sample\"><div class=\"label\">inline-block with visible overflow - its own last line's baseline is shared</div>" +
    "<div class=\"text\" style=\"font-size: 10pt\">before <span class=\"ib\">inside</span> after Agy</div></div>" +
    "<div class=\"sample\"><div class=\"label\">the same box with overflow: hidden - &sect;10.8.1 falls back to its bottom margin edge</div>" +
    "<div class=\"text\" style=\"font-size: 10pt\">before <span class=\"ib\" style=\"overflow: hidden\">inside</span> after Agy</div></div>" +

    "</body></html>";

await SaveShowcaseAsync("baseline_alignment", "Typography & Text", "Baseline alignment & leading",
    "Every inline box on a line shares one baseline - images, inline-blocks and markers included - with a declared line-height's leading split half above the text and half below.",
    baselineHtml, pdfConfig);

// --- letter-spacing / word-spacing showcase ---

const string SpacingCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 12pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    .row { margin-bottom: 0.6em }
    .label { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    </style>
    """;

var spacingHtml = "<!DOCTYPE html><html><head>" + SpacingCss + "</head><body>" +

    "<h1>CSS1 letter-spacing / word-spacing Test Page</h1>" +

    "<h2>letter-spacing</h2>" +
    "<div class=\"row\"><div class=\"label\">normal</div><div style=\"letter-spacing:normal\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">1px</div><div style=\"letter-spacing:1px\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">4px</div><div style=\"letter-spacing:4px\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">-1px (tightened)</div><div style=\"letter-spacing:-1px\">The quick brown fox jumps over the lazy dog</div></div>" +

    "<h2>word-spacing (for contrast)</h2>" +
    "<div class=\"row\"><div class=\"label\">normal</div><div style=\"word-spacing:normal\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">10px</div><div style=\"word-spacing:10px\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">25px</div><div style=\"word-spacing:25px\">The quick brown fox jumps over the lazy dog</div></div>" +

    "<h2>Combined (inherited from parent, per CSS1)</h2>" +
    "<div class=\"row\" style=\"letter-spacing:2px; word-spacing:8px\">" +
    "<div class=\"label\">div has letter-spacing:2px; word-spacing:8px - nested span inherits both</div>" +
    "<span>The quick brown fox</span> <b>jumps over</b> the lazy dog" +
    "</div>" +

    "</body></html>";

await SaveShowcaseAsync("letter_word_spacing", "Typography & Text", "Letter & Word Spacing",
    "letter-spacing and word-spacing adjustments across text runs.",
    spacingHtml, pdfConfig);

// --- text-decoration-thickness showcase (CSS Text Decoration 4 §3.3) ---

const string DecorationThicknessCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 16pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    .row { margin-bottom: 0.8em }
    .label { font-size: 9pt; font-family: "Courier New", monospace; color: #444; margin-bottom: 2px }
    </style>
    """;

var decorationThicknessHtml = "<!DOCTYPE html><html><head>" + DecorationThicknessCss + "</head><body>" +

    "<h1>CSS Text Decoration 4 text-decoration-thickness</h1>" +

    "<div class=\"row\"><div class=\"label\">auto (initial value - this engine's own fixed thickness)</div>" +
    "<span style=\"text-decoration:underline\">The quick brown fox</span></div>" +

    "<div class=\"row\"><div class=\"label\">from-font (the resolved font's real underline metric)</div>" +
    "<span style=\"text-decoration:underline; text-decoration-thickness:from-font\">The quick brown fox</span></div>" +

    "<div class=\"row\"><div class=\"label\">1px</div>" +
    "<span style=\"text-decoration:underline; text-decoration-thickness:1px\">The quick brown fox</span></div>" +

    "<div class=\"row\"><div class=\"label\">3px</div>" +
    "<span style=\"text-decoration:underline; text-decoration-thickness:3px\">The quick brown fox</span></div>" +

    "<div class=\"row\"><div class=\"label\">6px</div>" +
    "<span style=\"text-decoration:underline; text-decoration-thickness:6px\">The quick brown fox</span></div>" +

    "<div class=\"row\"><div class=\"label\">15% (of this 16pt font size)</div>" +
    "<span style=\"text-decoration:underline; text-decoration-thickness:15%\">The quick brown fox</span></div>" +

    "</body></html>";

await SaveShowcaseAsync("text_decoration_thickness", "Typography & Text", "text-decoration-thickness",
    "text-decoration-thickness (CSS Text Decoration 4): auto, from-font, and explicit length/percentage underline thickness.",
    decorationThicknessHtml, pdfConfig);

// --- text-decoration-style showcase (css-text-decor-3 §2.2) ---

var decorationStyleHtml = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 12pt sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.2em }
    p.lede { font-size: 10pt; color: #444; margin: 0 0 1.2em }
    h2 { font-size: 11pt; color: #444; margin: 1.3em 0 0.5em;
         border-bottom: 1px solid #ddd; padding-bottom: 2px }
    .row { margin-bottom: 1.5em; line-height: 1.6 }
    .label { font-size: 9pt; color: #666; margin-bottom: 4px; line-height: 1.2 }
    .solid { text-decoration: underline solid }
    .dotted { text-decoration: underline dotted }
    .dashed { text-decoration: underline dashed }
    .doubled { text-decoration: underline double }
    .wavy { text-decoration: underline wavy }
    .over { text-decoration: overline double }
    /* A double overline grows upward, so it needs headroom; flush against a page top the upper
       stroke falls outside the page. See docs/html-css-support.md. */
    .row.over-row { margin-top: 1.9em }
    .through { text-decoration: line-through double }
    .heavy { text-decoration: underline double; text-decoration-thickness: 2px;
             text-decoration-color: #c0392b }
    .wavy-over { text-decoration: overline wavy }
    .wavy-through { text-decoration: line-through wavy }
    .wavy-thick { text-decoration: underline wavy; text-decoration-thickness: 3px;
                  text-decoration-color: #c0392b }
    .wavy-skip { text-decoration: underline wavy }
    table { border-collapse: collapse; width: 62%; font-size: 11pt }
    td { padding: 3px 6px }
    td.n { text-align: right; font-variant-numeric: tabular-nums }
    tr.total td { font-weight: bold }
    tr.total td.n { text-decoration: underline double }
    </style>

    <h1>text-decoration-style</h1>
    <p class="lede">Every style is one stroke with a dash pattern, except <b>double</b> and <b>wavy</b>.
    Double is two strokes of the resolved thickness separated by a gap of the same thickness: the first
    stroke stays where a single one would sit and the second grows away from the text &mdash; downward
    for an underline and a line-through, upward for an overline. Wavy strokes a curved path instead of a
    dash pattern (css-text-decor-3 &sect;2.2: "Draw a wavy line") &mdash; its centerline grows away from
    the text the same direction double's second stroke does.</p>

    <h2>The five styles, as underlines</h2>
    <div class="row"><div class="label">solid</div><span class="solid">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">dotted</div><span class="dotted">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">dashed</div><span class="dashed">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">double</div><span class="doubled">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">wavy</div><span class="wavy">Hamburgefonstiv</span></div>

    <h2>double on each line, and at a heavier thickness</h2>
    <div class="row"><div class="label">underline double &mdash; grows downward</div><span class="doubled">Hamburgefonstiv</span></div>
    <div class="row over-row"><div class="label">overline double &mdash; grows upward</div><span class="over">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">line-through double &mdash; grows downward</div><span class="through">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">underline double, text-decoration-thickness: 2px</div><span class="heavy">Hamburgefonstiv</span></div>

    <h2>wavy on each line, at a heavier thickness, and skipping ink</h2>
    <div class="row"><div class="label">underline wavy</div><span class="wavy">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">overline wavy</div><span class="wavy-over">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">line-through wavy</div><span class="wavy-through">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">underline wavy, text-decoration-thickness: 3px</div><span class="wavy-thick">Hamburgefonstiv</span></div>
    <div class="row"><div class="label">underline wavy, text-decoration-skip-ink breaks it around descenders</div><span class="wavy-skip">Typography judging a quick pig</span></div>

    <h2>What it is for</h2>
    <table>
      <tr><td>Subtotal</td><td class="n">1,000.00</td></tr>
      <tr><td>Tax</td><td class="n">80.00</td></tr>
      <tr class="total"><td>Total</td><td class="n">1,080.00</td></tr>
    </table>
    """;

await SaveShowcaseAsync("text_decoration_style", "Typography & Text", "text-decoration-style",
    "text-decoration-style (CSS Text Decoration 3 \u00a72.2): solid, dotted and dashed as pen patterns, double as two strokes \u2014 the accounting rule under a grand total \u2014 and wavy as a stroked curve.",
    decorationStyleHtml, pdfConfig);

// --- text-decoration skipping showcase (css-text-decor-3 §2.4 + css-text-decor-4 §2.5) ---

// Bundled (see assets/fonts/SourceSans3-Regular.LICENSE.txt) rather than a system family: what this
// showcase demonstrates is which glyphs have descenders, so it must not depend on what happens to be
// installed on the machine that builds the doc site.
var decorationSkipFontB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")));

var decorationSkipCss = $$"""
    <style>
    @font-face { font-family: 'SkipInkDemo'; src: url('data:font/truetype;base64,{{decorationSkipFontB64}}') format('truetype'); }
    @page { size: a4; margin: 15mm }
    body { font: 17pt 'SkipInkDemo', sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.2em }
    h2 { font-size: 11pt; color: #444; margin: 1.1em 0 0.4em;
         border-bottom: 1px solid #ddd; padding-bottom: 2px }
    .row { margin-bottom: 0.7em }
    .label { font-size: 9pt; color: #666; margin-bottom: 1px }
    u, .u { text-decoration: underline }
    .thick { text-decoration: underline; text-decoration-thickness: 2px; text-decoration-color: #c0392b }
    .noskip { text-decoration-skip-ink: none }
    .chip { display: inline-block; width: 54pt; height: 13pt; background: #f0c419;
            border: 1px solid #b8960f; vertical-align: middle }
    .ghost { opacity: 0 }
    img.badge { width: 40pt; height: 14pt; vertical-align: middle }
    </style>
    """;

var decorationSkipBadgeUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
    """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 80 28"><rect width="80" height="28" rx="4" fill="#2980b9"/><circle cx="20" cy="14" r="7" fill="#ecf0f1"/><circle cx="60" cy="14" r="7" fill="#ecf0f1"/></svg>"""));

var decorationSkipHtml = "<!DOCTYPE html><html><head>" + decorationSkipCss + "</head><body>" +

    "<h1>Text decoration: skipping ink and atomic inlines</h1>" +

    "<h2>text-decoration-skip-ink (CSS Text Decoration 4 §2.5)</h2>" +

    "<div class=\"row\"><div class=\"label\">auto (initial) — the underline breaks around every descender</div>" +
    "<span class=\"u\">Typography judging a quick pig by page eighty-jog</span></div>" +

    "<div class=\"row\"><div class=\"label\">none — opt out: one unbroken line straight through the descenders</div>" +
    "<span class=\"u noskip\">Typography judging a quick pig by page eighty-jog</span></div>" +

    "<div class=\"row\"><div class=\"label\">auto, thicker line — a taller line meets more ink, so it interrupts more often</div>" +
    "<span class=\"thick\">Typography judging a quick pig by page eighty-jog</span></div>" +

    "<div class=\"row\"><div class=\"label\">line-through is never skipped (§2.5), even at skip-ink: all</div>" +
    "<span style=\"text-decoration:line-through; text-decoration-skip-ink:all\">Typography judging a quick pig</span></div>" +

    "<h2>Atomic inlines are not decorated (CSS Text Decoration 3 §2.4)</h2>" +

    "<div class=\"row\"><div class=\"label\">an inline-block on the line — the line stops at its margin box and resumes after it</div>" +
    "<span class=\"u\">before <span class=\"chip\"><div></div></span> after</span></div>" +

    "<div class=\"row\"><div class=\"label\">the same inline-block made invisible — the gap is real, not the chip covering the line</div>" +
    "<span class=\"u\">before <span class=\"chip ghost\"><div></div></span> after</span></div>" +

    "<div class=\"row\"><div class=\"label\">an image, on a block whose decoration propagates to its inline content</div>" +
    "<div class=\"u\">a picture <img class=\"badge\" src=\"" + decorationSkipBadgeUri + "\" alt=\"\"> follows the gap</div></div>" +

    "<div class=\"row\"><div class=\"label\">both rules at once — a gap for the inline-block, and one per descender</div>" +
    "<div class=\"u\">paging <span class=\"chip ghost\"><div></div></span> jaguar gorge</div></div>" +

    "</body></html>";

await SaveShowcaseAsync("text_decoration_skipping", "Typography & Text", "Decoration Skipping",
    "text-decoration-skip-ink (CSS Text Decoration 4) breaking underlines around descenders, and CSS Text Decoration 3's rule that atomic inlines are not decorated.",
    decorationSkipHtml, pdfConfig);

// --- tab-size showcase (CSS Text 4 §3.6) ---

const string TabSizeCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 12pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    .label { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 2px }
    pre { font-family: "Courier New", monospace; font-size: 10pt; background: #f4f4f4; border: 1px solid #ccc; padding: 6px 8px; margin: 0 0 0.8em }
    </style>
    """;

const string TabbedCode =
    "function greet(name) {\n" +
    "\tif (name) {\n" +
    "\t\tconsole.log(\"Hello, \" + name);\n" +
    "\t} else {\n" +
    "\t\tconsole.log(\"Hello!\");\n" +
    "\t}\n" +
    "}";

var tabSizeHtml = "<!DOCTYPE html><html><head>" + TabSizeCss + "</head><body>" +

    "<h1>CSS Text 4 tab-size Test Page</h1>" +

    "<h2>Default (tab-size: 8, initial value)</h2>" +
    "<div class=\"label\">Each tab indents to the next multiple of 8 space widths</div>" +
    $"<pre>{TabbedCode}</pre>" +

    "<h2>tab-size: 4</h2>" +
    "<div class=\"label\">A narrower numeric tab stop - same source, tighter indentation</div>" +
    $"<pre style=\"tab-size:4\">{TabbedCode}</pre>" +

    "<h2>tab-size: 2</h2>" +
    "<div class=\"label\">An even narrower numeric tab stop</div>" +
    $"<pre style=\"tab-size:2\">{TabbedCode}</pre>" +

    "<h2>tab-size: 40px (a &lt;length&gt;, not a multiplier)</h2>" +
    "<div class=\"label\">An explicit length fixes the stop width regardless of the font's own space width</div>" +
    $"<pre style=\"tab-size:40px\">{TabbedCode}</pre>" +

    "</body></html>";

await SaveShowcaseAsync("tab_size", "Typography & Text", "tab-size",
    "tab-size (CSS Text 4): controlling how far a preserved tab character indents inside white-space:pre content.",
    tabSizeHtml, pdfConfig);

// --- ::first-letter showcase ---

const string FirstLetterCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Georgia, serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p { margin: 0 0 0.8em }
    .dropcap::first-letter { font-size: 300%; float: left; color: crimson; font-weight: bold; line-height: 0.8; padding-right: 4px }
    .colored::first-letter { color: rgb(0,102,204); font-size: 150% }
    </style>
    """;

var firstLetterHtml = "<!DOCTYPE html><html><head>" + FirstLetterCss + "</head><body>" +

    "<h1>CSS1 ::first-letter Test Page</h1>" +

    "<h2>Classic drop cap</h2>" +
    "<p class=\"dropcap\">Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation.</p>" +

    "<h2>Nested inline content</h2>" +
    "<p class=\"colored\"><em>Emphasized</em> text starts this paragraph, and the very first letter should still pick up the first-letter color even though it begins inside the nested &lt;em&gt;.</p>" +

    "<h2>Leading punctuation</h2>" +
    "<p class=\"colored\">&#8220;Quoted&#8221; text at the start of a paragraph includes the opening quote mark as part of the first-letter unit, per CSS1 &sect;1.2.</p>" +

    "</body></html>";

await SaveShowcaseAsync("first_letter", "Typography & Text", "::first-letter",
    "The ::first-letter pseudo-element: drop-cap style initial letter formatting.",
    firstLetterHtml, pdfConfig);

// --- ::first-line showcase ---

const string FirstLineCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Georgia, serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p { margin: 0 0 0.8em; width: 350px }
    .lede::first-line { font-weight: bold; color: darkslateblue; font-variant: small-caps }
    .bigfont::first-line { font-size: 200% }
    .allseven::first-line {
      font-style: italic;
      color: rgb(180,60,0);
      background-color: rgb(255,240,200);
      text-decoration: underline;
      word-spacing: 6px;
      letter-spacing: 1px;
      text-transform: uppercase;
    }
    </style>
    """;

var firstLineHtml = "<!DOCTYPE html><html><head>" + FirstLineCss + "</head><body>" +

    "<h1>CSS2.1 ::first-line Test Page</h1>" +

    "<h2>Non-width-affecting (font-weight/color/small-caps)</h2>" +
    "<p class=\"lede\">Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua, only the first line should look different from the rest of this paragraph.</p>" +

    "<h2>Width-affecting (bigger first-line font-size, wrap point shifts)</h2>" +
    "<p class=\"bigfont\">Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat, and this line wraps once the bigger first-line font runs out of room.</p>" +

    "<h2>All 7 supported properties at once</h2>" +
    "<p class=\"allseven\">Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur, demonstrating font-style, color, background, decoration, spacing, and text-transform together on line one only.</p>" +

    "</body></html>";

await SaveShowcaseAsync("first_line", "Typography & Text", "::first-line",
    "The ::first-line pseudo-element styling the first rendered line of a paragraph.",
    firstLineHtml, pdfConfig);

// --- text-indent showcase (CSS Text 3 §3: plain, hanging, each-line, hanging + each-line) ---

const string TextIndentCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Georgia, serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p { margin: 0 0 0.8em; width: 380px }
    .plain { text-indent: 2em }
    .hanging { text-indent: 2em hanging }
    .eachline { text-indent: 2em each-line }
    .both { text-indent: 2em hanging each-line }
    .centered { text-indent: 2em; text-align: center }
    </style>
    """;

var textIndentHtml = "<!DOCTYPE html><html><head>" + TextIndentCss + "</head><body>" +

    "<h1>CSS Text 3 text-indent Test Page</h1>" +

    "<h2>Plain (first line only)</h2>" +
    "<p class=\"plain\">Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.</p>" +

    "<h2>hanging (a bibliography-style hanging indent - every line except the first)</h2>" +
    "<p class=\"hanging\">Doe, J. (2026). A very long reference title that wraps onto a second and third line, exactly the case a hanging indent is meant for. <em>Journal of Typesetting</em>, 12(3), 45&ndash;67.</p>" +

    "<h2>each-line (indents the first line and the line after every &lt;br&gt;, not soft-wrapped continuations)</h2>" +
    "<p class=\"eachline\">Roses are red,<br>Violets are blue,<br>This line is long enough that it wraps onto a continuation line which stays flush,<br>And so, dear reader, are you.</p>" +

    "<h2>hanging each-line together (only soft-wrap continuations are indented)</h2>" +
    "<p class=\"both\">Roses are red,<br>Violets are blue,<br>This line is long enough that it wraps onto a continuation line which is indented,<br>And so, dear reader, are you.</p>" +

    "<h2>text-align: center (the indent's own line-start reservation, not split away by centering)</h2>" +
    "<p class=\"centered\">Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>" +

    "</body></html>";

await SaveShowcaseAsync("text_indent", "Typography & Text", "text-indent",
    "text-indent's plain, hanging, and each-line forms (CSS Text 3), including their combination and interaction with text-align: center.",
    textIndentHtml, pdfConfig);

// --- text-align / text-align-last showcase (CSS Text 3 §6.1, §6.3, §6.4.3) ---

const string TextAlignLastCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Georgia, serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p { margin: 0 0 0.8em; width: 360px; text-align: justify; background: #f4f2ee }
    .last-justify { text-align-last: justify }
    .last-center { text-align-last: center }
    .last-right { text-align-last: right }
    .rtl { direction: rtl }
    .justify-all { text-align: justify-all; width: 360px }
    .match-parent-outer { direction: rtl; border: 1px solid #999; padding: 8px; width: 380px; font-family: Arial, sans-serif; font-size: 9pt }
    .match-parent-outer p { width: auto; text-align: match-parent; background: #eef6fb; margin: 0 0 6px }
    </style>
    """;

var textAlignLastHtml = "<!DOCTYPE html><html><head>" + TextAlignLastCss + "</head><body>" +

    "<h1>CSS Text 3 text-align: justify &amp; text-align-last</h1>" +

    "<h2>A forced break ends a paragraph: every &lt;br&gt; line stays ragged</h2>" +
    "<p>Peach State Technologies<br>1 Example Parkway, Suite 400<br>Atlanta, Georgia 30303<br>United States of America</p>" +

    "<h2>Default: only the lines that end a paragraph are ragged</h2>" +
    "<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.<br>Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.</p>" +

    "<h2>text-align-last: justify (the exemption declined - every line fills the measure)</h2>" +
    "<p class=\"last-justify\">Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.<br>Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.</p>" +

    "<h2>text-align-last: center</h2>" +
    "<p class=\"last-center\">Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.<br>Ut enim ad minim veniam, quis nostrud exercitation.</p>" +

    "<h2>text-align-last: right</h2>" +
    "<p class=\"last-right\">Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.<br>Ut enim ad minim veniam, quis nostrud exercitation.</p>" +

    "<h2>direction: rtl - the default text-align-last: auto is <i>start</i>, which is the right edge</h2>" +
    "<p class=\"rtl\">Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam.</p>" +

    "<h2>text-align is a real shorthand over text-align-all/text-align-last (issue #1027)</h2>" +
    "<p>Plain text-align: justify (default text-align-last: auto) - the closing line stays ragged.<br>Second sentence to give this paragraph a forced break.</p>" +
    "<p class=\"justify-all\">text-align: justify-all sets text-align-all AND text-align-last to justify - even the closing line stretches to the full measure.<br>Second sentence, same treatment.</p>" +

    "<h2>text-align: match-parent resolves against the *parent's* own direction (issue #1027)</h2>" +
    "<div class=\"match-parent-outer\">" +
    "<p>This RTL container's own child paragraphs declare <code>text-align: match-parent</code> with no direction of their own.</p>" +
    "<p>Match-parent's <i>start</i> resolves against the container's RTL direction, so both paragraphs pack against the physical right edge - the same result an explicit <code>text-align: right</code> would give here, but automatically following whichever direction the container ends up with.</p>" +
    "</div>" +

    "</body></html>";

await SaveShowcaseAsync("text_align_last", "Typography & Text", "Justification & text-align-last",
    "text-align: justify leaves every line that ends a paragraph ragged - the block's last line and the last line before a <br> - and text-align-last (auto, justify, center, right) says how those lines are aligned instead. text-align is a real shorthand (CSS Text 3 §6.1) over text-align-all and text-align-last: justify-all forces both to justify, and match-parent resolves a logical start/end against the parent's own direction rather than the element's own.",
    textAlignLastHtml, pdfConfig);

// --- writing-mode (vertical-rl/vertical-lr) showcase ---
// Real vertical line flow (issue #547): lines stack along the block axis (right-to-left for
// vertical-rl, left-to-right for vertical-lr), text runs top-to-bottom within each line, and
// glyphs paint rotated 90° by default (text-orientation: sideways/mixed's rotated runs) or
// upright without rotation (text-orientation: upright/mixed's upright runs, real Unicode
// Vertical_Orientation classification - issue #765). Flexbox and Table layout are also
// writing-mode-aware now, including Table's own captions, <thead>/<tfoot>, collapsed borders,
// vertical-align, and rowspan row-axis sizing (issue #762), and so is a vertical box's own
// block-level and orthogonal-flow content (issue #760). CreateVerticalLineBoxes (inline-only
// content) now has real feature parity with horizontal FlowBox too: text-align, Unicode Bidi
// Algorithm reordering, hyphenation, position: absolute/fixed, and float column wrap-around all
// work (issue #768) - see .claude/accepted-gaps/no-vertical-writing-mode-layout.md for what's
// still deferred (Multi-column axis awareness, a float's own starting position/clear, and
// Table's own colspan straddling the row axis / real per-row pagination combined with rowspan).

// A subset of Noto Sans JP (see assets/fonts/NotoSansJPSubset.LICENSE.txt) covering the CJK
// characters section 8 below uses, so mixed text-orientation renders real upright glyphs rather
// than .notdef boxes - the vast majority of real-world text-orientation: mixed content is CJK.
var writingModeCjkFontB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJPSubset.ttf")));

// Section 10b's bidi example needs real Hebrew glyphs, the same bundled subset the dedicated
// bidi_text showcase (below) already proves embeds cleanly.
var writingModeHebrewB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansHebrewSubset.ttf")));

const string WritingModeCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    .row { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 0.5em }
    .vbox { border: 2px solid #1a6b8a; padding: 8px; background: #eef6fb }
    .label { font-size: 7pt; color: #444; margin-top: 4px }
    </style>
    """;

var writingModeHtml = "<!DOCTYPE html><html><head>" + WritingModeCss + "</head><body>" +

    "<h1>CSS writing-mode Test Page</h1>" +

    "<h2>1 &mdash; vertical-rl: columns stack right-to-left</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 260px; height: 140px\">" +
    "This paragraph flows in real vertical-rl columns, wrapping to a new column to the left of the previous one as each fills up." +
    "</div><div class=\"label\">writing-mode: vertical-rl</div></div>" +
    "</div>" +

    "<h2>2 &mdash; vertical-lr: columns stack left-to-right</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-lr; width: 260px; height: 140px\">" +
    "This paragraph flows in real vertical-lr columns, wrapping to a new column to the right of the previous one as each fills up." +
    "</div><div class=\"label\">writing-mode: vertical-lr</div></div>" +
    "</div>" +

    "<h2>3 &mdash; forced line break starts a fresh column</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 260px; height: 120px\">" +
    "First column.<br>Second column, even though the first had room to spare." +
    "</div><div class=\"label\">writing-mode: vertical-rl with &lt;br&gt;</div></div>" +
    "</div>" +

    "<h2>4 &mdash; auto height shrinks to the content's own extent</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 260px\">Short auto-height line.</div>" +
    "<div class=\"label\">height: auto</div></div>" +
    "</div>" +

    "<h2>4b &mdash; auto width shrinks to the content's own extent (issue #761)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; height: 60px\">One Two Three</div>" +
    "<div class=\"label\">vertical-rl, width: auto</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-lr; height: 60px\">One Two Three</div>" +
    "<div class=\"label\">vertical-lr, width: auto</div></div>" +
    "</div>" +

    "<h2>5 &mdash; flex-direction: row under vertical-rl (main axis = inline = physical Y)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; display: flex; width: 220px; height: 160px\">" +
    "<div style=\"width: 60px; height: 40px; background: #1a6b8a; margin: 4px\"></div>" +
    "<div style=\"width: 60px; height: 40px; background: #4a9bc4; margin: 4px\"></div>" +
    "<div style=\"width: 60px; height: 40px; background: #7cc0e0; margin: 4px\"></div>" +
    "</div><div class=\"label\">flex row: items stack top-to-bottom</div></div>" +
    "</div>" +

    "<h2>6 &mdash; flex-direction: column under vertical-rl/vertical-lr (main axis = block = physical X)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; display: flex; flex-direction: column; width: 220px; height: 160px\">" +
    "<div style=\"width: 60px; height: 40px; background: #1a6b8a; margin: 4px\"></div>" +
    "<div style=\"width: 60px; height: 40px; background: #4a9bc4; margin: 4px\"></div>" +
    "<div style=\"width: 60px; height: 40px; background: #7cc0e0; margin: 4px\"></div>" +
    "</div><div class=\"label\">vertical-rl flex column: right-to-left</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-lr; display: flex; flex-direction: column; width: 220px; height: 160px\">" +
    "<div style=\"width: 60px; height: 40px; background: #1a6b8a; margin: 4px\"></div>" +
    "<div style=\"width: 60px; height: 40px; background: #4a9bc4; margin: 4px\"></div>" +
    "<div style=\"width: 60px; height: 40px; background: #7cc0e0; margin: 4px\"></div>" +
    "</div><div class=\"label\">vertical-lr flex column: left-to-right</div></div>" +
    "</div>" +

    "<h2>7 &mdash; table under vertical-rl/vertical-lr (rows = block axis, columns = inline axis)</h2>" +
    "<div class=\"row\">" +
    "<div><table style=\"writing-mode: vertical-rl; border-spacing: 4px\">" +
    "<tr><td style=\"width: 60px; height: 40px; background: #1a6b8a; color: #fff; text-align: center\">R1C1</td>" +
    "<td style=\"width: 60px; height: 40px; background: #4a9bc4; color: #fff; text-align: center\">R1C2</td></tr>" +
    "<tr><td style=\"width: 60px; height: 40px; background: #7cc0e0; text-align: center\">R2C1</td>" +
    "<td style=\"width: 60px; height: 40px; background: #cfe9f5; text-align: center\">R2C2</td></tr>" +
    "</table><div class=\"label\">vertical-rl table: rows stack right-to-left, columns run top-to-bottom</div></div>" +
    "<div><table style=\"writing-mode: vertical-lr; border-spacing: 4px\">" +
    "<tr><td style=\"width: 60px; height: 40px; background: #1a6b8a; color: #fff; text-align: center\">R1C1</td>" +
    "<td style=\"width: 60px; height: 40px; background: #4a9bc4; color: #fff; text-align: center\">R1C2</td></tr>" +
    "<tr><td style=\"width: 60px; height: 40px; background: #7cc0e0; text-align: center\">R2C1</td>" +
    "<td style=\"width: 60px; height: 40px; background: #cfe9f5; text-align: center\">R2C2</td></tr>" +
    "</table><div class=\"label\">vertical-lr table: rows stack left-to-right, columns run top-to-bottom</div></div>" +
    "</div>" +

    "<h2>7b &mdash; caption, &lt;thead&gt;/&lt;tfoot&gt;, collapsed borders and rowspan under vertical-rl (issue #762)</h2>" +
    "<div class=\"row\">" +
    "<div><table style=\"writing-mode: vertical-rl; border-collapse: collapse; border-spacing: 0\">" +
    "<caption style=\"caption-side: top; font-weight: bold\">Top caption</caption>" +
    "<caption style=\"caption-side: bottom; font-style: italic\">Bottom caption</caption>" +
    "<thead><tr><td style=\"width: 40px; height: 40px; background: #1a6b8a; color: #fff; text-align: center; border: 2px solid #123\">Head</td>" +
    "<td style=\"width: 40px; height: 40px; background: #1a6b8a; color: #fff; text-align: center; border: 2px solid #123\">Head</td></tr></thead>" +
    "<tbody>" +
    "<tr><td rowspan=\"2\" style=\"width: 30px; height: 40px; background: #cfe9f5; text-align: center; border: 2px solid #123\">Span</td>" +
    "<td style=\"width: 60px; height: 40px; background: #7cc0e0; text-align: center; border: 2px solid #123\">R1C2</td></tr>" +
    "<tr><td style=\"width: 60px; height: 40px; background: #7cc0e0; text-align: center; border: 2px solid #123\">R2C2</td></tr>" +
    "</tbody>" +
    "<tfoot><tr><td colspan=\"2\" style=\"width: 40px; height: 40px; background: #4a9bc4; color: #fff; text-align: center; border: 2px solid #123\">Foot</td></tr></tfoot>" +
    "</table><div class=\"label\">top/bottom caption flank the row axis, &lt;thead&gt;/&lt;tfoot&gt; sit at the row-axis start/end, collapsed borders paint as row-axis-tall/column-axis-wide stripes, and the rowspan cell spans both body rows' combined row-axis extent</div></div>" +
    "</div>" +

    "<h2>7c &mdash; multi-row &lt;thead&gt;/&lt;tfoot&gt; reverses its own internal row order under vertical-rl (issue #784)</h2>" +
    "<div class=\"row\">" +
    "<div><table style=\"writing-mode: vertical-rl; border-collapse: collapse; border-spacing: 0\">" +
    "<thead>" +
    "<tr><td style=\"width: 30px; height: 50px; background: #1a6b8a; color: #fff; text-align: center; border: 2px solid #123\">H1</td></tr>" +
    "<tr><td style=\"width: 30px; height: 50px; background: #2a86a8; color: #fff; text-align: center; border: 2px solid #123\">H2</td></tr>" +
    "</thead>" +
    "<tbody><tr><td style=\"width: 60px; height: 50px; background: #cfe9f5; text-align: center; border: 2px solid #123\">Body</td></tr></tbody>" +
    "<tfoot>" +
    "<tr><td style=\"width: 30px; height: 50px; background: #4a9bc4; color: #fff; text-align: center; border: 2px solid #123\">F1</td></tr>" +
    "<tr><td style=\"width: 30px; height: 50px; background: #3a7ba0; color: #fff; text-align: center; border: 2px solid #123\">F2</td></tr>" +
    "</tfoot>" +
    "</table><div class=\"label\">reading right-to-left (the table's own block-start-to-end order): H1, H2, Body, F1, F2 - topologically-first H1/F1 sit nearest their own group's block-start edge, not swapped with H2/F2 the way an unfixed #784 would paint them</div></div>" +
    "<div><table style=\"writing-mode: vertical-rl; border-spacing: 4px\">" +
    "<thead>" +
    "<tr><td rowspan=\"2\" style=\"width: 20px; height: 40px; background: #1a6b8a; color: #fff; text-align: center\">Span</td>" +
    "<td style=\"width: 30px; height: 40px; background: #2a86a8; color: #fff; text-align: center\">H1</td></tr>" +
    "<tr><td style=\"width: 40px; height: 40px; background: #2a86a8; color: #fff; text-align: center\">H2</td></tr>" +
    "</thead>" +
    "<tbody><tr><td style=\"width: 30px; height: 40px; background: #cfe9f5; text-align: center\">Body</td></tr></tbody>" +
    "</table><div class=\"label\">a rowspan cell entirely inside a multi-row &lt;thead&gt; spans the combined row-axis extent of its own two rows, correctly positioned at the reversed (block-start-ward) side rather than only against its opening row</div></div>" +
    "</div>" +

    "<div style=\"page-break-before: always\"></div>" +
    "<h2>7d &mdash; real per-column pagination of a plain-grid vertical table (issue #783)</h2>" +
    "<div class=\"row\">" +
    "<div><table style=\"writing-mode: vertical-rl; border-spacing: 0; border: 2px solid #123\">" +
    "<tr>" +
    "<td style=\"width: 60px; height: 100px; background: #1a6b8a; color: #fff; text-align: center; border: 1px solid #123\">C1</td>" +
    "<td style=\"width: 60px; height: 100px; background: #2a86a8; color: #fff; text-align: center; border: 1px solid #123\">C2</td>" +
    "<td style=\"width: 60px; height: 100px; background: #3a95bc; color: #fff; text-align: center; border: 1px solid #123\">C3</td>" +
    "<td style=\"width: 60px; height: 100px; background: #4a9bc4; color: #fff; text-align: center; border: 1px solid #123\">C4</td>" +
    "<td style=\"width: 60px; height: 100px; background: #5aa8d0; color: #fff; text-align: center; border: 1px solid #123\">C5</td>" +
    "<td style=\"width: 60px; height: 100px; background: #6ab5dc; color: #fff; text-align: center; border: 1px solid #123\">C6</td>" +
    "<td style=\"width: 60px; height: 100px; background: #7cc0e0; text-align: center; border: 1px solid #123\">C7</td>" +
    "<td style=\"width: 60px; height: 100px; background: #9cd2ea; text-align: center; border: 1px solid #123\">C8</td>" +
    "<td style=\"width: 60px; height: 100px; background: #bce3f2; text-align: center; border: 1px solid #123\">C9</td>" +
    "<td style=\"width: 60px; height: 100px; background: #cfe9f5; text-align: center; border: 1px solid #123\">C10</td>" +
    "<td style=\"width: 60px; height: 100px; background: #dff2fa; text-align: center; border: 1px solid #123\">C11</td>" +
    "<td style=\"width: 60px; height: 100px; background: #eef6fb; text-align: center; border: 1px solid #123\">C12</td>" +
    "</tr>" +
    "</table><div class=\"label\">a plain grid (no rowspan/colspan/caption/thead-tfoot/collapsed borders) whose own column-axis extent exceeds one page's band relocates the columns that don't fit onto the next page as whole units, rather than moving the entire table - the table's own outer border and each page's own slice-bottom border paint correctly on both pages</div></div>" +
    "</div>" +

    $"<h2>8 &mdash; text-orientation: real per-character upright/rotated splitting (issue #765)</h2>" +
    $"<style>@font-face {{ font-family: 'CJK'; src: url('data:font/truetype;base64,{writingModeCjkFontB64}') format('truetype'); }} .cjk {{ font-family: 'CJK', Arial, sans-serif }}</style>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox cjk\" style=\"writing-mode: vertical-rl; text-orientation: mixed; width: 60px; height: 260px\">縦書きテキストPDF2024</div>" +
    "<div class=\"label\">mixed (default): CJK upright, Latin/digits rotated</div></div>" +
    "<div><div class=\"vbox cjk\" style=\"writing-mode: vertical-rl; text-orientation: upright; width: 60px; height: auto\">縦書きテキストPDF2024</div>" +
    "<div class=\"label\">upright: every character upright</div></div>" +
    "<div><div class=\"vbox cjk\" style=\"writing-mode: vertical-rl; text-orientation: sideways; width: 60px; height: 260px\">縦書きテキストPDF2024</div>" +
    "<div class=\"label\">sideways: every character rotated</div></div>" +
    "</div>" +

"<div style=\"page-break-before: always\"></div>" +
    "<h2>9 &mdash; block-level and orthogonal-flow children of a vertical box (issue #760)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 200px; height: 90px\">" +
    "<div style=\"width: 40px; height: 60px; background: #1a6b8a; color: #fff; padding: 4px\">First.</div>" +
    "<div style=\"width: 40px; height: 60px; background: #4a9bc4; color: #fff; padding: 4px\">Second.</div>" +
    "<div style=\"width: 40px; height: 60px; background: #7cc0e0; padding: 4px\">Third.</div>" +
    "</div><div class=\"label\">vertical-rl: block children stack right-to-left</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 220px; height: 130px\">" +
    "<div style=\"writing-mode: horizontal-tb; width: 90px; background: #eef6fb; border: 1px solid #1a6b8a; padding: 4px\">" +
    "This orthogonal child keeps its own horizontal-tb line flow, while the vertical parent places its whole box as one atomic unit." +
    "</div>" +
    "</div><div class=\"label\">orthogonal child: horizontal-tb block inside a vertical-rl parent</div></div>" +
    "</div>" +

    "<h2>9b &mdash; direction: rtl block children anchor to the physical bottom edge (issue #778)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 200px; height: 90px\">" +
    "<div style=\"width: 40px; height: 60px; background: #1a6b8a; color: #fff; padding: 4px\">First.</div>" +
    "<div style=\"width: 40px; height: 60px; background: #4a9bc4; color: #fff; padding: 4px\">Second.</div>" +
    "<div style=\"width: 40px; height: 60px; background: #7cc0e0; padding: 4px\">Third.</div>" +
    "</div><div class=\"label\">direction: ltr (default): children flush against the top</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; direction: rtl; width: 200px; height: 90px\">" +
    "<div style=\"width: 40px; height: 60px; background: #1a6b8a; color: #fff; padding: 4px\">First.</div>" +
    "<div style=\"width: 40px; height: 60px; background: #4a9bc4; color: #fff; padding: 4px\">Second.</div>" +
    "<div style=\"width: 40px; height: 60px; background: #7cc0e0; padding: 4px\">Third.</div>" +
    "</div><div class=\"label\">direction: rtl: children flush against the bottom instead</div></div>" +
    "</div>" +

    "<h2>9c &mdash; real margin collapse between block-axis-stacked children (issue #776)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 200px; height: 90px\">" +
    "<div style=\"width: 40px; height: 60px; margin: 0 20px; background: #1a6b8a; color: #fff; padding: 4px\">First.</div>" +
    "<div style=\"width: 40px; height: 60px; margin: 0 20px; background: #4a9bc4; color: #fff; padding: 4px\">Second.</div>" +
    "</div><div class=\"label\">both children margin: 0 20px &mdash; the gap between them collapses to 20px (the larger margin), not 40px (their sum)</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 200px\">" +
    "<div style=\"width: 60px; margin-left: 30px; background: #eef6fb; border: 1px solid #1a6b8a; padding: 4px\">" +
    "Nested vertical-rl wrapper with no border/padding on its own block-start edge: its first child's own margin collapses with the wrapper's own edge instead of leaving an internal gap." +
    "</div>" +
    "</div><div class=\"label\">box-own-edge collapse: a nested vertical-rl wrapper's own edge collapsing with its first stacked child's margin</div></div>" +
    "</div>" +

"<div style=\"page-break-before: always\"></div>" +
    $"<style>@font-face {{ font-family: 'Hebrew'; src: url('data:font/truetype;base64,{writingModeHebrewB64}') format('truetype'); }}</style>" +
    "<h2>10 &mdash; text-align inside vertical line flow (issue #768)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 60px; height: 140px; text-align: left\">Hi there</div>" +
    "<div class=\"label\">text-align: left (flush physical top)</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 60px; height: 140px; text-align: right\">Hi there</div>" +
    "<div class=\"label\">text-align: right (flush physical bottom)</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 60px; height: 140px; text-align: center\">Hi there</div>" +
    "<div class=\"label\">text-align: center</div></div>" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 200px; height: 60px; text-align: justify\">Alpha Beta Gamma Delta Epsilon.</div>" +
    "<div class=\"label\">text-align: justify (spreads non-last columns)</div></div>" +
    "</div>" +

    "<h2>10b &mdash; Unicode Bidi Algorithm reordering inside vertical line flow (issue #768)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; direction: rtl; width: 60px; height: 220px; font-family: 'Hebrew', Arial, sans-serif\">שלום עולם 123 כאן</div>" +
    "<div class=\"label\">dir: rtl paragraph; the embedded \"123\" digit run reorders between its two Hebrew neighbors, same as horizontal RTL text</div></div>" +
    "</div>" +

    "<h2>10c &mdash; hyphenation splits a word across a column boundary (issue #768)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; width: 120px; height: 200px; hyphens: auto\" lang=\"en\">antidisestablishmentarianism</div>" +
    "<div class=\"label\">hyphens: auto — splits with a real hyphen instead of overflowing</div></div>" +
    "</div>" +

    "<h2>10d &mdash; position: absolute/fixed nested inside vertical line flow (issue #768)</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"vbox\" style=\"writing-mode: vertical-rl; position: relative; width: 260px; height: 200px\">" +
    "Before <span style=\"position: absolute; left: 8px; top: 8px; width: 40px; background: #1a6b8a; color: #fff; padding: 2px\">Positioned</span> after" +
    "</div><div class=\"label\">position: absolute, nested in otherwise inline-only vertical text — reserves no column space and resolves fully against its own nearest positioned ancestor</div></div>" +
    "</div>" +

    "</body></html>";

await SaveShowcaseAsync("writing_mode", "Typography & Text", "writing-mode (Vertical Text)",
    "Real vertical-rl/vertical-lr line flow: columns stacking along the block axis, text running top-to-bottom within each column, real per-character text-orientation (upright CJK next to rotated Latin), writing-mode-aware Flexbox and Table layout (including captions, thead/tfoot, collapsed borders, vertical-align and rowspan row-axis sizing), block-level/orthogonal-flow content inside a vertical box, direction: rtl block children anchoring to the physical bottom edge, real CSS2.1 margin collapse (sibling-to-sibling and box-own-edge) between block-axis-stacked children, and text-align, Unicode Bidi Algorithm reordering, hyphenation, position: absolute/fixed, and float column wrap-around all working inside vertical line flow.",
    writingModeHtml, pdfConfig);

// --- float and clear in a vertical writing mode (issue #796) ---
// float: left/right (and clear) are line-relative - CSS Writing Modes 4 section 7.5 - so in vertical-rl and
// vertical-lr the line-left side is the physical top and the line-right side the physical bottom, whatever the
// direction. A float sits at the current block-axis position and slides along the inline axis; the columns
// beside it start below a top float and stop above a bottom one.
var verticalFloatsHtml = """
    <!DOCTYPE html><html><head><style>
    @page { size: a4; margin: 15mm }
    body { font-family: Arial, sans-serif; font-size: 11pt; line-height: 1.4; color: #222 }
    h1 { font-size: 16pt; margin: 0 0 6pt }
    .note { color: #555; font-size: 9pt; margin: 0 0 10pt }
    .row { display: flex; gap: 14pt; flex-wrap: wrap; margin-bottom: 14pt }
    .cell { width: 150pt }
    .vbox { border: 1.5pt solid #1a6b8a; background: #f4f9fc; font-size: 10pt }
    .label { font-size: 8pt; color: #555; margin-top: 3pt }
    .top { background: #7cc0e0 } .bottom { background: #e0a87c }
    </style></head><body>
    <h1>Floats and clear in vertical text</h1>
    <p class="note">float: left and float: right are line-relative: in vertical-rl and vertical-lr the line-left
    side is the physical top and the line-right side the physical bottom, whatever the direction.</p>
    <div class="row">
    <div class="cell"><div class="vbox" style="writing-mode: vertical-rl; width: 140pt; height: 150pt">
    <div class="top" style="float: left; width: 22pt; height: 50pt"></div>
    <p style="margin: 0; width: 120pt; height: 150pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p></div>
    <div class="label">float: left - pinned to the physical top; the columns beside it start below it</div></div>
    <div class="cell"><div class="vbox" style="writing-mode: vertical-rl; width: 140pt; height: 150pt">
    <div class="bottom" style="float: right; width: 22pt; height: 50pt"></div>
    <p style="margin: 0; width: 120pt; height: 150pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p></div>
    <div class="label">float: right - pinned to the physical bottom; the columns beside it stop above it</div></div>
    <div class="cell"><div class="vbox" style="writing-mode: vertical-lr; direction: rtl; width: 140pt; height: 150pt">
    <div class="top" style="float: left; width: 22pt; height: 50pt"></div>
    <div class="bottom" style="float: right; width: 22pt; height: 50pt"></div>
    <p style="margin: 0; width: 120pt; height: 150pt">Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa.</p></div>
    <div class="label">vertical-lr with direction: rtl - the top float is still float: left; both sides at once</div></div>
    <div class="cell"><div class="vbox" style="writing-mode: vertical-rl; width: 140pt; height: 150pt">
    <div class="top" style="float: left; width: 40pt; height: 50pt"></div>
    <div style="width: 16pt; height: 90pt; background: #d5e8d4">A</div>
    <div style="clear: left; width: 30pt; height: 90pt; background: #f8cecc">B</div></div>
    <div class="label">clear: left - B is moved past the block-end edge of the float (the gap); A, uncleared, ignores it</div></div>
    </div>
    </body></html>
    """;

await SaveShowcaseAsync("vertical_floats", "Typography & Text", "Floats and clear in vertical text",
    "float and clear in a vertical writing mode: left and right are line-relative (top and bottom), floats slide along the inline axis and text wraps around them.",
    verticalFloatsHtml, pdfConfig);

// --- text-overflow: ellipsis showcase (issue #694) ---
// Per-line truncation of whatever content genuinely overflows an overflow:hidden container's
// content edge - Tailwind's truncate idiom (overflow:hidden; white-space:nowrap; text-overflow:
// ellipsis) for the common single-line case, a wrapping paragraph whose one unbreakable line
// overflows, and both horizontal directions and vertical writing modes, since the "end" edge
// (where truncation/the ellipsis lands) depends on writing-mode/direction: physical right for
// LTR, left for RTL, bottom for vertical-rl/vertical-lr under LTR direction, top under RTL.
// Reuses the Hebrew/CJK font subsets already loaded above for the writing-mode showcase.

const string TextOverflowCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 10pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    .card { border: 1px solid #ccc; border-radius: 4px; padding: 6px 10px; margin-bottom: 6px; background: #f7fafc }
    .truncate { overflow: hidden; white-space: nowrap; text-overflow: ellipsis }
    .w1 { width: 260px }
    .w2 { width: 160px }
    .w3 { width: 90px }
    .label { font-size: 7pt; color: #444; margin-top: 2px }
    .vrow { display: flex; gap: 12px; align-items: flex-start }
    .vcol { text-orientation: upright; overflow: hidden; text-overflow: ellipsis;
            border: 1px solid #ccc; border-radius: 4px; padding: 6px; width: 40px }
    </style>
    """;

var textOverflowHtml = "<!DOCTYPE html><html><head>" + TextOverflowCss +
    $"<style>@font-face {{ font-family: 'PeachPDF Hebrew Subset'; src: url(data:font/ttf;base64,{writingModeHebrewB64}) format('truetype') }}" +
    $"@font-face {{ font-family: 'PeachPDF CJK Subset'; src: url(data:font/ttf;base64,{writingModeCjkFontB64}) format('truetype') }}</style>" +
    "</head><body>" +

    "<h1>CSS text-overflow: ellipsis Test Page</h1>" +

    "<h2>1 &mdash; horizontal LTR: truncate at decreasing widths</h2>" +
    "<div class=\"card truncate w1\">The quick brown fox jumps over the lazy dog</div>" +
    "<div class=\"card truncate w2\">The quick brown fox jumps over the lazy dog</div>" +
    "<div class=\"card truncate w3\">The quick brown fox jumps over the lazy dog</div>" +
    "<div class=\"card truncate w1\">Short text needs no truncation at all</div>" +

    "<h2>2 &mdash; horizontal RTL: truncate stays flush-right, ellipsis on the physical left</h2>" +
    "<div class=\"card truncate w1\" dir=\"rtl\" style=\"font-family: 'PeachPDF Hebrew Subset', Arial, sans-serif\">" +
    "זהו משפט ארוך בעברית שאמור להיחתך בקצה הנכון של התיבה</div>" +

    "<h2>3 &mdash; a wrapping paragraph whose one unbreakable line overflows</h2>" +
    "<div class=\"card\" style=\"overflow: hidden; text-overflow: ellipsis; width: 220px\">" +
    "This paragraph wraps normally, but one line below has a single run with no spaces:<br>" +
    "ThisIsOneVeryLongUnbreakableTokenWithNoSpacesAtAllToWrapOn<br>" +
    "and this last line is short again.</div>" +

    "<h2>4 &mdash; vertical-rl/vertical-lr columns: truncation along the inline (top-to-bottom) axis</h2>" +
    "<div class=\"vrow\">" +
    "<div><div class=\"vcol\" style=\"writing-mode: vertical-rl; height: 120px; font-family: 'PeachPDF CJK Subset', Arial, sans-serif\">" +
    "この文章は縦書きの列の高さより長いため途中で省略記号に置き換えられます</div>" +
    "<div class=\"label\">vertical-rl</div></div>" +
    "<div><div class=\"vcol\" style=\"writing-mode: vertical-lr; height: 120px; font-family: 'PeachPDF CJK Subset', Arial, sans-serif\">" +
    "この文章は縦書きの列の高さより長いため途中で省略記号に置き換えられます</div>" +
    "<div class=\"label\">vertical-lr</div></div>" +
    "</div>" +

    "</body></html>";

await SaveShowcaseAsync("text_overflow", "Typography & Text", "text-overflow: ellipsis",
    "Per-line-box truncation with a trailing ellipsis wherever content overflows an overflow:hidden container's content edge - Tailwind's truncate idiom for the common single nowrap line, a wrapping paragraph whose one unbreakable line overflows, and both horizontal directions plus vertical-rl/vertical-lr writing modes, each finding the correct end edge (right/left/bottom/top) for its own writing-mode/direction combination.",
    textOverflowHtml, pdfConfig);

// --- line-clamp showcase (CSS Overflow 4 §line-clamp) ---

const string LineClampCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    .card { border: 1px solid #ccc; border-radius: 4px; padding: 8px 10px; margin-bottom: 0.6em; width: 320px; background: #fafafa }
    .label { font-size: 8pt; font-family: "Courier New", monospace; color: #444; margin-bottom: 3px }
    </style>
    """;

const string ClampParagraph =
    "The quick brown fox jumps over the lazy dog. This sentence keeps going long enough to wrap " +
    "across quite a few lines once the container is narrow, which is exactly what this showcase needs " +
    "in order to demonstrate line-clamp actually cutting it short partway through.";

var lineClampHtml = "<!DOCTYPE html><html><head>" + LineClampCss + "</head><body>" +

    "<h1>CSS Overflow 4 line-clamp</h1>" +

    "<h2>1 &mdash; the same paragraph clamped at increasing line counts</h2>" +
    "<div class=\"label\">line-clamp: 1</div>" +
    $"<div class=\"card\" style=\"line-clamp:1\">{ClampParagraph}</div>" +
    "<div class=\"label\">line-clamp: 2</div>" +
    $"<div class=\"card\" style=\"line-clamp:2\">{ClampParagraph}</div>" +
    "<div class=\"label\">line-clamp: 4</div>" +
    $"<div class=\"card\" style=\"line-clamp:4\">{ClampParagraph}</div>" +
    "<div class=\"label\">none (unclamped, for comparison)</div>" +
    $"<div class=\"card\">{ClampParagraph}</div>" +

    "<h2>2 &mdash; content that already fits within the limit gets no ellipsis</h2>" +
    "<div class=\"label\">line-clamp: 5 on a two-line paragraph</div>" +
    "<div class=\"card\" style=\"line-clamp:5\">Short paragraph that only wraps onto two lines in this narrow card.</div>" +

    "</body></html>";

await SaveShowcaseAsync("line_clamp", "Typography & Text", "line-clamp",
    "line-clamp (CSS Overflow 4): a layout-time cutoff limiting a block to a fixed number of visible lines, with a generated ellipsis on the last one whenever content is actually truncated - the block's own height shrinks to fit only the visible lines, and content past the limit is never laid out at all.",
    lineClampHtml, pdfConfig);

// --- CSS1 canvas background showcase ---

var canvasBackgroundHtml = "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    "body { font: 11pt Georgia, serif; margin: 0; background-color: rgb(230,240,255) }" +
    "h1 { font-size: 15pt; margin: 0; padding: 15mm 15mm 0.3em; font-family: Arial, sans-serif }" +
    "p { margin: 0 15mm 0.8em }" +
    "</style></head><body>" +
    "<h1>CSS1 Canvas Background Test Page</h1>" +
    "<p>The pale blue background here comes from &lt;body&gt;'s own background-color, but it fills the " +
    "whole page canvas (per CSS2.1 &sect;14.2) - not just the height of this short paragraph, which is " +
    "nowhere near a full page tall.</p>" +
    "</body></html>";

await SaveShowcaseAsync("canvas_background", "Backgrounds & Borders", "Canvas Background",
    "CSS1 canvas background propagation: body and html backgrounds covering the full page.",
    canvasBackgroundHtml, pdfConfig);

// ─── Font resolution showcase (CSS font-resolution compliance pass) ───────
// Uses only real, already-installed system fonts, referenced via @font-face src: local()
// fallback chains (matched by full font name) - each chain lists the docs build machine's
// face (Roboto, installed by pages.yml) first, then the local-Windows equivalent (Segoe UI /
// Arial). A rule whose local() candidates all miss simply doesn't register, so every section
// renders on both platforms from whichever real faces exist, with no bundled binary assets.

var fontShowcase = new StringBuilder();

// Nearest-weight matching (CSS Fonts Level 4 §5.2): "WeightDemo" is assembled from real
// multi-weight faces (Roboto's six weights on the docs build machine, Segoe UI's five on
// Windows) - requested weights with no exact face show the nearest-weight search.
fontShowcase.Append("<style>" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 100; src: local('Roboto Thin'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 300; src: local('Roboto Light'), local('Segoe UI Light'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 400; src: local('Roboto'), local('Segoe UI'), local('Arial'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 500; src: local('Roboto Medium'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 600; src: local('Segoe UI Semibold'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 700; src: local('Roboto Bold'), local('Segoe UI Bold'), local('Arial Bold'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 900; src: local('Roboto Black'), local('Segoe UI Black'); }" +
    "</style>");
fontShowcase.Append("<h2>Nearest-weight matching (CSS Fonts Level 4 &sect;5.2)</h2>");
fontShowcase.Append("<p class=\"note\">\"WeightDemo\" is built via <code>@font-face src: local()</code> from whichever real installed faces this machine provides (Roboto's six weights on the docs build machine, Segoe UI's five on Windows). Requested weights with no exact face are matched to the nearest real one per the &sect;5.2 search order.</p>");
foreach (var weight in new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 })
{
    fontShowcase.Append($"<p style=\"font-family: WeightDemo; font-weight: {weight}\">font-weight: {weight} &mdash; The quick brown fox jumps over the lazy dog</p>");
}

fontShowcase.Append("<h2>Faux-bold / faux-italic synthesis</h2>");
fontShowcase.Append("<p class=\"note\">\"SynthDemo\" is registered via @font-face from a single Regular-only face - bold/italic requests have no real matching face to use, so the renderer synthesizes them (fill+stroke render mode for bold, glyph shear for italic).</p>");
fontShowcase.Append("<style>@font-face { font-family: 'SynthDemo'; src: local('Roboto'), local('Segoe UI'), local('Arial'); }</style>");
fontShowcase.Append("<p style=\"font-family: SynthDemo\">Regular (the one real registered face)</p>");
fontShowcase.Append("<p style=\"font-family: SynthDemo; font-weight: bold\">Bold (synthesized: fill+stroke)</p>");
fontShowcase.Append("<p style=\"font-family: SynthDemo; font-style: italic\">Italic (synthesized: fixed default shear)</p>");
fontShowcase.Append("<p style=\"font-family: SynthDemo; font-weight: bold; font-style: italic\">Bold Italic (both synthesized)</p>");

fontShowcase.Append("<h2>CSS Fonts Level 4 <code>oblique &lt;angle&gt;</code></h2>");
fontShowcase.Append("<p class=\"note\">Each line requests a different explicit oblique angle against the same Regular-only face - the synthesized shear follows the declared angle exactly, not a fixed default.</p>");
foreach (var angle in new[] { 0, 8, 14, 20, 30 })
{
    fontShowcase.Append($"<p style=\"font-family: SynthDemo; font-style: oblique {angle}deg\">oblique {angle}deg</p>");
}

// Arial Narrow ships with Office, not base Windows - on a machine with neither condensed
// face installed, the condensed rule below silently doesn't register and both lines render
// identically. The docs build machine always has Roboto Condensed (installed by pages.yml).
fontShowcase.Append("<h2>font-stretch face selection</h2>");
fontShowcase.Append("<p class=\"note\">\"StretchDemo\" registers two real, differently-shaped faces under one family via two @font-face rules with declared <code>font-stretch</code> descriptors - selecting condensed picks the genuinely narrower face (Roboto Condensed on the docs build machine, Arial Narrow on Windows), not just a coincidentally-matching one.</p>");
fontShowcase.Append("<style>@font-face { font-family: 'StretchDemo'; font-stretch: normal; src: local('Roboto'), local('Arial'); } " +
    "@font-face { font-family: 'StretchDemo'; font-stretch: condensed; src: local('Roboto Condensed'), local('Arial Narrow'); }</style>");
fontShowcase.Append("<p style=\"font-family: StretchDemo; font-stretch: normal\">font-stretch: normal</p>");
fontShowcase.Append("<p style=\"font-family: StretchDemo; font-stretch: condensed\">font-stretch: condensed</p>");

// Generic families + system-ui: whichever real installed substitute each resolves to on
// the OS actually generating this showcase - fontconfig aliases on the docs build machine
// (cursive -> Great Vibes, fantasy -> Cabin Sketch, set up by pages.yml), the
// Chromium-matched platform table on Windows/macOS/Android (see GenericFontFamilyResolver /
// DefaultFontResolver.DefaultFont).
fontShowcase.Append("<h2>Generic families (platform-matched)</h2>");
fontShowcase.Append("<p class=\"note\">Each generic family resolves to a real installed font: via fontconfig on the docs build machine, via the Chromium-matched platform table elsewhere.</p>");
foreach (var generic in new[] { "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui" })
{
    fontShowcase.Append($"<p style=\"font-family: {generic}\">{generic}: The quick brown fox jumps over the lazy dog</p>");
}

var fontShowcaseHtml = "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm } body { margin: 0; font-size: 13pt }" +
    "h1 { font: bold 18pt Arial, sans-serif; margin: 0 0 8px }" +
    "h2 { font: bold 13pt Arial, sans-serif; margin: 18px 0 4px }" +
    "p { margin: 3px 0 } p.note { font: italic 10pt Arial, sans-serif; color: #555; margin: 0 0 8px }" +
    "</style></head><body>" +
    "<h1>Font Resolution Showcase</h1>" +
    fontShowcase +
    "</body></html>";

await SaveShowcaseAsync("font_resolution_showcase", "Typography & Text", "Font Resolution",
    "CSS font selection compliance: nearest-weight matching across a six-weight family, faux-bold/italic synthesis, oblique angles, font-stretch face selection, and generic family resolution.",
    fontShowcaseHtml, pdfConfig);

// --- Acid2 showcase ---
// The real, unmodified Acid2 test (http://acid2.acidtests.org/) - PeachPDF's non-interactive/static
// subset compliance target. See CLAUDE.md and docs/html-css-support.md for what "compliance" means
// for a static PDF renderer (no :hover/:active, no scripting).
var acid2Html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "acid2.html"));
var acid2Config = new PdfGenerateConfig
{
    PageSize = PageSize.A4,
    PageOrientation = PageOrientation.Portrait,
    ShrinkToFit = true
};
acid2Config.SetMargins(0);

await SaveShowcaseAsync("acid2", "Standards & Accessibility", "Acid2",
    "The unmodified Acid2 test rendered by PeachPDF - the classic CSS compliance smiley.",
    acid2Html, acid2Config);

// ─── Real-World Documents: Quarterly Sales Ledger (repeating table headers) ───
// A 60-row <table> spanning three US Letter pages - the <thead> repeats automatically on
// every page the table spans. The grand total is deliberately a styled final <tbody> row,
// not a <tfoot>: a repeating <tfoot> currently renders none of its content
// (https://github.com/jhaygood86/PeachPDF/issues/124). Row data is deterministic
// (index-based formulas, no Random) so the rendered PDF is stable across builds.

string[] ledgerRegions = ["Southeast", "Northeast", "Midwest", "Southwest", "West Coast", "Mid-Atlantic"];
string[] ledgerProducts = ["Meridian Core", "Meridian Analytics", "Atlas Connect", "Pulse Monitor", "Vertex API"];
(string Label, string CssClass)[] ledgerStatuses = [("Paid", "paid"), ("Pending", "pending"), ("Paid", "paid"), ("Paid", "paid"), ("Overdue", "overdue")];

static int LedgerUnits(int i) => 4 + i * 7 % 38;
static int LedgerUnitPrice(int i) => 120 + i * 53 % 480;

var ledgerRows = string.Join("\n", Enumerable.Range(1, 60).Select(i =>
{
    var (statusLabel, statusClass) = ledgerStatuses[i * 2 % ledgerStatuses.Length];
    var alt = i % 2 == 0 ? " class=\"alt\"" : "";
    var total = LedgerUnits(i) * LedgerUnitPrice(i);
    return $"<tr{alt}><td class=\"num\">INV-2026-{i:0000}</td><td>{ledgerRegions[i % ledgerRegions.Length]}</td><td>{ledgerProducts[i * 3 % ledgerProducts.Length]}</td>" +
           $"<td class=\"r\">{LedgerUnits(i)}</td><td class=\"r\">${LedgerUnitPrice(i)}.00</td><td class=\"r\">${total.ToString("N0", CultureInfo.InvariantCulture)}.00</td>" +
           $"<td><span class=\"badge {statusClass}\">{statusLabel}</span></td></tr>";
}));

var ledgerGrandTotal = Enumerable.Range(1, 60).Sum(i => LedgerUnits(i) * LedgerUnitPrice(i));

var ledgerHtml = $$"""
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: letter portrait;
      margin: 0.85in 0.6in 0.8in 0.6in;
      @top-left { content: "Peachtree Analytics, Inc."; font: bold 8pt Arial; color: #0b4f6c; }
      @top-right { content: "FY2026 Sales Ledger"; font: 8pt Arial; color: #777; }
      @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; color: #777; }
    }
    body { font: 9pt Arial, sans-serif; color: #1c2733; margin: 0; }
    h1 { font-size: 19pt; margin: 0 0 2pt; color: #0b4f6c; }
    .sub { color: #667; margin: 0 0 14pt; font-size: 9.5pt; }
    table { border-collapse: collapse; width: 100%; }
    thead th {
      background: linear-gradient(180deg, #11698e, #0b4f6c);
      color: #fff; font-size: 8pt; text-transform: uppercase; letter-spacing: 0.8pt; word-spacing: 2pt;
      padding: 7pt 8pt; text-align: left; border-bottom: 2.5pt solid #073b52;
    }
    thead th.r { text-align: right; }
    tbody td { padding: 5.5pt 8pt; border-bottom: 0.75pt solid #dde4ea; }
    tbody tr.alt td { background: #f2f6f9; }
    td.num { font-family: monospace; color: #345; }
    td.r { text-align: right; }
    .badge { font-size: 7pt; font-weight: bold; text-transform: uppercase; letter-spacing: 0.5pt;
      padding: 2pt 6pt; border-radius: 8pt; }
    .badge.paid { background: #d9f2e4; color: #14683c; }
    .badge.pending { background: #fdf0d3; color: #8a6410; }
    .badge.overdue { background: #fbdfdb; color: #a02318; }
    tr.grand td {
      background: #0b4f6c; color: #fff; font-weight: bold; padding: 7pt 8pt;
      border-bottom: none;
    }
    </style>
    </head>
    <body>
    <h1>Quarterly Sales Ledger</h1>
    <p class="sub">60 invoices &mdash; the table header repeats automatically on every page the table spans.</p>
    <table>
    <thead>
    <tr><th>Invoice</th><th>Region</th><th>Product</th><th class="r">Units</th><th class="r">Unit Price</th><th class="r">Total</th><th>Status</th></tr>
    </thead>
    <tbody>
    {{ledgerRows}}
    <tr class="grand"><td colspan="5">Grand total &mdash; 60 invoices</td><td class="r">${{ledgerGrandTotal.ToString("N0", CultureInfo.InvariantCulture)}}.00</td><td></td></tr>
    </tbody>
    </table>
    </body>
    </html>
    """;

await SaveShowcaseAsync("table_header_repeat", "Real-World Documents", "Repeating Table Headers",
    "A 60-row sales ledger spanning three pages - the table header repeats automatically on every page, framed by @page margin-box running headers and page counters.",
    ledgerHtml, new PdfGenerateConfig { PageSize = PageSize.Letter });

// ─── Real-World Documents: modern invoice ───
// A one-page, full-bleed (base-rule @page margin: 0) US Letter invoice in a dark green
// palette exercising the modern-CSS feature set: custom properties, calc()/clamp(),
// oklch-interpolated + radial + repeating gradients, gradient-border cards, CSS-counter
// line numbers, a generated-content gradient bullet, a rotated transform stamp, an inline
// SVG logo, and a full-height column-flex body whose footer anchors via margin-top: auto.
// Workarounds baked in (each tracks a filed issue): line numbers use "0" counter(line)
// because decimal-leading-zero renders nothing (#128); word-spacing accompanies every
// letter-spacing because tracked-out text otherwise loses its word gaps (#129); body
// height is calc(11in - 1mm) because an exact 11in flex body overflows to a blank page;
// card depth is faked with gradient borders because box-shadow is a silent no-op (#132).

var invoiceHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    :root {
      --ink: #13342a;
      --ink-2: #1e5540;
      --muted: #5e6e66;
      --accent: #1d6a52;
      --accent-2: #d9b64a;
      --panel: #f2f6f2;
      --radius: 10pt;
    }
    @page { size: letter portrait; margin: 0; }
    body {
      font: 10pt Arial, sans-serif; color: var(--ink); margin: 0;
      height: calc(11in - 1mm); display: flex; flex-direction: column;
    }
    .band {
      background:
        radial-gradient(circle at 85% -40%, rgba(217, 182, 74, 0.5), transparent 60%),
        linear-gradient(115deg in oklch, #0f2a21, #1e5540 55%, #5d9673);
      color: #fff; padding: 11mm 14mm 8mm;
    }
    .band-row { display: flex; justify-content: space-between; align-items: flex-start; }
    .brand { display: flex; align-items: center; gap: 9pt; }
    .brand h1 { font-size: 17pt; margin: 0; letter-spacing: 2.5pt; word-spacing: 6pt; text-transform: uppercase; }
    .brand p { margin: 1pt 0 0; font-size: 8pt; color: rgba(255,255,255,0.75); letter-spacing: 1pt; word-spacing: 3pt; }
    .doc-meta { text-align: right; }
    .doc-meta .kind {
      font-size: clamp(20pt, 18pt + 24%, 28pt); font-weight: bold; letter-spacing: 5pt; margin: 0;
      color: var(--accent-2);
    }
    .doc-meta p { margin: 2pt 0 0; font-size: 9pt; color: rgba(255,255,255,0.85); }
    .rule {
      height: 3.5pt; background: repeating-linear-gradient(90deg,
        var(--accent-2) 0 14pt, var(--accent) 14pt 28pt);
    }
    main { padding: 8mm 14mm 0; }
    .parties { display: flex; gap: 6mm; margin-bottom: 6mm; }
    .party {
      flex: 1 1 0; background: linear-gradient(135deg, var(--accent), var(--accent-2));
      border-radius: var(--radius); padding: 1.6pt;
    }
    .party-inner { background: var(--panel); border-radius: calc(var(--radius) - 1.6pt); padding: 8pt 10pt; }
    .party h3 {
      margin: 0 0 4pt; font-size: 7.5pt; text-transform: uppercase; letter-spacing: 1.5pt; word-spacing: 3.5pt; color: var(--accent);
    }
    .party p { margin: 0; line-height: 1.45; font-size: 9pt; }
    table.items { border-collapse: collapse; width: 100%; counter-reset: line; }
    table.items thead th {
      font-size: 7.5pt; text-transform: uppercase; letter-spacing: 1.2pt; word-spacing: 3pt; color: var(--muted);
      border-bottom: 1.5pt solid var(--ink); padding: 0 6pt 5pt; text-align: left;
    }
    table.items thead th.r, table.items td.r { text-align: right; }
    table.items tbody td { padding: 5.5pt 6pt; border-bottom: 0.75pt solid #e3e0db; vertical-align: top; }
    table.items tbody td.n::before {
      counter-increment: line; content: counter(line, decimal-leading-zero);
      font-weight: bold; color: var(--accent); font-size: 8pt;
    }
    .item-name { font-weight: bold; }
    .item-desc { color: var(--muted); font-size: 8pt; margin-top: 1.5pt; }
    .totals { display: flex; justify-content: flex-end; margin-top: 4mm; }
    .totals-box { width: 62mm; }
    .trow { display: flex; justify-content: space-between; padding: 3.5pt 6pt; font-size: 9.5pt; }
    .trow.muted { color: var(--muted); }
    .trow.grand {
      margin-top: 4pt; background: linear-gradient(115deg, var(--ink), var(--ink-2));
      color: #fff; border-radius: 6pt; padding: 7pt 10pt; font-size: 12pt; font-weight: bold;
    }
    .stamp {
      position: absolute; top: 172mm; left: 102mm; z-index: 3; transform: rotate(-11deg);
      border: 2.5pt solid #14683c; color: #14683c; border-radius: 6pt;
      font: bold 15pt Arial; letter-spacing: 4pt; padding: 4pt 12pt; opacity: 0.45;
      text-transform: uppercase;
    }
    footer { margin-top: auto; padding: 5mm 14mm 6mm; background: var(--panel); }
    footer .cols { display: flex; gap: 8mm; }
    footer h4 { margin: 0 0 3pt; font-size: 7.5pt; text-transform: uppercase; letter-spacing: 1.5pt; word-spacing: 3.5pt; color: var(--accent); }
    footer p { margin: 0; font-size: 8pt; color: var(--muted); line-height: 1.5; }
    .thanks { margin-top: 4mm; font-size: 8.5pt; color: var(--ink); }
    .thanks::before {
      content: linear-gradient(135deg, var(--accent), var(--accent-2));
      display: inline-block; width: 7pt; height: 7pt; margin-right: 5pt; border-radius: 2pt;
    }
    </style>
    </head>
    <body>
    <div class="band">
      <div class="band-row">
        <div class="brand">
          <svg width="46" height="46" viewBox="0 0 46 46" xmlns="http://www.w3.org/2000/svg">
    <defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#d9b64a"/><stop offset="1" stop-color="#4fa77f"/></linearGradient></defs>
    <circle cx="23" cy="23" r="21" fill="none" stroke="url(#lg)" stroke-width="4"/>
    <path d="M13 29 L23 12 L33 29 Z" fill="url(#lg)"/>
    </svg>
          <div><h1>Solstice Studio</h1><p>DESIGN &amp; ENGINEERING</p></div>
        </div>
        <div class="doc-meta">
          <p class="kind">Invoice</p>
          <p>No. SS-2026-0117</p>
          <p>Issued July 12, 2026 &middot; Due August 11, 2026</p>
        </div>
      </div>
    </div>
    <div class="rule"></div>
    <main>
      <div class="parties">
        <div class="party"><div class="party-inner">
          <h3>Billed To</h3>
          <p><b>Halcyon Robotics, Inc.</b><br/>1200 Congress Ave, Suite 300<br/>Austin, TX 78701<br/>EIN 74-2201457</p>
        </div></div>
        <div class="party"><div class="party-inner">
          <h3>From</h3>
          <p><b>Solstice Studio LLC</b><br/>384 Peachtree St NE, Suite 900<br/>Atlanta, GA 30308<br/>EIN 88-4113550</p>
        </div></div>
        <div class="party"><div class="party-inner">
          <h3>Project</h3>
          <p><b>Beacon telemetry dashboard</b><br/>Statement of work #4<br/>PO HR-2026-088<br/>Period: May &ndash; June 2026</p>
        </div></div>
      </div>
      <div class="stamp">Paid</div>
      <table class="items">
        <thead><tr><th></th><th>Description</th><th class="r">Qty</th><th class="r">Rate</th><th class="r">Amount</th></tr></thead>
        <tbody>
          <tr><td class="n"></td><td><div class="item-name">Discovery &amp; UX research</div><div class="item-desc">Stakeholder interviews, telemetry audit, journey mapping</div></td><td class="r">3 days</td><td class="r">$1,150.00</td><td class="r">$3,450.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">Design system &amp; component library</div><div class="item-desc">Tokens, dark mode, 42 documented components</div></td><td class="r">6 days</td><td class="r">$1,150.00</td><td class="r">$6,900.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">Real-time dashboard implementation</div><div class="item-desc">Streaming charts, alerting views, fleet map</div></td><td class="r">9 days</td><td class="r">$1,280.00</td><td class="r">$11,520.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">Accessibility &amp; performance pass</div><div class="item-desc">WCAG 2.2 AA audit, remediation, load-time budget</div></td><td class="r">2 days</td><td class="r">$1,150.00</td><td class="r">$2,300.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">On-site handover workshop</div><div class="item-desc">Austin, 14 people, incl. travel</div></td><td class="r">1 day</td><td class="r">$1,650.00</td><td class="r">$1,650.00</td></tr>
        </tbody>
      </table>
      <div class="totals">
        <div class="totals-box">
          <div class="trow muted"><span>Subtotal</span><span>$25,820.00</span></div>
          <div class="trow muted"><span>Early-payment discount (2%)</span><span>&minus;$516.40</span></div>
          <div class="trow muted"><span>Sales tax (services, exempt)</span><span>$0.00</span></div>
          <div class="trow grand"><span>Total Due</span><span>$25,303.60</span></div>
        </div>
      </div>
    </main>
    <footer>
      <div class="cols">
        <div><h4>Payment</h4><p>Truist Bank, Atlanta<br/>Routing 061000104 &middot; Acct 1000223344<br/>Ref. SS-2026-0117</p></div>
        <div><h4>Terms</h4><p>Payment due within 30 days.<br/>Late balances accrue 1.5% per month.<br/>Checks payable to Solstice Studio LLC.</p></div>
        <div><h4>Contact</h4><p>billing@solstice.studio<br/>+1 (404) 555-0117<br/>solstice.studio</p></div>
      </div>
      <p class="thanks">Thank you for building with us &mdash; we would love to work on statement of work #5.</p>
    </footer>
    </body>
    </html>
    """;

await SaveShowcaseAsync("invoice", "Real-World Documents", "Modern Invoice",
    "A one-page, full-bleed invoice in a dark green palette: custom properties, oklch gradient interpolation, gradient-border cards, zero-padded line numbers via counter(line, decimal-leading-zero), a transform stamp, and an inline SVG logo.",
    invoiceHtml, new PdfGenerateConfig { PageSize = PageSize.Letter });

// ─── Real-World Documents: ten-page print catalog ───
// A US Letter catalog designed as a book: full-bleed gradient cover (page 1), eight item
// pages, and a colophon (page 10). Running furniture mirrors between left- and right-hand
// pages via :left/:right page selectors - the string-set item name at the top outside
// corner, the SVG logo as a real margin-box image at top-center (box width pinned to the
// image so it truly centers; margin-box images ignore box alignment, see
// https://github.com/jhaygood86/PeachPDF/issues/140), and folios in the inside (gutter)
// bottom corners. The cover is a true four-edge full-bleed plate via
// "@page :first { margin: 0 }": per-page top/bottom margin overrides are layout-affecting
// (each page gets its own content band per CSS Paged Media 3's page-box model), so page 1's
// band is the entire 8.5in x 11in sheet and the plate fills it exactly - the forced break
// after it lands item No. 01 at the top of the normally-margined page 2 with no blank page
// (exact-boundary forced-break rule, css-break-3). The colophon's named page is the last
// page only, because named-page styles leak onto subsequent auto pages (#126).

const string catalogLogoSvg = "<svg width=\"20\" height=\"20\" viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\">\n" +
    "<defs><linearGradient id=\"ml\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">\n" +
    "<stop offset=\"0\" stop-color=\"#d9a441\"/><stop offset=\"1\" stop-color=\"#c97b4a\"/></linearGradient></defs>\n" +
    "<path d=\"M12 2 L20 20 L15 20 L12 13 L9 20 L4 20 Z\" fill=\"url(#ml)\"/>\n" +
    "<circle cx=\"12\" cy=\"5.5\" r=\"2.1\" fill=\"none\" stroke=\"url(#ml)\" stroke-width=\"1.4\"/>\n" +
    "</svg>";

const string coverLogoSvg = "<svg width=\"120\" height=\"120\" viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\">\n" +
    "<path d=\"M12 2 L20 20 L15 20 L12 13 L9 20 L4 20 Z\" fill=\"#f0c975\"/>\n" +
    "<circle cx=\"12\" cy=\"5.5\" r=\"2.1\" fill=\"none\" stroke=\"#d9834f\" stroke-width=\"1.4\"/>\n" +
    "</svg>";

var catalogLogoDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(catalogLogoSvg));

// Stylized geometric product art. Shapes to avoid (see the filed issues): a stroked
// <rect rx> loses its bottom edge and a stroked <ellipse> loses its stroke entirely
// (#134), so the shelf frame has square corners and the mirror ring is a <circle>; and
// the art panel wrapper must not combine border-radius with border/padding, which drops
// SVG gradient strokes (#135) - the breathing room lives in the viewBox instead.
static string CatalogItemArt(string accent1, string accent2, string kind, int idx)
{
    var defs = $"<defs>\n<linearGradient id=\"g{idx}\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">\n" +
        $"<stop offset=\"0\" stop-color=\"{accent1}\"/><stop offset=\"1\" stop-color=\"{accent2}\"/></linearGradient>\n" +
        $"<radialGradient id=\"h{idx}\" cx=\"0.5\" cy=\"0.35\" r=\"0.8\">\n" +
        $"<stop offset=\"0\" stop-color=\"{accent1}\" stop-opacity=\"0.35\"/><stop offset=\"1\" stop-color=\"{accent1}\" stop-opacity=\"0\"/></radialGradient>\n" +
        "</defs>";
    var shape = kind switch
    {
        "chair" => $$"""<path d="M60 30 Q60 20 70 20 L110 20 Q120 20 120 30 L120 95 L60 95 Z" fill="url(#g{{idx}})"/><rect x="52" y="95" width="76" height="12" rx="6" fill="{{accent2}}"/><rect x="60" y="107" width="8" height="38" rx="3" fill="#3a3a3a"/><rect x="112" y="107" width="8" height="38" rx="3" fill="#3a3a3a"/>""",
        "table" => $$"""<ellipse cx="90" cy="55" rx="62" ry="16" fill="url(#g{{idx}})"/><rect x="85" y="66" width="10" height="60" fill="{{accent2}}"/><path d="M60 145 L90 120 L120 145 Z" fill="#3a3a3a"/>""",
        "lamp" => $$"""<path d="M65 25 L115 25 L100 70 L80 70 Z" fill="url(#g{{idx}})"/><rect x="87" y="70" width="6" height="55" fill="#3a3a3a"/><ellipse cx="90" cy="130" rx="28" ry="8" fill="{{accent2}}"/><circle cx="90" cy="86" r="9" fill="{{accent1}}" opacity="0.5"/>""",
        "shelf" => $$"""<rect x="45" y="20" width="90" height="120" fill="none" stroke="url(#g{{idx}})" stroke-width="8"/><rect x="53" y="58" width="74" height="6" fill="{{accent2}}"/><rect x="53" y="96" width="74" height="6" fill="{{accent2}}"/><rect x="60" y="34" width="14" height="20" fill="{{accent1}}"/><rect x="94" y="72" width="20" height="20" fill="{{accent1}}" opacity="0.7"/>""",
        "desk" => $$"""<rect x="40" y="55" width="100" height="10" rx="4" fill="url(#g{{idx}})"/><rect x="48" y="65" width="8" height="60" fill="#3a3a3a"/><rect x="124" y="65" width="8" height="60" fill="#3a3a3a"/><rect x="96" y="65" width="36" height="26" rx="3" fill="{{accent2}}"/><circle cx="103" cy="78" r="2.6" fill="#fff"/>""",
        "sofa" => $$"""<rect x="40" y="60" width="100" height="42" rx="10" fill="url(#g{{idx}})"/><rect x="32" y="52" width="18" height="52" rx="8" fill="{{accent2}}"/><rect x="130" y="52" width="18" height="52" rx="8" fill="{{accent2}}"/><rect x="46" y="102" width="10" height="18" rx="4" fill="#3a3a3a"/><rect x="124" y="102" width="10" height="18" rx="4" fill="#3a3a3a"/>""",
        "stool" => $$"""<path d="M58 40 Q90 26 122 40 L116 52 Q90 42 64 52 Z" fill="url(#g{{idx}})"/><path d="M68 52 L60 130" stroke="#3a3a3a" stroke-width="7" stroke-linecap="round"/><path d="M112 52 L120 130" stroke="#3a3a3a" stroke-width="7" stroke-linecap="round"/><path d="M90 50 L90 130" stroke="{{accent2}}" stroke-width="7" stroke-linecap="round"/>""",
        "mirror" => $$"""<circle cx="90" cy="72" r="50" fill="url(#h{{idx}})" stroke="url(#g{{idx}})" stroke-width="8"/><path d="M66 52 Q80 34 102 40" stroke="#fff" stroke-width="5" fill="none" stroke-linecap="round" opacity="0.7"/><path d="M74 132 L106 132 L98 148 L82 148 Z" fill="{{accent2}}"/>""",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown catalog art kind"),
    };
    return $"<svg width=\"216\" height=\"186\" viewBox=\"-18 -18 216 186\" xmlns=\"http://www.w3.org/2000/svg\">" +
        $"{defs}<rect x=\"-18\" y=\"-18\" width=\"216\" height=\"186\" fill=\"url(#h{idx})\"/>{shape}</svg>";
}

(string Name, string Kind, string Tagline, string Price, string Accent, string AccentDeep, string Material, string Dimensions, string Weight, string Finish)[] catalogItems =
[
    ("Blue Ridge Lounge Chair", "chair", "A low-slung chair for long evenings", "1,240", "#c97b4a", "#8a5a3b", "Steam-bent white oak, wool boucl&eacute;", "27 &times; 29 &times; 32 in", "31 lb", "Natural oil"),
    ("Savannah Dining Table", "table", "Six seats, one continuous grain", "2,890", "#7d8c6f", "#55614b", "Solid ash, brass levelers", "87 &times; 37 &times; 29 in", "137 lb", "Matte lacquer"),
    ("Firefly Pendant Lamp", "lamp", "Warm light, folded like paper", "480", "#d9a441", "#a8762a", "Spun aluminum, oak stem", "&Oslash; 18 &times; 15 in", "4.6 lb", "Powder coat"),
    ("Piedmont Bookshelf", "shelf", "Open storage that breathes", "1,680", "#5f7a8c", "#3e5260", "Birch ply, steel frame", "35 &times; 13 &times; 75 in", "84 lb", "Soap finish"),
    ("Oconee Writing Desk", "desk", "A quiet place to think", "1,450", "#8c6f7d", "#5f4a55", "Walnut, leather inlay", "51 &times; 26 &times; 30 in", "64 lb", "Hard wax oil"),
    ("Sweetwater Two-Seat Sofa", "sofa", "Deep seats, honest stitching", "3,420", "#a8582f", "#753d20", "Kiln-dried beech, linen", "66 &times; 35 &times; 31 in", "106 lb", "Removable covers"),
    ("Tybee Counter Stool", "stool", "Three legs, perfect balance", "390", "#6f8c85", "#4a615c", "Turned oak, cork seat", "&Oslash; 14 &times; 26 in", "11 lb", "Natural oil"),
    ("Magnolia Wall Mirror", "mirror", "Morning light, doubled", "720", "#b08d57", "#7d6238", "Brass ring, float glass", "&Oslash; 31 &times; 1.5 in", "20 lb", "Brushed brass"),
];

var catalogSections = string.Join("\n", catalogItems.Select((item, index) =>
{
    var n = index + 1;
    var art = CatalogItemArt(item.Accent, item.AccentDeep, item.Kind, n);
    var nameWords = item.Name.Split(' ');
    var family = string.Join(' ', nameWords[..Math.Max(1, nameWords.Length - 2)]);
    return $$"""
        <section class="item" style="--accent: {{item.Accent}}; --accent-deep: {{item.AccentDeep}};">
          <div class="brand-band"><span class="wordmark">AURORA &amp; PINE</span><span class="rule"></span><span class="season-tag">F/W 2026</span></div>
          <p class="item-no">No. {{n:00}} <span>/ 08</span></p>
          <h2>{{item.Name}}</h2>
          <p class="tagline">{{item.Tagline}}</p>
          <div class="item-body">
            <div class="art-panel">{{art}}</div>
            <div class="item-info">
              <p class="desc">Each {{family}} piece is bench-made in our Savannah workshop from Georgia
              longleaf pine and certified Appalachian hardwoods, assembled without visible fasteners and finished
              by hand. Designed to be repaired rather than replaced, it carries a 25-year structural guarantee.</p>
              <table class="specs">
                <tr><th>Material</th><td>{{item.Material}}</td></tr>
                <tr><th>Dimensions</th><td>{{item.Dimensions}}</td></tr>
                <tr><th>Weight</th><td>{{item.Weight}}</td></tr>
                <tr><th>Finish</th><td>{{item.Finish}}</td></tr>
              </table>
              <ul class="features">
                <li>Bench-made in Savannah, Georgia</li>
                <li>FSC-certified timber only</li>
                <li>Ships flat, assembles without tools</li>
              </ul>
            </div>
          </div>
          <div class="price-row">
            <div class="price-ring"><div class="price-inner">${{item.Price}}</div></div>
            <p class="order-note">Order code AP-{{100 + n * 7}} &middot; lead time 6 weeks &middot; aurorapine.com/no{{n:00}}</p>
          </div>
        </section>
        """;
}));

var catalogHtml = $$"""
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: letter portrait;
      margin: 0.95in 0.7in 0.85in 0.7in;
    }
    /* Book-style running furniture, mirrored between left- and right-hand pages:
       the product name sits at the top on the matching (outside) side, aligned to
       that edge; the company logo (a real image in a margin box) sits at top-center,
       centered within the box by the margin box's own default alignment (no explicit
       box width needed); the folio sits in the inside (gutter) bottom corner */
    @page :right {
      @top-right { content: string(item-name); font: italic 8.5pt Georgia, serif; color: #666; }
      @top-center { content: url("{{catalogLogoDataUri}}"); }
      @bottom-left { content: counter(page); font: 9pt Georgia, serif; color: #8a5a3b; }
    }
    @page :left {
      @top-left { content: string(item-name); font: italic 8.5pt Georgia, serif; color: #666; }
      @top-center { content: url("{{catalogLogoDataUri}}"); }
      @bottom-right { content: counter(page); font: 9pt Georgia, serif; color: #8a5a3b; }
    }
    @page :first {
      margin: 0;
      @top-left { content: none; }
      @top-center { content: none; }
      @top-right { content: none; }
      @bottom-left { content: none; }
    }
    @page colophon {
      @top-left { content: none; }
      @top-center { content: none; }
      @top-right { content: none; }
    }
    body { font: 10pt Georgia, serif; color: #2b2620; margin: 0; }

    /* ── Cover: true four-edge full-bleed plate via @page :first { margin: 0 }.
          The margin-0 first page's content band is the whole 8.5in × 11in sheet
          (per-page top/bottom margins are layout-affecting), so the plate simply
          sizes to the sheet: width 8.5in overflows the base layout width to the
          physical right edge, and height 11in fills page 1's own band exactly -
          ending flush on the pagination boundary, which the exact-boundary
          forced-break rule treats as already-satisfied (no blank page 2). ── */
    .cover {
      width: 8.5in;
      height: 11in;
      background:
        radial-gradient(circle at 30% 20%, rgba(240, 201, 117, 0.35), transparent 55%),
        radial-gradient(circle at 80% 85%, rgba(201, 123, 74, 0.4), transparent 60%),
        linear-gradient(160deg in oklch, #221a12, #3d2c1c 55%, #59402a);
      color: #f4e9d8; text-align: center;
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8mm;
      page-break-after: always;
    }
    .cover h1 { font-size: 34pt; margin: 0; letter-spacing: 8pt; word-spacing: 12pt; text-transform: uppercase; font-weight: normal; }
    .cover .amp { color: #f0c975; }
    .cover .strap { font: 9.5pt Arial; letter-spacing: 4pt; word-spacing: 8pt; text-transform: uppercase; color: rgba(244,233,216,0.7); margin: 0; }
    .cover .season {
      font: bold 10pt Arial; letter-spacing: 2pt; word-spacing: 5pt; text-transform: uppercase; color: #221a12;
      background: linear-gradient(100deg, #f0c975, #d9834f); border-radius: 20pt; padding: 6pt 18pt;
    }
    .cover .edition { font-style: italic; color: rgba(244,233,216,0.65); margin: 0; }
    .cover .cover-mark { text-align: center; }

    /* ── Per-page brand band: in-flow wordmark strip at the top of each item
          page's content (the graphical logo itself is a margin-box image above) ── */
    .brand-band { display: flex; align-items: center; gap: 6pt; margin-bottom: 7mm; }
    .brand-band .wordmark {
      font: bold 8.5pt Georgia, serif; letter-spacing: 2.5pt; word-spacing: 5pt; color: #8a5a3b;
    }
    .brand-band .rule {
      flex: 1 1 0; height: 1.5pt;
      background: repeating-linear-gradient(90deg, #d9a441 0 8pt, transparent 8pt 13pt);
    }
    .brand-band .season-tag { font: italic 8pt Georgia, serif; color: #a0937f; }

    /* ── Item pages ── */
    .item { page-break-after: always; }
    .item h2 { font-size: 24pt; font-weight: normal; margin: 0 0 2pt; color: var(--accent-deep); string-set: item-name content(); }
    .item-no { font: bold 8pt Arial; letter-spacing: 2.5pt; word-spacing: 4pt; color: var(--accent); margin: 0 0 6pt; text-transform: uppercase; }
    .item-no span { color: #b5a894; }
    .tagline { font-style: italic; color: #6d6154; margin: 0 0 8mm; font-size: 11.5pt; }
    .item-body { display: flex; gap: 8mm; align-items: flex-start; }
    /* NOTE: no padding/border here — combined with border-radius they trigger a
       renderer bug that drops SVG gradient strokes (see the filed issue); the
       breathing room lives in the SVG viewBox instead */
    .art-panel {
      flex: 0 0 auto; border-radius: 10pt;
      background: linear-gradient(165deg, #faf6ef, #efe6d8);
    }
    .item-info { flex: 1 1 0; }
    .desc { margin: 0 0 6mm; line-height: 1.65; }
    table.specs { border-collapse: collapse; width: 100%; margin-bottom: 6mm; font-size: 9pt; }
    table.specs th {
      text-align: left; font: bold 7.5pt Arial; text-transform: uppercase; letter-spacing: 1.2pt;
      color: var(--accent-deep); padding: 4pt 8pt 4pt 0; border-bottom: 0.75pt solid #e0d4c2; width: 30mm;
    }
    table.specs td { padding: 4pt 0; border-bottom: 0.75pt solid #e0d4c2; }
    ul.features { margin: 0; padding: 0 0 0 14pt; }
    ul.features li { margin-bottom: 3pt; }
    ul.features li::marker { content: "\25C6  "; color: var(--accent); }
    .price-row { display: flex; align-items: center; gap: 7mm; margin-top: 9mm; }
    .price-ring {
      width: 34mm; height: 34mm; border-radius: 50%;
      background: conic-gradient(var(--accent), var(--accent-deep), var(--accent));
      display: flex; align-items: center; justify-content: center;
    }
    .price-inner {
      width: 28mm; height: 28mm; border-radius: 50%; background: #fff;
      display: flex; align-items: center; justify-content: center;
      font: bold 13pt Arial; color: var(--accent-deep);
    }
    .order-note { font: 8.5pt Arial; color: #8c8272; margin: 0; letter-spacing: 0.4pt; word-spacing: 1.5pt; }

    /* ── Colophon ── */
    .colophon {
      page: colophon; text-align: center; padding-top: 60mm;
    }
    .colophon .mark { margin-bottom: 8mm; }
    .colophon h2 { font-size: 13pt; font-weight: normal; letter-spacing: 5pt; word-spacing: 9pt; text-transform: uppercase; margin: 0 0 10mm; color: #59402a; }
    .colophon p { font-size: 8.5pt; color: #6d6154; line-height: 1.9; margin: 0 auto 6mm; width: 110mm; }
    .colophon .fine { font-size: 7.5pt; color: #a0937f; }
    </style>
    </head>
    <body>

    <div class="cover">
      <div class="cover-mark">{{coverLogoSvg}}</div>
      <h1>Aurora <span class="amp">&amp;</span> Pine</h1>
      <p class="strap">Handcrafted Furniture &middot; Savannah, Georgia</p>
      <p class="season">Fall / Winter 2026 Collection</p>
      <p class="edition">Catalog No. 14 &mdash; eight pieces, made to last a lifetime</p>
    </div>

    {{catalogSections}}

    <div class="colophon">
      <div class="mark">{{catalogLogoSvg}}</div>
      <h2>Aurora &amp; Pine</h2>
      <p>All pieces designed and bench-made at our workshop at 212 East Broad Street,
      Savannah, Georgia. Timber sourced from certified Georgia and Appalachian forests.
      Textiles woven in the Carolinas; brass fittings cast in Macon.</p>
      <p>&copy; 2026 Aurora &amp; Pine Co. All rights reserved. No part of this catalog may be
      reproduced without written permission. Prices in U.S. dollars, valid through
      February 28, 2027; applicable sales tax added at order.</p>
      <p class="fine">Catalog No. 14 &middot; Printed October 2026 on 100% recycled stock &middot;
      Proudly set in Georgia (the typeface) in Georgia (the state) &middot; aurorapine.com</p>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("print_catalog", "Real-World Documents", "Print Catalog",
    "A ten-page furniture catalog designed as a book: true four-edge full-bleed gradient cover via @page :first { margin: 0 }, per-item SVG art, string-set running headers, a margin-box image logo, and mirrored gutter folios via :left/:right page selectors.",
    catalogHtml, new PdfGenerateConfig { PageSize = PageSize.Letter });

// @font-face unicode-range: font matching is per-character. The monospaced webfont is declared only
// for the digit range (U+0030-0039), so digits render in it while letters in the same run fall back to
// the serif family - a per-character split within one text run, honoring the unicode-range descriptor.
var monoDigitsFontUri = "data:font/woff;base64," +
    Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LiberationMono-Regular.woff")));
var unicodeRangeHtml = $$"""
    <html>
    <head>
    <style>
        @font-face {
            font-family: 'MonoDigits';
            src: url('{{monoDigitsFontUri}}') format('woff');
            unicode-range: U+0030-0039;
        }
        body { font-family: serif; margin: 40px; }
        h1 { font-size: 20pt; }
        .demo { font-family: 'MonoDigits', serif; font-size: 32pt; }
        .note { color: #666; font-size: 11pt; }
    </style>
    </head>
    <body>
        <h1>@font-face unicode-range</h1>
        <p class="demo">Invoice 2024-00731 - Total 1,299</p>
        <p class="note">The digits come from the monospaced webfont (unicode-range: U+0030-0039);
        every letter falls back to serif within the same run - per-character font matching.</p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("unicode_range", "Typography & Text", "@font-face unicode-range",
    "Per-character font matching: a monospaced webfont declared only for the digit range (U+0030-0039) supplies the digits, while letters in the same text run fall back to serif - each character resolved to the family whose unicode-range (or glyph coverage) covers it.",
    unicodeRangeHtml, pdfConfig);

// Last-resort system-fallback font matching (issue #172): when NO declared family covers a character,
// PeachPDF now searches every OTHER font registered with the document before giving up to a
// missing-glyph box - the final step of the CSS Fonts 4 matching algorithm. The body here declares only
// 'serif' (no Arabic coverage), and nothing ever lists 'RegisteredArabic' in a font-family value either -
// it is only registered via @font-face - yet the Arabic word still renders correctly.
var systemFallbackArabicUri = "data:font/truetype;base64," +
    Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansArabicSubset.ttf")));
var systemFallbackHtml = $$"""
    <html>
    <head>
    <style>
        @font-face {
            font-family: 'RegisteredArabic';
            src: url('{{systemFallbackArabicUri}}') format('truetype');
        }
        body { font-family: serif; margin: 40px; }
        h1 { font-size: 20pt; }
        .demo { font-size: 26pt; line-height: 1.6; }
        .note { color: #666; font-size: 11pt; }
    </style>
    </head>
    <body>
        <h1>Last-resort system font fallback</h1>
        <p class="demo">House &mdash; بيت</p>
        <p class="note"><code>body</code> declares only <code>font-family: serif</code>, which has no
        Arabic glyphs, and "RegisteredArabic" is never referenced by any font-family value - it is
        registered on the page purely via @font-face. Because no declared family covers "&#1576;&#1610;&#1578;",
        PeachPDF searches every OTHER font it knows about and finds it there, instead of drawing
        missing-glyph boxes.</p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("system_fallback_font", "Typography & Text", "Last-Resort System Font Fallback",
    "The final step of the CSS Fonts 4 matching algorithm: when no font in the declared font-family stack covers a character, PeachPDF searches every OTHER font registered with the document - here a Noto Sans Arabic subset registered but never referenced in any font-family - instead of drawing a missing-glyph box.",
    systemFallbackHtml, pdfConfig);

// Emoji / astral (supplementary-plane, codepoint > U+FFFF) rendering. Nearly all emoji live above
// U+FFFF and are reached through the font's cmap format-12 subtable; this showcase deliberately uses
// a monochrome subset of Noto Emoji to demonstrate ordinary glyf text. The separate color-font
// showcase below demonstrates supported COLR/CPAL rendering.
var emojiFontUri = "data:font/truetype;base64," +
    Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoEmoji-Regular.ttf")));
var emojiRow = string.Join(" ", new[] { 0x1F600, 0x1F60A, 0x1F602, 0x1F44D, 0x1F389, 0x1F680, 0x2764 }
    .Select(char.ConvertFromUtf32));
var wrappedEmojiRow = string.Concat(Enumerable.Repeat(emojiRow.Replace(" ", ""), 3));
var grinning = char.ConvertFromUtf32(0x1F600);
var emojiHtml = $$"""
    <html>
    <head>
    <style>
        @font-face {
            font-family: 'Emoji';
            src: url('{{emojiFontUri}}') format('truetype');
        }
        body { font-family: serif; margin: 40px; }
        h1 { font-size: 20pt; }
        .demo { font-family: 'Emoji'; font-size: 40pt; line-height: 1.4; }
        .wrap { box-sizing: border-box; width: 330px; padding: 8px; border: 1px solid #bbb;
                font-size: 18px; font-weight: bold; overflow-wrap: anywhere; white-space: pre-wrap; }
        .wrap .emoji-run { font-family: 'Emoji'; font-size: 28px; font-weight: normal; }
        .note { color: #666; font-size: 11pt; }
    </style>
    </head>
    <body>
        <h1>Emoji (astral codepoints)</h1>
        <p class="demo">{{emojiRow}}</p>
        <p class="note">Every glyph above U+FFFF (e.g. {{grinning}} = U+1F600) is resolved through the
        font's cmap format-12 subtable and rendered from this font's monochrome outline. The separate
        Color Fonts showcase demonstrates COLR/CPAL color emoji.</p>
        <h1>Grapheme-aware wrapping</h1>
        <div class="wrap">Northline Office B.V. <span class="emoji-run">{{wrappedEmojiRow}}</span> following-unbreakable-token</div>
        <p class="note">Adjacent emoji wrap between complete grapheme clusters without requiring spaces.
        Inline markup does not itself create a break, and the authored space before the final token takes
        priority over <code>overflow-wrap</code>'s emergency opportunities.</p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("emoji", "Typography & Text", "Emoji (astral codepoints)",
    "Supplementary-plane glyph rendering and grapheme-aware wrapping: adjacent emoji resolve through a bundled Noto Emoji subset and wrap as complete clusters; the separate Color Fonts showcase covers COLR/CPAL.",
    emojiHtml, pdfConfig);

// clip-path with CSS basic shapes (polygon/inset/circle/ellipse/path/url), a <geometry-box>
// reference-box keyword, and inset()'s round <border-radius>. The shape is parsed once by the
// shared BasicShapeGrammar and resolved - against the element's border-box by default, or another
// box a <geometry-box> keyword selects - then pushed as a PDF clip region around the whole element
// rendering (background + content). path() reuses the same SVG path-data parser <path d="..."> uses
// (SvgPathDataParser), so its heart shape below exercises cubic Beziers through the identical
// parse-once-resolve-at-paint-time pipeline as the other shapes; url() reuses the same clipPath
// shape-to-path geometry an inline <svg>'s own clip-path: url(#id) uses (SvgRenderer.BuildClipPath),
// referencing a <clipPath> defined in a hidden, defs-only <svg> elsewhere in this same document.
var clipPathHtml = """
    <html>
    <head>
    <style>
        body { font-family: sans-serif; margin: 40px; }
        h1 { font-size: 20pt; }
        .row { display: flex; gap: 24px; margin-top: 16px; }
        .cell { text-align: center; font-size: 10pt; color: #555; }
        .shape { width: 130px; height: 130px; margin-bottom: 6px; box-sizing: border-box; }
        .poly { clip-path: polygon(50% 0, 100% 38%, 82% 100%, 18% 100%, 0 38%);
                background: linear-gradient(135deg, #ff6b6b, #c92a2a); }
        .inset { clip-path: inset(12% 8% round 14px); background: linear-gradient(135deg, #4dabf7, #1971c2); }
        .circ { clip-path: circle(50% at center); background: linear-gradient(135deg, #69db7c, #2f9e44); }
        .ell { clip-path: ellipse(50% 34% at center); background: linear-gradient(135deg, #da77f2, #9c36b5); }
        .path { clip-path: path("M65 20 C46 0 0 0 0 46 C0 79 33 105 65 130 C97 105 130 79 130 46 C130 0 84 0 65 20 Z");
                background: linear-gradient(135deg, #ffa94d, #e8590c); }
        .box { clip-path: circle(50%) padding-box; border: 10px solid #495057;
               background: linear-gradient(135deg, #ffd43b, #f08c00); }
        .url { clip-path: url(#showcaseStarClip); background: linear-gradient(135deg, #63e6be, #0ca678); }
    </style>
    </head>
    <body>
        <h1>clip-path basic shapes</h1>
        <p>A single <code>clip-path</code> value clips the whole element (here a gradient fill) to a
        <code>polygon()</code>, <code>inset()</code>, <code>circle()</code>, <code>ellipse()</code>,
        <code>path()</code>, or <code>url(#id)</code>, resolved against the border-box by default or
        another box a <code>&lt;geometry-box&gt;</code> keyword selects. <code>path()</code> takes a
        string of SVG path data, mapped 1 unit = 1px onto the resolved reference box's top-left
        corner.</p>
        <svg style="display:none">
            <clipPath id="showcaseStarClip">
                <polygon points="65,10 79,48 120,48 87,72 100,112 65,88 30,112 43,72 10,48 51,48" />
            </clipPath>
        </svg>
        <div class="row">
            <div class="cell"><div class="shape poly"></div>polygon()</div>
            <div class="cell"><div class="shape inset"></div>inset() round</div>
            <div class="cell"><div class="shape circ"></div>circle()</div>
            <div class="cell"><div class="shape ell"></div>ellipse()</div>
            <div class="cell"><div class="shape path"></div>path()</div>
            <div class="cell"><div class="shape box"></div>circle() padding-box</div>
            <div class="cell"><div class="shape url"></div>url(#id)</div>
        </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("clip_path", "Backgrounds & Borders", "clip-path basic shapes",
    "CSS clip-path with basic shapes: polygon(), inset() (including a round <border-radius>), circle(), ellipse(), path(), and url(#id) clip an element (here a gradient fill) to a shape resolved against its border-box by default, or another box a <geometry-box> keyword (e.g. padding-box) selects. A shared basic-shape grammar validates the value at parse time (path() by parsing its SVG path-data string with the same parser <path d=\"...\"> uses; url() by referencing a <clipPath> defined in a hidden, defs-only <svg> elsewhere in the document) and the resolved region is pushed as a PDF clip path.",
    clipPathHtml, pdfConfig);

// ─── legacy clip: rect() showcase ────────────────────────────────────────────

var clipRectHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    body { font: 12pt Arial, sans-serif; margin: 20px; color: #212529; }
    h2 { font-size: 13pt; margin: 24px 0 6px; }
    p.intro { font-size: 9pt; color: #555; }
    .stage { position: relative; width: 160px; height: 120px; }
    .swatch { position: absolute; top: 0; left: 0; width: 160px; height: 120px;
              background: linear-gradient(135deg, #e8590c, #1971c2); color: #fff;
              display: flex; align-items: center; justify-content: center; font-size: 10pt; }
    </style>
    </head>
    <body>

    <h2>1 — clip: rect(top, right, bottom, left)</h2>
    <p class="intro">Only the region inside the rect is painted; top/bottom are offsets from the top
    edge, right/left are offsets from the left edge (not a width/height pair).</p>
    <div class="stage">
      <div class="swatch" style="clip: rect(20px, 130px, 90px, 30px)">full box, mostly clipped away</div>
    </div>

    <h2>2 — auto on some edges leaves that side unclipped</h2>
    <p class="intro"><code>clip: rect(auto, 130px, auto, 30px)</code> - top and bottom keep the box's own
    edges; only left/right are cropped.</p>
    <div class="stage">
      <div class="swatch" style="clip: rect(auto, 130px, auto, 30px)">top/bottom uncropped</div>
    </div>

    <h2>3 — position: static ignores clip entirely</h2>
    <p class="intro">Per CSS 2.1 §11.1.2, legacy <code>clip</code> only applies to absolutely/fixed
    positioned elements - the same rect() value here has no effect at all.</p>
    <div class="stage">
      <div class="swatch" style="position: static; clip: rect(20px, 130px, 90px, 30px)">not clipped (position: static)</div>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("clip_rect", "Backgrounds & Borders", "Legacy clip: rect()",
    "The legacy CSS 2.1 clip property (rect(top, right, bottom, left), superseded by clip-path) clipping " +
    "an absolutely positioned element to a rectangle, including auto edges and its position:static no-op.",
    clipRectHtml, pdfConfig);

// aspect-ratio: sizes the axis whose length is otherwise automatic (CSS Box Sizing 4 §5), in either
// direction and on both normal boxes and replaced elements (a 2:1 image below).
const string AspectRatioSvg2x1 =
    "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='200' height='100' viewBox='0 0 200 100' preserveAspectRatio='none'%3E%3Crect width='200' height='100' fill='%231971c2'/%3E%3Crect x='3' y='3' width='194' height='94' fill='none' stroke='%23fff' stroke-width='4'/%3E%3C/svg%3E";

var aspectRatioHtml = $$"""
    <html>
    <head>
    <style>
        body { font-family: sans-serif; margin: 40px; color: #212529; }
        h1 { font-size: 20pt; }
        h2 { font-size: 13pt; margin: 26px 0 6px; border-bottom: 1px solid #adb5bd; padding-bottom: 3px; }
        p { margin: 4px 0; }
        .row { display: flex; gap: 20px; align-items: flex-start; margin-top: 12px; }
        .cell { text-align: center; font-size: 9pt; color: #555; }
        .box { width: 130px; background: #e9ecef; border: 1px solid #adb5bd; color: #495057;
               font-size: 11pt; display: flex; align-items: center; justify-content: center; }
        .r-16-9 { aspect-ratio: 16 / 9; }
        .r-1-1 { aspect-ratio: 1; }
        .r-3-4 { aspect-ratio: 3 / 4; }
        .fill { width: 130px; aspect-ratio: 2; background: #dee2e6; }
        .fill > .bar { height: 60%; width: 40px; background: #1971c2; margin: 0 auto; }
        .rel { position: relative; width: 320px; height: 96px; background: #f1f3f5; border: 1px solid #ced4da; }
        .abs { position: absolute; top: 8px; left: 8px; height: 64px; aspect-ratio: 3 / 1;
               background: #e8590c; color: #fff; display: flex; align-items: center; justify-content: center; font-size: 10pt; }
        .stretch-parent { width: 320px; border: 1px dashed #ced4da; padding: 8px; margin-top: 10px; }
        .stretch { height: 40px; aspect-ratio: 3 / 1; background: #2b8a3e; color: #fff;
                   display: flex; align-items: center; justify-content: center; font-size: 10pt; }
        .auto-parent { width: 300px; border: 1px dashed #ced4da; padding: 10px; }
        .pct { width: 200px; height: 50%; aspect-ratio: 4 / 1; background: #7048e8; color: #fff;
               display: flex; align-items: center; justify-content: center; font-size: 10pt; }
        .lbl { margin-top: 4px; }
    </style>
    </head>
    <body>
        <h1>aspect-ratio</h1>

        <h2>Definite width &rarr; height</h2>
        <p>Each box is 130px wide with an auto height taken from its <code>aspect-ratio</code>.</p>
        <div class="row">
            <div class="cell"><div class="box r-16-9">16 / 9</div>16 / 9</div>
            <div class="cell"><div class="box r-1-1">1 / 1</div>1 / 1</div>
            <div class="cell"><div class="box r-3-4">3 / 4</div>3 / 4</div>
        </div>
        <p style="margin-top:14px">The ratio-derived height is definite, so a percentage-height child
        resolves against it — the blue bar is <code>height: 60%</code> of the <code>aspect-ratio: 2</code> box:</p>
        <div class="fill"><div class="bar"></div></div>

        <h2>Definite height &rarr; width</h2>
        <p>An absolutely-positioned box (<code>height: 64px; aspect-ratio: 3 / 1</code>) takes its width
        (192px) from the height. A normal-flow block instead fills its container — stretch-fit wins over the ratio.</p>
        <div class="row">
            <div class="rel"><div class="abs">abspos &rarr; 192px wide</div></div>
        </div>
        <div class="stretch-parent"><div class="stretch">normal block fills 320px (not 120px)</div></div>

        <h2>Replaced elements</h2>
        <p>A 2:1 image at <code>width: 130px</code>: a bare <code>&lt;ratio&gt;</code> overrides its natural
        ratio, <code>auto &lt;ratio&gt;</code> keeps the natural one, and no value uses the natural 2:1.</p>
        <div class="row">
            <div class="cell"><img src="{{AspectRatioSvg2x1}}" style="width:130px; aspect-ratio: 1 / 1"><div class="lbl">1 / 1 (square)</div></div>
            <div class="cell"><img src="{{AspectRatioSvg2x1}}" style="width:130px; aspect-ratio: auto 1 / 1"><div class="lbl">auto 1 / 1 (natural)</div></div>
            <div class="cell"><img src="{{AspectRatioSvg2x1}}" style="width:130px"><div class="lbl">no ratio (natural 2:1)</div></div>
        </div>

        <h2>Indefinite percentage height</h2>
        <p>Inside an auto-height parent, <code>height: 50%</code> is indefinite, so the ratio sizes the
        height (200px wide, <code>aspect-ratio: 4 / 1</code> &rarr; 50px tall) instead of collapsing to auto.</p>
        <div class="auto-parent"><div class="pct">width 200px, ratio 4/1</div></div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("aspect_ratio", "Layout", "aspect-ratio",
    "CSS aspect-ratio in both directions: width→height (with a percentage-height child resolving against the ratio-derived height) and height→width for shrink-to-fit boxes, replaced-element natural ratio vs. a specified/auto ratio, and indefinite-percentage-height sizing.",
    aspectRatioHtml, pdfConfig);

// ── Charts.css showcase ─────────────────────────────────────────────────────
// Renders charts with the pure-CSS Charts.css framework (https://chartscss.org, MIT, v1.2.0).
// The full official stylesheet is embedded verbatim (its MIT header kept intact); the five
// data-drawing chart types (column, bar, area, line, pie) plus a multi-series example and the
// documented customizations (extruded 3D bars via stacked box-shadows, a skewY tilt, and
// shadowed bars via inset+outset box-shadows) are authored with real charts.css markup. This one document exercises @property (the
// whole palette + typed descriptors), aspect-ratio (every chart takes its height from a
// ratio-sized tbody), clip-path (area/line fills and the a11y label hiding), box-shadow (the
// extruded 3D bars), CSS logical properties (axes, spacing), conic/linear gradients, transforms,
// and flexbox — all together, at once.
var chartsCss = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "charts.css"));

// A charts.css <td>: the normalized value goes in inline custom properties (0..1), the human
// label in a <span class="data">. --size drives column/bar/area/line; --start/--end drive pie.
string ColRow(string label, int pct, string value) =>
    $"""<tr><th scope="row">{label}</th><td style="--size:calc({pct}/100)"><span class="data">{value}</span></td></tr>""";

string PieRow(double start, double end, string value) =>
    $"""<tr><td style="--start:{start.ToString("0.####", CultureInfo.InvariantCulture)}; --end:{end.ToString("0.####", CultureInfo.InvariantCulture)}"><span class="data">{value}</span></td></tr>""";

// Multi-series row: two <td>s per row, each a dataset column, coloured by :nth-of-type.
string MultiRow(string label, int a, int b) =>
    $"""<tr><th scope="row">{label}</th><td style="--size:calc({a}/100)"><span class="data">{a}</span></td><td style="--size:calc({b}/100)"><span class="data">{b}</span></td></tr>""";

// Area/line rows: each cell's segment rises from the previous point (--start, left edge) to this
// point (--end, right edge), so consecutive cells share a boundary and the points connect into a
// sloped area/line rather than flat steps. The first cell is flat (starts at its own value).
static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
string ConnectedRows(string[] labels, int[] vals, string suffix)
{
    var rows = new StringBuilder();
    for (var i = 0; i < vals.Length; i++)
    {
        var start = (i > 0 ? vals[i - 1] : vals[i]) / 100.0;
        var end = vals[i] / 100.0;
        rows.Append($"""<tr><th scope="row">{labels[i]}</th><td style="--start:{F(start)}; --end:{F(end)}"><span class="data">{vals[i]}{suffix}</span></td></tr>""");
    }
    return rows.ToString();
}

var columnChart = $"""
    <table class="charts-css column show-heading show-labels show-primary-axis show-4-secondary-axes data-spacing-10">
      <caption>Quarterly Revenue — 2024 ($M)</caption>
      <thead><tr><th scope="col">Quarter</th><th scope="col">Revenue</th></tr></thead>
      <tbody>
        {ColRow("Q1", 42, "$42M")}
        {ColRow("Q2", 63, "$63M")}
        {ColRow("Q3", 55, "$55M")}
        {ColRow("Q4", 91, "$91M")}
      </tbody>
    </table>
    """;

var barChart = $"""
    <table class="charts-css bar show-heading show-labels show-primary-axis data-spacing-5" style="--aspect-ratio: 5 / 2">
      <caption>Language Popularity (% of respondents)</caption>
      <thead><tr><th scope="col">Language</th><th scope="col">Share</th></tr></thead>
      <tbody>
        {ColRow("JavaScript", 95, "95%")}
        {ColRow("Python", 88, "88%")}
        {ColRow("Java", 65, "65%")}
        {ColRow("C#", 60, "60%")}
        {ColRow("Go", 42, "42%")}
      </tbody>
    </table>
    """;

var areaChart = $"""
    <table class="charts-css area show-heading show-labels show-primary-axis show-4-secondary-axes">
      <caption>Monthly Active Users (thousands)</caption>
      <thead><tr><th scope="col">Month</th><th scope="col">Users</th></tr></thead>
      <tbody>
        {ConnectedRows(["Jan", "Feb", "Mar", "Apr", "May", "Jun"], [30, 45, 40, 62, 78, 92], "k")}
      </tbody>
    </table>
    """;

var lineChart = $"""
    <table class="charts-css line show-heading show-labels show-primary-axis show-4-secondary-axes">
      <caption>Response Time p95 (ms, lower is better)</caption>
      <thead><tr><th scope="col">Week</th><th scope="col">p95</th></tr></thead>
      <tbody>
        {ConnectedRows(["W1", "W2", "W3", "W4", "W5", "W6"], [80, 72, 58, 61, 44, 35], "")}
      </tbody>
    </table>
    """;

var pieChart = $"""
    <table class="charts-css pie show-heading show-data-on-hover">
      <caption>Browser Market Share</caption>
      <tbody>
        {PieRow(0.00, 0.35, "35%")}
        {PieRow(0.35, 0.60, "25%")}
        {PieRow(0.60, 0.80, "20%")}
        {PieRow(0.80, 0.92, "12%")}
        {PieRow(0.92, 1.00, "8%")}
      </tbody>
    </table>
    """;

var multiChart = $"""
    <table class="charts-css column multiple show-heading show-labels show-primary-axis show-4-secondary-axes data-spacing-10">
      <caption>Revenue vs. Target by Quarter ($M)</caption>
      <thead><tr><th scope="col">Quarter</th><th scope="col">Actual</th><th scope="col">Target</th></tr></thead>
      <tbody>
        {MultiRow("Q1", 42, 50)}
        {MultiRow("Q2", 63, 55)}
        {MultiRow("Q3", 55, 60)}
        {MultiRow("Q4", 91, 75)}
      </tbody>
    </table>
    """;

// 3D effects — Charts.css documents these as *customization examples* layered on a plain
// .column chart (https://chartscss.org/customization/3d-effects/), not built-in classes.
//   • 3D Bars — a stack of ~10 offset box-shadows extrudes each bar's top/right face.
//   • 3D Tilt — a single skewY() shears the whole chart.
//   • Charts with Shadows — a white panel + each bar get a rounded inset+outset box-shadow.
var barsChart = $"""
    <table class="charts-css column fx-bars data-spacing-10">
      <tbody>
        {ColRow("A", 55, "55")}
        {ColRow("B", 80, "80")}
        {ColRow("C", 48, "48")}
        {ColRow("D", 95, "95")}
      </tbody>
    </table>
    """;

var tiltChart = $"""
    <table class="charts-css column fx-tilt data-spacing-8">
      <tbody>
        {ColRow("A", 55, "55")}
        {ColRow("B", 80, "80")}
        {ColRow("C", 48, "48")}
        {ColRow("D", 95, "95")}
      </tbody>
    </table>
    """;

// "Charts with Shadows" — the official example gives the whole tbody and each bar a rounded,
// inset+outset box-shadow (a raised/pressed look), spacing the bars with margin-inline.
var shadowChart = $"""
    <table class="charts-css column fx-shadow hide-data">
      <tbody>
        {ColRow("A", 40, "40")}
        {ColRow("B", 60, "60")}
        {ColRow("C", 75, "75")}
        {ColRow("D", 55, "55")}
        {ColRow("E", 90, "90")}
      </tbody>
    </table>
    """;

// Page chrome + the three "3D effect" recipes (authored on top of the framework, as the
// Charts.css docs present them). Kept separate from the embedded stylesheet above.
var chartsPageStyles = """
    body { font-family: sans-serif; margin: 34px 40px; color: #212529; }
    h1 { font-size: 22pt; margin: 0 0 4px; }
    h2 { font-size: 15pt; margin: 26px 0 4px; color: #1864ab; }
    .lede { color: #555; font-size: 10.5pt; margin: 0 0 8px; }
    p.note { color: #666; font-size: 9.5pt; margin: 4px 0 14px; }
    .chart { width: 460px; margin: 6px 0 8px; }
    .chart.tall { height: 240px; }
    .chart.square { width: 300px; height: 300px; }
    .charts-css caption { font-weight: 600; font-size: 11pt; padding-bottom: 8px; text-align: center; }
    .charts-css .data { font-size: 8pt; color: #333; }
    .chart.fx { width: 340px; }
    h3.fx-h { font-size: 11pt; margin: 18px 0 2px; color: #333; font-weight: 600; }
    .page-break { break-before: page; }

    /* --- 3D effect recipes (Charts.css customization examples) --- */
    /* 3D Bars: a stack of offset box-shadows extrudes each bar's top-right face. */
    .charts-css.column.fx-bars tbody tr td {
      box-shadow:
        1px -1px 1px lightgrey, 2px -2px 1px lightgrey, 3px -3px 1px lightgrey,
        4px -4px 1px lightgrey, 5px -5px 1px lightgrey, 6px -6px 1px lightgrey,
        7px -7px 1px lightgrey, 8px -8px 1px lightgrey, 9px -9px 1px lightgrey,
        10px -10px 1px lightgrey;
    }
    /* 3D Tilt: a single skew on the whole chart. */
    .charts-css.column.fx-tilt { transform: skewY(-4deg); transform-origin: bottom left; }
    /* Charts with Shadows: a rounded inset+outset box-shadow on the chart area and each bar. */
    .charts-css.column.fx-shadow tbody {
      padding: 30px;
      border-radius: 10px;
      background: #fff;
      box-shadow: inset -5px -5px 10px rgba(0, 0, 0, 0.5), 5px 5px 5px rgba(0, 0, 0, 0.5);
    }
    .charts-css.column.fx-shadow tbody td {
      margin-inline: 10px;
      border-radius: 10px;
      box-shadow: inset -5px -5px 10px rgba(0, 0, 0, 0.5), 5px 5px 5px rgba(0, 0, 0, 0.5);
    }
    """;

var chartsHtml =
    "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>\n" +
    chartsCss + "\n" + chartsPageStyles +
    "</style></head><body>" +
    "<h1>Charts.css</h1>" +
    "<p class=\"lede\">Charts rendered purely with CSS via the <a href=\"https://chartscss.org\">Charts.css</a> " +
    "framework (MIT). A semantic <code>&lt;table&gt;</code> of normalized values becomes a chart through " +
    "<code>@property</code>, <code>aspect-ratio</code>, <code>clip-path</code>, <code>conic-gradient</code>, " +
    "logical properties, and flexbox — no script, no canvas, no images.</p>" +

    "<h2>Column</h2>" +
    "<p class=\"note\">Vertical bars: each <code>td</code> is <code>height: calc(100% * var(--size))</code> " +
    "of a <code>tbody</code> whose height comes from <code>aspect-ratio: 21/9</code>.</p>" +
    $"<div class=\"chart\">{columnChart}</div>" +

    "<h2>Bar</h2>" +
    "<p class=\"note\">Horizontal bars: <code>td</code> is <code>width: calc(100% * var(--size))</code>.</p>" +
    $"<div class=\"chart\" style=\"height:200px\">{barChart}</div>" +

    "<div class=\"page-break\"></div>" +
    "<h2>Area</h2>" +
    "<p class=\"note\">A filled quad per point, drawn by a <code>td::before</code> with " +
    "<code>clip-path: polygon(...)</code> whose vertices are <code>calc()</code> of the data values.</p>" +
    $"<div class=\"chart tall\">{areaChart}</div>" +

    "<h2>Line</h2>" +
    "<p class=\"note\">The same <code>clip-path: polygon()</code> technique, clipped to a thin band " +
    "(<code>--line-size</code>) so only the connecting line shows.</p>" +
    $"<div class=\"chart tall\">{lineChart}</div>" +

    "<div class=\"page-break\"></div>" +
    "<h2>Pie</h2>" +
    "<p class=\"note\">Each slice is a <code>conic-gradient</code> from <code>var(--start)turn</code> to " +
    "<code>var(--end)turn</code>, stacked in a square (<code>aspect-ratio: 1</code>) rounded container.</p>" +
    $"<div class=\"chart square\">{pieChart}</div>" +

    "<h2>Multiple series</h2>" +
    "<p class=\"note\">Two datasets per row (<code>.multiple</code>): each <code>td</code> is coloured from the " +
    "<code>--color-1..10</code> palette by <code>:nth-of-type</code>.</p>" +
    $"<div class=\"chart\">{multiChart}</div>" +

    "<div class=\"page-break\"></div>" +
    "<h2>3D effects</h2>" +
    "<p class=\"note\">Charts.css documents 3D looks as CSS customizations layered on a plain column chart — " +
    "no special classes (see <a href=\"https://chartscss.org/customization/3d-effects/\">chartscss.org</a>).</p>" +
    "<h3 class=\"fx-h\">3D Bars — a stack of offset <code>box-shadow</code>s</h3>" +
    "<p class=\"note\">Ten <code>box-shadow</code> layers, each offset one more pixel up-and-right, build the " +
    "extruded top-right face of every bar.</p>" +
    $"<div class=\"chart fx\">{barsChart}</div>" +
    "<h3 class=\"fx-h\">3D Tilt — a single <code>transform: skewY()</code></h3>" +
    "<p class=\"note\">One skew on the whole chart shears every bar in concert.</p>" +
    $"<div class=\"chart fx\">{tiltChart}</div>" +

    "<h3 class=\"fx-h\">Charts with Shadows — inset + outset <code>box-shadow</code></h3>" +
    "<p class=\"note\">The chart area and each bar get a rounded <code>box-shadow</code> combining an inset " +
    "(inner top-left) and an outset (bottom-right) layer, for a raised, tactile look.</p>" +
    $"<div class=\"chart fx\">{shadowChart}</div>" +

    "</body></html>";

await SaveShowcaseAsync("charts_css", "Charts & Data Viz", "Charts.css",
    "The Charts.css framework (MIT) rendering column, bar, area, line, and pie charts — plus a multi-series " +
    "example and the documented customizations (extruded 3D bars via stacked box-shadows, a skewY tilt, and " +
    "shadowed bars with inset+outset box-shadow) — purely from CSS. The full official stylesheet is embedded, " +
    "exercising @property, aspect-ratio, clip-path, box-shadow, logical properties, and conic/linear gradients together.",
    chartsHtml, pdfConfig);

// ── object-fit / object-position on a replaced <img> ──────────────────────────────────
var objectFitImg = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAGAAAAAwCAIAAABhdOiYAAABrElEQVR4nO2aO04CURSGDxOWYWOnC7CkcQU0IolBGisM4oMoIWgIEgTjI0iwRzBRXINuwwW4D47Flefg/BVzpvi/6s65d5J/vpybzNxMTJNJCSSxnvN05Kl6op6qp6O/gWhsWl9cEPunHsEbbz7XAh7fC7ZDKAhAQQAKAlAQgIIAFASgIAAFASgIEN/cKMH3d+uQq6W8/RPwjcIOAlAQgIIAFASIWweYUn09nb1spZtWSWaJhKB6v+AvXryVROR+txF6nDnst1ijf+QGsXlc8ey9bBdNxFxQ8yUvYzULU5PiybBikGyMpaDb3qGI+NXM4mYLw8uQMvmw32IRx0zQXS8nqH0cbk3+42rlmZbBDgJQEICCABQEMBNUzD6LiCo+bHJrOju1lWdaBjsIYCnoPNsV1ERutp26DimTD+MOKu13RERV/ZomxcdU3SDZGPstVs48uYHO44rmX/OROO6oZNqeam1wPFtspZtRODOPhCBHde9h7j8o6zyOiMSILhQEoCAABQHi3wfF4BWJrs1BTGhsfQ0CZtlBAAoCUBCAggAUBKAgAAUBKAhAQYBfzx35oNDJUOwAAAAASUVORK5CYII=";
static string FitCell(string img, string fit) =>
    "<div class=\"cell\">" +
    $"<div class=\"frame\"><img src=\"{img}\" style=\"width:120px;height:120px;object-fit:{fit}\"></div>" +
    $"<div class=\"lbl\">object-fit: {fit}</div></div>";
static string PosCell(string img, string pos) =>
    "<div class=\"cell\">" +
    $"<div class=\"frame\"><img src=\"{img}\" style=\"width:120px;height:120px;object-fit:cover;object-position:{pos}\"></div>" +
    $"<div class=\"lbl\">object-position: {pos}</div></div>";
var objectFitHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; } h3 { font-size: 14px; margin: 20px 0 10px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 4px; }" +
    ".row { display: flex; flex-wrap: wrap; gap: 14px; }" +
    ".cell { font-size: 11px; color: #555; }" +
    ".frame { width: 120px; height: 120px; border: 1px solid #cbd5e1; border-radius: 6px; overflow: hidden; background: #f1f5f9; }" +
    ".lbl { margin-top: 6px; font-family: monospace; }" +
    "</style></head><body>" +
    "<h2>object-fit &amp; object-position</h2>" +
    "<p class=\"note\">The same 2:1 image placed in a 120&times;120 box. Corner markers (red/green/yellow/white) and the center dot make the fit and crop obvious.</p>" +
    "<h3>object-fit</h3>" +
    "<div class=\"row\">" +
    FitCell(objectFitImg, "fill") + FitCell(objectFitImg, "contain") + FitCell(objectFitImg, "cover") +
    FitCell(objectFitImg, "none") + FitCell(objectFitImg, "scale-down") +
    "</div>" +
    "<h3>object-position (with object-fit: cover)</h3>" +
    "<div class=\"row\">" +
    PosCell(objectFitImg, "left") + PosCell(objectFitImg, "center") + PosCell(objectFitImg, "right") +
    PosCell(objectFitImg, "top") + PosCell(objectFitImg, "bottom") +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("object_fit", "Images & Replaced Content", "object-fit & object-position",
    "The CSS object-fit (fill/contain/cover/none/scale-down) and object-position properties applied to a " +
    "replaced <img>: the same 2:1 image sized/cropped/positioned inside a fixed square box, with corner " +
    "markers making each fit and crop obvious.",
    objectFitHtml, pdfConfig);

// ── CMYK JPEG images: preserved, never converted to RGB ────────────────────────────────
// A real Adobe-authored CMYK JPEG (Adobe APP14 transform=0, inverted-CMYK convention) - copied
// byte-for-byte from PeachImage's own test corpus (tests/corpus/image-rs-jpeg-decoder/tests/reftest/
// images/mozilla/jpg-cmyk-1.jpg), same "reuse a known-good real file" approach PeachPDF.Tests uses for
// this exact fixture (see PeachPDF.Tests/TestSupport/CmykJpegFixture.cs). This showcase deliberately
// carries no embedded ICC profile - PdfACmykImageGuard requires one under PdfAConformance, so this one
// showcase is expected to (correctly) fail the PDF/A sweep below, proving the guard actually fires
// rather than being an oversight.
const string cmykJpegBase64 =
    "/9j/4AAQSkZJRgABAQEASABIAAD/7gAOQWRvYmUAZAAAAAAA/9sAQwABAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB" +
    "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB/8AAFAgAIAAgBEMRAE0RAFkRAEsRAP/EABkAAAIDAQAAAAAAAAAA" +
    "AAAAAAAKBwkLCP/EAEoQAAECAgYDCwUNBwUAAAAAAAECAwQFAAYHERIhCBUxCRMUIiMkMjNBUXIYQ2FigUJERWRlcZOxssHC" +
    "4vAlNHODkaGjUrPR4fH/2gAOBEMATQBZAEsAAD8AedtQszgK7yqJKGEawDS7gEp5xkfR1v2/H0nnbM7UJVXeAYQYloTDAkAY" +
    "xzjijLb1v2/H0s/+jtNpdpczqTM4jnC+A41HNSuQ4x9PV/Y8PRoT0qrBomB1lfBKGHfs96I79mX1dmdJboUijyl/lBP0v5qL" +
    "UaVVm7sDrK+HKcO++5uIGfs7ProUKHlL/KCfpfzUWq0qpSYHWV6cN2+7b9ueff37fvoUmmyWyCeWizWGDUG8ZeXUZhtXOMxm" +
    "Muq+bp+DNR5S/wAoJ+l/NTbzotxYNpVcBiYI6yw3Kbv5buIy2+zu76QtQ0l/hDwu/ipAVt9jEstKkMaWYZsTYMOEJCBzq5J2" +
    "Zdfls872cp1l5VjFt8htKlkMyY1gTYNoCQXEjhWQy2jl+4ed2dZ1hSke0u0uZVJmT/LuGBLi/dnkM/8Ab+x4eiqPp5aN8fIt" +
    "c45etvBwja0Rsxei7/r+lJ9p01o+aONbLZp9BJhZZFLlKn2wFBlZ4XxhmOL1HcfO/wAPpxT5Svx7/LRK/Tyqm7Itc42ijBwi" +
    "+9N2zF3/AK/tcUbT0DdynrHPdTb3ViIXj4PmINRJvw+p/wAd/ZQ8pX49/lptC0zYbN9KrgLsOdZYcJT53IEe0dn/ALRJKjB2" +
    "kv8ACHhd/FQpbTo36eWoo+Xr1zg3tbR/eLthHrX7PZ/anTWjjo+T62atkshUwT6pSuKZSoBtXO71pyOXUd487/D6a+Okt7+/" +
    "nffSundDbPLNYiyCe13rQtqXx0I04wlLbbRcmjy2HVpASpbZS4jAN/e4ycKkqUnfSA8w/Z5uhtkERZqutFd561CR0vabbSll" +
    "xhb00cLS1JASt1GBxJb5Z+5ScKkqUku3B7RV3HrceqhzSocurvXeXCWQcsEGlKUwba3Jg4tsrS00lZbCVJDfKu8dCELSpSSs" +
    "oQ7TRWGsNaWa06lku+PpfcWQCtYTDJCwCSQFXpOLiIyN4IBw3lGXrup1YZRGzqscHJYduHg0vRaEAELcUkKWAVuAC9V23ClC" +
    "fVvpXHpI7s3J4dEwlNQ4qGk0EA60l5mISuOeQd8SC7FXpUMSFYVpZSy0sAEtnbRr6zmyCzyyiVMSmpFW4CUtMNBrhQZbcj3U" +
    "pCk3uRRQlScSVYVJZSy2oAYkEi+nadhNitYK0RUHFTpURFrWpte9qCgwgkpOTeYJBF6SsqUDfcodmxBTM+lOlVwEpOsrsOfW" +
    "57e3Pvz2/wBaYXdkFks1tFnkG0IZ0y8vNg8Q844wyOXVdnr92DpNpaS/wh4XfxUKTdVPTy1E60vXODAUm/hF2zP/AFfr6nBN" +
    "yn0Dde1jqw3qbHjiIMHm995Kker+rs7qL46S3v7+d99Fzd2b0kUQ8niqhymYAQUmhohh5LTt6Ho5aTwp0hLikKuUlLKVpuC2" +
    "mW1EC80mWsO6nTqNlEPJYOsbyYOHbICERaglTiwA4sgLuxG5Kb8uKhPbTRAsgs5lVlFnlW6kSlhphqUwDIit6ASl2PcbQYpw" +
    "4VKSrCpKWUqTcFNsoVcCTTjuxWwmKrRWBU6ioNS1xcQFN42yShgK5MZpBBIJWUnMKUodmWb3pyVhXOZnOXFOY8bj5vvvvvKr" +
    "8/b6b/nzpzTWHTkmc5W4pycuLx333vk337c8R/R7TfSS6X2aNOjTfq/9n5cl5rbszOWz6/m2/wD/2Q==";
var cmykImagesHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 16px; max-width: 640px; }" +
    "img { width: 220px; height: 220px; border: 1px solid #cbd5e1; }" +
    "</style></head><body>" +
    "<h2>CMYK JPEG images</h2>" +
    "<p class=\"note\">A print-ready CMYK JPEG (Adobe's inverted-CMYK convention, no embedded ICC profile) " +
    "embedded via byte-for-byte pass-through - <code>/DeviceCMYK</code> with a <code>/Decode</code> array " +
    "undoing the inversion - rather than naively converted to RGB and re-encoded, which would destroy the " +
    "separations and (without the Decode array) render as a photographic negative.</p>" +
    $"<img src=\"data:image/jpeg;base64,{cmykJpegBase64}\">" +
    "</body></html>";

await SaveShowcaseAsync("cmyk_jpeg", "Images & Replaced Content", "CMYK JPEG Images",
    "A CMYK JPEG embedded via byte-for-byte pass-through (DeviceCMYK, with a Decode array undoing Adobe's " +
    "inverted-CMYK convention) instead of being converted to RGB - preserving the original print " +
    "separations exactly rather than destroying them with a naive, color-management-free conversion.",
    cmykImagesHtml, pdfConfig);

// --- CMYK TIFF showcase (issue #1096) ---
// Unlike the CMYK JPEG showcase above (byte-for-byte pass-through of a real file), TIFF has no PDF-native
// pass-through filter - PeachPDF decodes it natively and embeds the decoded pixel buffer as a raw
// /FlateDecode CMYK stream instead (PdfImage.InitializeCmykRaster), a genuinely new encode path worth its
// own visual check per this repo's rasterize-and-look convention. PeachImage's TIFF codec is decode-only,
// so (like the ICC profile fixtures elsewhere in this file) the source TIFF is hand-built rather than
// round-tripped - see ShowcaseCmykTiffFixture.

var cmykTiffBytes = ShowcaseCmykTiffFixture.BuildCheckerboard(64, 64, 8,
    c1: 0, m1: 255, y1: 255, k1: 0,    // process red
    c2: 255, m2: 0, y2: 255, k2: 0);   // process green
var cmykTiffBase64 = Convert.ToBase64String(cmykTiffBytes);

var cmykTiffHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 16px; max-width: 640px; }" +
    "img { width: 192px; height: 192px; border: 1px solid #cbd5e1; image-rendering: pixelated; }" +
    "</style></head><body>" +
    "<h2>CMYK TIFF images</h2>" +
    "<p class=\"note\">A hand-built CMYK TIFF (an 8x8-block checkerboard of process red and process " +
    "green), decoded natively and embedded as a raw <code>/FlateDecode</code> <code>/DeviceCMYK</code> " +
    "stream - TIFF has no <code>/DCTDecode</code>-equivalent pass-through filter, so (unlike the CMYK " +
    "JPEG showcase above) this is a genuinely new encode path, not a copy of the original file bytes.</p>" +
    $"<img src=\"data:image/tiff;base64,{cmykTiffBase64}\">" +
    "</body></html>";

await SaveShowcaseAsync("cmyk_tiff", "Images & Replaced Content", "CMYK TIFF Images",
    "A CMYK TIFF decoded natively and embedded as a raw FlateDecode DeviceCMYK stream - TIFF has no " +
    "DCTDecode-equivalent PDF pass-through filter, so this is a genuinely new encode path rather than a " +
    "byte-for-byte copy, preserving the source's CMYK separations without ever touching RGB.",
    cmykTiffHtml, pdfConfig);

// ── PNG lossless pass-through (issue #1086) ─────────────────────────────────────────────
// Two opaque PNGs, deliberately the kind of content JPEG re-encoding used to visibly damage: a
// QR-code-style pattern (hard black/white edges - a real QR code would stop scanning if re-encoded
// lossily) and a multi-color flat-fill logo/icon (indexed palette). Both are embedded byte-for-byte via
// /FlateDecode pass-through instead of being decoded and re-encoded as lossy JPEG.
static byte[] BuildQrLikePatternPngBytes(int modules, int scale)
{
    // A deterministic, QR-ish-looking black/white module grid - not a real scannable QR code, just a
    // visual stand-in with the same "hard edges everywhere" property that makes lossy re-encoding
    // visibly wrong for this kind of content. Gray8 (not Rgb24) so this actually demonstrates the
    // DeviceGray pass-through branch, distinct from the indexed-palette logo below - an Rgb24 source
    // here would auto-index under PeachImage's PngColorMode.Auto default instead (only two colors),
    // exercising the same /Indexed path twice rather than two different colorspaces.
    int size = modules * scale;
    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Gray8);
    var pixels = image.GetPixelSpan();
    for (int y = 0; y < size; y++)
    {
        int my = y / scale;
        for (int x = 0; x < size; x++)
        {
            int mx = x / scale;
            bool isFinder = (mx < 7 && my < 7) || (mx >= modules - 7 && my < 7) || (mx < 7 && my >= modules - 7);
            bool dark = isFinder
                ? (mx == 0 || mx == 6 || my == 0 || my == 6 || (mx >= 2 && mx <= 4 && my >= 2 && my <= 4))
                : ((mx * 7 + my * 3) % 5 == 0);
            pixels[y * size + x] = dark ? (byte)0 : (byte)255;
        }
    }

    using var ms = new MemoryStream();
    image.Save(ms, "png");
    return ms.ToArray();
}

static byte[] BuildFlatLogoPngBytes(int width, int height)
{
    var palette = new (byte R, byte G, byte B)[]
    {
        (0xEA, 0x58, 0x0C), // orange
        (0x16, 0xA3, 0x4A), // green
        (0x25, 0x63, 0xEB), // blue
        (0xFF, 0xFF, 0xFF), // white background
    };

    using var image = PeachImage.Image.Create(width, height, PeachImage.PixelFormat.Rgb24);
    var pixels = image.GetPixelSpan();
    int cx = width / 2, cy = height / 2;
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            int dx = x - cx, dy = y - cy;
            int distSq = dx * dx + dy * dy;
            int radius = Math.Min(width, height) / 2;
            var (r, g, b) = distSq > radius * radius
                ? palette[3]
                : palette[((x / (width / 6)) + (y / (height / 6))) % 3];
            int i = (y * width + x) * 3;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
        }
    }

    using var ms = new MemoryStream();
    image.Save(ms, "png");
    return ms.ToArray();
}

// A five-pointed-star icon on a flat background color, the background declared transparent via a
// tRNS chroma-key chunk (PngEncoderOptions.TransparentColor) - no alpha channel at all, just PNG's
// non-alpha transparency convention. Passes through with a PDF color-key /Mask array built from the
// tRNS chunk instead of a separate alpha plane. Drawn over a checkerboard so the transparency is
// visibly doing something, the same "checker" idiom the Modern CSS Colors showcase already uses.
static byte[] BuildTrnsStarIconPngBytes(int size, (byte R, byte G, byte B) background, (byte R, byte G, byte B) foreground)
{
    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Rgb24);
    var pixels = image.GetPixelSpan();
    double cx = size / 2.0, cy = size / 2.0;
    double outerR = size * 0.48, innerR = outerR * 0.42;

    // Point-in-polygon test against a 10-vertex star (5 outer points, 5 inner points).
    var star = new (double X, double Y)[10];
    for (int i = 0; i < 10; i++)
    {
        double angle = -Math.PI / 2 + i * Math.PI / 5;
        double r = i % 2 == 0 ? outerR : innerR;
        star[i] = (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
    }

    bool InsideStar(double px, double py)
    {
        bool inside = false;
        for (int i = 0, j = star.Length - 1; i < star.Length; j = i++)
        {
            var (xi, yi) = star[i];
            var (xj, yj) = star[j];
            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            var (r, g, b) = InsideStar(x + 0.5, y + 0.5) ? foreground : background;
            int i = (y * size + x) * 3;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
        }
    }

    using var ms = new MemoryStream();
    image.Save(ms, "png", new PeachImage.Formats.Png.PngEncoderOptions
    {
        ColorMode = PeachImage.Formats.Png.PngColorMode.Truecolor,
        TransparentColor = background,
    });
    return ms.ToArray();
}

var qrLikeBase64 = Convert.ToBase64String(BuildQrLikePatternPngBytes(21, 6));
var flatLogoBase64 = Convert.ToBase64String(BuildFlatLogoPngBytes(120, 120));
var trnsStarBase64 = Convert.ToBase64String(BuildTrnsStarIconPngBytes(120, (255, 0, 255), (0xF5, 0x9E, 0x0B)));

var pngPassthroughHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 16px; max-width: 640px; }" +
    ".row { display: flex; gap: 24px; align-items: flex-start; }" +
    ".row img { image-rendering: pixelated; border: 1px solid #cbd5e1; }" +
    ".label { font-size: 11px; color: #555; margin-top: 4px; }" +
    ".checker { background: repeating-conic-gradient(#ddd 0% 25%, #fff 0% 50%) 0 / 16px 16px; " +
    "  display: inline-block; border-radius: 4px; }" +
    "</style></head><body>" +
    "<h2>PNG lossless pass-through</h2>" +
    "<p class=\"note\">Each opaque PNG below embeds via byte-for-byte <code>/FlateDecode</code> " +
    "pass-through - the PNG's own compressed pixel data, unchanged - instead of being decoded and " +
    "re-encoded as a lossy JPEG. Every edge stays exactly as sharp as the source, and the embedded " +
    "file is typically smaller too.</p>" +
    "<div class=\"row\">" +
    $"<div><img src=\"data:image/png;base64,{qrLikeBase64}\" width=\"189\" height=\"189\">" +
    "<div class=\"label\">Grayscale, hard-edged pattern</div></div>" +
    $"<div><img src=\"data:image/png;base64,{flatLogoBase64}\" width=\"120\" height=\"120\">" +
    "<div class=\"label\">Indexed-palette flat-fill logo</div></div>" +
    "<div><div class=\"checker\">" +
    $"<img src=\"data:image/png;base64,{trnsStarBase64}\" width=\"120\" height=\"120\" style=\"border:none\">" +
    "</div><div class=\"label\">tRNS chroma-key transparency (PDF color-key /Mask)</div></div>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("png_passthrough", "Images & Replaced Content", "PNG Lossless Pass-through",
    "An opaque PNG (no real per-pixel alpha, not interlaced) embeds byte-for-byte via /FlateDecode " +
    "pass-through - the PNG's own compressed IDAT data, unchanged, with an /Indexed color space for a " +
    "palette source or a color-key /Mask array for tRNS chroma-key transparency - instead of being " +
    "decoded and re-encoded as a lossy JPEG at quality 75.",
    pngPassthroughHtml, pdfConfig);

// ── Modern CSS colors: oklch/oklab/lab/lch palette + color-mix() opacity ──────────────
var modernColorHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; } h3 { font-size: 14px; margin: 20px 0 8px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 12px; }" +
    ".row { display: flex; flex-wrap: wrap; gap: 10px; }" +
    ".swatch { width: 96px; height: 72px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,.25);" +
    "  display: flex; align-items: flex-end; }" +
    ".swatch span { font-size: 10px; color: #fff; padding: 4px 6px; }" +
    // A two-tone backdrop so a semi-transparent color-mix result visibly composites over it.
    ".checker { background: repeating-conic-gradient(#ddd 0% 25%, #fff 0% 50%) 0 / 24px 24px; padding: 10px; border-radius: 10px; }" +
    "</style></head><body>" +
    "<h2>Modern CSS Colors</h2>" +
    "<p class=\"note\">A utility-framework-style palette authored entirely in <code>oklch()</code>, plus " +
    "<code>oklab()/lab()/lch()/hsl()/hwb()</code> and <code>color-mix()</code> opacity modifiers — all resolved to real PDF colors.</p>" +

    "<h3>oklch() palette (constant lightness &amp; chroma, hue sweep)</h3>" +
    "<div class=\"row\">" +
    string.Concat(Enumerable.Range(0, 8).Select(i =>
    {
        var hue = 30 + i * 45;
        return $"<div class=\"swatch\" style=\"background: oklch(0.68 0.17 {hue})\"><span>{hue}&deg;</span></div>";
    })) +
    "</div>" +

    "<h3>Other color spaces</h3>" +
    "<div class=\"row\">" +
    "<div class=\"swatch\" style=\"background: oklab(0.62 0.22 0.13)\"><span>oklab</span></div>" +
    "<div class=\"swatch\" style=\"background: lab(55 70 -40)\"><span>lab</span></div>" +
    "<div class=\"swatch\" style=\"background: lch(65 90 130)\"><span>lch</span></div>" +
    "<div class=\"swatch\" style=\"background: hsl(280 70% 55%)\"><span>hsl</span></div>" +
    "<div class=\"swatch\" style=\"background: hwb(200 10% 10%)\"><span>hwb</span></div>" +
    "</div>" +

    "<h3>color-mix() opacity modifiers (over a checkerboard)</h3>" +
    "<div class=\"checker\"><div class=\"row\">" +
    string.Concat(new[] { 100, 75, 50, 25 }.Select(p =>
        $"<div class=\"swatch\" style=\"background: color-mix(in oklab, oklch(0.62 0.2 265) {p}%, transparent)\"><span>{p}%</span></div>")) +
    "</div></div>" +

    // Tailwind v4 compiles bg-[#2563eb]/50 to exactly this shape - a digit-leading hex color-mix()
    // operand used to be silently dropped while a letter-leading one (e.g. #e11d48) resolved fine.
    "<h3>color-mix() with a hex operand (Tailwind v4 opacity-modifier shape)</h3>" +
    "<div class=\"checker\"><div class=\"row\">" +
    "<div class=\"swatch\" style=\"background: color-mix(in oklab, #2563eb 50%, transparent)\"><span>#2563eb/50</span></div>" +
    "<div class=\"swatch\" style=\"background: color-mix(in oklab, #e11d48 50%, transparent)\"><span>#e11d48/50</span></div>" +
    "</div></div>" +

    "<h3>color-mix() blends</h3>" +
    "<div class=\"row\">" +
    "<div class=\"swatch\" style=\"background: color-mix(in oklch, oklch(0.7 0.2 30), oklch(0.7 0.2 260))\"><span>oklch mix</span></div>" +
    "<div class=\"swatch\" style=\"background: color-mix(in srgb, crimson, royalblue)\"><span>srgb mix</span></div>" +
    "<div class=\"swatch\" style=\"background: color-mix(in oklab, gold 60%, black)\"><span>+black</span></div>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("modern_colors", "Color", "Modern CSS Colors (oklch, color-mix)",
    "A wide-gamut palette authored in oklch() with oklab()/lab()/lch()/hsl()/hwb() companions, plus " +
    "color-mix() opacity modifiers (including hex operands of either form) and blends composited over a " +
    "checkerboard — the CSS Color 4/5 function set modern utility frameworks emit, resolved to real PDF colors.",
    modernColorHtml, pdfConfig);

// ── @layer cascade layers: layer order beats specificity; unlayered beats layered ─────
var cascadeLayerHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; } .note { color: #555; font-size: 12px; margin: 0 0 16px; }" +
    ".card { width: 150px; padding: 16px; border-radius: 10px; color: #fff; font-size: 13px; margin: 0 12px 12px 0;" +
    "  display: inline-block; vertical-align: top; box-shadow: 0 1px 3px rgba(0,0,0,.25); }" +
    ".card b { display: block; font-size: 15px; margin-bottom: 6px; }" +
    // Declare the layer order up front, exactly like a utility framework does.
    "@layer base, components, utilities;" +
    // A high-specificity rule in an EARLIER layer...
    "@layer base { #c1.card, #c2.card, #c3.card { background: #64748b; } }" +
    // ...loses to a low-specificity rule in a LATER layer.
    "@layer utilities { .card { background: oklch(0.55 0.17 250); } }" +
    // components sits between base and utilities.
    "@layer components { .card { background: #16a34a; } }" +
    // An UNLAYERED rule beats every layer, regardless of specificity (no !important needed).
    ".unlayered { background: #db2777; }" +
    // !important REVERSES layer order: among !important declarations the EARLIER layer (base) wins.
    "@layer base { #c4 { background: #dc2626 !important; } }" +
    "@layer utilities { #c4 { background: #2563eb !important; } }" +
    // revert-layer reveals the lower layer's value: utilities reverts, so base's green shows through.
    "@layer base { #c5 { background: #16a34a; } }" +
    "@layer utilities { #c5 { background: revert-layer; } }" +
    "</style></head><body>" +
    "<h2>@layer cascade layers</h2>" +
    "<p class=\"note\">Layer order (declared <code>base, components, utilities</code>) decides the winner ahead of " +
    "specificity: the <code>utilities</code> rule wins over a higher-specificity <code>base</code> rule; an unlayered rule wins over all layers. " +
    "For <code>!important</code> the layer order <b>reverses</b>, and <code>revert-layer</code> reveals a lower layer.</p>" +
    "<div class=\"card\" id=\"c1\"><b>utilities wins</b>base #id rule loses to the later utilities layer &rarr; blue.</div>" +
    "<div class=\"card\" id=\"c2\"><b>order matters</b>components is later than base but earlier than utilities.</div>" +
    "<div class=\"card unlayered\" id=\"c3\"><b>unlayered wins</b>An unlayered rule outranks every layer &rarr; pink.</div>" +
    "<div class=\"card\" id=\"c4\"><b>!important reverses</b>Among <code>!important</code> rules the earlier base layer wins &rarr; red.</div>" +
    "<div class=\"card\" id=\"c5\"><b>revert-layer</b>utilities does <code>revert-layer</code>, revealing base &rarr; green.</div>" +
    "</body></html>";

await SaveShowcaseAsync("cascade_layers", "Selectors & Cascade", "@layer Cascade Layers",
    "CSS cascade layers: an @layer order declaration makes a later layer's low-specificity rule win over an " +
    "earlier layer's high-specificity (#id) rule, and an unlayered rule beat every layer; for !important the " +
    "layer order reverses (earlier layer wins), and revert-layer reveals a lower layer — the layering model " +
    "modern utility frameworks rely on.",
    cascadeLayerHtml, pdfConfig);

// ── CSS Nesting: nested style rules and the & nesting selector ────────────────────────
var nestingHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; } .note { color: #555; font-size: 12px; margin: 0 0 16px; }" +
    // One rule per card, authored with nesting — the whole card's look comes from nested rules and `&`.
    ".card {" +
    "  display: block; max-width: 420px; margin: 0 0 14px; padding: 16px;" +
    "  border-radius: 10px; border: 1px solid #cbd5e1; background: #f8fafc;" +
    "  & .title { font-size: 15px; font-weight: 700; color: #0f172a; margin-bottom: 6px; }" + // & .title == .card .title
    "  .body { font-size: 13px; color: #475569; }" +                                          // implicit descendant
    "  &.accent { background: oklch(0.95 0.03 250); border-color: #2563eb; }" +                // &.accent == .card.accent
    "  > .tag {" +                                                                             // > .tag == .card > .tag
    "    display: inline-block; margin-top: 10px; padding: 2px 8px; border-radius: 999px;" +
    "    font-size: 11px; background: #2563eb; color: #fff;" +
    "    & + .tag { margin-left: 6px; }" +                                                     // nested combinator, 2 levels deep
    "  }" +
    "}" +
    "</style></head><body>" +
    "<h2>CSS Nesting</h2>" +
    "<p class=\"note\">Each card is styled entirely by <b>nested rules</b>: <code>&amp; .title</code>, an implicit-descendant " +
    "<code>.body</code>, a compound <code>&amp;.accent</code>, a child <code>&gt; .tag</code>, and a two-level-deep " +
    "<code>&amp; + .tag</code> — the shape modern utility frameworks emit for browser targets.</p>" +
    "<div class=\"card\">" +
    "<div class=\"title\">Plain card</div>" +
    "<div class=\"body\">Title, body, and pill tags all come from selectors nested under <code>.card</code>.</div>" +
    "<span class=\"tag\">alpha</span><span class=\"tag\">beta</span>" +
    "</div>" +
    "<div class=\"card accent\">" +
    "<div class=\"title\">Accent card</div>" +
    "<div class=\"body\">This one adds the <code>.accent</code> class, so <code>&amp;.accent</code> tints it blue.</div>" +
    "<span class=\"tag\">one</span><span class=\"tag\">two</span>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("css_nesting", "Selectors & Cascade", "CSS Nesting",
    "CSS Nesting: a card styled entirely by rules nested inside `.card` — the `&` nesting selector " +
    "(`&.accent`), an implicit-descendant nested selector (`.body`), a child combinator (`> .tag`), and a " +
    "two-level-deep nested combinator (`& + .tag`), resolved against the parent exactly like `:is(.card)`.",
    nestingHtml, pdfConfig);

// ── Selectors PeachPDF recognizes but never matches: they must not invalidate their list ───
// Every declaration below arrives through a selector list that also names something PeachPDF has no
// elements for (`:host`, `::placeholder`, a vendor-prefixed reset pseudo-element). An unrecognized name
// invalidates the WHOLE list (CSS Selectors 3 §4), so before these were registered this page rendered
// with no design tokens at all — which is what a real Tailwind v4 document did.
var unmatchableSelectorHtml =
    "<html><head><style>" +
    // The exact shape Tailwind v4 emits: the entire design-token block, guarded by `:root, :host`.
    ":root, :host {" +
    "  --font-body: sans-serif; --ink: oklch(0.25 0.03 260); --muted: #64748b;" +
    "  --brand: oklch(0.55 0.17 250); --accent: oklch(0.62 0.19 25); --surface: #f1f5f9;" +
    "  --spacing: 4pt; --radius: 10px;" +
    "}" +
    "body { font-family: var(--font-body); color: var(--ink); margin: calc(var(--spacing) * 6); }" +
    "h2 { font-size: 20px; margin: 0 0 4px; } .note { color: var(--muted); font-size: 12px; margin: 0 0 16px; }" +
    // A preflight-shaped reset: the `::placeholder` half selects nothing, the `p`/`.card` half must apply.
    ".card, ::placeholder {" +
    "  font-size: 13px; background: var(--surface); border-left: calc(var(--spacing) * 1.5) solid var(--brand);" +
    "}" +
    ".card {" +
    "  max-width: 420px; margin-bottom: calc(var(--spacing) * 3.5); padding: calc(var(--spacing) * 4);" +
    "  border-radius: var(--radius);" +
    "}" +
    ".card b { display: block; font-size: 15px; color: var(--brand); margin-bottom: calc(var(--spacing) * 1.5); }" +
    ".card .body { display: block; }" +
    // Vendor-prefixed pseudo-elements, verbatim from a normalize/reset sheet.
    ".card.alt b, ::-webkit-file-upload-button, ::-moz-focus-inner { color: var(--accent); }" +
    ".card.alt { border-left-color: var(--accent); }" +
    // A functional shadow-DOM selector and a vendor pseudo-class, again sharing their list.
    ".tag, :host(.theme), :-moz-focusring {" +
    "  display: inline-block; margin-top: calc(var(--spacing) * 2); padding: 2px 8px;" +
    "  border-radius: 999px; font-size: 11px; background: var(--brand); color: #fff;" +
    "}" +
    ".card.alt .tag { background: var(--accent); }" +
    "</style></head><body>" +
    "<h2>Recognized-but-unmatchable selectors</h2>" +
    "<p class=\"note\">Every colour, font and measure on this page is declared in a rule whose selector list also names " +
    "something PeachPDF has no elements for. Registering those names keeps the list <b>valid</b>, so the half that " +
    "does match still applies — and the unmatchable half still selects nothing.</p>" +
    "<div class=\"card\">" +
    "<b>:root, :host</b>" +
    "<span class=\"body\">The design tokens driving this card come from a <code>:root, :host</code> block — the shape a " +
    "utility framework emits. Its <code>:root</code> half applies; <code>:host</code> matches nothing.</span>" +
    "<span class=\"tag\">tokens</span><span class=\"tag\">var()</span>" +
    "</div>" +
    "<div class=\"card alt\">" +
    "<b>::placeholder and friends</b>" +
    "<span class=\"body\">This card's accent comes from a list shared with <code>::-webkit-file-upload-button</code> " +
    "and <code>::-moz-focus-inner</code>; its panel style from one shared with <code>::placeholder</code>, and its " +
    "pills from one shared with <code>:host(.theme)</code>.</span>" +
    "<span class=\"tag\">::placeholder</span><span class=\"tag\">:host()</span>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("unmatchable_selectors", "Selectors & Cascade", "Unmatchable Selectors",
    "Selectors PeachPDF recognizes but can never match — `:host`, `:host()`, `::placeholder`, and vendor " +
    "extensions like `::-webkit-file-upload-button` — no longer invalidate the selector list they share with a " +
    "real selector. Every token, colour and measure here arrives through such a list, including a Tailwind-v4-" +
    "shaped `:root, :host` design-token block.",
    unmatchableSelectorHtml, pdfConfig);

// ── The pseudo-classes a document tree alone can answer (issue #417) ───────────────────────
// :empty, :any-link and :scope used to sit in the recognized-but-unmatchable list with :hover and
// :host. They depend only on the document tree, so a static renderer can evaluate all three - and
// this page is styled entirely through them: the design tokens arrive via :scope, every link colour
// via :any-link, and the gaps in the table are filled by :empty alone.
var documentTreeSelectorHtml = """
    <!DOCTYPE html>
    <html>
    <head><style>
      /* :scope with no scoping root in play IS the root element, so this is the :root token block. */
      :scope { --ink: #1e293b; --muted: #64748b; --brand: #2563eb; --line: #cbd5e1; --gap: #fef3c7; }
      :scope > body { font-family: sans-serif; color: var(--ink); margin: 28pt; font-size: 12px; }
      :scope h2 { font-size: 20px; margin: 0 0 4px; }
      .note { color: var(--muted); font-size: 12px; margin: 0 0 18px; max-width: 460px; }
      h3 { font-size: 13px; margin: 18px 0 6px; }

      /* Reset the UA sheet's blanket `a { color: #0055BB; text-decoration: underline }` first, so the
         only thing distinguishing a real link below is :any-link - the union of :link and :visited,
         which stylesheets increasingly prefer over :link. An <a> with no href is not a link source. */
      a { color: var(--ink); text-decoration: none; }
      :any-link { color: var(--brand); font-weight: bold; text-decoration: underline; }
      a.tag:any-link { display: inline-block; padding: 2px 8px; border-radius: 999px;
                       background: var(--brand); color: #fff; text-decoration: none; font-size: 11px; }

      table { border-collapse: collapse; font-size: 12px; }
      th, td { border: 1px solid var(--line); padding: 4px 10px; text-align: left; }
      th { background: #f1f5f9; font-size: 11px; text-transform: uppercase; color: var(--muted); }
      /* The "mark the holes in the data" idiom: only cells with no content at all. */
      td:empty { background: var(--gap); }
      td:empty::after { content: "not supplied"; color: #92400e; font-style: italic; }
      /* And its inverse - a cell that DOES have content keeps the plain treatment. */
      td:not(:empty) { color: var(--ink); }

      /* The other half of the idiom: a container the document left empty collapses away. */
      .callout { border-left: 3px solid var(--brand); background: #eff6ff; padding: 8px 12px;
                 margin: 6px 0; max-width: 460px; }
      .callout:empty { display: none; }
    </style></head>
    <body>
      <h2>Document-tree pseudo-classes</h2>
      <p class="note">Nothing on this page is selected by a class or an id alone. The colours come from a
      <code>:scope</code> token block, the links from <code>:any-link</code>, and every highlight in the
      table below from <code>:empty</code> - three selectors that need only the document tree.</p>

      <h3>:empty marks the gaps</h3>
      <table>
        <tr><th>Part</th><th>Supplier</th><th>Lead time</th></tr>
        <tr><td>Hinge, 40mm</td><td>Ferrant &amp; Co.</td><td>3 weeks</td></tr>
        <tr><td>Bracket, L</td><td></td><td>2 weeks</td></tr>
        <tr><td>Spacer, 6mm</td><td>Ferrant &amp; Co.</td><td>   </td></tr>
      </table>
      <p class="note">Row 2's supplier cell is written <code>&lt;td&gt;&lt;/td&gt;</code> and row 3's lead
      time holds three spaces. Both are <code>:empty</code> - white space is not content. A cell holding a
      non-breaking space would not be, and neither would one holding a <code>&lt;br&gt;</code>.</p>

      <h3>:empty collapses an unused container</h3>
      <div class="callout">This callout has content, so it stays.</div>
      <div class="callout"></div>
      <div class="callout">The template emitted an empty callout between these two. <code>.callout:empty
      { display: none }</code> removed it, so no stray blue rule prints.</div>

      <h3>:any-link, and what it leaves alone</h3>
      <p>A <a href="https://peachpdf.net">real hyperlink</a> is styled by <code>:any-link</code>; a
      named <a name="section">anchor with no href</a> is not a link source, so it keeps the plain body
      treatment this page's <code>a { }</code> reset gives every anchor.</p>
      <p><a class="tag" href="https://peachpdf.net/showcase.html">showcase</a>
         <a class="tag" href="https://peachpdf.net/docs">docs</a></p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("document_tree_selectors", "Selectors & Cascade", "Document-Tree Pseudo-Classes",
    "`:empty`, `:any-link` and `:scope` — the three pseudo-classes that depend only on the document tree, " +
    "so a static renderer can evaluate them. The design tokens here come from a `:scope` block, the link " +
    "styling from `:any-link`, and the table's flagged gaps plus the collapsed callout from `:empty` (which " +
    "counts white space as no content, and ignores generated `::before`/`::after` boxes entirely).",
    documentTreeSelectorHtml, pdfConfig);

// ─── CSS Grid layout showcase ──────────────────────────────────────────────

// Exercises the grid engine (issue #232): fixed + fr tracks, item spanning, dense auto-placement
// backfilling a hole, and the Tailwind responsive-card idiom
// repeat(auto-fill, minmax(...)). Each section is a self-contained grid.
var gridHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
      body { font: 12pt Arial, sans-serif; color: #222; margin: 24pt; }
      h1 { font-size: 20pt; margin: 0 0 4pt; }
      h2 { font-size: 13pt; margin: 20pt 0 8pt; color: #4a5568; }
      .note { color: #718096; font-size: 10pt; margin: 0 0 8pt; }
      .cell { background: #4a90d9; color: #fff; padding: 10pt; border-radius: 4pt; font-size: 10pt; }
      .cell.b { background: #e07b53; }
      .cell.c { background: #4caf72; }

      .fixed-fr { display: grid; grid-template-columns: 120pt 1fr 1fr; gap: 10pt; }

      .spanning { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8pt; margin-top: 8pt; }
      .spanning .wide { grid-column: span 2; }
      .spanning .tall { grid-row: span 2; }

      .dense { display: grid; grid-auto-flow: row dense; grid-template-columns: repeat(3, 1fr); gap: 8pt; }
      .dense .lead { grid-column: 2 / 4; }

      .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(150pt, 1fr)); gap: 12pt; }
      .cards .cell { min-height: 40pt; }
    </style>
    </head>
    <body>
      <h1>CSS Grid</h1>
      <p class="note">A real grid track-sizing and placement engine — no browser engine involved.</p>

      <h2>Fixed + flexible tracks &mdash; <code>grid-template-columns: 120pt 1fr 1fr</code></h2>
      <div class="fixed-fr">
        <div class="cell">120pt</div><div class="cell b">1fr</div><div class="cell c">1fr</div>
      </div>

      <h2>Spanning &mdash; <code>grid-column: span 2</code> / <code>grid-row: span 2</code></h2>
      <div class="spanning">
        <div class="cell wide">span 2 columns</div>
        <div class="cell b tall">span 2 rows</div>
        <div class="cell c">c</div>
        <div class="cell">d</div>
        <div class="cell">e</div>
        <div class="cell c">f</div>
      </div>

      <h2>Dense auto-placement &mdash; a 1-wide item backfills the hole before the wide item</h2>
      <div class="dense">
        <div class="cell b lead">grid-column: 2 / 4</div>
        <div class="cell">backfills col 1</div>
        <div class="cell c">next</div>
        <div class="cell">next</div>
      </div>

      <h2>Responsive cards &mdash; <code>repeat(auto-fill, minmax(150pt, 1fr))</code></h2>
      <div class="cards">
        <div class="cell">card 1</div><div class="cell b">card 2</div><div class="cell c">card 3</div>
        <div class="cell b">card 4</div><div class="cell c">card 5</div><div class="cell">card 6</div>
      </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("css_grid", "Layout", "CSS Grid",
    "CSS Grid layout: fixed and `fr` tracks, item spanning (`grid-column: span 2`, `grid-row: span 2`), " +
    "dense auto-placement backfilling a hole, and the responsive `repeat(auto-fill, minmax(150pt, 1fr))` " +
    "card idiom — all laid out by PeachPDF's own grid track-sizing and placement engine.",
    gridHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Grid: grid-template-areas + named lines (#261) ─────────────────────

// The classic "holy grail" page layout expressed with grid-template-areas: each region places itself
// with `grid-area: <name>` (the §8.3.1 custom-ident copy rule fills the whole named area), and a second
// grid demonstrates named lines `[name]` referenced from grid-column.
var gridAreasHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
      body { font: 12pt Arial, sans-serif; color: #222; margin: 24pt; }
      h1 { font-size: 20pt; margin: 0 0 4pt; }
      h2 { font-size: 13pt; margin: 20pt 0 8pt; color: #4a5568; }
      .note { color: #718096; font-size: 10pt; margin: 0 0 8pt; }

      .page {
        display: grid;
        grid-template-columns: 120pt 1fr 120pt;
        grid-template-rows: 44pt 220pt 40pt;
        grid-template-areas:
          "header header header"
          "nav    main   aside"
          "footer footer footer";
        gap: 8pt;
      }
      .page > div { color: #fff; padding: 8pt; border-radius: 4pt; font-size: 10pt; }
      .page .header { grid-area: header; background: #2d3748; }
      .page .nav    { grid-area: nav;    background: #4a90d9; }
      .page .main   { grid-area: main;   background: #4caf72; }
      .page .aside  { grid-area: aside;  background: #e07b53; }
      .page .footer { grid-area: footer; background: #718096; }

      .named { display: grid; grid-template-columns: [full-start] 100pt [main-start] 1fr [main-end] 100pt [full-end]; gap: 8pt; margin-top: 8pt; }
      .named > div { color: #fff; padding: 8pt; border-radius: 4pt; font-size: 10pt; }
      .named .banner { grid-column: full-start / full-end; background: #2d3748; }
      .named .body   { grid-column: main-start / main-end; background: #4a90d9; }
    </style>
    </head>
    <body>
      <h1>grid-template-areas &amp; named lines</h1>

      <h2>"Holy grail" layout &mdash; every region placed by <code>grid-area: &lt;name&gt;</code></h2>
      <p class="note">The ASCII-art template names each area; each child fills its area via the
      custom-ident copy rule.</p>
      <div class="page">
        <div class="header">header</div>
        <div class="nav">nav</div>
        <div class="main">main content</div>
        <div class="aside">aside</div>
        <div class="footer">footer</div>
      </div>

      <h2>Named lines &mdash; <code>grid-column: main-start / main-end</code></h2>
      <div class="named">
        <div class="banner">spans [full-start] to [full-end]</div>
        <div class="body">spans the named [main-start]&hellip;[main-end] lines</div>
      </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("css_grid_areas", "Layout", "CSS Grid Template Areas",
    "CSS `grid-template-areas`: a classic \"holy grail\" page layout where each region places itself with " +
    "`grid-area: <name>`, plus named grid lines (`[main-start]`) referenced from `grid-column` — the named " +
    "placement model of PeachPDF's grid engine.",
    gridAreasHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Grid: subgrid (#262) ───────────────────────────────────────────────

// Two subgrid idioms (CSS Grid Level 2 §9). A "detail rows" grid subgrids the COLUMN axis: each row is a
// nested grid spanning all parent columns with grid-template-columns:subgrid, so every row's cells line up
// on the same column tracks even though the rows are independent elements. A "card deck" subgrids the ROW
// axis: each card spans the same three parent rows with grid-template-rows:subgrid, so the title / body /
// footer bands align across cards regardless of how much content each band holds.
var gridSubgridHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
      body { font: 12pt Arial, sans-serif; color: #222; margin: 24pt; }
      h1 { font-size: 20pt; margin: 0 0 4pt; }
      h2 { font-size: 13pt; margin: 20pt 0 8pt; color: #4a5568; }
      .note { color: #718096; font-size: 10pt; margin: 0 0 8pt; }
      .cell { color: #fff; padding: 8pt; border-radius: 4pt; font-size: 10pt; }

      /* Column subgrid: the outer grid defines the columns; each row adopts them. */
      .rows { display: grid; grid-template-columns: 120pt 1fr 80pt; gap: 8pt; }
      .row  { grid-column: 1 / 4; display: grid; grid-template-columns: subgrid; gap: 8pt; }
      .row .k { background: #2d3748; }
      .row .v { background: #4a90d9; }
      .row .n { background: #4caf72; text-align: right; }

      /* Row subgrid: each card spans the same three rows and adopts their heights. Its single column is 1fr
         (own, not subgridded) so the body text wraps to the card width. */
      .deck  { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12pt; }
      .card  { grid-row: span 3; display: grid; grid-template-columns: 1fr; grid-template-rows: subgrid; gap: 6pt; }
      .card .title  { background: #2d3748; }
      .card .body   { background: #4a90d9; }
      .card .footer { background: #718096; }
    </style>
    </head>
    <body>
      <h1>Subgrid</h1>

      <h2>Column subgrid &mdash; independent rows share the parent's columns</h2>
      <p class="note">Each <code>.row</code> is its own grid with <code>grid-template-columns: subgrid</code>;
      its three cells align to the parent's <code>120pt 1fr 80pt</code> tracks.</p>
      <div class="rows">
        <div class="row"><div class="cell k">Item</div><div class="cell v">Wireless keyboard</div><div class="cell n">$49</div></div>
        <div class="row"><div class="cell k">SKU</div><div class="cell v">A very long descriptive value that stretches the flexible middle column</div><div class="cell n">$7</div></div>
        <div class="row"><div class="cell k">Total</div><div class="cell v">Two line items</div><div class="cell n">$56</div></div>
      </div>

      <h2>Row subgrid &mdash; card bands align across the deck</h2>
      <p class="note">Every <code>.card</code> spans the same three parent rows with
      <code>grid-template-rows: subgrid</code>, so the title, body and footer bands line up even though each
      card's body is a different length.</p>
      <div class="deck">
        <div class="card">
          <div class="cell title">Starter</div>
          <div class="cell body">Everything you need to get going.</div>
          <div class="cell footer">$0 / mo</div>
        </div>
        <div class="card">
          <div class="cell title">Pro</div>
          <div class="cell body">A longer description that wraps onto several lines so this card's body band is the tallest of the three, and the shared row grows to fit it.</div>
          <div class="cell footer">$12 / mo</div>
        </div>
        <div class="card">
          <div class="cell title">Team</div>
          <div class="cell body">For groups.</div>
          <div class="cell footer">$40 / mo</div>
        </div>
      </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("css_grid_subgrid", "Layout", "CSS Grid Subgrid",
    "CSS Grid Level 2 `subgrid`: independent rows adopting the parent's columns (`grid-template-columns: " +
    "subgrid`) so their cells align, and a card deck adopting the parent's rows (`grid-template-rows: " +
    "subgrid`) so title/body/footer bands line up across cards — the nested grid adopts its parent's tracks.",
    gridSubgridHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ─── CSS Grid: intrinsic sizing, calc() tracks & baseline alignment (#265) ──────

// Four sections exercise the #265 sizing/placement fixes. Track functions: a min-content column sizes to
// its longest word (narrower than max-content), and a calc() breadth resolves inside minmax()/fit-content().
// Spanning intrinsics: an item spanning two auto columns grows both so neither collapses. Implicit columns:
// an item explicitly placed on a line past the explicit grid generates the implicit tracks it needs instead
// of overlapping. Baseline: mixed-size labels in a row align on a common text baseline.
var gridIntrinsicHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
      body { font: 12pt Arial, sans-serif; color: #222; margin: 24pt; }
      h1 { font-size: 20pt; margin: 0 0 4pt; }
      h2 { font-size: 13pt; margin: 20pt 0 8pt; color: #4a5568; }
      .note { color: #718096; font-size: 10pt; margin: 0 0 8pt; }
      .cell { color: #fff; padding: 6pt; border-radius: 4pt; font-size: 10pt; }

      /* min-content / max-content + a calc() minmax floor and a calc() fit-content cap. */
      .tracks { display: grid; grid-template-columns:
                  min-content minmax(calc(30pt + 20pt), 80pt) fit-content(calc(60pt + 20pt)) 1fr; gap: 8pt; }
      .tracks .a { background: #2d3748; }
      .tracks .b { background: #4a90d9; }
      .tracks .c { background: #b4690e; }
      .tracks .d { background: #4caf72; }

      /* A single item spanning two auto columns: both grow to share it, and the second row's cells land on
         those same non-zero tracks. */
      .spanned { display: grid; grid-template-columns: auto auto 1fr; gap: 8pt; }
      .spanned .wide { grid-column: 1 / 3; background: #2d3748; }
      .spanned .e { background: #4a90d9; }
      .spanned .f { background: #4caf72; }
      .spanned .g { background: #718096; }

      /* An item explicitly placed on column 5 of a 3-column grid generates implicit columns 4 and 5. */
      .implicit { display: grid; grid-template-columns: repeat(3, 60pt); grid-auto-columns: 60pt; gap: 8pt; }
      .implicit .p { background: #4a90d9; }
      .implicit .far { grid-column: 5; background: #b4690e; }

      /* Baseline alignment: differently-sized labels share one baseline. */
      .baseline { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8pt; align-items: baseline;
                  background: #edf2f7; padding: 8pt; border-radius: 4pt; }
      .baseline span { display: block; }
      /* Two auto tracks in a container too narrow for both max-contents. */
      .cramped { display: grid; grid-template-columns: auto auto; gap: 8pt; width: 200pt;
                 border: 1pt dashed #a0aec0; }
      .roomy   { display: grid; grid-template-columns: auto auto; gap: 8pt; width: 460pt;
                 border: 1pt dashed #a0aec0; }
      .cramped .h, .roomy .h { background: #6b46c1; }
      .cramped .i, .roomy .i { background: #dd6b20; }

      .baseline .big   { font-size: 30pt; color: #2d3748; }
      .baseline .mid   { font-size: 18pt; color: #4a90d9; }
      .baseline .small { font-size: 11pt; color: #4caf72; }
    </style>
    </head>
    <body>
      <h1>Intrinsic sizing, calc() tracks &amp; baseline</h1>

      <h2>Track functions &mdash; min-content, calc() in minmax()/fit-content()</h2>
      <p class="note"><code>min-content</code> fits the longest word; <code>minmax(calc(30pt + 20pt), 80pt)</code>
      floors at a calc()-resolved 50pt and grows to its 80pt limit while the space is there;
      <code>fit-content(calc(60pt + 20pt))</code> caps at 80pt; <code>1fr</code> takes what is left after
      all three.</p>
      <div class="tracks">
        <div class="cell a">Antidisestablishmentarianism sample</div>
        <div class="cell b">flex floor</div>
        <div class="cell c">capped at eighty points; the rest overflows the fit-content track</div>
        <div class="cell d">remainder</div>
      </div>

      <h2>Spanning item grows shared intrinsic columns</h2>
      <p class="note">The dark bar spans both <code>auto</code> columns; each now takes a share of its width,
      so the second row's cells sit on real, non-zero tracks instead of collapsing to the left edge.</p>
      <div class="spanned">
        <div class="cell wide">This bar spans columns 1&ndash;2</div>
        <div class="cell g">1fr</div>
        <div class="cell e">col 1</div>
        <div class="cell f">col 2</div>
        <div class="cell g">1fr</div>
      </div>

      <h2>Explicit line past the grid generates implicit columns</h2>
      <p class="note">Three explicit 60pt columns; the orange cell is placed on <code>grid-column: 5</code>, so
      implicit columns 4 and 5 are created and it no longer overlaps the blue auto-placed cell.</p>
      <div class="implicit">
        <div class="cell p">auto</div>
        <div class="cell far">column&nbsp;5</div>
      </div>

      <h2>Auto tracks stop where the space runs out</h2>
      <p class="note">The same two <code>auto</code> columns twice. Given room, each grows to its own
      max-content width; given a container narrower than both together, they share what there is instead of
      growing past it &mdash; the dashed outline is the container, and nothing may reach outside it.</p>
      <div class="roomy">
        <div class="cell h">alpha beta gamma delta</div>
        <div class="cell i">epsilon zeta eta theta</div>
      </div>
      <div class="cramped" style="margin-top: 8pt">
        <div class="cell h">alpha beta gamma delta</div>
        <div class="cell i">epsilon zeta eta theta</div>
      </div>

      <h2>Baseline item alignment</h2>
      <p class="note">With <code>align-items: baseline</code>, the three differently-sized labels rest on a
      single shared text baseline.</p>
      <div class="baseline">
        <span class="big">Ag 30pt</span>
        <span class="mid">Ag 18pt</span>
        <span class="small">Ag 11pt</span>
      </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("css_grid_intrinsic", "Layout", "CSS Grid Intrinsic Sizing & Baseline",
    "CSS Grid sizing/placement depth: `min-content` vs `max-content` tracks, `calc()` inside " +
    "`minmax()`/`fit-content()`, a spanning item growing the shared intrinsic columns it covers, an explicit " +
    "line past the grid generating implicit columns, `auto` tracks stopping at the container rather than " +
    "overflowing it, and `align-items: baseline` sharing one text baseline.",
    gridIntrinsicHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Responsive @media feature queries ──────────────────────────────────────
// One mobile-first stylesheet rendered at three configurations. The @media feature
// conditions are evaluated against the page box (and the configured color scheme),
// so the SAME HTML lays out differently per page size / scheme.
const string responsiveHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
      html, body { margin: 0; font-family: sans-serif; background: #ffffff; color: #1f2937; }
      .wrap { padding: 28px; }
      h1 { font-size: 20pt; margin: 0 0 6px; }
      h1 code { background: #f3f4f6; padding: 2px 6px; border-radius: 5px; font-size: 15pt; }
      .badge { display: inline-block; padding: 5px 12px; border-radius: 999px; font-size: 10pt; font-weight: bold; }
      /* Mobile-first defaults: single column, amber "mobile" badge. */
      .badge { background: #fde68a; color: #92400e; }
      .badge::after { content: "mobile layout — width < 60rem"; }
      .grid { display: grid; grid-template-columns: 1fr; gap: 14px; margin-top: 18px; }
      .card { background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 12px; padding: 18px; }
      .card h2 { margin: 0 0 6px; font-size: 13pt; color: #1d4ed8; }
      .card p { margin: 0; font-size: 10.5pt; line-height: 1.5; color: #475569; }
      /* Wide pages: three columns, green "desktop" badge. */
      @media (min-width: 60rem) {
        .grid { grid-template-columns: 1fr 1fr 1fr; }
        .badge { background: #bbf7d0; color: #166534; }
        .badge::after { content: "desktop layout — width \2265 60rem"; }
      }
      /* Dark scheme (PdfGenerateConfig.PreferredColorScheme = Dark). */
      @media (prefers-color-scheme: dark) {
        html, body { background: #0b1220; color: #e5e7eb; }
        h1 code { background: #1f2937; }
        .card { background: #111827; border-color: #374151; }
        .card h2 { color: #93c5fd; }
        .card p { color: #9ca3af; }
      }
    </style>
    </head>
    <body>
      <div class="wrap">
        <h1>Responsive <code>@media</code></h1>
        <span class="badge"></span>
        <div class="grid">
          <div class="card"><h2>Fast</h2><p>Pure .NET, no external process — the same engine at every page size.</p></div>
          <div class="card"><h2>Responsive</h2><p>Breakpoints select by the page-box width, not all at once.</p></div>
          <div class="card"><h2>Range syntax</h2><p>Both <code>min-width</code> and <code>width &gt;= 48rem</code> work.</p></div>
          <div class="card"><h2>Dark mode</h2><p><code>prefers-color-scheme</code> is driven by config.</p></div>
          <div class="card"><h2>Print-aware</h2><p>Static-document features report their resting state.</p></div>
          <div class="card"><h2>Standards</h2><p>Media Queries 4 feature evaluation over the page box.</p></div>
        </div>
      </div>
    </body>
    </html>
    """;

// Wide (A4 landscape, 842pt ≈ 1123px ≥ 60rem) → three-column desktop layout.
await SaveShowcaseAsync("responsive_media_wide", "Responsive Design", "Responsive @media (wide page)",
    "A mobile-first stylesheet rendered on a wide (A4 landscape) page: the `@media (min-width: 60rem)` " +
    "breakpoint matches the page-box width, so the grid becomes three columns and the badge flips to " +
    "\"desktop\".",
    responsiveHtml, new PdfGenerateConfig { PageSize = PageSize.A4, PageOrientation = PageOrientation.Landscape });

// Narrow (A4 portrait, 595pt ≈ 793px < 60rem) → single-column mobile layout, SAME HTML.
await SaveShowcaseAsync("responsive_media_narrow", "Responsive Design", "Responsive @media (narrow page)",
    "The identical stylesheet rendered on a narrow (A4 portrait) page: the breakpoint does not match, so " +
    "the grid stays a single column and the badge reads \"mobile\" — proof the width feature actually gates.",
    responsiveHtml, new PdfGenerateConfig { PageSize = PageSize.A4, PageOrientation = PageOrientation.Portrait });

// Dark scheme (same HTML) via the new PreferredColorScheme config.
await SaveShowcaseAsync("responsive_media_dark", "Responsive Design", "Dark Mode (prefers-color-scheme)",
    "The same page rendered with `PdfGenerateConfig.PreferredColorScheme = PdfColorScheme.Dark`, so the " +
    "`@media (prefers-color-scheme: dark)` rules apply — the dark surface and light text render, a " +
    "utility framework's `dark:` variants included.",
    responsiveHtml, new PdfGenerateConfig
    {
        PageSize = PageSize.A4,
        PageOrientation = PageOrientation.Landscape,
        PreferredColorScheme = PdfColorScheme.Dark
    });

// ── Responsive @container size queries ─────────────────────────────────────
// Proves @container is NOT @media in a different name: one page, one stylesheet, one card
// component - three containers of different declared widths, each a `container-type: inline-size`
// query container. The @container (min-width: ...) breakpoint is evaluated against each container's
// OWN resolved width, not the page's, so the identical .card markup lays out differently in each
// one purely because of the width its own wrapper declares.
const string containerQueryHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
      html, body { margin: 0; font-family: sans-serif; background: #ffffff; color: #1f2937; }
      .wrap { padding: 28px; }
      h1 { font-size: 20pt; margin: 0 0 6px; }
      h1 code { background: #f3f4f6; padding: 2px 6px; border-radius: 5px; font-size: 15pt; }
      p.lede { font-size: 10.5pt; color: #475569; margin: 0 0 20px; max-width: 640px; }
      .container { container-type: inline-size; container-name: card-shelf; border: 1px dashed #94a3b8; border-radius: 10px; padding: 14px; margin-bottom: 18px; }
      .container-label { font-size: 9pt; color: #64748b; margin: 0 0 10px; font-family: monospace; }
      .card { display: flex; flex-direction: column; gap: 10px; background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 10px; padding: 14px; }
      .card .thumb { width: 100%; height: 40px; border-radius: 6px; background: linear-gradient(135deg, #60a5fa, #2563eb); flex: none; }
      .card .card-text { display: flex; flex-direction: column; gap: 4px; }
      .card h2 { margin: 0; font-size: 12pt; color: #1d4ed8; }
      .card p { margin: 0; font-size: 9.5pt; line-height: 1.4; color: #475569; }
      /* Applies once THIS container's own content-box width reaches 400px - independent of every
         other .container on the page, and independent of the page width itself. */
      @container card-shelf (min-width: 400px) {
        .card { flex-direction: row; align-items: center; }
        .card .thumb { width: 80px; height: 56px; }
      }
    </style>
    </head>
    <body>
      <div class="wrap">
        <h1>Responsive <code>@container</code></h1>
        <p class="lede">The same <code>.card</code> component, unmodified, in three containers of different widths on one page. Each stacks or goes horizontal purely from its own container's width crossing <code>@container (min-width: 400px)</code> - not the page's.</p>
        <div class="container" style="width: 220px;">
          <p class="container-label">container-type: inline-size; width: 220px</p>
          <div class="card"><div class="thumb"></div><div class="card-text"><h2>Stacked</h2><p>Narrower than 400px, so the card stays a column.</p></div></div>
        </div>
        <div class="container" style="width: 500px;">
          <p class="container-label">container-type: inline-size; width: 500px</p>
          <div class="card"><div class="thumb"></div><div class="card-text"><h2>Horizontal</h2><p>500px crosses the 400px breakpoint, so the same rule flips this card to a row.</p></div></div>
        </div>
        <div class="container" style="width: 320px;">
          <p class="container-label">container-type: inline-size; width: 320px</p>
          <div class="card"><div class="thumb"></div><div class="card-text"><h2>Stacked</h2><p>320px is still under 400px, so this one stays stacked too - despite being on the exact same page as the 500px one above.</p></div></div>
        </div>
      </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("container_queries", "Responsive Design", "Responsive @container (size queries)",
    "Three `container-type: inline-size` containers of different declared widths on one page, all " +
    "using the identical `.card` component. The `@container card-shelf (min-width: 400px)` rule " +
    "applies independently per container - the 500px one flips to a horizontal layout while the " +
    "220px and 320px ones stay stacked, proving the breakpoint reads each container's own resolved " +
    "width rather than the page's.",
    containerQueryHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Viewport units (vw/vh/vmin/vmax) and the cq* -> sv* no-container fallback ──────────────
// The "viewport" for a paged medium is the page box itself - every bar below is sized purely
// from vw/vh/vmin/vmax/ch, with no percentage/pixel fallback, so a broken or zeroed conversion
// would collapse every bar to nothing rather than just rendering at the wrong size.
const string viewportUnitsHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
      html, body { margin: 0; font-family: sans-serif; background: #ffffff; color: #1f2937; }
      .wrap { padding: 28px; }
      h1 { font-size: 20pt; margin: 0 0 6px; }
      h1 code { background: #f3f4f6; padding: 2px 6px; border-radius: 5px; font-size: 15pt; }
      p.lede { font-size: 10.5pt; color: #475569; margin: 0 0 20px; max-width: 640px; }
      .row { margin-bottom: 16px; }
      .label { font-size: 9pt; color: #64748b; margin: 0 0 6px; font-family: monospace; }
      .bar { height: 28px; border-radius: 6px; background: linear-gradient(135deg, #60a5fa, #2563eb); }
      .square { height: 25vmin; border-radius: 6px; background: linear-gradient(135deg, #34d399, #059669); }
      .no-container { width: 50cqw; height: 28px; border-radius: 6px; background: linear-gradient(135deg, #f472b6, #db2777); }
      .ch-box { width: 20ch; height: 28px; border-radius: 6px; background: linear-gradient(135deg, #fbbf24, #d97706); }
    </style>
    </head>
    <body>
      <div class="wrap">
        <h1>Viewport units (<code>vw</code>/<code>vh</code>/<code>vmin</code>/<code>vmax</code>)</h1>
        <p class="lede">The "viewport" for a paged medium is the page box itself. Every shape below is sized purely from a viewport-relative (or <code>cq*</code>/<code>ch</code>) unit, so a broken conversion would collapse it to nothing rather than just the wrong size.</p>
        <div class="row">
          <p class="label">width: 100vw</p>
          <div class="bar" style="width: 100vw;"></div>
        </div>
        <div class="row">
          <p class="label">width: 60vw</p>
          <div class="bar" style="width: 60vw;"></div>
        </div>
        <div class="row">
          <p class="label">width: 25vmax</p>
          <div class="bar" style="width: 25vmax;"></div>
        </div>
        <div class="row">
          <p class="label">height: 25vmin (square - the page's shorter side)</p>
          <div class="square" style="width: 25vmin;"></div>
        </div>
        <div class="row">
          <p class="label">width: 50cqw, no ancestor container-type - falls back to 50svw (the page's own width), not 0</p>
          <div class="no-container"></div>
        </div>
        <div class="row">
          <p class="label">width: 20ch - approximates 10em (0.5em per "0" glyph)</p>
          <div class="ch-box"></div>
        </div>
      </div>
    </body>
    </html>
    """;

await SaveShowcaseAsync("viewport_units", "Responsive Design", "Viewport units (vw/vh/vmin/vmax)",
    "Bars and boxes sized entirely from `vw`/`vmin`/`vmax` against a fixed A4 page - proving the page " +
    "box is a real, working viewport numerator. Also includes a `cqw` box with no ancestor " +
    "`container-type`, which now falls back to the small-viewport unit instead of collapsing to a " +
    "zero-width box, and a `ch`-sized box (the `0.5em` approximation).",
    viewportUnitsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// Color fonts (COLR/CPAL) rendered as vector content. The hero row is a subset of the real COLRv1
// build of Noto Color Emoji; the feature breakdown uses a small hand-authored public-domain COLRv1
// fixture whose glyphs isolate each paint feature.
var notoColorB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoColorEmoji-Subset.ttf")));
var colorFontB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ColorTestV1.ttf")));
// A separate Noto Color Emoji subset that keeps the font's `ccmp` feature and the glyphs the
// multi-codepoint sequences need - NotoColorEmoji-Subset.ttf above carries no GSUB at all.
var notoSeqB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoColorEmojiSequences-Subset.ttf")));
string ColorGlyph(string ch, string title, string detail) =>
    "<td>" +
    $"<div class=\"cg\">{ch}</div>" +
    $"<div class=\"desc\">{title}</div>" +
    $"<div class=\"css\">{detail}</div>" +
    "</td>";
string SeqGlyph(string text, string title, string detail) =>
    "<td>" +
    $"<div class=\"cg seq\">{text}</div>" +
    $"<div class=\"desc\">{title}</div>" +
    $"<div class=\"css\">{detail}</div>" +
    "</td>";
var colorEmojiHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'NotoColor'; src: url('data:font/truetype;base64,{notoColorB64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'ColorTest'; src: url('data:font/truetype;base64,{colorFontB64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'NotoSeq'; src: url('data:font/truetype;base64,{notoSeqB64}') format('truetype'); }}" +
    "body { font: 9pt Arial, sans-serif; margin: 0 }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.1em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { margin: 0 0 0.8em; color: #555 }" +
    ".emoji { font-family: 'NotoColor'; font-size: 46pt; line-height: 1.25; letter-spacing: 10pt }" +
    "table.sw { border-collapse: collapse; width: 100%; }" +
    "table.sw td { padding: 6px; vertical-align: top; width: 33%; text-align: center }" +
    ".cg { font-family: 'ColorTest'; font-size: 52pt; line-height: 1; height: 70px }" +
    ".cg.seq { font-family: 'NotoSeq'; font-size: 34pt; height: 56px }" +
    ".desc { font-size: 8pt; font-weight: bold; color: #444; margin-top: 4px }" +
    ".css { font-size: 7pt; color: #666 }" +
    "</style></head><body>" +
    "<h1>Color fonts (COLR / CPAL)</h1>" +
    "<p class=\"intro\">COLR/CPAL color-glyph fonts render their visible artwork as native PDF vector " +
    "content. An embedded subset supplies invisible selectable/searchable text, while per-glyph " +
    "<code>/ActualText</code> preserves exact emoji sequences when copied. The row below is the real " +
    "COLR&nbsp;v1 build of Noto Color Emoji (gradients, transforms, compositing all handled).</p>" +
    "<div class=\"emoji\">\U0001F600 ❤ \U0001F44D \U0001F680 \U0001F308 ⭐ \U0001F525 \U0001F642</div>" +
    "<h2>COLR paint features</h2>" +
    "<p class=\"intro\">The same pipeline, broken down by paint type (hand-authored COLR&nbsp;v1 fixture):</p>" +
    "<table class=\"sw\"><tr>" +
    ColorGlyph("A", "Layered solids", "PaintColrLayers → red box under a green triangle") +
    ColorGlyph("B", "Single solid", "PaintGlyph → PaintSolid (blue)") +
    ColorGlyph("G", "Linear gradient", "PaintLinearGradient (red → blue)") +
    "</tr><tr>" +
    ColorGlyph("T", "Transform", "PaintTranslate over a yellow triangle") +
    ColorGlyph("M", "Blend compositing", "PaintComposite MULTIPLY (blue × yellow → black)") +
    ColorGlyph("F", "Reflect gradient", "PaintLinearGradient, EXTEND_REFLECT") +
    "</tr></table>" +
    "<h2>Emoji sequences</h2>" +
    "<p class=\"intro\">A multi-codepoint emoji sequence composes into the single glyph the font " +
    "defines for it, via the font's <code>ccmp</code> feature — which is where Noto Color Emoji keeps " +
    "every one of these (it declares no <code>liga</code>/<code>rlig</code> at all). A " +
    "<code>U+FE0F</code> variation selector inside a sequence draws nothing of its own and does not " +
    "stop the ligature forming.</p>" +
    "<table class=\"sw\"><tr>" +
    SeqGlyph("\U0001F3F3️‍\U0001F308", "ZWJ + VS16", "U+1F3F3 U+FE0F U+200D U+1F308") +
    SeqGlyph("\U0001F469‍\U0001F4BB", "ZWJ sequence", "U+1F469 U+200D U+1F4BB") +
    SeqGlyph("\U0001F1FA\U0001F1F8", "Regional indicators", "U+1F1FA U+1F1F8") +
    "</tr><tr>" +
    SeqGlyph("\U0001F1EF\U0001F1F5", "Regional indicators", "U+1F1EF U+1F1F5") +
    SeqGlyph("\U0001F3F4\U000E0067\U000E0062\U000E0073\U000E0063\U000E0074\U000E007F",
        "Tag sequence", "U+1F3F4 + 5 TAG letters + U+E007F") +
    SeqGlyph("❤️", "Lone VS16", "U+2764 U+FE0F — selector draws nothing") +
    "</tr></table>" +
    "<p class=\"intro\">Skin tone modifiers (U+1F3FB–U+1F3FF) compose the same way. Unlike a variation " +
    "selector these are <em>not</em> default-ignorable — each has a real swatch glyph — so a font whose " +
    "<code>ccmp</code> never ran renders the base emoji followed by a bare coloured square rather than " +
    "the modified emoji.</p>" +
    "<div class=\"cg seq\">\U0001F44D \U0001F44D\U0001F3FB \U0001F44D\U0001F3FC \U0001F44D\U0001F3FD " +
    "\U0001F44D\U0001F3FE \U0001F44D\U0001F3FF</div>" +
    "</body></html>";
await SaveShowcaseAsync("color_emoji", "Typography & Text", "Color Fonts (COLR/CPAL)",
    "COLR/CPAL color-glyph fonts — including the real COLR v1 build of Noto Color Emoji — rendered as " +
    "native PDF vector content: layered palette colors, gradients, transforms, and blend-mode " +
    "compositing, with an invisible embedded subset for searchable, selectable, exact-copy text.",
    colorEmojiHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// CSS font-palette: selecting among a color font's CPAL palettes, defining custom palettes via
// @font-palette-values (base-palette + override-colors), the light/dark keywords, and palette-mix().
// Uses a subset of Nabla (a real COLR v1 font with 7 palettes).
var nablaB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NablaSubset.ttf")));
string PaletteSwatch(string cls, string title, string detail) =>
    "<td>" +
    $"<div class=\"pw {cls}\">PALETTE</div>" +
    $"<div class=\"desc\">{title}</div>" +
    $"<div class=\"css\">{detail}</div>" +
    "</td>";
var fontPaletteHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'Nabla'; src: url('data:font/truetype;base64,{nablaB64}') format('truetype'); }}" +
    "@font-palette-values --blue { font-family: 'Nabla'; base-palette: 2; }" +
    "@font-palette-values --grey { font-family: 'Nabla'; base-palette: 3; }" +
    "@font-palette-values --custom { font-family: 'Nabla'; base-palette: 0; override-colors: 0 #1db954, 1 #0a7d34, 2 #14532d, 3 #86efac; }" +
    "body { font: 9pt Arial, sans-serif; margin: 0 }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.1em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { margin: 0 0 0.8em; color: #555 }" +
    ".hero { font-family: 'Nabla'; font-size: 40pt; line-height: 1.2 }" +
    "table.sw { border-collapse: collapse; width: 100%; table-layout: fixed }" +
    "table.sw td { padding: 8px 6px; vertical-align: top; text-align: center }" +
    ".pw { font-family: 'Nabla'; font-size: 22pt; line-height: 1; letter-spacing: 1pt }" +
    ".desc { font-size: 8pt; font-weight: bold; color: #444; margin-top: 6px }" +
    ".css { font-size: 7pt; color: #666 }" +
    ".p-normal { font-palette: normal }" +
    ".p-blue { font-palette: --blue }" +
    ".p-grey { font-palette: --grey }" +
    ".p-light { font-palette: light }" +
    ".p-dark { font-palette: dark }" +
    ".p-custom { font-palette: --custom }" +
    ".p-mix { font-palette: palette-mix(in oklab, --blue, --grey) }" +
    "</style></head><body>" +
    "<h1>CSS <code>font-palette</code></h1>" +
    "<p class=\"intro\">A COLR/CPAL color font can ship several palettes; <code>font-palette</code> chooses " +
    "between them, and <code>@font-palette-values</code> defines custom palettes (a <code>base-palette</code> " +
    "plus per-index <code>override-colors</code>). The hero below is a subset of Nabla, a real COLR&nbsp;v1 " +
    "font with 7 palettes, shown in its default palette.</p>" +
    "<div class=\"hero p-normal\">PALETTE</div>" +
    "<h2>Palette selection</h2>" +
    "<p class=\"intro\">The same text, same font — only <code>font-palette</code> differs:</p>" +
    "<table class=\"sw\"><tr>" +
    PaletteSwatch("p-normal", "normal", "font-palette: normal (palette 0)") +
    PaletteSwatch("p-blue", "base-palette", "@font-palette-values --blue { base-palette: 2 }") +
    PaletteSwatch("p-grey", "base-palette", "@font-palette-values --grey { base-palette: 3 }") +
    "</tr><tr>" +
    PaletteSwatch("p-light", "light", "font-palette: light (light-flagged palette)") +
    PaletteSwatch("p-dark", "dark", "font-palette: dark (dark-flagged palette)") +
    PaletteSwatch("p-custom", "override-colors", "override-colors: 0 #1db954, 1 #0a7d34, …") +
    "</tr><tr>" +
    PaletteSwatch("p-mix", "palette-mix()", "palette-mix(in oklab, --blue, --grey)") +
    "</tr></table>" +
    "</body></html>";
await SaveShowcaseAsync("font_palette", "Typography & Text", "CSS font-palette",
    "Selecting among a COLR/CPAL color font's palettes with the CSS font-palette property: the light/dark " +
    "keywords, custom palettes via @font-palette-values (base-palette + override-colors), and palette-mix(). " +
    "Rendered against a subset of Nabla, a real 7-palette COLR v1 font.",
    fontPaletteHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// GSUB ligature substitution: font-variant-ligatures actually turns real GSUB liga/clig ligatures
// on/off (not just a synthesized effect), and the same shaping applies to SVG <text> outlined for a
// gradient fill. Source Sans 3's GSUB `liga` feature ligates "ff"/"ft"/"fft" (confirmed via
// fontTools) - not "fi"/"fl", so this deliberately shows those instead of the more familiar fi/fl
// example most fonts use.
var sourceSans3B64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")));
string LigatureRow(string word) =>
    "<tr>" +
    $"<td class=\"word\">{word}</td>" +
    $"<td class=\"lig on\">{word}</td>" +
    $"<td class=\"lig off\">{word}</td>" +
    "</tr>";
var ligatureHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    "body { font-family: 'SS3', serif; margin: 0; color: #222 }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.2em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555; font-family: Arial, sans-serif }" +
    "table.lig { border-collapse: collapse; width: 100%; font-size: 22pt }" +
    "table.lig th { font-size: 8pt; font-family: Arial, sans-serif; color: #666; text-align: left; padding: 4px 8px }" +
    "table.lig td { padding: 6px 8px; border-top: 1px solid #ddd }" +
    "table.lig td.word { font-size: 8pt; font-family: Arial, sans-serif; color: #666; vertical-align: middle }" +
    "table.lig td.lig.off { font-variant-ligatures: none }" +
    "svg text { font-family: 'SS3'; }" +
    "</style></head><body>" +
    "<h1>GSUB Ligatures</h1>" +
    "<p class=\"intro\">PeachPDF applies a font's real GSUB <code>liga</code>/<code>clig</code> ligature " +
    "substitution (not a synthesized effect) - <code>font-variant-ligatures: none</code> turns it back " +
    "off. Source Sans 3's <code>liga</code> feature merges \"ff\"/\"ft\"/\"fft\" into single connected " +
    "glyphs.</p>" +
    "<table class=\"lig\">" +
    "<tr><th>word</th><th>default (ligated)</th><th>font-variant-ligatures: none</th></tr>" +
    LigatureRow("office") +
    LigatureRow("soft") +
    LigatureRow("offset") +
    "</table>" +
    "<h2>SVG gradient-filled text</h2>" +
    "<p class=\"intro\">A gradient/pattern fill outlines each glyph to a vector path instead of showing " +
    "text (see Text &amp; Fonts → SVG support) - ligature shaping applies to that outlined path the " +
    "same way it applies to ordinary text.</p>" +
    "<svg width=\"500\" height=\"70\" viewBox=\"0 0 500 70\">" +
    "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\">" +
    "<stop offset=\"0\" stop-color=\"#4a90d9\"/><stop offset=\"1\" stop-color=\"#d94a90\"/>" +
    "</linearGradient></defs>" +
    "<text x=\"0\" y=\"50\" font-size=\"48\" fill=\"url(#g)\">office staff</text>" +
    "</svg>" +
    "</body></html>";
await SaveShowcaseAsync("gsub_ligatures", "Typography & Text", "GSUB Ligatures",
    "Real GSUB liga/clig ligature substitution (not a synthesized effect): font-variant-ligatures " +
    "actually turns a font's ligatures on and off, for both ordinary text and gradient-filled SVG text.",
    ligatureHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// font-variant-caps: real GSUB smcp/c2sc/titl substitution on a font that has the feature (Source
// Sans 3, confirmed via direct byte inspection), falling back to a synthesized uppercase+shrink
// approximation on a font that doesn't (Source Code Pro, confirmed to carry zero caps-family GSUB
// tags) - both are the same CSS, only the resolved font differs. font-variant-numeric activates a
// font's real numeric-variant GSUB features (oldstyle figures, tabular figures, slashed zero) the
// same way, and font-variant-position does the same for sups/subs - synthesizing from the font's own
// OS/2 recommended scale and offset on STIX Two Math, which has no positional features at all.
var sourceCodeProB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceCodePro-Regular.otf")));
var stixTwoMathB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "StixTwoMath-Regular.ttf")));
var fontVariantCapsHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'SCP'; src: url('data:font/opentype;base64,{sourceCodeProB64}') format('opentype'); }}" +
    $"@font-face {{ font-family: 'STIX'; src: url('data:font/truetype;base64,{stixTwoMathB64}') format('truetype'); }}" +
    "body { font-family: 'SS3', serif; margin: 0; color: #222 }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.2em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555; font-family: Arial, sans-serif }" +
    "table.caps { border-collapse: collapse; width: 100%; font-size: 16pt }" +
    "table.caps th { font-size: 8pt; font-family: Arial, sans-serif; color: #666; text-align: left; padding: 4px 8px }" +
    "table.caps td { padding: 6px 8px; border-top: 1px solid #ddd }" +
    "table.caps td.label { font-size: 8pt; font-family: Arial, sans-serif; color: #666; vertical-align: middle }" +
    "</style></head><body>" +
    "<h1>Font Variant: Caps, Numerals &amp; Position</h1>" +
    "<p class=\"intro\">PeachPDF prefers a font's real OpenType GSUB substitution for " +
    "<code>font-variant-caps</code>, <code>font-variant-numeric</code> and " +
    "<code>font-variant-position</code>, falling back to a synthesized approximation only where the " +
    "standard specifically allows one.</p>" +

    "<h2>font-variant-caps: real GSUB vs. synthesized fallback</h2>" +
    "<p class=\"intro\">Source Sans 3 has real <code>smcp</code>/<code>c2sc</code>/<code>titl</code> " +
    "data, so its small-caps/all-small-caps/titling-caps below use actual substituted glyphs and the " +
    "text stays a single run. Source Code Pro has none of those features, so the same CSS instead " +
    "synthesizes small-caps/all-small-caps by upper-casing and shrinking the affected letters - " +
    "<code>petite-caps</code>/<code>unicase</code> never synthesize, so on a font that lacks them " +
    "(both bundled fonts here) they render identically to normal text.</p>" +
    "<table class=\"caps\">" +
    "<tr><th>font-variant-caps</th><th>Source Sans 3 (real GSUB)</th><th>Source Code Pro (synthesized/none)</th></tr>" +
    "<tr><td class=\"label\">small-caps</td>" +
    "<td style=\"font-family: 'SS3'; font-variant-caps: small-caps\">Hello World</td>" +
    "<td style=\"font-family: 'SCP'; font-variant-caps: small-caps\">Hello World</td></tr>" +
    "<tr><td class=\"label\">all-small-caps</td>" +
    "<td style=\"font-family: 'SS3'; font-variant-caps: all-small-caps\">Hello World</td>" +
    "<td style=\"font-family: 'SCP'; font-variant-caps: all-small-caps\">Hello World</td></tr>" +
    "<tr><td class=\"label\">titling-caps</td>" +
    "<td style=\"font-family: 'SS3'; font-variant-caps: titling-caps\">Hello World</td>" +
    "<td style=\"font-family: 'SCP'; font-variant-caps: titling-caps\">Hello World</td></tr>" +
    "<tr><td class=\"label\">petite-caps</td>" +
    "<td style=\"font-family: 'SS3'; font-variant-caps: petite-caps\">Hello World</td>" +
    "<td style=\"font-family: 'SCP'; font-variant-caps: petite-caps\">Hello World</td></tr>" +
    "</table>" +

    "<h2>font-variant-numeric: real GSUB numeric substitution</h2>" +
    "<p class=\"intro\">Source Sans 3 has real <code>onum</code>/<code>tnum</code>/<code>zero</code> " +
    "data - oldstyle figures use lowercase-height/descending forms instead of the default lining " +
    "figures, tabular figures fix every digit to the same advance width for column alignment, and " +
    "slashed-zero adds a distinguishing slash through the digit 0.</p>" +
    "<table class=\"caps\" style=\"font-family: 'SS3'; font-size: 20pt\">" +
    "<tr><th>font-variant-numeric</th><th>Sample</th></tr>" +
    "<tr><td class=\"label\">normal (lining, default)</td><td>1234567890</td></tr>" +
    "<tr><td class=\"label\">oldstyle-nums</td><td style=\"font-variant-numeric: oldstyle-nums\">1234567890</td></tr>" +
    "<tr><td class=\"label\">tabular-nums</td><td style=\"font-variant-numeric: tabular-nums\">1234567890</td></tr>" +
    "<tr><td class=\"label\">slashed-zero</td><td style=\"font-variant-numeric: slashed-zero\">1002000</td></tr>" +
    "</table>" +

    "<h2>font-variant-position: real GSUB vs. synthesized fallback</h2>" +
    "<p class=\"intro\">Source Sans 3 has real <code>sups</code>/<code>subs</code> data, so its " +
    "superscripts and subscripts below are the font's own purpose-drawn glyphs. STIX Two Math has " +
    "neither (28 GSUB features, none of them positional), so the same CSS is synthesized instead - " +
    "drawn at a reduced size with a shifted baseline, using the scale and offset that font's own OS/2 " +
    "table recommends. Neither form changes the line's height, unlike vertical-align.</p>" +
    "<table class=\"caps\" style=\"font-size: 16pt\">" +
    "<tr><th>font-variant-position</th><th>Source Sans 3 (real GSUB)</th><th>STIX Two Math (synthesized)</th></tr>" +
    "<tr><td class=\"label\">normal</td>" +
    "<td style=\"font-family: 'SS3'\">E = mc2 and H2O</td>" +
    "<td style=\"font-family: 'STIX'\">E = mc2 and H2O</td></tr>" +
    "<tr><td class=\"label\">super</td>" +
    "<td style=\"font-family: 'SS3'\">E = mc<span style=\"font-variant-position: super\">2</span></td>" +
    "<td style=\"font-family: 'STIX'\">E = mc<span style=\"font-variant-position: super\">2</span></td></tr>" +
    "<tr><td class=\"label\">sub</td>" +
    "<td style=\"font-family: 'SS3'\">H<span style=\"font-variant-position: sub\">2</span>O</td>" +
    "<td style=\"font-family: 'STIX'\">H<span style=\"font-variant-position: sub\">2</span>O</td></tr>" +
    "</table>" +
    "</body></html>";
await SaveShowcaseAsync("font_variant_caps", "Typography & Text", "Font Variant: Caps, Numerals & Position",
    "font-variant-caps prefers a font's real GSUB smcp/c2sc/titl substitution, falling back to a " +
    "synthesized approximation only where the standard allows it; font-variant-numeric activates a " +
    "font's real oldstyle/tabular/slashed-zero GSUB features the same way, and font-variant-position " +
    "uses real sups/subs glyphs where a font has them and synthesizes them from its OS/2 metrics " +
    "where it doesn't.",
    fontVariantCapsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// font-variant-alternates + @font-feature-values (CSS Fonts Module 4 §6.8): named aliases for a
// font's numbered stylistic-set GSUB features. Uses a subset of Recursive (Mono Casual Static
// Regular), a real display font whose ss01/ss02 features are genuine GSUB Single Substitution data
// (a -> a.simple, g -> g.simple - confirmed via direct byte inspection), not a synthetic
// conformance font.
var recursiveB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RecursiveSubset.ttf")));
var fontVariantAlternatesHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'Recursive'; src: url('data:font/truetype;base64,{recursiveB64}') format('truetype'); }}" +
    "@font-feature-values Recursive { @styleset { simple-a: 1; simple-g: 2; } }" +
    "body { font-family: Arial, sans-serif; margin: 0; color: #222 }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.2em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555 }" +
    "code.rule { display: block; font-size: 8pt; background: #f4f4f4; border: 1px solid #ddd; " +
    "border-radius: 3px; padding: 8px 10px; margin: 0 0 1em; white-space: pre-wrap }" +
    "table.alt { border-collapse: collapse; width: 100%; font-size: 24pt; font-family: 'Recursive' }" +
    "table.alt th { font-size: 8pt; font-family: Arial, sans-serif; color: #666; text-align: left; padding: 4px 8px }" +
    "table.alt td { padding: 6px 8px; border-top: 1px solid #ddd }" +
    "table.alt td.label { font-size: 8pt; font-family: Arial, sans-serif; color: #666; vertical-align: middle }" +
    "</style></head><body>" +
    "<h1>CSS <code>font-variant-alternates</code> &amp; <code>@font-feature-values</code></h1>" +
    "<p class=\"intro\"><code>@font-feature-values</code> gives a font's numbered stylistic-set " +
    "features (<code>ss01</code>–<code>ss20</code>, <code>cv01</code>–<code>cv99</code>, and the " +
    "single-feature <code>salt</code>, <code>swsh</code>, <code>ornm</code> and <code>nalt</code>) a " +
    "readable name per font family, so <code>font-variant-alternates</code> can select them by name " +
    "instead of a raw index. Recursive's <code>ss01</code> swaps a double-story lowercase \"a\" for a " +
    "simplified single-story form, and its <code>ss02</code> does the same for \"g\" - both real GSUB " +
    "Single Substitution data, confirmed by direct byte inspection, not an approximation.</p>" +
    "<code class=\"rule\">@font-feature-values Recursive {\n" +
    "  @styleset { simple-a: 1; simple-g: 2; }\n" +
    "}</code>" +
    "<h2>styleset(): named aliases for ss01/ss02</h2>" +
    "<p class=\"intro\">Same text, same font - only <code>font-variant-alternates</code> differs:</p>" +
    "<table class=\"alt\">" +
    "<tr><th>font-variant-alternates</th><th>Sample</th></tr>" +
    "<tr><td class=\"label\">normal (default forms)</td><td>agog gala</td></tr>" +
    "<tr><td class=\"label\">styleset(simple-a)</td>" +
    "<td style=\"font-variant-alternates: styleset(simple-a)\">agog gala</td></tr>" +
    "<tr><td class=\"label\">styleset(simple-g)</td>" +
    "<td style=\"font-variant-alternates: styleset(simple-g)\">agog gala</td></tr>" +
    "<tr><td class=\"label\">styleset(simple-a, simple-g)</td>" +
    "<td style=\"font-variant-alternates: styleset(simple-a, simple-g)\">agog gala</td></tr>" +
    "</table>" +
    "</body></html>";
await SaveShowcaseAsync("font_variant_alternates", "Typography & Text", "font-variant-alternates & @font-feature-values",
    "Named aliases for a font's numbered stylistic-set GSUB features: @font-feature-values maps a " +
    "readable name to a feature index per font family, and font-variant-alternates's styleset()/" +
    "character-variant()/swash()/ornaments()/annotation()/stylistic() functions select them by name. " +
    "Rendered against a subset of Recursive, a real font with genuine ss01/ss02 stylistic-set data.",
    fontVariantAlternatesHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// Bidirectional text: the `dir` global attribute (including `auto`), `<bdo>`/`<bdi>`, CSS
// direction/unicode-bidi, and a real UAX#9 Unicode Bidi Algorithm - not the old whole-word-mirror
// approximation. Real Hebrew (strong-R) content, not placeholder boxes, so per-character
// reordering, digit/Latin run embedding, and L4 bracket mirroring are all visibly correct.
var notoHebrewB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansHebrewSubset.ttf")));
var bidiHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'Hebrew'; src: url('data:font/truetype;base64,{notoHebrewB64}') format('truetype'); }}" +
    // Latin font listed first, Hebrew second: PeachPDF's per-codepoint font fallback (see Text &
    // Fonts -> Per-character font matching) sends each character to the first family that covers
    // it, so one font-family list renders both scripts correctly with no manual span-wrapping.
    "body { font-family: 'SS3', 'Hebrew', sans-serif; margin: 0; color: #222; font-size: 11pt }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.2em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555; font-family: 'SS3', sans-serif }" +
    "p.sample { margin: 0.4em 0; padding: 0.5em 0.7em; background: #f5f6f8; border-left: 3px solid #4a90d9 }" +
    "ul.scores { list-style: none; margin: 0.3em 0; padding: 0 }" +
    "ul.scores li { padding: 0.2em 0.7em }" +
    "</style></head><body>" +
    "<h1>Bidirectional Text</h1>" +
    "<p class=\"intro\">The <code>dir</code> global attribute (including <code>auto</code>), " +
    "<code>&lt;bdo&gt;</code>/<code>&lt;bdi&gt;</code>, and the CSS <code>direction</code>/" +
    "<code>unicode-bidi</code> properties are all driven by a real Unicode Bidirectional Algorithm " +
    "(UAX #9) implementation - per-character reordering of mixed-direction text, not just mirroring " +
    "whole words.</p>" +

    "<h2>Plain right-to-left paragraph (<code>dir=\"rtl\"</code>)</h2>" +
    "<p class=\"sample\" dir=\"rtl\">שלום עולם! זהו טקסט בעברית, והוא זורם מימין לשמאל.</p>" +

    "<h2>Mixed-direction text and bracket mirroring</h2>" +
    "<p class=\"intro\">The embedded Latin word and version number stay left-to-right within the " +
    "surrounding Hebrew paragraph, and the parentheses mirror to match the paragraph's own " +
    "direction - both resolved per character, not per word.</p>" +
    "<p class=\"sample\" dir=\"rtl\">מספר הגרסה של הספרייה (PeachPDF) הוא 0.9.6.</p>" +

    "<h2><code>&lt;bdo&gt;</code>: forcing a direction override</h2>" +
    "<p>Normal: <span>Hello, World!</span> &nbsp;&nbsp; Overridden: " +
    "<bdo dir=\"rtl\">Hello, World!</bdo></p>" +

    "<h2><code>&lt;bdi&gt;</code>: isolating unknown-direction content</h2>" +
    "<p class=\"intro\">A right-to-left username embedded directly in a left-to-right sentence can " +
    "drag the following punctuation/score into the wrong visual position:</p>" +
    "<ul class=\"scores\">" +
    "<li><span>אבי</span>: 9 points</li>" +
    "<li><span>Bob</span>: 15 points</li>" +
    "</ul>" +
    "<p class=\"intro\"><code>&lt;bdi&gt;</code> isolates it, so the score always reads correctly " +
    "regardless of the username's own direction:</p>" +
    "<ul class=\"scores\">" +
    "<li><bdi>אבי</bdi>: 9 points</li>" +
    "<li><bdi>Bob</bdi>: 15 points</li>" +
    "</ul>" +

    "<h2><code>dir=\"auto\"</code>: detecting direction from content</h2>" +
    "<p class=\"sample\" dir=\"auto\">שלום, זהו טקסט עם dir=\"auto\" שמתחיל בעברית - מזוהה אוטומטית " +
    "כטקסט מימין לשמאל.</p>" +
    "<p class=\"sample\" dir=\"auto\">Hello, this is text with dir=\"auto\" that starts in English - " +
    "auto-detected as left-to-right.</p>" +
    "</body></html>";
await SaveShowcaseAsync("bidi_text", "Typography & Text", "Bidirectional Text (dir, bdo, bdi)",
    "A real Unicode Bidi Algorithm (UAX #9): the dir global attribute (incl. auto-detection), " +
    "bdo/bdi, and CSS direction/unicode-bidi, with per-character reordering, digit/Latin run " +
    "embedding, and bracket mirroring in real Hebrew text.",
    bidiHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// Arabic-family complex-script joining (issue #533): each character's initial/medial/final/isolated
// GSUB form is resolved from its own Unicode Joining_Type (not just rendered as its isolated nominal
// glyph), a font's own rlig ligature rules (lam-alef) fire correctly because shaping always runs in
// true logical order regardless of the word's own right-to-left display order, and GPOS cursive
// attachment (curs) connects a calligraphic font's own flowing baseline strokes for fonts whose
// joining relies on it rather than purely on positional substitution.
var notoArabicB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansArabicSubset.ttf")));
var arefRuqaaB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ArefRuqaaSubset.ttf")));
var arabicJoiningHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'ArabicNaskh'; src: url('data:font/truetype;base64,{notoArabicB64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'ArabicRuqaa'; src: url('data:font/truetype;base64,{arefRuqaaB64}') format('truetype'); }}" +
    "body { font-family: 'SS3', sans-serif; margin: 0; color: #222; font-size: 11pt }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.2em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555 }" +
    "p.sample { margin: 0.4em 0; padding: 0.5em 0.7em; background: #f5f6f8; border-left: 3px solid #4a90d9; font-size: 20pt }" +
    "p.sample.naskh { font-family: 'ArabicNaskh', sans-serif }" +
    "p.sample.ruqaa { font-family: 'ArabicRuqaa', sans-serif; font-size: 28pt }" +
    "table.forms { border-collapse: collapse; margin: 0.4em 0; font-size: 9pt }" +
    "table.forms th, table.forms td { border: 1px solid #ccc; padding: 0.3em 0.6em; text-align: center }" +
    "table.forms td.letters { font-family: 'ArabicNaskh', sans-serif; font-size: 16pt }" +
    "</style></head><body>" +
    "<h1>Arabic-Family Complex-Script Joining</h1>" +
    "<p class=\"intro\">Each character's initial/medial/final/isolated GSUB form is resolved from its " +
    "own Unicode <code>Joining_Type</code>, not rendered as an unjoined isolated glyph - the same " +
    "shaping a browser applies.</p>" +

    "<h2>Positional joining forms</h2>" +
    "<p class=\"intro\">بيت (\"house\", Beh-Yeh-Teh) - each letter takes its own " +
    "initial/medial/final connected form:</p>" +
    "<p class=\"sample naskh\">بيت</p>" +

    "<p class=\"intro\">The same dual-joining letter (Beh) repeated once, twice, and three times in a " +
    "row forces isolated, then initial+final, then initial+medial+final forms of the identical " +
    "character - the standard way to demonstrate all four forms unambiguously:</p>" +
    "<table class=\"forms\">" +
    "<tr><th>Isolated</th><th>Initial + Final</th><th>Initial + Medial + Final</th></tr>" +
    "<tr><td class=\"letters\">ب</td><td class=\"letters\">بب</td><td class=\"letters\">ببب</td></tr>" +
    "</table>" +

    "<h2>Lam-alef ligature (<code>rlig</code>)</h2>" +
    "<p class=\"intro\">لا (\"no\", Lam-Alef) - a font's own required ligature connects the " +
    "two letters into one glyph. This needs shaping to run in true logical (Lam-then-Alef) order even " +
    "though the word displays right-to-left - reversing the source text before shaping (as a naive " +
    "implementation might) would prevent this exact ligature rule from ever matching.</p>" +
    "<p class=\"sample naskh\">لا</p>" +

    "<h2>GPOS cursive attachment (<code>curs</code>)</h2>" +
    "<p class=\"intro\">A calligraphic font (\"Aref Ruqaa\") whose flowing baseline connections rely on " +
    "GPOS cursive attachment, not only positional substitution - each letter's own exit/entry anchor " +
    "points connect edge to edge:</p>" +
    "<p class=\"sample ruqaa\">بيتالف</p>" +
    "</body></html>";
await SaveShowcaseAsync("arabic_joining", "Typography & Text", "Arabic-Family Complex-Script Joining",
    "Positional initial/medial/final/isolated GSUB joining forms resolved from each character's own " +
    "Unicode Joining_Type, a font's rlig lam-alef ligature, and GPOS cursive attachment (curs) for a " +
    "calligraphic font whose joining relies on it.",
    arabicJoiningHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// Devanagari Universal Shaping Engine (USE) syllable reordering (issue #533, Phase 5b): each
// character's Indic_Syllabic_Category/Indic_Positional_Category (not just its raw joining/ligature
// behavior) drives syllable classification, a font's own nukt/ccmp/locl/akhn/rphf/half/rkrf/cjct
// GSUB features form conjuncts and reph, and the resulting glyph list is reordered (repha
// repositioning, pre-base matra movement) before the font's own abvs/blws/pres/psts features apply
// their final presentation forms - all ported from HarfBuzz's own USE shaper.
var notoDevanagariB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansDevanagariSubset.ttf")));
var devanagariUseHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'Devanagari'; src: url('data:font/truetype;base64,{notoDevanagariB64}') format('truetype'); }}" +
    "body { font-family: 'SS3', sans-serif; margin: 0; color: #222; font-size: 11pt }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.2em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555 }" +
    "p.sample { margin: 0.4em 0; padding: 0.5em 0.7em; background: #f5f6f8; border-left: 3px solid #d9824a; font-size: 24pt; font-family: 'Devanagari', sans-serif }" +
    "table.forms { border-collapse: collapse; margin: 0.4em 0; font-size: 9pt }" +
    "table.forms th, table.forms td { border: 1px solid #ccc; padding: 0.3em 0.6em; text-align: center }" +
    "table.forms td.letters { font-family: 'Devanagari', sans-serif; font-size: 18pt }" +
    "</style></head><body>" +
    "<h1>Devanagari Universal Shaping Engine (USE) Syllable Reordering</h1>" +
    "<p class=\"intro\">Each character's Unicode <code>Indic_Syllabic_Category</code>/" +
    "<code>Indic_Positional_Category</code> drives syllable classification and glyph reordering - " +
    "the same shaping a browser applies, ported from HarfBuzz's own Universal Shaping Engine.</p>" +

    "<h2>Pre-base vowel sign (matra) reordering</h2>" +
    "<p class=\"intro\">कि (\"ki\") - the vowel sign I is a pre-base matra: in the source text it " +
    "follows the consonant it belongs to, but a real font expects it drawn <em>before</em> the " +
    "consonant, so the shaped glyph list is reordered accordingly:</p>" +
    "<p class=\"sample\">कि</p>" +

    "<h2>Conjunct formation (<code>cjct</code>) with a pre-base matra</h2>" +
    "<p class=\"intro\">क्षि (\"kṣi\") - the two-consonant conjunct क्ष (KA+VIRAMA+SSA) fuses into a " +
    "single glyph via the font's own <code>cjct</code> feature, and the following vowel sign still " +
    "reorders to before the whole fused conjunct:</p>" +
    "<p class=\"sample\">क्षि</p>" +

    "<h2>Reph formation (<code>rphf</code>) and repositioning</h2>" +
    "<p class=\"intro\">र्कि (\"rki\") - a word-initial रँ (RA+VIRAMA) forms a reph via the font's own " +
    "<code>rphf</code> feature, which the shaping engine then moves forward past the base consonant " +
    "to sit beside the pre-base vowel sign - the font's own <code>pres</code> feature then fuses the " +
    "repositioned reph with the vowel sign into one combined presentation glyph:</p>" +
    "<p class=\"sample\">र्कि</p>" +

    "<h2>Vowel modifiers (anusvara, visarga) - not reordered</h2>" +
    "<p class=\"intro\">कं (anusvara) and कः (visarga) attach after the base without any glyph " +
    "reordering - only pre-base vowel signs move:</p>" +
    "<table class=\"forms\">" +
    "<tr><th>Anusvara</th><th>Visarga</th><th>Post-base vowel (contrast)</th></tr>" +
    "<tr><td class=\"letters\">कं</td><td class=\"letters\">कः</td><td class=\"letters\">का</td></tr>" +
    "</table>" +
    "</body></html>";
await SaveShowcaseAsync("devanagari_use", "Typography & Text", "Devanagari Universal Shaping Engine (USE)",
    "Indic_Syllabic_Category/Indic_Positional_Category-driven syllable classification, GSUB conjunct " +
    "(cjct)/reph (rphf) formation, and the resulting glyph reorder (repha repositioning, pre-base " +
    "matra movement) - ported from HarfBuzz's own Universal Shaping Engine.",
    devanagariUseHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// Universal Shaping Engine (USE) syllable reordering extended to Bengali, Gujarati, and Tamil
// (issue #533, Phase 5c) - the same classify/scan/reorder pipeline as devanagari_use above, now
// also covering Bengali's own two USE categories Devanagari never reaches (GB - Consonant
// Placeholder, U+0980 BENGALI ANJI; FMAbv - Syllable Modifier, U+09FE BENGALI SANDHI MARK) and a
// real Noto Sans Gujarati font whose abvs feature needed a GSUB fix (nested contextual-lookup
// recursion) to pick the correct pre-base-matra glyph variant - see this feature's own
// recent-fixes entry.
var notoBengaliB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansBengaliSubset.ttf")));
var notoGujaratiB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansGujaratiSubset.ttf")));
var notoTamilB64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansTamilSubset.ttf")));
var bengaliGujaratiTamilUseHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'Bengali'; src: url('data:font/truetype;base64,{notoBengaliB64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'Gujarati'; src: url('data:font/truetype;base64,{notoGujaratiB64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'Tamil'; src: url('data:font/truetype;base64,{notoTamilB64}') format('truetype'); }}" +
    "body { font-family: 'SS3', sans-serif; margin: 0; color: #222; font-size: 11pt }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 12pt; margin: 1.2em 0 0.2em }" +
    "h3 { font-size: 10.5pt; margin: 0.8em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555 }" +
    "p.sample { margin: 0.3em 0; padding: 0.4em 0.7em; background: #f5f6f8; border-left: 3px solid #4a8fd9; font-size: 20pt }" +
    "p.sample.bengali { font-family: 'Bengali', sans-serif }" +
    "p.sample.gujarati { font-family: 'Gujarati', sans-serif; border-left-color: #7fbf4a }" +
    "p.sample.tamil { font-family: 'Tamil', sans-serif; border-left-color: #d94a8f }" +
    "table.forms { border-collapse: collapse; margin: 0.3em 0; font-size: 9pt }" +
    "table.forms th, table.forms td { border: 1px solid #ccc; padding: 0.3em 0.6em; text-align: center }" +
    "table.forms td.bengali { font-family: 'Bengali', sans-serif; font-size: 16pt }" +
    "</style></head><body>" +
    "<h1>Universal Shaping Engine (USE): Bengali, Gujarati &amp; Tamil</h1>" +
    "<p class=\"intro\">The same Indic_Syllabic_Category/Indic_Positional_Category-driven classify/" +
    "scan/reorder pipeline as Devanagari, extended to three more Brahmic scripts.</p>" +

    "<h2>Bengali</h2>" +
    "<h3>Pre-base vowel sign reordering and conjunct formation</h3>" +
    "<p class=\"intro\">কি (\"ki\") reorders its pre-base matra before the base; ক্ষি (\"kṣi\") fuses " +
    "the KA+VIRAMA+SSA conjunct via the font's own <code>cjct</code> feature before reordering the " +
    "following matra:</p>" +
    "<p class=\"sample bengali\">কি &nbsp; ক্ষি</p>" +
    "<h3>Bengali's own USE categories: Consonant Placeholder (GB) and Syllable Modifier (FMAbv)</h3>" +
    "<p class=\"intro\">ঀ (BENGALI ANJI, U+0980) is a Consonant Placeholder - grouped with an " +
    "ordinary base consonant as a syllable start; a base consonant followed by the Sandhi Mark " +
    "(U+09FE, a Syllable Modifier) stays in logical order, resolved to above-base position by its " +
    "own GPOS mark anchoring - two categories no Devanagari codepoint ever reaches:</p>" +
    "<table class=\"forms\">" +
    "<tr><th>Consonant Placeholder (Anji) alone</th><th>Base + Sandhi Mark</th></tr>" +
    "<tr><td class=\"bengali\">ঀ</td><td class=\"bengali\">ক৾</td></tr>" +
    "</table>" +

    "<h2>Gujarati</h2>" +
    "<h3>Pre-base vowel sign reordering, conjunct and reph formation</h3>" +
    "<p class=\"intro\">કિ (\"ki\") reorders its pre-base matra; ક્ષિ (\"kṣi\") fuses the conjunct; " +
    "ર્કિ (\"rki\") forms a reph via <code>rphf</code> and fuses it with the repositioned matra into " +
    "one combined presentation glyph via <code>pres</code> - the same mechanism as Devanagari's own " +
    "reph+matra fusion, verified against this real font too:</p>" +
    "<p class=\"sample gujarati\">કિ &nbsp; ક્ષિ &nbsp; ર્કિ</p>" +

    "<h2>Tamil</h2>" +
    "<h3>Pre-base vowel sign reordering and a Grantha-loanword conjunct</h3>" +
    "<p class=\"intro\">கெ (\"ke\") reorders its pre-base matra; க்ஷெ (\"kṣe\") fuses KA+VIRAMA+SSA " +
    "(SSA is a Grantha-origin letter Tamil borrows for Sanskrit loanwords) into one conjunct glyph " +
    "via the font's own <code>cjct</code>/<code>half</code> features, exactly like a native Indic " +
    "conjunct:</p>" +
    "<p class=\"sample tamil\">கெ &nbsp; க்ஷெ</p>" +
    "</body></html>";
await SaveShowcaseAsync("bengali_gujarati_tamil_use", "Typography & Text", "USE: Bengali, Gujarati & Tamil",
    "Universal Shaping Engine syllable reordering extended to three more Brahmic scripts - pre-base " +
    "matra movement, conjunct (cjct)/reph (rphf) formation, and Bengali's own two additional USE " +
    "categories (Consonant Placeholder, Syllable Modifier) no Devanagari codepoint reaches.",
    bengaliGujaratiTamilUseHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// SVG <text> Arabic-family joining / Devanagari USE reordering: the same real per-character
// joining-form resolution, GSUB shaping, and USE syllable reordering the two HTML showcases above
// demonstrate, now applied to native SVG <text>/<tspan> content - previously SVG rendered Arabic and
// Devanagari text as isolated, unjoined, unreordered nominal glyphs only (see
// .claude/accepted-gaps/no-text-shaping.md's prior "SVG does not resolve script, joining forms, or
// USE categories at all" entry). SvgRenderer.ResolveComplexScriptRuns detects each maximal run of
// mutually-joining/reordering characters and shapes/measures/paints it as one atomic unit, exactly
// like HTML's per-word CssRectWord treatment, including reversing only the resulting glyph list (never
// the source text) for a right-to-left run so a font's own contextual rlig rules still match.
var svgArabicDevanagariHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'ArabicNaskh'; src: url('data:font/truetype;base64,{notoArabicB64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'ArabicRuqaa'; src: url('data:font/truetype;base64,{arefRuqaaB64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'Devanagari'; src: url('data:font/truetype;base64,{notoDevanagariB64}') format('truetype'); }}" +
    "body { font-family: 'SS3', sans-serif; margin: 0; color: #222; font-size: 11pt }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.8em; color: #555 }" +
    "</style></head><body>" +
    "<h1>SVG Text: Arabic-Family Joining &amp; Devanagari USE Reordering</h1>" +
    "<p class=\"intro\">The same real joining-form resolution, GSUB shaping, and USE syllable " +
    "reordering as HTML text (see the two showcases above), now applied inside a native SVG " +
    "&lt;text&gt; element.</p>" +
    "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 700 520\" width=\"700\" height=\"520\">" +
    "<style>" +
    ".label { font-family: 'SS3', sans-serif; font-size: 12px; fill: #555 }" +
    ".naskh { font-family: 'ArabicNaskh', sans-serif; font-size: 30px }" +
    ".ruqaa { font-family: 'ArabicRuqaa', sans-serif; font-size: 40px }" +
    ".deva { font-family: 'Devanagari', sans-serif; font-size: 30px }" +
    ".box { fill: #f5f6f8; stroke: #ddd }" +
    "</style>" +

    "<rect class=\"box\" x=\"10\" y=\"10\" width=\"680\" height=\"90\"/>" +
    "<text class=\"label\" x=\"20\" y=\"30\">Positional joining forms (Beh-Yeh-Teh, direction=\"rtl\")</text>" +
    "<text class=\"naskh\" x=\"670\" y=\"75\" direction=\"rtl\" text-anchor=\"end\">بيت</text>" +

    "<rect class=\"box\" x=\"10\" y=\"110\" width=\"680\" height=\"90\"/>" +
    "<text class=\"label\" x=\"20\" y=\"130\">Lam-alef ligature (rlig), direction=\"rtl\"</text>" +
    "<text class=\"naskh\" x=\"670\" y=\"180\" direction=\"rtl\" text-anchor=\"end\">لا</text>" +

    "<rect class=\"box\" x=\"10\" y=\"210\" width=\"680\" height=\"110\"/>" +
    "<text class=\"label\" x=\"20\" y=\"230\">GPOS cursive attachment (Aref Ruqaa), direction=\"rtl\"</text>" +
    "<text class=\"ruqaa\" x=\"670\" y=\"295\" direction=\"rtl\" text-anchor=\"end\">بيتالف</text>" +

    "<rect class=\"box\" x=\"10\" y=\"330\" width=\"680\" height=\"90\"/>" +
    "<text class=\"label\" x=\"20\" y=\"350\">Devanagari pre-base matra / conjunct / reph reordering</text>" +
    "<text class=\"deva\" x=\"20\" y=\"400\">कि — क्षि — र्कि</text>" +

    "<rect class=\"box\" x=\"10\" y=\"430\" width=\"680\" height=\"80\"/>" +
    "<text class=\"label\" x=\"20\" y=\"450\">Mixed script: Latin, embedded Arabic, embedded Devanagari in one line</text>" +
    "<text x=\"20\" y=\"495\" font-size=\"20px\">Hello <tspan class=\"naskh\" font-size=\"24px\">بيت</tspan> " +
    "<tspan class=\"deva\" font-size=\"24px\">कि</tspan></text>" +
    "</svg>" +
    "</body></html>";
await SaveShowcaseAsync("svg_arabic_devanagari_shaping", "Graphics & Effects", "SVG Text: Arabic-Family Joining & Devanagari USE",
    "Native SVG <text>/<tspan> content shaped with the same real per-character joining-form " +
    "resolution, GSUB substitution (including a font's own rlig lam-alef ligature and GPOS cursive " +
    "attachment), and Devanagari USE syllable reordering HTML text already had - previously SVG " +
    "rendered this text as isolated, unjoined, unreordered nominal glyphs only.",
    svgArabicDevanagariHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// visibility: collapse on table rows/row-groups/columns/column-groups (CSS 2.1 §17.6.1): unlike
// visibility: hidden, which only skips painting and still reserves the element's layout space, a
// collapsed table row/column is removed from the table's geometry entirely - the rows/columns after
// it shift up/left to fill the gap, as if it had display: none.
var visibilityCollapseHtml = """
    <!DOCTYPE html><html><head><style>
    @page { size: a5 landscape; margin: 12mm }
    body { font: 10pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 14pt; margin: 0 0 0.3em }
    h2 { font-size: 11pt; margin: 1.2em 0 0.4em }
    p.intro { color: #6b7280; font-size: 9pt; margin: 0 0 0.6em; max-width: 34em }
    table { border-collapse: separate; border-spacing: 4pt 0; margin-bottom: 0.6em }
    td, th { border: 0.75pt solid #94a3b8; padding: 4pt 8pt; text-align: left }
    th { background: #eef2ff }
    tr.collapsed, col.collapsed { visibility: collapse }
    tr.hidden { visibility: hidden }
    </style></head><body>
    <h1>visibility: collapse on Table Rows and Columns</h1>
    <p class="intro">A collapsed row/column takes no layout space at all - the content around it
    closes the gap, exactly as if it had <code>display: none</code>. This is distinct from
    <code>visibility: hidden</code>, which keeps reserving the element's space and only omits
    painting it.</p>

    <h2>Row collapse (row 2 has <code>visibility: collapse</code>)</h2>
    <table>
    <tr><th>Row</th><th>Status</th></tr>
    <tr><td>Row 1</td><td>Visible</td></tr>
    <tr class="collapsed"><td>Row 2</td><td>Collapsed - not rendered, and its space is reclaimed</td></tr>
    <tr><td>Row 3</td><td>Follows immediately after Row 1</td></tr>
    </table>

    <h2>Compare with <code>visibility: hidden</code> (row 2's space stays reserved)</h2>
    <table>
    <tr><th>Row</th><th>Status</th></tr>
    <tr><td>Row 1</td><td>Visible</td></tr>
    <tr class="hidden"><td>Row 2</td><td>Hidden - not painted, but still reserves its space</td></tr>
    <tr><td>Row 3</td><td>A visible gap remains where Row 2 was</td></tr>
    </table>

    <h2>Column collapse (middle column has <code>visibility: collapse</code>)</h2>
    <table>
    <colgroup>
    <col style="width:120pt">
    <col class="collapsed" style="width:120pt">
    <col style="width:120pt">
    </colgroup>
    <tr><th>Column A</th><th>Column B</th><th>Column C</th></tr>
    <tr><td>Visible</td><td>Collapsed</td><td>Follows Column A directly</td></tr>
    </table>
    </body></html>
    """;
await SaveShowcaseAsync("table_visibility_collapse", "Layout", "Table Row/Column Collapse",
    "visibility: collapse on table rows, row-groups, columns, and column-groups (CSS 2.1 §17.6.1): "
    + "the collapsed row/column takes no layout space and its neighbors shift in to close the gap, "
    + "unlike visibility: hidden which still reserves the space.",
    visibilityCollapseHtml, pdfConfig);

// --- caption-side showcase (issue #705) ---
//
// caption-side was previously unimplemented: a <table>'s <caption> child was never assigned a
// position at all by CssLayoutEngineTable.AssignBoxKinds, so it painted with degenerate zero-height
// geometry. This exercises both values (CSS 2.1 §17.4's only two: top, the initial value, and
// bottom), stacked above or below the row grid and stretched to the table's own content width.
var tableCaptionHtml = """
    <!DOCTYPE html><html><head><style>
    @page { size: a5 landscape; margin: 12mm }
    body { font: 10pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 14pt; margin: 0 0 0.3em }
    h2 { font-size: 11pt; margin: 1.2em 0 0.4em }
    p.intro { color: #6b7280; font-size: 9pt; margin: 0 0 0.6em; max-width: 34em }
    table { border-collapse: separate; border-spacing: 0; margin-bottom: 0.6em; border: 1.5pt solid #334155; background: #eef2ff }
    caption { font-weight: 600; padding: 5pt; text-align: center }
    td, th { border: 0.75pt solid #94a3b8; padding: 4pt 8pt; text-align: left; background: white }
    th { background: #f8fafc }
    #bottom-table caption { caption-side: bottom }
    </style></head><body>
    <h1>caption-side: Positioning a &lt;caption&gt; Above or Below a Table</h1>
    <p class="intro">caption-side's only two values (CSS 2.1 §17.4) stack the caption above the row
    grid (<code>top</code>, the initial value) or below it (<code>bottom</code>) - always spanning the
    table's own content width. The <code>&lt;table&gt;</code> element's own border/background (CSS 2.1
    §17.4) wrap the row grid only - the caption sits outside them, in its own unbordered/unfilled area.</p>

    <h2>caption-side: top (default)</h2>
    <table>
    <caption>Table 1. Quarterly results by region</caption>
    <tr><th>Region</th><th>Q1</th><th>Q2</th></tr>
    <tr><td>North</td><td>120</td><td>135</td></tr>
    <tr><td>South</td><td>98</td><td>110</td></tr>
    </table>

    <h2>caption-side: bottom</h2>
    <table id="bottom-table">
    <caption>Table 2. Same data, caption moved below the table</caption>
    <tr><th>Region</th><th>Q1</th><th>Q2</th></tr>
    <tr><td>North</td><td>120</td><td>135</td></tr>
    <tr><td>South</td><td>98</td><td>110</td></tr>
    </table>
    </body></html>
    """;
await SaveShowcaseAsync("table_caption", "Layout", "Table Captions (caption-side)",
    "caption-side: top (the initial value) and bottom (CSS 2.1 §17.4), stacking a <table>'s <caption> "
    + "above or below its row grid and stretching it to the table's own content width, with the "
    + "<table>'s own border/background wrapping the row grid only.",
    tableCaptionHtml, pdfConfig);

// --- table/row explicit height showcase (issue #1116) ---
//
// height/min-height on a <table> or <tr> was previously silently ignored - the table/row always
// laid out at its content height, which also made vertical-align on a cell look like a no-op (a
// one-line-tall row leaves top/middle/bottom nowhere to go). CSS 2.1 §17.5.3 makes both a minimum:
// the table's used height is the maximum of its specified height and the rows' natural total, with
// surplus distributed proportionally across rows - the same rule this engine already applies to
// column-width surplus.
var tableRowHeightHtml = """
    <!DOCTYPE html><html><head><style>
    @page { size: a5 landscape; margin: 12mm }
    body { font: 9.5pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 14pt; margin: 0 0 0.3em }
    h2 { font-size: 10.5pt; margin: 1.1em 0 0.35em; break-after: avoid }
    p.intro { color: #6b7280; font-size: 9pt; margin: 0 0 0.6em; max-width: 44em }
    table { width: 100%; border-collapse: collapse; margin: 0 0 0.4em }
    td, th { border: 0.75pt solid #94a3b8; padding: 3pt 8pt; text-align: left }
    th { background: #f1f5f9; font-weight: 600 }
    #valign td { width: 33%; background: #eef2ff }
    #band tr { height: 26pt }
    #band td:first-child { color: #6b7280; width: 30% }
    </style></head><body>

    <h1>height / min-height on &lt;table&gt; and &lt;tr&gt;</h1>
    <p class="intro">CSS 2.1 &sect;17.5.3: a table's (or row's) specified height is a
    <em>minimum</em>, never a clip - the used height is the greater of the specified value and the
    rows' own natural content height, with any surplus spread proportionally across the rows.</p>

    <h2>1 &mdash; table height, with vertical-align finally having room to matter</h2>
    <table id="valign" style="height: 70pt">
    <tr>
    <td style="vertical-align: top">top</td>
    <td style="vertical-align: middle">middle</td>
    <td style="vertical-align: bottom">bottom</td>
    </tr>
    </table>

    <h2>2 &mdash; per-row height, for a uniform banded/letterhead layout</h2>
    <table id="band">
    <tr><td>Invoice #</td><td>INV-2026-0142</td></tr>
    <tr><td>Bill to</td><td>Acme Logistics, 400 Harbor Way</td></tr>
    <tr><td>Notes</td><td>Net 30. A short cell and a much longer wrapped one still share the exact
    same 26pt row height, since it's declared on each &lt;tr&gt; rather than left to content.</td></tr>
    </table>
    </body></html>
    """;
await SaveShowcaseAsync("table_row_height", "Layout", "Table & Row Height (height / min-height)",
    "height/min-height on a <table> or <tr> (CSS 2.1 §17.5.3): a minimum, never a clip - the table "
    + "grows to fit an explicit height taller than its content, with the surplus distributed "
    + "proportionally across rows, giving vertical-align real room to differ within a row.",
    tableRowHeightHtml, pdfConfig);

// --- table-layout showcase (issue #918) ---
//
// table-layout was previously parsed but never wired into CssLayoutEngineTable, so a table declared
// table-layout: fixed rendered identically to table-layout: auto. This shows the two CSS 2.1 §17.5.2
// algorithms on byte-identical markup and data: automatic (§17.5.2.2, content-driven - the widest
// cell wins its column) versus fixed (§17.5.2.1 - <col>/first-row widths, then an equal split, with
// cell content never measured). The third table shows the <col>/first-row priority order and a
// colspan width being divided over its spanned columns.
var tableLayoutHtml = """
    <!DOCTYPE html><html><head><style>
    @page { size: a4; margin: 15mm }
    body { font: 9.5pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10.5pt; margin: 1.1em 0 0.35em; break-after: avoid }
    p.intro { color: #6b7280; font-size: 9pt; margin: 0 0 0.8em; max-width: 44em }
    p.note { color: #6b7280; font-size: 8pt; margin: 0.25em 0 0 }
    table { width: 100%; border-collapse: collapse; margin: 0 0 0.2em }
    th, td { border: 0.75pt solid #94a3b8; padding: 4pt 6pt; text-align: left; vertical-align: top }
    th { background: #f1f5f9; font-weight: 600 }
    table.fixed { table-layout: fixed }
    table.auto { table-layout: auto }
    #priority col.narrow { width: 15% }
    </style></head><body>

    <h1>table-layout: Choosing the Column-Width Algorithm</h1>
    <p class="intro">CSS 2.1 &sect;17.5.2 defines two ways to size a table's columns.
    <code>auto</code> (&sect;17.5.2.2) measures every cell in every row and lets the widest content
    win its column. <code>fixed</code> (&sect;17.5.2.1) never looks at cell content at all: a column
    takes its width from a <code>&lt;col&gt;</code> element, failing that from that column's cell in
    the <em>first row</em>, and every column left over splits the remaining space equally. Both
    tables below hold exactly the same markup and the same text.</p>

    <h2>1 &mdash; table-layout: auto (the default)</h2>
    <table class="auto">
    <tr><th>Item</th><th>Description</th><th>Status</th></tr>
    <tr><td>SKU-1</td><td>A deliberately long description that pulls its own column wide, squeezing
    everything beside it - the column is sized from this cell's content.</td><td>OK</td></tr>
    <tr><td>SKU-2</td><td>Short</td><td>OK</td></tr>
    </table>
    <p class="note">The middle column swelled to fit its longest cell; the outer two were squeezed
    down to roughly their own content width.</p>

    <h2>2 &mdash; table-layout: fixed</h2>
    <table class="fixed">
    <tr><th>Item</th><th>Description</th><th>Status</th></tr>
    <tr><td>SKU-1</td><td>A deliberately long description that pulls its own column wide, squeezing
    everything beside it - the column is sized from this cell's content.</td><td>OK</td></tr>
    <tr><td>SKU-2</td><td>Short</td><td>OK</td></tr>
    </table>
    <p class="note">Same markup, same text: no cell was measured, so all three columns are exactly
    one third of the table each and the long description simply wraps inside its own column.</p>

    <h2>3 &mdash; fixed with &lt;col&gt; and first-row widths</h2>
    <table class="fixed" id="priority">
    <colgroup><col class="narrow"><col><col><col></colgroup>
    <tr><th>#</th><th colspan="2" style="width: 50%">Spanned header (50%, split over two columns)</th><th>Rest</th></tr>
    <tr><td>1</td><td>25%</td><td>25%</td><td>remainder</td></tr>
    <tr><td>2</td><td>ignored width: 90%</td><td style="width: 90%">ignored</td><td>&mdash;</td></tr>
    </table>
    <p class="note">Column 1 takes its 15% from the <code>&lt;col&gt;</code>. The first row's
    <code>colspan="2"</code> header states 50%, divided evenly over the two columns it spans (25%
    each). The last column takes the 35% remainder (100% - 15% - 25% - 25%). The second row's 90%
    is ignored entirely - under fixed layout no row after the first can change a column's width.</p>

    </body></html>
    """;
await SaveShowcaseAsync("table_layout_fixed", "Layout", "Fixed vs Automatic Table Layout (table-layout)",
    "CSS 2.1 §17.5.2's two column-width algorithms on identical markup: auto measures every cell and "
    + "lets the widest content win its column, while fixed sizes columns from <col> elements and the "
    + "first row alone - never measuring cell content - and splits the remaining space equally, so long "
    + "text wraps inside its column instead of widening it. Also shows fixed layout's priority order "
    + "(<col> beats a first-row cell), a colspan width divided over its spanned columns, and a later "
    + "row's width being correctly ignored.",
    tableLayoutHtml, pdfConfig);

// --- collapsed border conflict resolution showcase (issue #735) ---
//
// border-collapse: collapse previously overlapped adjacent rows/columns by a flat, hardcoded 1pt
// regardless of the borders actually declared, and every participating box painted its own border
// independently - so a later row's opaque background could paint over and erase the border it shared
// with the row above (#735). This exercises the real CSS 2.1 §17.6.2 resolution that replaced it: width
// wins, then style priority, then origin priority (cell > row > row-group > column > column-group >
// table), then position - plus <col>/<colgroup> border/background participation and a repeated <thead>
// whose boundary to the body re-resolves correctly on every page, not just the first.
var collapsedBorderRepeatRows = string.Join("\n", Enumerable.Range(1, 24).Select(i =>
    i == 1
        ? "<tr class=\"first\"><td>Row 1</td><td>The first body row's own border wins here - it's the true DOM neighbor of the header.</td></tr>"
        : $"<tr><td>Row {i}</td><td>An ordinary row - every later page's header boundary resolves against whichever row actually starts that page, not row 1's.</td></tr>"));

var collapsedBorderHtml = $$"""
    <!DOCTYPE html><html><head><style>
    @page { size: a4 portrait; margin: 15mm }
    body { font: 10pt Helvetica, Arial, sans-serif; margin: 0; color: #1f2937 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 11pt; margin: 1.2em 0 0.4em }
    p.intro { color: #6b7280; font-size: 9pt; margin: 0 0 0.6em; max-width: 34em }
    table { border-collapse: collapse; width: 100%; margin-bottom: 0.8em }
    td, th { padding: 5pt 8pt; text-align: left }

    /* Conflict matrix: three adjacent cells whose shared edges are contested by a different origin
       each time, so the resolved border visibly differs edge to edge. */
    #matrix td, #matrix th { border: 1pt solid #cbd5e1 }
    #matrix tr { border-bottom: 4pt solid #f59e0b }               /* row-origin: medium, orange */
    #matrix td.cell-wins { border-bottom: 8pt double #0b4f6c }      /* cell-origin: widest, wins outright */
    #matrix { border: 2pt solid #7c3aed }                          /* table-origin: only at the outer edge */

    /* <col>/<colgroup> participation: a colgroup border/background competing with (and losing to) a
       narrower <col>-level border, and both painting behind the cells' own backgrounds. */
    #cols colgroup.grp { border: 3pt solid #059669; background: #ecfdf5 }
    #cols col.narrow { border-right: 1pt solid #dc2626; background: #fef2f2 }
    #cols td, #cols th { border: 0.75pt solid #94a3b8; background: white }

    /* border-style: hidden suppresses an edge outright, regardless of what competes for it. */
    #hidden td, #hidden th { border: 2pt solid #0b4f6c }
    #hidden td.gap { border-right-style: hidden }

    #repeat thead th { border: 1pt solid #0b4f6c; background: #0b4f6c; color: white; padding: 6pt 8pt }
    #repeat tbody td { border: 1pt solid #94a3b8; padding: 5pt 8pt }
    #repeat tr.first td { border-top: 6pt solid #dc2626 }
    </style></head><body>
    <h1>Collapsed Table Borders: CSS 2.1 §17.6.2 Conflict Resolution</h1>
    <p class="intro">A <code>border-collapse: collapse</code> table resolves exactly one border per shared
    grid line - the widest declared border wins, ties break by style priority, further ties by how
    specific the declaring box is (cell &gt; row &gt; row-group &gt; column &gt; column-group &gt; table),
    and finally by position. Every other participant's own border at that edge is suppressed rather than
    separately painted.</p>

    <h2>Width, then origin, decide each edge</h2>
    <table id="matrix">
    <tr><td>A</td><td>B</td></tr>
    <tr><td class="cell-wins">The 8pt double border on this cell's own bottom edge outranks the row's 4pt orange border below - cell beats row regardless of which one is wider here.</td><td>Where no cell states an opinion, the row's own 4pt orange border wins instead.</td></tr>
    </table>

    <h2>&lt;col&gt;/&lt;colgroup&gt; border and background participation</h2>
    <table id="cols">
    <colgroup class="grp"><col class="narrow"><col></colgroup>
    <tr><th>Column A</th><th>Column B</th></tr>
    <tr><td>The colgroup's 3pt green border frames both columns; column A's own narrower red border wins the shared edge between them.</td><td>Column backgrounds layer under the cells' own white background, per CSS 2.1 §17.5.1's table &rarr; column-group &rarr; column &rarr; row &rarr; cell order.</td></tr>
    </table>

    <h2>border-style: hidden suppresses an edge outright</h2>
    <table id="hidden">
    <tr><th>Left</th><th class="gap">Right</th></tr>
    <tr><td>No border here</td><td class="gap">or here - hidden always wins, whatever else competes for this edge</td></tr>
    </table>

    <h2>A repeated &lt;thead&gt;'s boundary resolves fresh on every page</h2>
    <p class="intro">Only row 1 - the header's real DOM neighbor - has the bold red top border. On the
    first page the header sits directly above it, so that border is what's resolved and drawn. On every
    later page the header repeats above a different row instead, and the boundary is re-resolved against
    <em>that</em> row - not reused from page 1 - so only the first page shows the red border.</p>
    <table id="repeat">
    <thead><tr><th>Row</th><th>Note</th></tr></thead>
    <tbody>
    {{collapsedBorderRepeatRows}}
    </tbody>
    </table>
    </body></html>
    """;
await SaveShowcaseAsync("table_collapsed_border_resolution", "Layout", "Collapsed Table Border Resolution",
    "CSS 2.1 §17.6.2's full border-conflict resolution for border-collapse: collapse - width, then style "
    + "priority, then cell/row/row-group/column/column-group/table origin, then position - plus "
    + "<col>/<colgroup> border/background participation and a repeated <thead> whose boundary to the body "
    + "re-resolves correctly on every page (issue #735).",
    collapsedBorderHtml, pdfConfig);

// --- interactive PDF forms showcase ---
//
// See docs/html-css-support.md#interactive-pdf-forms-support. EnableInteractivePdfForms turns
// <input>/<select> into real fillable AcroForm fields (text, checkbox, radio group, select) instead
// of the default static rendering - this is also what actually exercises the flag-on static-look
// painter (checkbox/radio glyphs, bordered text/select chrome) end to end, the same way earlier
// showcases in this file have caught real paint-order bugs before automated tests did.

const string InteractiveFormsCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0; color: #222 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555 }
    .field { margin: 0 0 0.6em }
    label { display: inline-block; width: 130pt }
    input[type=text] { width: 160pt }
    input.styled-placeholder:placeholder-shown { border-color: #8a2be2; background-color: #f8f1ff }
    input.styled-placeholder::placeholder { color: #6b21a8; font-style: italic }
    </style>
    """;

var interactiveFormsHtml = "<!DOCTYPE html><html><head>" + InteractiveFormsCss + "</head><body>" +

    "<h1>Interactive PDF Forms</h1>" +
    "<p class=\"intro\">-peachpdf-pdf-form-field turns &lt;input&gt;/&lt;select&gt; into real, fillable AcroForm fields when PdfGenerateConfig.EnableInteractivePdfForms is set - open this PDF in a real reader and fill it in.</p>" +

    "<h2>Text field</h2>" +
    "<div class=\"field\"><label for=\"name\">Full name</label><input type=\"text\" id=\"name\" name=\"name\" value=\"Jane Doe\" /></div>" +

    "<h2>Comb text field</h2>" +
    "<div class=\"field\"><label for=\"code\">Confirmation code</label><input type=\"text\" id=\"code\" name=\"code\" value=\"AB12\" style=\"-peachpdf-pdf-form-field-comb: 6\" /></div>" +

    "<h2>Checkbox</h2>" +
    "<div class=\"field\"><label for=\"subscribe\">Subscribe to updates</label><input type=\"checkbox\" id=\"subscribe\" name=\"subscribe\" checked /></div>" +

    "<h2>Radio group</h2>" +
    "<div class=\"field\"><label>Shipping plan</label>" +
    "<input type=\"radio\" id=\"plan-basic\" name=\"plan\" value=\"basic\" checked /> <label for=\"plan-basic\" style=\"width:auto\">Basic</label>&nbsp;&nbsp;" +
    "<input type=\"radio\" id=\"plan-pro\" name=\"plan\" value=\"pro\" /> <label for=\"plan-pro\" style=\"width:auto\">Pro</label>" +
    "</div>" +

    "<h2>Select (combo box)</h2>" +
    "<div class=\"field\"><label for=\"country\">Country</label>" +
    "<select id=\"country\" name=\"country\">" +
    "<option value=\"us\">United States</option>" +
    "<option value=\"ca\" selected>Canada</option>" +
    "<option value=\"mx\">Mexico</option>" +
    "</select></div>" +

    "<h2>Custom-styled field (border, background, padding, color, font - all ordinary CSS)</h2>" +
    "<div class=\"field\"><label for=\"custom\">Nickname</label>" +
    "<input type=\"text\" id=\"custom\" name=\"nickname\" value=\"Buttercup\" style=\"" +
    "border: 2pt dashed #8a2be2; background-color: #f5e9ff; color: #4b0082; " +
    "padding: 4pt 8pt; font: italic 12pt Georgia, serif; width: 180pt\" /></div>" +

    // The HTML attributes that become PDF field entries rather than CSS - see
    // docs/html-css-support.md#form-control-attributes. Worth a showcase of its own because every
    // one of them is invisible in a static render and only shows up in a real reader: the password
    // field must echo asterisks rather than its value, the read-only field must refuse the caret,
    // and the required one must be flagged before the form submits.
    "<h2>Attributes (readonly, required, maxlength, password, placeholder)</h2>" +
    "<div class=\"field\"><label for=\"pw\">Password</label>" +
    "<input type=\"password\" id=\"pw\" name=\"password\" value=\"hunter2\" /></div>" +
    "<div class=\"field\"><label for=\"initials\">Initials (max 3)</label>" +
    "<input type=\"text\" id=\"initials\" name=\"initials\" maxlength=\"3\" /></div>" +
    "<div class=\"field\"><label for=\"ref\">Reference (read-only)</label>" +
    "<input type=\"text\" id=\"ref\" name=\"reference\" value=\"INV-2026-0042\" readonly /></div>" +
    "<div class=\"field\"><label for=\"email\">Email (required)</label>" +
    "<input type=\"email\" id=\"email\" name=\"email\" required placeholder=\"you@example.com\" /></div>" +

    "<h2>Placeholder (standard attribute and selectors)</h2>" +
    "<div class=\"field\"><label for=\"hint\">Company</label>" +
    "<input class=\"styled-placeholder\" type=\"text\" id=\"hint\" name=\"company\" " +
    "placeholder=\"Acme Corporation\" /></div>" +

    "</body></html>";

await SaveShowcaseAsync("interactive_pdf_forms", "Interactivity", "Interactive PDF Forms",
    "Real, fillable AcroForm text/checkbox/radio/select fields generated from ordinary <input>/<select> markup via "
    + "EnableInteractivePdfForms - including readonly/required/maxlength/password/placeholder, and a drawn placeholder hint.",
    interactiveFormsHtml, new PdfGenerateConfig
    {
        PageSize = PageSize.A4,
        PageOrientation = PageOrientation.Portrait,
        ShrinkToFit = true,
        EnableInteractivePdfForms = true
    });

// MathML: fractions, radicals, sub/superscripts, stretchy fences, and matrices rendered as real
// vector PDF content using STIX Two Math's own OpenType MATH table. (stixTwoMathB64 is already read
// above, for the font-variant-position showcase.)
string MathPanel(string title, string mathml) =>
    "<div class=\"mpanel\">" +
    $"<div class=\"mtitle\">{title}</div>" +
    $"<div class=\"mformula\">{mathml}</div>" +
    "</div>";
var mathHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'STIX Two Math'; src: url('data:font/truetype;base64,{stixTwoMathB64}') format('truetype'); }}" +
    "body { font: 10pt Arial, sans-serif; margin: 0; color: #222 }" +
    "h1 { font-size: 16pt; margin: 0 0 0.2em }" +
    "p.intro { margin: 0 0 1em; color: #555 }" +
    "math { font-family: 'STIX Two Math' }" +
    ".grid { display: flex; flex-wrap: wrap; gap: 14px }" +
    ".mpanel { flex: 1 1 46%; border: 1px solid #ddd; border-radius: 6px; padding: 12px 14px; background: #fafafa }" +
    ".mtitle { font-size: 9pt; font-weight: bold; color: #444; margin-bottom: 8px }" +
    ".mformula { font-size: 16pt; text-align: center; padding: 6px 0 }" +
    "</style></head><body>" +
    "<h1>MathML</h1>" +
    "<p class=\"intro\">Inline &lt;math&gt; rendered as real vector PDF content - fraction bars, radical " +
    "signs, and stretchy fences built from the font's own OpenType MATH table, never rasterized.</p>" +
    "<div class=\"grid\">" +
    MathPanel("Quadratic formula",
        "<math display=\"block\"><mi>x</mi><mo>=</mo><mfrac>" +
        "<mrow><mo>-</mo><mi>b</mi><mo>&#177;</mo><msqrt><mrow><msup><mi>b</mi><mn>2</mn></msup><mo>-</mo><mn>4</mn><mi>a</mi><mi>c</mi></mrow></msqrt></mrow>" +
        "<mrow><mn>2</mn><mi>a</mi></mrow></mfrac></math>") +
    MathPanel("Pythagorean theorem",
        "<math display=\"block\"><msup><mi>a</mi><mn>2</mn></msup><mo>+</mo><msup><mi>b</mi><mn>2</mn></msup><mo>=</mo><msup><mi>c</mi><mn>2</mn></msup></math>") +
    MathPanel("2&#215;2 identity matrix (stretchy fences)",
        "<math display=\"block\"><mrow><mo stretchy=\"true\">(</mo><mtable>" +
        "<mtr><mtd><mn>1</mn></mtd><mtd><mn>0</mn></mtd></mtr>" +
        "<mtr><mtd><mn>0</mn></mtd><mtd><mn>1</mn></mtd></mtr>" +
        "</mtable><mo stretchy=\"true\">)</mo></mrow></math>") +
    MathPanel("Binomial coefficient (nested scripts)",
        "<math display=\"block\"><mo stretchy=\"true\">(</mo><mfrac linethickness=\"0\"><mi>n</mi><mi>k</mi></mfrac><mo stretchy=\"true\">)</mo>" +
        "<mo>=</mo><mfrac><mrow><mi>n</mi><mo>!</mo></mrow><mrow><mi>k</mi><mo>!</mo><mo>(</mo><mi>n</mi><mo>-</mo><mi>k</mi><mo>)</mo><mo>!</mo></mrow></mfrac></math>") +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("mathml", "Math", "MathML",
    "Inline <math> formulas rendered as true vector PDF content: fraction bars, radical signs, " +
    "sub/superscripts, and MATH-table stretchy fences, using STIX Two Math's own OpenType MATH table.",
    mathHtml, pdfConfig);

// --- CSS filter showcase (native functions: opacity/brightness/contrast/invert) ---

static string FilterSwatch(string desc, string cssValue) =>
    "<td>" +
    $"<div class=\"fbox\" style=\"filter: {cssValue}\"><div class=\"chip\">Peach</div></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">filter: {cssValue}</div>" +
    "</td>";

const string FilterCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25%; text-align: center }
    .fbox { width: 140px; height: 90px; margin: 0 auto 3px; border-radius: 6px;
            background: linear-gradient(135deg, #ff5e62 0%, #ffb347 50%, #2980b9 100%);
            display: flex; align-items: center; justify-content: center; }
    .chip { color: #fff; font-size: 13pt; font-weight: bold; }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var cssFilterHtml = "<!DOCTYPE html><html><head>" + FilterCss + "</head><body>" +

    "<h1>CSS filter: Native PDF Color Math</h1>" +
    "<p class=\"intro\">opacity(), brightness(), contrast(), and invert() apply as real PDF color math - opacity() reuses the same isolated transparency group as the opacity property, the other three compose into one ExtGState /TR transfer function - not a rasterized approximation. (docs/html-css-support.md#filters-and-blend-modes)</p>" +

    "<h2>1 — Individual functions</h2>" +
    Row(
        FilterSwatch("baseline (no filter)", "none"),
        FilterSwatch("brightness", "brightness(1.6)"),
        FilterSwatch("contrast", "contrast(1.8)"),
        FilterSwatch("invert", "invert(1)")
    ) +

    "<h2>2 — Composed in one filter: list</h2>" +
    "<p class=\"intro\">Multiple native functions in one filter: value compose into a single PDF transfer function, so a whole native-only chain still costs one PDF object, the same as one function alone.</p>" +
    Row(
        FilterSwatch("brightness + contrast", "brightness(1.3) contrast(1.4)"),
        FilterSwatch("brightness + invert", "brightness(0.9) invert(1)"),
        FilterSwatch("opacity + brightness", "opacity(0.6) brightness(1.4)"),
        FilterSwatch("all four together", "opacity(0.85) brightness(1.2) contrast(1.3) invert(0.15)")
    ) +

    "</body></html>";

await SaveShowcaseAsync("css_filter", "Graphics & Effects", "CSS Filter",
    "filter: opacity(), brightness(), contrast(), and invert() - genuinely native PDF color math via ExtGState /TR, composed into a single transfer function per element.",
    cssFilterHtml, pdfConfig);

// --- CSS filter showcase (rendered through the raster backend: blur/grayscale/sepia/saturate/hue-rotate) ---

var cssFilterRasterHtml = "<!DOCTYPE html><html><head>" + FilterCss + "</head><body>" +

    "<h1>CSS filter: Rendered as a Bitmap</h1>" +
    "<p class=\"intro\">blur(), grayscale(), sepia(), saturate(), and hue-rotate() have no PDF equivalent - a blur needs per-pixel convolution and the colour functions mix channels - so an element carrying one is rendered into a bitmap at 300 dpi, filtered in the order written, and embedded at exactly its own size. Zoom in: the edges of the blur stay smooth, and the unfiltered swatch next to them stays vector. (docs/html-css-support.md#rasterized-effects)</p>" +

    "<h2>1 — Blur</h2>" +
    Row(
        FilterSwatch("baseline (no filter)", "none"),
        FilterSwatch("blur 2px", "blur(2px)"),
        FilterSwatch("blur 5px", "blur(5px)"),
        FilterSwatch("blur 10px", "blur(10px)")
    ) +

    "<h2>2 — Cross-channel colour functions</h2>" +
    Row(
        FilterSwatch("grayscale", "grayscale(1)"),
        FilterSwatch("sepia", "sepia(1)"),
        FilterSwatch("saturate up", "saturate(2.5)"),
        FilterSwatch("hue-rotate", "hue-rotate(120deg)")
    ) +

    "<h2>3 — Partial amounts and chains, applied in order</h2>" +
    Row(
        FilterSwatch("half grayscale", "grayscale(0.5)"),
        FilterSwatch("desaturated", "saturate(0.3)"),
        FilterSwatch("sepia then blur", "sepia(0.8) blur(3px)"),
        FilterSwatch("everything together", "hue-rotate(200deg) saturate(1.6) brightness(1.1) blur(1.5px) opacity(0.9)")
    ) +

    "</body></html>";

await SaveShowcaseAsync("css_filter_raster", "Graphics & Effects", "CSS Filter (Rasterized)",
    "filter: blur(), grayscale(), sepia(), saturate(), and hue-rotate() - rendered into a bitmap at the configured RasterizationDpi and embedded at exactly the element's own size, while the rest of the page stays vector.",
    cssFilterRasterHtml, pdfConfig);

// --- Raster shadows showcase (text-shadow, Gaussian box-shadow, silhouette drop-shadow) ---

// A 64x64 PNG whose corners are fully transparent: a filled circle with a soft (partial-alpha) rim.
string MakeRasterShadowPng()
{
    using var image = PeachImage.Image.Create(64, 64, PeachImage.PixelFormat.Rgba32);
    var pixels = image.GetPixelSpan();
    for (var y = 0; y < 64; y++)
    {
        for (var x = 0; x < 64; x++)
        {
            var d = Math.Sqrt((x - 31.5) * (x - 31.5) + (y - 31.5) * (y - 31.5));
            var alpha = (byte)Math.Clamp((int)((30 - d) * 255 / 3), 0, 255);
            var p = (y * 64 + x) * 4;
            pixels[p] = (byte)(60 + x * 3);
            pixels[p + 1] = (byte)(200 - y * 2);
            pixels[p + 2] = 90;
            pixels[p + 3] = alpha;
        }
    }

    using var ms = new MemoryStream();
    image.Save(ms, "png", new PeachImage.Formats.Png.PngEncoderOptions());
    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
}

var rasterShadowPng = MakeRasterShadowPng();

const string RasterShadowCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0; background: #fafafa }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt }
    .row { display: flex; gap: 26px; margin-bottom: 12px; align-items: flex-start }
    .cap { font-size: 6.5pt; color: #666; margin-top: 4px }
    .t { font: bold 26pt Arial, sans-serif; color: #2a5db0 }
    .card { width: 110px; height: 70px; background: #fff; border-radius: 10px }
    .sil { font: bold 20pt Arial, sans-serif; color: #c33 }
    </style>
    """;

var rasterShadowsHtml = "<!DOCTYPE html><html><head>" + RasterShadowCss + "</head><body>" +
    "<h1>Shadows</h1>" +
    "<p class=\"intro\">Blurred shadows are true Gaussian blurs rendered into bitmaps at 300 dpi, drawn underneath content that stays vector and selectable. text-shadow, box-shadow (outer and inset) and drop-shadow() all follow the real shape: a drop-shadow of text or of a PNG with transparent corners is the glyph or image outline, not a rectangle. (docs/html-css-support.md#rasterized-effects)</p>" +

    "<h2>1 — text-shadow</h2>" +
    "<div class=\"row\">" +
    "<div><div class=\"t\" style=\"text-shadow: 3px 3px 0 #9ab\">Hard</div><div class=\"cap\">3px 3px 0 #9ab</div></div>" +
    "<div><div class=\"t\" style=\"text-shadow: 0 0 6px #4af\">Glow</div><div class=\"cap\">0 0 6px #4af</div></div>" +
    "<div><div class=\"t\" style=\"text-shadow: 2px 3px 5px rgba(0,0,0,.6)\">Soft</div><div class=\"cap\">2px 3px 5px rgba(0,0,0,.6)</div></div>" +
    "<div><div class=\"t\" style=\"text-shadow: 0 0 3px #fff, 0 0 10px #f80, 0 0 20px #f00; background:#222; padding:0 10px\">Fire</div><div class=\"cap\">three layers, first on top</div></div>" +
    "</div>" +

    "<h2>2 — box-shadow: a real Gaussian, knocked out under the box</h2>" +
    "<div class=\"row\" style=\"padding: 14px\">" +
    "<div><div class=\"card\" style=\"box-shadow: 0 4px 14px rgba(0,0,0,.45)\"></div><div class=\"cap\">0 4px 14px rgba(0,0,0,.45)</div></div>" +
    "<div><div class=\"card\" style=\"background: transparent; box-shadow: 6px 6px 12px #36c\"></div><div class=\"cap\">transparent box: no shadow shows through</div></div>" +
    "<div><div class=\"card\" style=\"box-shadow: inset 0 0 14px #36c, 0 0 0 2px #36c\"></div><div class=\"cap\">inset 0 0 14px, rounded hole</div></div>" +
    "<div><div class=\"card\" style=\"box-shadow: 0 0 0 4px #fff, 0 10px 20px 2px rgba(200,0,60,.5)\"></div><div class=\"cap\">spread + two layers</div></div>" +
    "</div>" +

    "<h2>3 — filter: drop-shadow() follows the alpha shape</h2>" +
    "<div class=\"row\" style=\"padding: 10px\">" +
    "<div><div class=\"sil\" style=\"filter: drop-shadow(4px 4px 3px rgba(0,0,0,.6))\">Silhouette</div><div class=\"cap\">text: shadow is the glyph outline</div></div>" +
    "<div><img src=\"" + rasterShadowPng + "\" width=\"64\" height=\"64\" style=\"filter: drop-shadow(5px 5px 4px #000)\"><div class=\"cap\">PNG with transparent corners</div></div>" +
    "<div><svg width=\"90\" height=\"70\" viewBox=\"0 0 90 70\" style=\"filter: drop-shadow(4px 5px 3px rgba(0,0,80,.7))\"><path d=\"M10 60 L45 8 L80 60 Z\" fill=\"#e8a\"/><circle cx=\"45\" cy=\"42\" r=\"11\" fill=\"#fff\"/></svg><div class=\"cap\">inline SVG (not its bounding box)</div></div>" +
    "<div><div class=\"card\" style=\"background:#9cf; filter: drop-shadow(0 6px 5px rgba(0,0,0,.5)) drop-shadow(6px 0 0 #f80)\"></div><div class=\"cap\">two drop-shadows chained</div></div>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("shadows_raster", "Graphics & Effects", "Shadows (Gaussian blur)",
    "text-shadow, blurred box-shadow (outer and inset), and filter: drop-shadow() - real Gaussian blurs rendered into bitmaps beneath vector content, following the actual glyph, image, and SVG shapes.",
    rasterShadowsHtml, pdfConfig);

// --- SVG filters over pixels showcase ---

const string svgRasterFiltersHtml = """
<!DOCTYPE html>
<html><head><style>
body { margin: 10px; font-family: sans-serif; background: #fff; }
.grid { display: flex; flex-wrap: wrap; gap: 8px; }
figure { margin: 0; width: 170px; font-size: 9px; text-align: center; }
svg { width: 160px; height: 120px; border: 1px solid #ddd; }
</style></head><body>
<div class="grid">
<figure><svg viewBox="0 0 160 120"><defs><filter id="blur"><feGaussianBlur stdDeviation="4"/></filter></defs><g filter="url(#blur)"><rect x="30" y="20" width="100" height="70" fill="#c33"/><circle cx="80" cy="55" r="25" fill="#fc3"/></g></svg>blur 4</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="blurx"><feGaussianBlur stdDeviation="8 0"/></filter></defs><g filter="url(#blurx)"><rect x="30" y="20" width="100" height="70" fill="#c33"/></g></svg>blur x-only</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="sat"><feColorMatrix type="saturate" values="0.2"/></filter></defs><g filter="url(#sat)"><rect x="10" y="10" width="60" height="100" fill="#f00"/><rect x="80" y="10" width="60" height="100" fill="#0a0"/></g></svg>saturate .2</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="hue"><feColorMatrix type="hueRotate" values="120"/></filter></defs><g filter="url(#hue)"><rect x="10" y="10" width="60" height="100" fill="#f00"/><rect x="80" y="10" width="60" height="100" fill="#0a0"/></g></svg>hueRotate 120</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="shadow" x="-20%" y="-20%" width="150%" height="150%"><feDropShadow dx="5" dy="5" stdDeviation="3" flood-color="#000" flood-opacity="0.6"/></filter></defs><g filter="url(#shadow)"><rect x="30" y="20" width="80" height="60" fill="#39c" rx="10"/></g></svg>feDropShadow</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="morph"><feMorphology operator="dilate" radius="3"/></filter></defs><g filter="url(#morph)"><text x="15" y="70" font-size="40" font-family="sans-serif" fill="#000">Ab</text></g></svg>dilate 3</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="erode"><feMorphology operator="erode" radius="2"/></filter></defs><g filter="url(#erode)"><text x="15" y="70" font-size="50" font-family="sans-serif" font-weight="bold" fill="#000">Ab</text></g></svg>erode 2</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="turb" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.04" numOctaves="3" seed="3"/></filter></defs><rect width="160" height="120" fill="#fff" filter="url(#turb)"/></svg>fractalNoise</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="turb2" x="0" y="0" width="100%" height="100%"><feTurbulence type="turbulence" baseFrequency="0.05" numOctaves="2"/></filter></defs><rect width="160" height="120" fill="#fff" filter="url(#turb2)"/></svg>turbulence</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="disp" x="-10%" y="-10%" width="120%" height="120%">
      <feTurbulence type="turbulence" baseFrequency="0.05" numOctaves="2" result="t"/>
      <feDisplacementMap in="SourceGraphic" in2="t" scale="18" xChannelSelector="R" yChannelSelector="G"/>
    </filter></defs><g filter="url(#disp)"><rect x="20" y="20" width="120" height="80" fill="#c33"/><circle cx="80" cy="60" r="25" fill="#fff"/></g></svg>displacement</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="light" x="0" y="0" width="100%" height="100%">
      <feGaussianBlur in="SourceAlpha" stdDeviation="3" result="b"/>
      <feDiffuseLighting in="b" surfaceScale="6" diffuseConstant="1" lighting-color="#fff" result="d"><feDistantLight azimuth="225" elevation="45"/></feDiffuseLighting>
      <feComposite in="SourceGraphic" in2="d" operator="arithmetic" k1="1" k2="0.2" k3="0" k4="0"/>
    </filter></defs><g filter="url(#light)"><rect x="20" y="20" width="120" height="80" rx="20" fill="#c63"/></g></svg>diffuse light</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="spec" x="0" y="0" width="100%" height="100%">
      <feGaussianBlur in="SourceAlpha" stdDeviation="3" result="b"/>
      <feSpecularLighting in="b" surfaceScale="5" specularConstant="1" specularExponent="20" lighting-color="#fff" result="s"><fePointLight x="40" y="20" z="60"/></feSpecularLighting>
      <feComposite in="s" in2="SourceAlpha" operator="in" result="s2"/>
      <feMerge><feMergeNode in="SourceGraphic"/><feMergeNode in="s2"/></feMerge>
    </filter></defs><g filter="url(#spec)"><rect x="20" y="20" width="120" height="80" rx="20" fill="#369"/></g></svg>specular point</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="conv"><feConvolveMatrix order="3" kernelMatrix="0 -1 0 -1 5 -1 0 -1 0"/></filter></defs><g filter="url(#conv)"><rect x="20" y="20" width="120" height="80" fill="#888"/><circle cx="80" cy="60" r="25" fill="#eee"/></g></svg>sharpen</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="emboss"><feConvolveMatrix order="3" kernelMatrix="-2 -1 0 -1 1 1 0 1 2" preserveAlpha="true"/></filter></defs><g filter="url(#emboss)"><rect x="20" y="20" width="120" height="80" fill="#888"/><circle cx="80" cy="60" r="25" fill="#eee"/></g></svg>emboss</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="ct"><feComponentTransfer><feFuncR type="discrete" tableValues="0 0.5 1"/><feFuncG type="gamma" amplitude="1" exponent="0.5"/><feFuncB type="table" tableValues="1 0"/></feComponentTransfer></filter></defs><g filter="url(#ct)"><rect x="10" y="10" width="140" height="100" fill="url(#grad)"/></g><defs><linearGradient id="grad"><stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#fff"/></linearGradient></defs></svg>transfer fns</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="lin"><feGaussianBlur stdDeviation="6" color-interpolation-filters="linearRGB"/></filter></defs><g filter="url(#lin)"><rect x="20" y="20" width="60" height="80" fill="#f00"/><rect x="80" y="20" width="60" height="80" fill="#00f"/></g></svg>blur linearRGB</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="srgb" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="6"/></filter></defs><g filter="url(#srgb)"><rect x="20" y="20" width="60" height="80" fill="#f00"/><rect x="80" y="20" width="60" height="80" fill="#00f"/></g></svg>blur sRGB</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="sub" x="0" y="0" width="100%" height="100%"><feFlood flood-color="#39f" x="20" y="20" width="60" height="40"/></filter></defs><rect width="160" height="120" fill="#eee" filter="url(#sub)"/></svg>subregion flood</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="tile" x="0" y="0" width="100%" height="100%"><feFlood flood-color="#e63" x="0" y="0" width="20" height="20"/><feOffset dx="0" dy="0" x="0" y="0" width="20" height="20" result="o"/><feTile in="o"/></filter></defs><rect width="160" height="120" fill="#eee" filter="url(#tile)"/></svg>feTile</figure>
</div>
</body></html>
""";

await SaveShowcaseAsync("svg_filters_raster", "Graphics & Effects", "SVG Filters (Rasterized)",
    "SVG filter primitives PDF has no operator for - blur, drop shadow, morphology, convolution, turbulence, displacement and lighting, cross-channel colour matrices, table/gamma transfer functions, arithmetic compositing - evaluated over pixels in linear light and embedded at the document's raster resolution.",
    svgRasterFiltersHtml, pdfConfig);

// --- SVG filter inputs showcase: feImage, FillPaint/StrokePaint, BackgroundImage/BackgroundAlpha ---

const string svgFilterInputImage = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40' viewBox='0 0 40 40'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#f0a'/><stop offset='1' stop-color='#0af'/></linearGradient></defs><rect width='40' height='40' fill='url(#g)'/><circle cx='20' cy='20' r='9' fill='#fff' fill-opacity='.7'/></svg>";
var svgFilterInputImageUri = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svgFilterInputImage));

// A stand-alone SVG (used through <img>): its filter's BackgroundAlpha sees only the circles painted before the filtered rectangle.
const string svgFilterSilhouette = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='120' viewBox='0 0 160 120'><defs><filter id='sil' x='0' y='0' width='1' height='1' color-interpolation-filters='sRGB'><feOffset in='BackgroundAlpha' dx='7' dy='7' result='off'/><feFlood flood-color='#036' flood-opacity='.7'/><feComposite in2='off' operator='in'/></filter></defs><circle cx='55' cy='55' r='32' fill='#fc3'/><circle cx='100' cy='62' r='26' fill='#e33'/><rect x='10' y='10' width='140' height='100' fill='none' filter='url(#sil)'/></svg>";
var svgFilterSilhouetteUri = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svgFilterSilhouette));

var svgFilterInputsHtml = $$"""
<!DOCTYPE html>
<html><head><style>
body { margin: 10px; font-family: sans-serif; background: #fff; }
.grid { display: flex; flex-wrap: wrap; gap: 8px; }
figure { margin: 0; width: 170px; font-size: 9px; text-align: center; }
svg { width: 160px; height: 120px; border: 1px solid #ddd; }
.stripes { width: 160px; height: 120px; background: repeating-linear-gradient(45deg, #e33 0 10px, #fc3 10px 20px); }
.stripes svg { border: 0; display: block; }
</style></head><body>
<div class="grid">
<figure><svg viewBox="0 0 160 120"><defs><filter id="img" x="0" y="0" width="1" height="1"><feImage href="{{svgFilterInputImageUri}}" preserveAspectRatio="xMidYMid slice" result="pic"/><feComposite in="pic" in2="SourceAlpha" operator="in"/></filter></defs><text x="80" y="78" text-anchor="middle" font-size="64" font-weight="bold" filter="url(#img)">Aa</text></svg>feImage (image) masked to text</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="sub" x="0" y="0" width="1" height="1"><feImage href="{{svgFilterInputImageUri}}" x="20" y="20" width="50" height="80" preserveAspectRatio="xMidYMid meet" result="a"/><feImage href="{{svgFilterInputImageUri}}" x="90" y="20" width="50" height="80" preserveAspectRatio="none" result="b"/><feMerge><feMergeNode in="a"/><feMergeNode in="b"/></feMerge></filter></defs><rect width="160" height="120" fill="#eee" filter="url(#sub)"/></svg>feImage subregions: meet / none</figure>
<figure><svg viewBox="0 0 160 120"><defs><path id="star" d="M30 -10 L36 8 L56 8 L40 20 L46 40 L30 28 L14 40 L20 20 L4 8 L24 8 Z" fill="#fc0" stroke="#a60" stroke-width="2"/><filter id="ref" x="0" y="0" width="1" height="1"><feImage href="#star" x="50" y="40" result="s"/><feGaussianBlur in="s" stdDeviation="1.5" result="soft"/><feMerge><feMergeNode in="soft"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs><rect x="10" y="10" width="140" height="100" fill="none" stroke="#69c" stroke-width="3" filter="url(#ref)"/></svg>feImage (element) + blur</figure>
<figure><svg viewBox="0 0 160 120"><defs><linearGradient id="fp" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#e33"/><stop offset=".5" stop-color="#fc3"/><stop offset="1" stop-color="#36f"/></linearGradient><filter id="fill" color-interpolation-filters="sRGB"><feOffset in="SourceAlpha" dx="6" dy="6" result="o"/><feGaussianBlur in="o" stdDeviation="2" result="shadow"/><feComposite in="FillPaint" in2="SourceAlpha" operator="in" result="paint"/><feMerge><feMergeNode in="shadow"/><feMergeNode in="paint"/></feMerge></filter></defs><text x="80" y="76" text-anchor="middle" font-size="46" font-weight="bold" fill="url(#fp)" filter="url(#fill)">Paint</text></svg>FillPaint (gradient) clipped to glyphs</figure>
<figure><svg viewBox="0 0 160 120"><defs><filter id="ring" x="-20%" y="-20%" width="140%" height="140%" color-interpolation-filters="sRGB"><feMorphology in="SourceAlpha" operator="dilate" radius="5" result="fat"/><feComposite in="StrokePaint" in2="fat" operator="in" result="halo"/><feMerge><feMergeNode in="halo"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs><text x="80" y="76" text-anchor="middle" font-size="46" font-weight="bold" fill="#fff" stroke="#d0208a" filter="url(#ring)">Ring</text><rect width="160" height="120" fill="none"/></svg>StrokePaint halo (dilate)</figure>
<figure><div class="stripes"><svg viewBox="0 0 160 120"><defs><filter id="glass" x="0" y="0" width="1" height="1"><feGaussianBlur in="BackgroundImage" stdDeviation="4" result="blur"/><feComposite in="blur" in2="SourceAlpha" operator="in" result="pane"/><feMerge><feMergeNode in="pane"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs><text x="8" y="46" font-size="24" font-weight="bold" fill="#111">Behind glass</text><rect x="18" y="30" width="124" height="70" rx="12" fill="#fff" fill-opacity=".28" stroke="#fff" stroke-opacity=".7" filter="url(#glass)"/></svg></div>BackgroundImage: frosted pane over HTML and SVG content</figure>
<figure><img src="{{svgFilterSilhouetteUri}}" width="160" height="120" style="border:1px solid #ddd;background:#eef">BackgroundAlpha silhouette (SVG as &lt;img&gt;)</figure>
</div>
</body></html>
""";

await SaveShowcaseAsync("svg_filter_inputs", "Graphics & Effects", "SVG Filter Inputs (feImage, FillPaint, BackgroundImage)",
    "feImage with a stand-alone image (fitted by preserveAspectRatio into its subregion) and with a reference to an element, plus the FillPaint / StrokePaint / BackgroundImage / BackgroundAlpha inputs: a filter can paint the element's own gradient, stroke a halo in its stroke colour, or blur the page and the SVG content painted behind it.",
    svgFilterInputsHtml, pdfConfig);

// --- backdrop-filter showcase ---

const string backdropFilterHtml = """
<!DOCTYPE html>
<html><head><style>
body { margin: 0; font-family: sans-serif; }
.stage { position: relative; width: 520px; height: 300px; background: linear-gradient(90deg, #e33 0, #fc3 33%, #3c6 66%, #36f 100%); overflow: hidden; }
.stage p { position: absolute; margin: 0; font-size: 24px; font-weight: bold; color: #111; white-space: nowrap; }
.glass { position: absolute; border-radius: 18px; border: 1px solid rgba(255,255,255,.6); color: #000; font-size: 13px; padding: 8px 12px; box-sizing: border-box; }
.g1 { left: 40px; top: 44px; width: 210px; height: 100px; background: rgba(255,255,255,.18); backdrop-filter: blur(6px); }
.g2 { left: 280px; top: 44px; width: 210px; height: 100px; background: rgba(0,0,0,.05); backdrop-filter: grayscale(1) contrast(1.2); }
.g3 { left: 40px; top: 170px; width: 210px; height: 100px; background: rgba(255,255,255,.1); -webkit-backdrop-filter: blur(3px) saturate(2); }
.g4 { left: 280px; top: 170px; width: 210px; height: 100px; border-radius: 45px; background: rgba(255,255,255,.1); backdrop-filter: invert(1) hue-rotate(90deg); }
.iso { position: absolute; left: 0; top: 0; width: 100%; height: 100%; opacity: .99; }
</style></head><body>
<div class="stage">
<p style="left:16px;top:10px">BACKDROP FILTER</p>
<p style="left:16px;top:88px">Text behind glass</p>
<p style="left:256px;top:88px">Text behind glass</p>
<p style="left:16px;top:214px">Text behind glass</p>
<p style="left:256px;top:214px">Text behind glass</p>
<div class="glass g1">blur(6px)</div>
<div class="glass g2">grayscale + contrast</div>
<div class="glass g3">blur + saturate</div>
<div class="glass g4">invert + hue-rotate</div>
</div>
</body></html>
""";

await SaveShowcaseAsync("backdrop_filter", "Graphics & Effects", "Backdrop Filter",
    "backdrop-filter on frosted-glass panels over a gradient and text: blur, grayscale + contrast, blur + saturate, and invert + hue-rotate, each applied to what was painted behind the panel and clipped to its rounded corners.",
    backdropFilterHtml, pdfConfig);

// --- PDF/A-1 with flattened transparency showcase ---

const string flattenedTransparencyHtml = """
<html><head><style>
body { margin: 20px; font-family: sans-serif; }
.stage { position: relative; width: 480px; height: 250px; background: linear-gradient(90deg,#e33,#fc3,#3c6,#36f); margin-bottom: 30px; }
.card { position: absolute; padding: 10px; border-radius: 14px; }
.a { left: 20px; top: 20px; width: 200px; height: 80px; background: rgba(255,255,255,.55); }
.b { left: 260px; top: 20px; width: 190px; height: 80px; background: #000; opacity: .45; color: #fff; }
.c { left: 40px; top: 150px; width: 180px; height: 60px; background: #fff; box-shadow: 0 6px 14px rgba(0,0,0,.5); }
.d { left: 260px; top: 150px; width: 190px; height: 60px; background: #f80; mix-blend-mode: multiply; }
</style></head><body>
<h2>PDF/A-1b with transparency, flattened</h2>
<p>PDF/A-1 forbids transparency. With <code>TransparencyPolicy.Flatten</code> each translucent element below is rendered, with what is behind it, into an opaque bitmap; the rest of the page stays vector, and the text stays selectable.</p>
<div class="stage">
  <div class="card a">rgba background over a gradient</div>
  <div class="card b">opacity .45 group with text</div>
  <div class="card c">box-shadow with blur</div>
  <div class="card d">mix-blend-mode multiply</div>
</div>
<p style="color: rgba(0,0,0,.5); font-size: 20px">Half-transparent text</p>
</body></html>
""";

await SaveShowcaseAsync("pdfa1_flattened_transparency", "Standards & Accessibility", "PDF/A-1 with Flattened Transparency",
    "A PDF/A-1b document that uses rgba backgrounds, opacity, a blurred shadow, mix-blend-mode and translucent text - all forbidden by PDF/A-1 - generated with TransparencyPolicy.Flatten, which renders each affected region as an opaque bitmap.",
    flattenedTransparencyHtml,
    new PdfGenerateConfig
    {
        PageSize = PageSize.A4,
        PdfAConformance = PdfAConformance.PdfA1B,
        TransparencyPolicy = TransparencyPolicy.Flatten,
        Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
    });

// --- CSS perspective and 3D transforms showcase ---

const string perspectiveHtml = """
<!DOCTYPE html>
<html><head><style>
body { margin: 0; font-family: sans-serif; background: #eef; }
.row { display: flex; gap: 30px; padding: 20px; }
.scene { width: 200px; height: 160px; perspective: 400px; background: #dde; position: relative; }
.card { position: absolute; left: 40px; top: 30px; width: 120px; height: 90px; background: linear-gradient(135deg,#e33,#fc3); border: 3px solid #333; color: #000; font-size: 18px; font-weight: bold; padding: 8px; box-sizing: border-box; }
.a { transform: rotateY(50deg); }
.b { transform: rotateX(45deg); }
.c { transform: perspective(300px) rotateY(-40deg); }
.d { transform: translateZ(100px); }
.e { transform: rotateY(180deg); backface-visibility: hidden; }
.f { transform: rotateY(180deg); }
.g { transform: rotateY(30deg) rotateX(20deg); opacity: .7; }
.cap { font-size: 11px; text-align: center; margin-top: 4px; }
</style></head><body>
<div class="row">
  <div><div class="scene"><div class="card a">rotateY 50 (persp 400)</div></div><div class="cap">parent perspective, rotateY(50deg)</div></div>
  <div><div class="scene"><div class="card b">rotateX 45</div></div><div class="cap">rotateX(45deg)</div></div>
  <div><div class="scene"><div class="card c">perspective() fn</div></div><div class="cap">perspective(300px) rotateY(-40deg)</div></div>
</div>
<div class="row">
  <div><div class="scene"><div class="card d">translateZ 100</div></div><div class="cap">translateZ(100px): closer, larger</div></div>
  <div><div class="scene"><div class="card e">hidden back</div></div><div class="cap">rotateY(180) backface hidden: nothing</div></div>
  <div><div class="scene"><div class="card f">shows back</div></div><div class="cap">rotateY(180): mirrored</div></div>
</div>
<div class="row">
  <div><div class="scene"><div class="card g">g 30/20 op .7</div></div><div class="cap">rotateY+rotateX, opacity</div></div>
</div>
</body></html>
""";

await SaveShowcaseAsync("perspective_3d_transforms", "Graphics & Effects", "Perspective and 3D Transforms",
    "CSS perspective on a parent and perspective() in a transform: rotateX/rotateY cards foreshortened in real perspective, translateZ scaling, backface-visibility, and a perspective card with opacity - warped from a bitmap of the element at the raster resolution.",
    perspectiveHtml, pdfConfig);

// --- transform-style: preserve-3d showcase ---

const string preserve3dHtml = """
<!DOCTYPE html>
<html><head><style>
body { margin: 0; font-family: sans-serif; background: #eef; }
.row { display: flex; gap: 24px; padding: 18px; }
.scene { width: 230px; height: 230px; perspective: 700px; background: #dde; position: relative; }
.cap { font-size: 11px; text-align: center; margin-top: 4px; width: 230px; }
h2 { font-size: 13px; margin: 8px 18px 0; }

/* A cube: six faces in one 3D space, near faces over far ones. */
.cube { position: absolute; left: 65px; top: 65px; width: 100px; height: 100px; transform-style: preserve-3d; transform: rotateX(-25deg) rotateY(-35deg); }
.face { position: absolute; left: 0; top: 0; width: 96px; height: 96px; border: 2px solid #222; font-size: 15px; font-weight: bold; text-align: center; line-height: 96px; color: #111; }
.front  { background: #f66; transform: translateZ(50px); }
.back   { background: #6cf; transform: rotateY(180deg) translateZ(50px); }
.right  { background: #6d6; transform: rotateY(90deg) translateZ(50px); }
.left   { background: #fd5; transform: rotateY(-90deg) translateZ(50px); }
.top    { background: #c9f; transform: rotateX(90deg) translateZ(50px); }
.bottom { background: #ccc; transform: rotateX(-90deg) translateZ(50px); }

/* A carousel: six panels around a vertical axis, the far ones hidden behind the near ones. */
.carousel { position: absolute; left: 63px; top: 70px; width: 108px; height: 90px; transform-style: preserve-3d; transform: translateZ(-110px) rotateX(-10deg) rotateY(25deg); }
.panel { position: absolute; left: 0; top: 0; width: 104px; height: 86px; border: 2px solid #222; font-size: 22px; font-weight: bold; text-align: center; line-height: 86px; color: #111; backface-visibility: hidden; }
.p1 { background: #f66; transform: rotateY(0deg)   translateZ(110px); }
.p2 { background: #fa5; transform: rotateY(60deg)  translateZ(110px); }
.p3 { background: #fe5; transform: rotateY(120deg) translateZ(110px); }
.p4 { background: #6d6; transform: rotateY(180deg) translateZ(110px); }
.p5 { background: #6cf; transform: rotateY(240deg) translateZ(110px); }
.p6 { background: #c9f; transform: rotateY(300deg) translateZ(110px); }

/* A flip card: two faces with backface-visibility: hidden inside a rotating parent. */
.flip { position: absolute; left: 45px; top: 55px; width: 140px; height: 110px; transform-style: preserve-3d; }
.flip .side { position: absolute; left: 0; top: 0; width: 136px; height: 106px; border: 2px solid #222; backface-visibility: hidden; font-size: 18px; font-weight: bold; text-align: center; line-height: 106px; color: #111; }
.flip .front { background: #f66; transform: none; }
.flip .back  { background: #6cf; transform: rotateY(180deg); }
.turn0   { transform: rotateY(0deg); }
.turn70  { transform: rotateY(70deg); }
.turn180 { transform: rotateY(180deg); }

/* Planes that cross: each is nearer on one side of the line where they meet. */
.cross { position: absolute; left: 40px; top: 40px; width: 150px; height: 150px; transform-style: preserve-3d; transform: rotateX(-15deg); }
.plane { position: absolute; left: 0; top: 0; width: 146px; height: 146px; border: 2px solid #222; opacity: .92; }
.pa { background: #f66; transform: rotateY(50deg); }
.pb { background: #6cf; transform: rotateY(-50deg); }
.pc { background: #6d6; transform: rotateX(80deg); }

/* The same planes flattened: document order only. */
.flat { transform-style: flat; }
</style></head><body>
<h2>transform-style: preserve-3d</h2>
<div class="row">
  <div><div class="scene"><div class="cube"><div class="face front">front</div><div class="face back">back</div><div class="face right">right</div><div class="face left">left</div><div class="face top">top</div><div class="face bottom">bottom</div></div></div><div class="cap">a cube: six faces, depth-tested</div></div>
  <div><div class="scene"><div class="carousel"><div class="panel p1">1</div><div class="panel p2">2</div><div class="panel p3">3</div><div class="panel p4">4</div><div class="panel p5">5</div><div class="panel p6">6</div></div></div><div class="cap">a carousel: six panels around an axis, backs hidden</div></div>
</div>
<div class="row">
  <div><div class="scene"><div class="flip turn0"><div class="side front">front</div><div class="side back">back</div></div></div><div class="cap">flip card, rotateY(0deg)</div></div>
  <div><div class="scene"><div class="flip turn70"><div class="side front">front</div><div class="side back">back</div></div></div><div class="cap">rotateY(70deg): turning</div></div>
  <div><div class="scene"><div class="flip turn180"><div class="side front">front</div><div class="side back">back</div></div></div><div class="cap">rotateY(180deg): the back, not the front</div></div>
</div>
<div class="row">
  <div><div class="scene"><div class="cross"><div class="plane pa"></div><div class="plane pb"></div><div class="plane pc"></div></div></div><div class="cap">three planes that intersect, preserve-3d</div></div>
  <div><div class="scene"><div class="cross flat"><div class="plane pa"></div><div class="plane pb"></div><div class="plane pc"></div></div></div><div class="cap">the same planes, transform-style: flat</div></div>
</div>
</body></html>
""";

await SaveShowcaseAsync("preserve_3d_rendering_context", "Graphics & Effects", "3D Rendering Contexts (preserve-3d)",
    "transform-style: preserve-3d: a cube, a carousel, flip cards with hidden back faces and intersecting planes, each depth-tested per pixel in one shared 3D space, with the parent's perspective reaching every nested plane - against the same planes flattened.",
    preserve3dHtml, pdfConfig);

// --- CSS mix-blend-mode showcase ---

static string BlendSwatch(string desc, string blendMode) =>
    "<td>" +
    "<div class=\"bstage\">" +
        "<div class=\"circle circle-a\"></div>" +
        $"<div class=\"circle circle-b\" style=\"mix-blend-mode: {blendMode}\"></div>" +
    "</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">mix-blend-mode: {blendMode}</div>" +
    "</td>";

const string BlendCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25%; text-align: center }
    .bstage { position: relative; width: 140px; height: 100px; margin: 0 auto 3px; background: #f4e9d8; border: 1px solid #ccc; }
    .circle { position: absolute; width: 80px; height: 80px; border-radius: 50%; }
    .circle-a { left: 15px; top: 10px; background: #e63946; }
    .circle-b { left: 55px; top: 10px; background: #1d4ed8; }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666 }
    </style>
    """;

var mixBlendModeHtml = "<!DOCTYPE html><html><head>" + BlendCss + "</head><body>" +

    "<h1>CSS mix-blend-mode</h1>" +
    "<p class=\"intro\">Each pair of overlapping circles blends via a genuine PDF blend mode (ExtGState /BM, ISO 32000-1 §11.3.5) - real compositing math, not an approximation. (docs/html-css-support.md#filters-and-blend-modes)</p>" +
    Row(
        BlendSwatch("normal (baseline)", "normal"),
        BlendSwatch("multiply", "multiply"),
        BlendSwatch("screen", "screen"),
        BlendSwatch("difference", "difference")
    ) +

    "</body></html>";

await SaveShowcaseAsync("mix_blend_mode", "Graphics & Effects", "mix-blend-mode",
    "mix-blend-mode composites overlapping content via a real PDF blend mode (ExtGState /BM) - normal, multiply, screen, and difference shown over overlapping circles.",
    mixBlendModeHtml, pdfConfig);

// --- CSS filter: drop-shadow() showcase ---

static string DropShadowSwatch(string desc, string cssValue) =>
    "<td>" +
    $"<div class=\"dstage\"><div class=\"dshape\" style=\"filter: {cssValue}\"></div></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">filter: {cssValue}</div>" +
    "</td>";

const string DropShadowCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 33.33%; text-align: center }
    .dstage { width: 160px; height: 110px; margin: 0 auto 3px; background: #fafafa; border: 1px solid #ddd;
              display: flex; align-items: center; justify-content: center; }
    .dshape { width: 70px; height: 70px; background: #16a085; }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; word-break: break-all }
    </style>
    """;

var dropShadowHtml = "<!DOCTYPE html><html><head>" + DropShadowCss + "</head><body>" +

    "<h1>CSS filter: drop-shadow()</h1>" +
    "<p class=\"intro\">drop-shadow() reuses box-shadow's own concentric-fill blur approximation, keyed off the filtered element's own border-box rectangle rather than a true per-pixel alpha silhouette of its content. (docs/html-css-support.md#filters-and-blend-modes)</p>" +
    Row(
        DropShadowSwatch("offset only, no blur", "drop-shadow(10px 10px 0 rgba(0,0,0,0.45))"),
        DropShadowSwatch("offset + blur", "drop-shadow(6px 6px 8px rgba(0,0,0,0.5))"),
        DropShadowSwatch("colored shadow", "drop-shadow(-8px 8px 10px rgba(41,128,185,0.6))")
    ) +

    "</body></html>";

await SaveShowcaseAsync("css_filter_drop_shadow", "Graphics & Effects", "filter: drop-shadow()",
    "CSS filter: drop-shadow() approximated with box-shadow's own vector blur technique, keyed off the filtered element's own rectangle - not a true alpha silhouette.",
    dropShadowHtml, pdfConfig);

// --- SVG <filter>: feBlend & feColorMatrix showcase ---

static string SvgFilterPanel(string desc, string svg) =>
    "<td>" +
    $"<div class=\"stage\">{svg}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    "</td>";

const string SvgFilterCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 33.33%; text-align: center }
    .stage { background: #fafafa; border: 1px solid #ccc }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    </style>
    """;

var svgFilterBlendMatrixHtml = "<!DOCTYPE html><html><head>" + SvgFilterCss + "</head><body>" +

    "<h1>SVG &lt;filter&gt;: feBlend &amp; feColorMatrix</h1>" +
    "<p class=\"intro\">A native primitive graph evaluated over PDF tiles - feBlend via a real PDF blend mode, feColorMatrix (restricted to a channel-independent/diagonal matrix) via a PDF ExtGState /TR transfer function. Never rasterized. (docs/supported-svg-features.md#filters)</p>" +

    "<h2>1 — feBlend</h2>" +
    "<table class=\"sw\"><tr>" +
    SvgFilterPanel("unfiltered",
        """
        <svg viewBox="0 0 200 140" width="190" height="133">
          <rect x="20" y="20" width="160" height="100" rx="10" fill="#2980b9"/>
        </svg>
        """) +
    SvgFilterPanel("feBlend mode=\"multiply\" against a flood color",
        """
        <svg viewBox="0 0 200 140" width="190" height="133">
          <defs>
            <filter id="blendMul" x="-20%" y="-20%" width="140%" height="140%">
              <feFlood flood-color="#f1c40f" result="flood"/>
              <feBlend in="SourceGraphic" in2="flood" mode="multiply"/>
            </filter>
          </defs>
          <rect x="20" y="20" width="160" height="100" rx="10" fill="#2980b9" filter="url(#blendMul)"/>
        </svg>
        """) +
    SvgFilterPanel("feBlend mode=\"difference\"",
        """
        <svg viewBox="0 0 200 140" width="190" height="133">
          <defs>
            <filter id="blendDiff" x="-20%" y="-20%" width="140%" height="140%">
              <feFlood flood-color="#e74c3c" result="flood"/>
              <feBlend in="SourceGraphic" in2="flood" mode="difference"/>
            </filter>
          </defs>
          <rect x="20" y="20" width="160" height="100" rx="10" fill="#2980b9" filter="url(#blendDiff)"/>
        </svg>
        """) +
    "</tr></table>" +

    "<h2>2 — feColorMatrix (diagonal, channel-independent)</h2>" +
    "<p class=\"intro\">Only a channel-independent (diagonal) matrix maps onto PDF's per-channel /TR transfer function - a cross-channel matrix (e.g. type=\"saturate\") has no native PDF mechanism, per ISO 32000-1 §8.6.5.3.</p>" +
    "<table class=\"sw\"><tr>" +
    SvgFilterPanel("unfiltered",
        """
        <svg viewBox="0 0 200 140" width="190" height="133">
          <circle cx="100" cy="70" r="55" fill="#8e44ad"/>
        </svg>
        """) +
    SvgFilterPanel("diagonal matrix: boost R, dim G",
        """
        <svg viewBox="0 0 200 140" width="190" height="133">
          <defs>
            <filter id="diagMatrix" x="-20%" y="-20%" width="140%" height="140%">
              <feColorMatrix type="matrix" values="1.6 0 0 0 0  0 0.4 0 0 0  0 0 1 0 0  0 0 0 1 0"/>
            </filter>
          </defs>
          <circle cx="100" cy="70" r="55" fill="#8e44ad" filter="url(#diagMatrix)"/>
        </svg>
        """) +
    SvgFilterPanel("type=\"luminanceToAlpha\"",
        """
        <svg viewBox="0 0 200 140" width="190" height="133">
          <defs>
            <filter id="lumaAlpha" x="-20%" y="-20%" width="140%" height="140%">
              <feColorMatrix type="luminanceToAlpha"/>
            </filter>
          </defs>
          <rect x="10" y="10" width="180" height="120" fill="#fafafa"/>
          <circle cx="100" cy="70" r="55" fill="#8e44ad" filter="url(#lumaAlpha)"/>
        </svg>
        """) +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("svg_filter_blend_color_matrix", "Graphics & Effects", "SVG Filter: feBlend & feColorMatrix",
    "Native SVG <filter> primitives - feBlend (a real PDF blend mode) and feColorMatrix (a channel-independent matrix via PDF's /TR transfer function) - never rasterized.",
    svgFilterBlendMatrixHtml, pdfConfig);

// --- SVG <filter>: multi-primitive graph showcase (named result/in references) ---

static string SvgFilterGraphPanel(string desc, string svg) =>
    "<td>" +
    $"<div class=\"stage\">{svg}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    "</td>";

const string SvgFilterGraphCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 50%; text-align: center }
    .stage { background: #fafafa; border: 1px solid #ccc }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    </style>
    """;

// Vertically centered within the 0 0 220 180 viewBox: a 5-point star's own bounding box isn't
// symmetric around its geometric center (one point up, two points down), so the points below are
// built around a center shifted down from the viewBox's true center (90) to 97, splitting the
// resulting 44px of vertical slack evenly (22px top and bottom) rather than the 20px-top/0px-bottom
// the original points left, which is what made the star look bottom-crammed relative to its box.
const string FilterGraphStarPoints = "110,22 128,73 181,74 139,106 154,158 110,127 66,158 81,106 39,74 92,73";

var svgFilterGraphHtml = "<!DOCTYPE html><html><head>" + SvgFilterGraphCss + "</head><body>" +

    "<h1>SVG &lt;filter&gt;: a multi-primitive graph</h1>" +
    "<p class=\"intro\">The classic drop-shadow-via-primitives idiom: feFlood + feComposite (\"in\") builds a solid shadow shape from the source's own alpha, feOffset displaces it, and feMerge layers it under the original. Each step names its own result (flood, shadowColor, offsetShadow) so a later primitive - including feMergeNode's own \"in\" - can look it up directly, not just chain to the immediately-previous step. (docs/supported-svg-features.md#filters)</p>" +
    "<table class=\"sw\"><tr>" +
    SvgFilterGraphPanel("unfiltered star",
        $"""
        <svg viewBox="0 0 220 180" width="210" height="172">
          <polygon points="{FilterGraphStarPoints}"
                   fill="#f39c12"/>
        </svg>
        """) +
    SvgFilterGraphPanel("feFlood + feComposite(in) + feOffset + feMerge",
        $"""
        <svg viewBox="0 0 220 180" width="210" height="172">
          <defs>
            <filter id="primitiveShadow" x="-40%" y="-40%" width="180%" height="180%">
              <feFlood flood-color="#000000" flood-opacity="0.55" result="flood"/>
              <feComposite in="flood" in2="SourceAlpha" operator="in" result="shadowColor"/>
              <feOffset in="shadowColor" dx="8" dy="10" result="offsetShadow"/>
              <feMerge>
                <feMergeNode in="offsetShadow"/>
                <feMergeNode in="SourceGraphic"/>
              </feMerge>
            </filter>
          </defs>
          <polygon points="{FilterGraphStarPoints}"
                   fill="#f39c12" filter="url(#primitiveShadow)"/>
        </svg>
        """) +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("svg_filter_graph", "Graphics & Effects", "SVG Filter: Multi-Primitive Graph",
    "A real SVG <filter> primitive graph (feFlood, feComposite, feOffset, feMerge) building a drop shadow from named, non-adjacently-referenced results - the general filter graph evaluator, not a simple linear chain.",
    svgFilterGraphHtml, pdfConfig);

// ── Lossless WebP/AVIF/TIFF embedding (issue #1107) ───────────────────────────────────
static byte[] BuildLosslessCheckerPatternBytes(int size, string format, object encoderOptions)
{
    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Rgb24);
    var pixels = image.GetPixelSpan();
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            bool dark = ((x / 8) + (y / 8)) % 2 == 0;
            var (r, g, b) = dark ? ((byte)0x0F, (byte)0x17, (byte)0x2A) : ((byte)0xF8, (byte)0xFA, (byte)0xFC);
            int i = (y * size + x) * 3;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
        }
    }

    using var ms = new MemoryStream();
    switch (encoderOptions)
    {
        case PeachImage.Formats.Webp.WebpEncoderOptions webpOptions:
            image.Save(ms, format, webpOptions);
            break;
        case PeachImage.Formats.Avif.AvifEncoderOptions avifOptions:
            image.Save(ms, format, avifOptions);
            break;
    }
    return ms.ToArray();
}

var losslessWebpBase64 = Convert.ToBase64String(
    BuildLosslessCheckerPatternBytes(96, "webp", new PeachImage.Formats.Webp.WebpEncoderOptions { Lossless = true }));
var losslessAvifBase64 = Convert.ToBase64String(
    BuildLosslessCheckerPatternBytes(96, "avif", new PeachImage.Formats.Avif.AvifEncoderOptions { Lossless = true }));

var losslessRasterHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 16px; max-width: 640px; }" +
    ".row { display: flex; gap: 24px; align-items: flex-start; }" +
    ".row img { image-rendering: pixelated; border: 1px solid #cbd5e1; }" +
    ".label { font-size: 11px; color: #555; margin-top: 4px; }" +
    "</style></head><body>" +
    "<h2>Lossless WebP &amp; AVIF embedding</h2>" +
    "<p class=\"note\">A WebP or AVIF source encoded with its own format's lossless mode (VP8L, or AV1's " +
    "lossless coding path) embeds via a raw /FlateDecode stream instead of being silently re-encoded as " +
    "lossy JPEG - the hard checkerboard edges below stay pixel-exact, with no JPEG block artifacts.</p>" +
    "<div class=\"row\">" +
    $"<div><img src=\"data:image/webp;base64,{losslessWebpBase64}\" width=\"96\" height=\"96\">" +
    "<div class=\"label\">Lossless WebP (VP8L)</div></div>" +
    $"<div><img src=\"data:image/avif;base64,{losslessAvifBase64}\" width=\"96\" height=\"96\">" +
    "<div class=\"label\">Lossless AVIF</div></div>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("lossless_webp_avif", "Images & Replaced Content", "Lossless WebP & AVIF Embedding",
    "A WebP or AVIF source that was itself encoded losslessly (VP8L, or AV1's lossless coding path) is " +
    "detected via PeachImage's ImageInfo.IsLosslessEncoding and embedded as a raw /FlateDecode stream " +
    "under ImageCompression.Auto/Lossless, instead of always being re-encoded as lossy JPEG the way every " +
    "earlier PeachPDF version did regardless of the source's own encoding.",
    losslessRasterHtml, pdfConfig);

// ── GIF LZW pass-through (issue #1110) ─────────────────────────────────────────────────
static byte[] BuildFullPaletteGifBytes(int size)
{
    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Rgb24);
    var pixels = image.GetPixelSpan();
    for (int i = 0; i < size * size; i++)
    {
        int color = i % 256;
        pixels[i * 3] = (byte)((color * 53) % 256);
        pixels[i * 3 + 1] = (byte)((color * 97) % 256);
        pixels[i * 3 + 2] = (byte)((color * 181) % 256);
    }

    using var ms = new MemoryStream();
    image.Save(ms, "gif", new PeachImage.Formats.Gif.GifEncoderOptions { MaxColors = 256 });
    return ms.ToArray();
}

var gifPassthroughBase64 = Convert.ToBase64String(BuildFullPaletteGifBytes(64));

var gifPassthroughHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 16px; max-width: 640px; }" +
    "img { image-rendering: pixelated; border: 1px solid #cbd5e1; }" +
    ".label { font-size: 11px; color: #555; margin-top: 4px; }" +
    "</style></head><body>" +
    "<h2>GIF lossless pass-through</h2>" +
    "<p class=\"note\">A GIF whose LZW minimum code size is exactly 8, isn't interlaced, and whose frame " +
    "covers the full logical canvas embeds the same LZW codes its own encoder produced as a PDF " +
    "/LZWDecode stream (re-packed into PDF's bit order) - no LZW decompress/recompress round trip.</p>" +
    $"<img src=\"data:image/gif;base64,{gifPassthroughBase64}\" width=\"128\" height=\"128\">" +
    "<div class=\"label\">Full-palette (MinCodeSize 8) GIF</div>" +
    "</body></html>";

await SaveShowcaseAsync("gif_passthrough", "Images & Replaced Content", "GIF Lossless Pass-through",
    "A GIF that isn't interlaced, whose LZW minimum code size is exactly 8, and whose frame covers the " +
    "full logical canvas embeds the same LZW codes its own encoder produced as /LZWDecode with an " +
    "/Indexed color space - re-packed from GIF's bit order into PDF's, instead of being decoded and " +
    "re-encoded.",
    gifPassthroughHtml, pdfConfig);

// ── PNG alpha-channel /SMask split (issue #1109) ───────────────────────────────────────
static byte[] BuildAlphaGradientPngBytes(int size)
{
    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Rgba32);
    var pixels = image.GetPixelSpan();
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            pixels[i] = 0x2E; pixels[i + 1] = 0x86; pixels[i + 2] = 0xDE; // a consistent blue
            pixels[i + 3] = (byte)(x * 255 / (size - 1)); // alpha ramps left (transparent) to right (opaque)
        }
    }

    using var ms = new MemoryStream();
    image.Save(ms, "png", new PeachImage.Formats.Png.PngEncoderOptions { ColorMode = PeachImage.Formats.Png.PngColorMode.Truecolor });
    return ms.ToArray();
}

var alphaGradientBase64 = Convert.ToBase64String(BuildAlphaGradientPngBytes(160));

var pngAlphaSplitHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 16px; max-width: 640px; }" +
    ".checker { background: repeating-conic-gradient(#ddd 0% 25%, #fff 0% 50%) 0 / 16px 16px; " +
    "  display: inline-block; border-radius: 4px; }" +
    "img { display: block; }" +
    "</style></head><body>" +
    "<h2>PNG alpha-channel /SMask split</h2>" +
    "<p class=\"note\">A PNG with a real per-pixel alpha channel (color type 6, TruecolorAlpha) embeds " +
    "as a color /FlateDecode XObject plus a child /SMask, both PNG-predictor-compressed - split out of " +
    "the source's own interleaved color+alpha IDAT data, instead of a full pixel decode.</p>" +
    "<div class=\"checker\">" +
    $"<img src=\"data:image/png;base64,{alphaGradientBase64}\" width=\"160\" height=\"160\">" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("png_alpha_split", "Images & Replaced Content", "PNG Alpha-Channel /SMask Split",
    "A PNG with a real per-pixel alpha channel (color type 4/6, or a palette source with a genuine " +
    "partial-alpha tRNS entry) embeds as a color /FlateDecode XObject plus a child /SMask, both split " +
    "out of the source's own interleaved IDAT data, instead of a full pixel decode and a raw alpha mask.",
    pngAlphaSplitHtml, pdfConfig);

// ── PNG/WebP/AVIF ICC profile preservation (issue #1106) ───────────────────────────────
// Minimal spec-valid ICC RGB-matrix-TRC profile (ICC.1:2010 §6.3.1.2), just large enough for
// PeachImage's IccColorProfile.TryCreate to parse it successfully - mirrors PeachPDF.Tests'
// IccProfileFixture.BuildRgbProfile (not referenced directly; TestHarness doesn't depend on the
// test project), condensed since only "the profile rides the embed" needs demonstrating here, not
// colorimetric accuracy.
static byte[] BuildShowcaseRgbIccProfile()
{
    static byte[] Curve()
    {
        var d = new byte[14];
        "curv"u8.CopyTo(d);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(8), 1);
        d[12] = 1; d[13] = 0; // gamma = 1.0 (u8Fixed8)
        return d;
    }

    static byte[] Xyz(double x, double y, double z)
    {
        var d = new byte[20];
        "XYZ "u8.CopyTo(d);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(8), (int)Math.Round(x * 65536.0));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(12), (int)Math.Round(y * 65536.0));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(16), (int)Math.Round(z * 65536.0));
        return d;
    }

    var tags = new (string Signature, byte[] Data)[]
    {
        ("rTRC", Curve()), ("gTRC", Curve()), ("bTRC", Curve()),
        ("rXYZ", Xyz(0.4360747, 0.2225045, 0.0139322)),
        ("gXYZ", Xyz(0.3850649, 0.7168786, 0.0971045)),
        ("bXYZ", Xyz(0.1430804, 0.0606169, 0.7141733)),
    };

    const int headerSize = 128;
    int tagTableSize = 4 + tags.Length * 12;
    int tagDataStart = headerSize + tagTableSize;
    var offsets = new int[tags.Length];
    int cursor = tagDataStart;
    for (var i = 0; i < tags.Length; i++)
    {
        offsets[i] = cursor;
        cursor += tags[i].Data.Length;
    }

    var buffer = new byte[cursor];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0), (uint)buffer.Length);
    "mntr"u8.CopyTo(buffer.AsSpan(12));
    "RGB "u8.CopyTo(buffer.AsSpan(16));
    "XYZ "u8.CopyTo(buffer.AsSpan(20));
    "acsp"u8.CopyTo(buffer.AsSpan(36));

    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(headerSize), (uint)tags.Length);
    for (var i = 0; i < tags.Length; i++)
    {
        int entryOffset = headerSize + 4 + i * 12;
        Encoding.ASCII.GetBytes(tags[i].Signature).CopyTo(buffer.AsSpan(entryOffset));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(entryOffset + 4), (uint)offsets[i]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(entryOffset + 8), (uint)tags[i].Data.Length);
        tags[i].Data.CopyTo(buffer.AsSpan(offsets[i]));
    }

    return buffer;
}

static byte[] InsertShowcaseIccProfileIntoPng(byte[] pngBytes, byte[] iccProfile)
{
    uint ihdrLength = (uint)((pngBytes[8] << 24) | (pngBytes[9] << 16) | (pngBytes[10] << 8) | pngBytes[11]);
    int ihdrChunkEnd = 8 + 4 + 4 + (int)ihdrLength + 4;

    using var compressed = new MemoryStream();
    using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
    {
        zlib.Write(iccProfile);
    }

    var chunkData = new byte[4 + 1 + compressed.Length]; // "icc\0" (name + terminator) + compression-method + data
    "icc\0"u8.CopyTo(chunkData); // chunkData[4] (compression method: zlib/deflate) stays 0
    compressed.ToArray().CopyTo(chunkData.AsSpan(5));

    using var result = new MemoryStream();
    result.Write(pngBytes, 0, ihdrChunkEnd);

    Span<byte> lengthBytes = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)chunkData.Length);
    result.Write(lengthBytes);
    var typeBytes = "iCCP"u8.ToArray();
    result.Write(typeBytes);
    result.Write(chunkData);
    var crcInput = new byte[typeBytes.Length + chunkData.Length];
    typeBytes.CopyTo(crcInput, 0);
    chunkData.CopyTo(crcInput, typeBytes.Length);
    Span<byte> crcBytes = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ShowcaseCrc32(crcInput));
    result.Write(crcBytes);

    result.Write(pngBytes, ihdrChunkEnd, pngBytes.Length - ihdrChunkEnd);
    return result.ToArray();
}

static uint ShowcaseCrc32(byte[] data)
{
    uint crc = 0xFFFFFFFF;
    foreach (var b in data)
    {
        crc ^= b;
        for (var k = 0; k < 8; k++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
    }
    return crc ^ 0xFFFFFFFF;
}

static byte[] BuildIccTaggedGradientPngBytes(int size)
{
    using var image = PeachImage.Image.Create(size, size, PeachImage.PixelFormat.Rgb24);
    var pixels = image.GetPixelSpan();
    for (var y = 0; y < size; y++)
    {
        for (var x = 0; x < size; x++)
        {
            int i = (y * size + x) * 3;
            pixels[i] = (byte)(x * 255 / (size - 1));
            pixels[i + 1] = (byte)(y * 255 / (size - 1));
            pixels[i + 2] = 0x99;
        }
    }

    using var ms = new MemoryStream();
    image.Save(ms, "png", new PeachImage.Formats.Png.PngEncoderOptions { ColorMode = PeachImage.Formats.Png.PngColorMode.Truecolor });
    return InsertShowcaseIccProfileIntoPng(ms.ToArray(), BuildShowcaseRgbIccProfile());
}

var iccPngBase64 = Convert.ToBase64String(BuildIccTaggedGradientPngBytes(160));

var iccPreservationHtml =
    "<html><head><style>" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 16px; max-width: 640px; }" +
    "img { display: block; border: 1px solid #cbd5e1; }" +
    "</style></head><body>" +
    "<h2>ICC color profile preservation</h2>" +
    "<p class=\"note\">A PNG carrying an embedded iCCP color profile now embeds that profile as an " +
    "/ICCBased color space alongside its pass-through pixel data (instead of a bare /DeviceRGB that " +
    "silently drops the source's color intent) - the same treatment WebP and AVIF sources get via a " +
    "second native decode used only to read their embedded profile.</p>" +
    $"<img src=\"data:image/png;base64,{iccPngBase64}\" width=\"160\" height=\"160\">" +
    "</body></html>";

await SaveShowcaseAsync("icc_profile_preservation", "Images & Replaced Content", "ICC Color Profile Preservation",
    "A PNG, WebP, or AVIF source's embedded ICC color profile is now extracted and embedded as an " +
    "/ICCBased color space (/N + /Alternate + the raw profile bytes) instead of being silently dropped " +
    "in favor of a bare /DeviceRGB or /DeviceGray - preserving the source's actual color intent for " +
    "wide-gamut or non-sRGB-tagged images.",
    iccPreservationHtml, pdfConfig);

// ── text-underline-offset / text-underline-position ─────────────────────────────────
var textUnderlineOffsetPositionHtml =
    "<html><head><style>" +
    "body { font-family: serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; font-family: sans-serif; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 20px; max-width: 640px; font-family: sans-serif; }" +
    "table { border-collapse: collapse; font-size: 22px; }" +
    "td { padding: 10px 24px; border-bottom: 1px solid #ddd; }" +
    "td.label { font-family: sans-serif; font-size: 12px; color: #555; white-space: nowrap; }" +
    "</style></head><body>" +
    "<h2>text-underline-offset &amp; text-underline-position</h2>" +
    "<p class=\"note\">Both were previously unimplemented and silently dropped. text-underline-offset " +
    "moves the line further from (or closer to) the text; text-underline-position chooses where it " +
    "starts from before that offset is applied.</p>" +
    "<table>" +
    "<tr><td class=\"label\">auto (default)</td><td style=\"text-decoration:underline\">Hamburgefonstiv</td></tr>" +
    "<tr><td class=\"label\">offset: 6px</td><td style=\"text-decoration:underline; text-underline-offset:6px\">Hamburgefonstiv</td></tr>" +
    "<tr><td class=\"label\">offset: -2px</td><td style=\"text-decoration:underline; text-underline-offset:-2px\">Hamburgefonstiv</td></tr>" +
    "<tr><td class=\"label\">offset: 25%</td><td style=\"text-decoration:underline; text-underline-offset:25%\">Hamburgefonstiv</td></tr>" +
    "<tr><td class=\"label\">position: from-font</td><td style=\"text-decoration:underline; text-underline-position:from-font\">Hamburgefonstiv</td></tr>" +
    "<tr><td class=\"label\">position: under</td><td style=\"text-decoration:underline; text-underline-position:under\">Hamburgefonstiv gjpqy</td></tr>" +
    "</table>" +
    "</body></html>";

await SaveShowcaseAsync("text_underline_offset_position", "Typography & Text", "text-underline-offset & text-underline-position",
    "text-underline-offset moves an underline further from (or closer to) the text it decorates; " +
    "text-underline-position chooses auto/from-font/under as the position that offset is measured from. " +
    "Both were previously unimplemented and silently dropped at parse time.",
    textUnderlineOffsetPositionHtml, pdfConfig);

// ── Vertical writing mode: text-decoration geometry ──────────────────────────────────
var verticalDecorationHtml =
    "<html><head><style>" +
    "body { font-family: serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 24px 0 4px; font-family: sans-serif; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 20px; max-width: 640px; font-family: sans-serif; }" +
    ".row { display: flex; gap: 40px; align-items: flex-start; }" +
    ".column { writing-mode: vertical-rl; height: 220px; font-size: 22px; }" +
    ".column.lr { writing-mode: vertical-lr; }" +
    ".column.big { font-size: 40px; font-weight: bold; }" +
    "</style></head><body>" +
    "<h2>Vertical writing mode: text-decoration</h2>" +
    "<p class=\"note\">An underline/overline/line-through on vertical text now runs down the column, on " +
    "the correct physical side of the glyphs (css-writing-modes-4's over/under sides), instead of being " +
    "drawn as a short horizontal stroke across the top.</p>" +
    "<div class=\"row\">" +
    "<div class=\"column\" style=\"text-decoration:underline\">Underline (vertical-rl)</div>" +
    "<div class=\"column\" style=\"text-decoration:overline\">Overline (vertical-rl)</div>" +
    "<div class=\"column\" style=\"text-decoration:overline double\">Double overline (vertical-rl)</div>" +
    "<div class=\"column lr\" style=\"text-decoration:underline\">Underline (vertical-lr)</div>" +
    "</div>" +
    "<h2>text-decoration-skip-ink: rotated run now breaks around ink (issue #1145)</h2>" +
    "<p class=\"note\">Latin text is a rotated run under the default (mixed) text-orientation - one " +
    "ordinary horizontal glyph run reoriented as a whole - so its ink can now be measured and skipped, " +
    "same as under a horizontal writing mode. An upright run (forced here via text-orientation:upright) " +
    "has no single natural horizontal layout to reduce to and still draws unbroken - the honestly-partial " +
    "half of this issue.</p>" +
    "<div class=\"row\">" +
    "<div class=\"column\" style=\"text-decoration:underline\">gypsy jog (rotated)</div>" +
    "<div class=\"column\" style=\"text-decoration:underline; text-orientation:upright\">gypsy jog (upright)</div>" +
    "</div>" +
    "<h2>text-underline-position: left/right (issue #1146)</h2>" +
    "<p class=\"note\">left/right pin the underline to a literal physical edge instead of the writing " +
    "mode's own default under/over mapping. The third column combines underline+overline with a side " +
    "that would put the underline where the overline normally sits - per css-text-decor-4 &sect;2.5, the " +
    "overline switches to the opposite edge instead of overlapping it.</p>" +
    "<div class=\"row\">" +
    "<div class=\"column big\" style=\"text-decoration:underline; text-underline-position:left\">Left</div>" +
    "<div class=\"column big\" style=\"text-decoration:underline; text-underline-position:right\">Right</div>" +
    "<div class=\"column big\" style=\"text-decoration:underline overline; text-underline-position:right\">Switch</div>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("vertical_writing_mode_decoration", "Typography & Text", "Vertical Writing Mode: text-decoration",
    "Under a true vertical writing mode (vertical-rl/vertical-lr), a text-decoration line now runs " +
    "along the column's own extent on the correct physical side of the glyphs, instead of being drawn " +
    "as a short horizontal stroke across its top. Also covers text-decoration-skip-ink now applying to " +
    "a rotated run and text-underline-position: left/right pinning the underline to a " +
    "literal physical edge, switching a same-line overline to the opposite edge when they would " +
    "otherwise collide.",
    verticalDecorationHtml, pdfConfig);

// ── Vertical writing mode: background-clip: text (issue #1123, #1194) ───────────────
var verticalBackgroundClipTextHtml =
    $"<html><head><style>" +
    $"@font-face {{ font-family: 'PeachPDF CJK Subset'; src: url(data:font/ttf;base64,{writingModeCjkFontB64}) format('truetype') }}" +
    "body { font-family: sans-serif; margin: 24px; color: #1a1a1a; }" +
    "h2 { font-size: 20px; margin: 0 0 4px; }" +
    ".note { color: #555; font-size: 12px; margin: 0 0 20px; max-width: 640px; }" +
    ".row { display: flex; gap: 40px; align-items: flex-start; }" +
    ".col { writing-mode: vertical-rl; height: 220px; font-size: 44px; font-weight: bold; " +
    "background: linear-gradient(to bottom, #e91e63, #3f51b5); background-clip: text; " +
    "-webkit-background-clip: text; color: transparent; }" +
    ".col.upright { text-orientation: upright; }" +
    ".col.lr { writing-mode: vertical-lr; }" +
    ".col.cjk { font-family: 'PeachPDF CJK Subset', sans-serif; }" +
    "</style></head><body>" +
    "<h2>background-clip: text under a vertical writing mode</h2>" +
    "<p class=\"note\">A gradient background now clips to the actual glyph-outline union under " +
    "vertical-rl/vertical-lr, matching the horizontal case, instead of falling back to a plain " +
    "border-box fill. The rotated column builds one outline per whole word, transformed into its " +
    "physical footprint; the upright column builds one outline per character, translated to its own " +
    "cell. The CJK column's font carries real OpenType vertical metrics (vhea/vmtx) - each upright " +
    "character's outline is additionally clipped to its own reserved cell before being unioned in " +
    "(issue #1194), so the gradient hugs the glyph ink tightly instead of falling back to a plain " +
    "border-box fill the way this exact combination used to.</p>" +
    "<div class=\"row\">" +
    "<div class=\"col\">PEACH</div>" +
    "<div class=\"col upright\">PEACH</div>" +
    "<div class=\"col lr\">PEACH</div>" +
    "<div class=\"col upright cjk\">縦書きテキスト</div>" +
    "</div>" +
    "</body></html>";

await SaveShowcaseAsync("vertical_writing_mode_background_clip_text", "Typography & Text", "Vertical Writing Mode: background-clip: text",
    "background-clip: text now clips to the real glyph-outline union under a true vertical writing " +
    "mode (vertical-rl/vertical-lr), for both a rotated (sideways) run and an upright run - including " +
    "one whose font carries real vhea/vmtx vertical metrics - instead of falling back to " +
    "a plain border-box fill.",
    verticalBackgroundClipTextHtml, pdfConfig);

// --- Font-relative units showcase: ex/ch/cap/ic/lh measured from the font, plus the root-element forms ---
//
// The units are measured from the used font (CSS Values and Units 4 section 6.1), not a fixed fraction of the font
// size, so every bar below is sized by a unit and lines up with the text or the ruler it claims to measure. Source
// Code Pro is monospace (its "0" is exactly 0.6em); Source Sans 3 is proportional, so the same 20ch is a different
// width in each. The grey "0.5em" bars are what these units resolved to before they were measured.
var fontRelativeUnitsHtml =
    "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    $"@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{sourceSans3B64}') format('truetype'); }}" +
    $"@font-face {{ font-family: 'SCP'; src: url('data:font/opentype;base64,{sourceCodeProB64}') format('opentype'); }}" +
    $"@font-face {{ font-family: 'STIX'; src: url('data:font/truetype;base64,{stixTwoMathB64}') format('truetype'); }}" +
    "html { font-size: 12pt } body { font-family: 'SS3', serif; margin: 0; color: #222 }" +
    "h1 { font-size: 15pt; margin: 0 0 0.3em }" +
    "h2 { font-size: 11pt; margin: 1.1em 0 0.4em; padding-bottom: 2px; border-bottom: 1px solid #999 }" +
    "p.intro { font-size: 9pt; margin: 0 0 0.6em; color: #555; font-family: Arial, sans-serif }" +
    ".lab { font: 7pt Arial, sans-serif; color: #666; margin: 5px 0 1px }" +
    ".bar { height: 8px; background: steelblue; margin: 0 0 2px } .old { height: 4px; background: #bbb; margin: 0 0 4px }" +
    ".mono { font-family: 'SCP', monospace } .sans { font-family: 'SS3', serif } .big { font-size: 20pt }" +
    ".ruler { font-size: 20pt; line-height: 1; white-space: pre; border-bottom: 1px solid #c33 }" +
    ".capbox { display: inline-block; width: 6ch; height: 1cap; background: #e8a; vertical-align: baseline }" +
    ".exbox { display: inline-block; width: 6ch; height: 1ex; background: #8ae; vertical-align: baseline }" +
    ".lhstack div { height: 1lh; border-top: 1px solid #c33; font-size: 10pt }" +
    "svg { border: 1px solid #ddd }" +
    "</style></head><body>" +
    "<h1>Font-relative units</h1>" +
    "<p class=\"intro\"><code>ex</code>, <code>ch</code>, <code>cap</code>, <code>ic</code> and <code>lh</code> " +
    "(and the root-element <code>rex</code>/<code>rch</code>/<code>rcap</code>/<code>ric</code>/<code>rlh</code>) are " +
    "measured from the font an element actually uses.</p>" +

    "<h2>ch: the width of the \"0\" glyph</h2>" +
    "<div class=\"ruler big mono\">00000000000000000000</div>" +
    "<div class=\"lab\">Source Code Pro, 20pt, width: 20ch (blue) vs. the old 0.5em approximation (grey)</div>" +
    "<div class=\"big mono\"><div class=\"bar\" style=\"width: 20ch\"></div><div class=\"old\" style=\"width: 10em\"></div></div>" +
    "<div class=\"ruler big sans\">00000000000000000000</div>" +
    "<div class=\"lab\">Source Sans 3, 20pt, width: 20ch - a proportional font, so a different width</div>" +
    "<div class=\"big sans\"><div class=\"bar\" style=\"width: 20ch\"></div><div class=\"old\" style=\"width: 10em\"></div></div>" +

    "<h2>ex and cap: the x-height and cap height</h2>" +
    "<div class=\"big sans\"><span class=\"exbox\"></span> <span class=\"capbox\"></span> xxxx HHHH</div>" +
    "<div class=\"lab\">boxes 1ex tall (blue) and 1cap tall (pink), for comparing their heights with a lowercase x and a capital H</div>" +

    "<h2>lh: the used line-height</h2>" +
    "<div class=\"lhstack\" style=\"line-height: 18pt\"><div>line-height: 18pt - each row is 1lh tall</div><div>second row</div><div>third row</div></div>" +
    "<div class=\"lhstack\" style=\"line-height: 1.6\"><div>line-height: 1.6 - each row is 1lh tall</div><div>second row</div><div>third row</div></div>" +

    "<h2>Root-element forms follow the root, not the element</h2>" +
    "<div class=\"lab\">html is 12pt Source Sans 3. Both bars sit in a 30pt Source Code Pro box, but the blue bar is 10rch - ten of the ROOT's zeros - " +
    "while the green bar is 10ch of the box's own font</div>" +
    "<div class=\"mono\" style=\"font-size: 30pt\"><div class=\"bar\" style=\"width: 10rch\"></div><div class=\"bar\" style=\"width: 10ch; background: seagreen\"></div></div>" +

    "<h2>SVG</h2>" +
    "<svg width=\"400\" height=\"70\" font-family=\"SCP\" font-size=\"20\" viewBox=\"0 0 400 70\">" +
    "<rect x=\"1ch\" y=\"1ch\" width=\"10ch\" height=\"1cap\" fill=\"steelblue\"/>" +
    "<rect x=\"1ch\" y=\"3em\" width=\"10ch\" height=\"6\" fill=\"#bbb\" stroke=\"#222\" stroke-width=\"calc(1em / 10)\" stroke-dasharray=\"1ch 1ch\"/>" +
    "<text x=\"16ch\" y=\"1.2em\" font-size=\"1em\">0000000000</text>" +
    "</svg>" +
    "<div class=\"lab\">Widths, heights and the dashed stroke in ch/em/cap against Source Code Pro; the text is ten zeros wide - the same ten as the blue bar's 10ch</div>" +

    "<h2>MathML</h2>" +
    "<div style=\"font-size: 16pt; font-family: 'STIX'\">" +
    "<math><mi>a</mi><mspace width=\"4ch\" style=\"background:#fcc\"/><mi>b</mi><mspace width=\"2em\"/><mi>c</mi></math>" +
    " <span style=\"font: 7pt Arial\">mspace width=\"4ch\" then \"2em\" - measured from the math font</span></div>" +
    "</body></html>";

await SaveShowcaseAsync("font_relative_units", "Typography & Text", "Font-relative units: ex, ch, cap, ic, lh",
    "ex, ch, cap, ic and lh - and their root-element rex/rch/rcap/ric/rlh forms - measured from the font an element " +
    "actually uses instead of a fixed 0.5em, in HTML lengths, SVG geometry/stroke/text and MathML spacing.",
    fontRelativeUnitsHtml, pdfConfig);

const string declarativeApiSource =
    """"
    var generator = new PdfGenerator();

    var document = await generator.CreateDocument(doc =>
    {
        doc.Page(page =>
        {
            page.Size(PageSize.A4);
            page.Margin(24);
            page.Header(header => header.Text(t =>
            {
                t.Alignment(TextAlignment.Center);
                t.Span("Quarterly Report").Bold().FontColor(PdfColor.FromRgb(70, 70, 70));
            }));
            page.Footer(footer => footer.Text(t =>
            {
                t.Alignment(TextAlignment.Center);
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            }));
            page.Content(container =>
            {
                container.Column(column =>
                {
                    column.Spacing(16);

                    column.Item()
                        .Padding(16).Border(1, PdfColor.FromHex("#DDDDDD")).CornerRadius(8)
                        .Background(PdfColor.FromRgb(248, 248, 248))
                        .Shadow(new PdfBoxShadow(PdfColor.FromArgb(40, 0, 0, 0), 0, 3, 6))
                        .Text(t =>
                        {
                            t.Span("Built directly in C# - ").FontSize(14);
                            t.Span("no HTML or CSS strings involved.").FontSize(14).Italic();
                        });

                    column.Item().Table(table =>
                    {
                        table.Columns(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Padding(4).Text(t => t.Span("Region").Bold());
                            header.Cell().Padding(4).Text(t => t.Span("Q1").Bold());
                            header.Cell().Padding(4).Text(t => t.Span("Q2").Bold());
                        });
                        foreach (var (region, q1, q2) in new[] { ("North", "$42k", "$48k"), ("South", "$31k", "$35k"), ("West", "$27k", "$30k") })
                        {
                            table.Row(row =>
                            {
                                row.Cell().Padding(4).Text(region);
                                row.Cell().Padding(4).Text(q1);
                                row.Cell().Padding(4).Text(q2);
                            });
                        }
                    });

                    column.Item().LineHorizontal(1, PdfColor.FromHex("#BBBBBB"), dashed: true);

                    column.Item().UnorderedList(list =>
                    {
                        list.Item().Text("Revenue grew across every region.");
                        list.Item().Text("West opened its first retail location.");
                    }, PdfListMarkerType.Disc);

                    column.Item().ClampLines(2, "… [continued]").Text(
                        "A full year in review: every region grew its revenue year over year, led by a " +
                        "particularly strong second half, and the West region opened its first physical " +
                        "retail location - a milestone the team has been working toward since the start " +
                        "of the fiscal year.");

                    column.Item().Text(t =>
                    {
                        // PdfTextDirection.Auto detects each span's own base direction from its text (the
                        // same first-strong-character detection dir="auto" uses on the HTML side) - no
                        // need to know ahead of time which language a given run of content is in. This
                        // English span resolves to ltr; a Hebrew or Arabic span (given a font covering
                        // those glyphs) would resolve to rtl the same way.
                        t.Span("Auto-detected direction, per span: ").Direction(PdfTextDirection.Auto);
                        t.Span("this paragraph resolves left-to-right.").Direction(PdfTextDirection.Auto);
                    });

                    // Standalone SVG: author-supplied markup placed directly, and a real charting
                    // library's own SVG export - both go through the same Svg(string) overload.
                    column.Item().Row(row =>
                    {
                        row.Spacing(12);

                        row.Item().Grow().Svg(
                            """
                            <svg xmlns="http://www.w3.org/2000/svg" width="300" height="180" viewBox="0 0 100 60">
                              <circle cx="50" cy="30" r="26" fill="#FFDEE9" stroke="#B5FFFC" stroke-width="3"/>
                              <text x="50" y="34" font-size="10" text-anchor="middle" fill="#444">Hand-authored</text>
                            </svg>
                            """);

                        var scatterPlot = new Plot();
                        scatterPlot.Add.Scatter(
                            new double[] { 1, 2, 3, 4, 5, 6 },
                            new double[] { 12, 18, 15, 24, 21, 30 });
                        scatterPlot.Title("Weekly signups");
                        row.Item().Grow().Svg(scatterPlot.GetSvgXml(300, 180));
                    });

                    // A dynamic raster chart, generated at exactly the container's own resolved pixel
                    // size once layout knows it - the reason Image(Func<PdfSize,byte[]>) exists at all.
                    column.Item().Width(300).Height(160).Image(size =>
                    {
                        var barPlot = new Plot();
                        barPlot.Add.Bars(new double[] { 42, 31, 27 });
                        barPlot.Title("Q1 revenue by region ($k)");
                        // 2x the container's own point size for a crisp raster at normal PDF viewer zoom
                        // (a PDF point isn't a device pixel) - the fill-by-default width:100%/height:100%
                        // still scales this to the container's actual size regardless of pixel count.
                        return barPlot.GetImageBytes((int)size.Width * 2, (int)size.Height * 2, ImageFormat.Png);
                    });
                });
            });
        });
    });

    var stream = new MemoryStream();
    document.Save(stream);
    """";

await SaveDeclarativeShowcaseAsync("declarative_api", "Document Building", "Declarative Document-Building API",
    "PdfGenerator.CreateDocument: pages, a padded/bordered/shadowed card, a table with a repeating header, a dashed divider, a bulleted list, a line-clamped paragraph with a custom ellipsis, auto-detected per-span text direction, hand-authored and ScottPlot-generated standalone SVG, a dynamically-generated raster chart sized to its own container, and a repeating page-numbered footer, built directly in C# with no HTML/CSS strings - layered entirely on PeachPDF's own flexbox, table, list, line-clamp, bidi-detection, SVG, and running-header/footer machinery.",
    declarativeApiSource,
    async gen =>
    {
        return await gen.CreateDocument(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSize.A4);
                page.Margin(24);
                page.Header(header => header.Text(t =>
                {
                    t.Alignment(TextAlignment.Center);
                    t.Span("Quarterly Report").Bold().FontColor(PdfColor.FromRgb(70, 70, 70));
                }));
                page.Footer(footer => footer.Text(t =>
                {
                    t.Alignment(TextAlignment.Center);
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                }));
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        column.Spacing(16);

                        column.Item()
                            .Padding(16).Border(1, PdfColor.FromHex("#DDDDDD")).CornerRadius(8)
                            .Background(PdfColor.FromRgb(248, 248, 248))
                            .Shadow(new PdfBoxShadow(PdfColor.FromArgb(40, 0, 0, 0), 0, 3, 6))
                            .Text(t =>
                            {
                                t.Span("Built directly in C# - ").FontSize(14);
                                t.Span("no HTML or CSS strings involved.").FontSize(14).Italic();
                            });

                        column.Item().Table(table =>
                        {
                            table.Columns(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Padding(4).Text(t => t.Span("Region").Bold());
                                header.Cell().Padding(4).Text(t => t.Span("Q1").Bold());
                                header.Cell().Padding(4).Text(t => t.Span("Q2").Bold());
                            });
                            foreach (var (region, q1, q2) in new[] { ("North", "$42k", "$48k"), ("South", "$31k", "$35k"), ("West", "$27k", "$30k") })
                            {
                                table.Row(row =>
                                {
                                    row.Cell().Padding(4).Text(region);
                                    row.Cell().Padding(4).Text(q1);
                                    row.Cell().Padding(4).Text(q2);
                                });
                            }
                        });

                        column.Item().LineHorizontal(1, PdfColor.FromHex("#BBBBBB"), dashed: true);

                        column.Item().UnorderedList(list =>
                        {
                            list.Item().Text("Revenue grew across every region.");
                            list.Item().Text("West opened its first retail location.");
                        }, PdfListMarkerType.Disc);

                        column.Item().ClampLines(2, "… [continued]").Text(
                            "A full year in review: every region grew its revenue year over year, led by a " +
                            "particularly strong second half, and the West region opened its first physical " +
                            "retail location - a milestone the team has been working toward since the start " +
                            "of the fiscal year.");

                        column.Item().Text(t =>
                        {
                            t.Span("Auto-detected direction, per span: ").Direction(PdfTextDirection.Auto);
                            t.Span("this paragraph resolves left-to-right.").Direction(PdfTextDirection.Auto);
                        });

                        column.Item().Row(row =>
                        {
                            row.Spacing(12);

                            row.Item().Grow().Svg(
                                """
                                <svg xmlns="http://www.w3.org/2000/svg" width="300" height="180" viewBox="0 0 100 60">
                                  <circle cx="50" cy="30" r="26" fill="#FFDEE9" stroke="#B5FFFC" stroke-width="3"/>
                                  <text x="50" y="34" font-size="10" text-anchor="middle" fill="#444">Hand-authored</text>
                                </svg>
                                """);

                            var scatterPlot = new Plot();
                            scatterPlot.Add.Scatter(
                                new double[] { 1, 2, 3, 4, 5, 6 },
                                new double[] { 12, 18, 15, 24, 21, 30 });
                            scatterPlot.Title("Weekly signups");
                            row.Item().Grow().Svg(scatterPlot.GetSvgXml(300, 180));
                        });

                        column.Item().Width(300).Height(160).Image(size =>
                        {
                            var barPlot = new Plot();
                            barPlot.Add.Bars(new double[] { 42, 31, 27 });
                            barPlot.Title("Q1 revenue by region ($k)");
                            return barPlot.GetImageBytes((int)size.Width * 2, (int)size.Height * 2, ImageFormat.Png);
                        });
                    });
                });
            });
        });
    });

const string sectionedPageNumbersSource =
    """
    var generator = new PdfGenerator();

    var document = await generator.CreateDocument(doc =>
    {
        doc.Page(page =>
        {
            page.Size(PageSize.A4);
            page.Margin(36);
            page.Footer(footer => footer.Text(t =>
            {
                t.Alignment(TextAlignment.Center);
                t.Span("Doc ");
                t.CurrentPageNumber();
                t.Span("/");
                t.TotalPages();
                t.Span("  ·  Chapter ");
                t.PageNumberWithinSection("chapter1");
                t.Span("/");
                t.TotalPagesWithinSection("chapter1");
            }));
            page.Content(content => content.Column(column =>
            {
                column.Spacing(12);
                column.Item().Text(t => t.Span("Front matter (outside the section)").Bold().FontSize(16));
                for (var i = 0; i < 10; i++)
                {
                    column.Item().Height(28).Text($"Front matter, line {i + 1}. Not counted by the chapter's own page numbers.");
                }

                column.Item().BeginPageNumberOfSection("chapter1").Text(t => t.Span("Chapter 1").Bold().FontSize(16));
                for (var i = 0; i < 24; i++)
                {
                    var paragraph = column.Item().Height(28);
                    if (i == 23) paragraph = paragraph.EndPageNumberOfSection("chapter1");
                    paragraph.Text($"Chapter 1, paragraph {i + 1}.");
                }

                column.Item().Text(t => t.Span("Appendix (outside the section)").Bold().FontSize(16));
                for (var i = 0; i < 10; i++)
                {
                    column.Item().Height(28).Text($"Appendix, line {i + 1}. Also not counted by the chapter's own page numbers.");
                }
            }));
        });
    });

    var stream = new MemoryStream();
    document.Save(stream);
    """;

await SaveDeclarativeShowcaseAsync("declarative_sectioned_page_numbers", "Document Building", "Sectioned Page Numbers",
    "BeginPageNumberOfSection/EndPageNumberOfSection mark a chapter's own page range; PageNumberWithinSection/TotalPagesWithinSection then resolve \"page 2 of 5\" scoped to just that chapter, shown here alongside the document-wide CurrentPageNumber/TotalPages for comparison - unsectioned front matter and an appendix bracket the chapter to make the difference visible.",
    sectionedPageNumbersSource,
    async gen =>
    {
        return await gen.CreateDocument(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSize.A4);
                page.Margin(36);
                page.Footer(footer => footer.Text(t =>
                {
                    t.Alignment(TextAlignment.Center);
                    t.Span("Doc ");
                    t.CurrentPageNumber();
                    t.Span("/");
                    t.TotalPages();
                    t.Span("  ·  Chapter ");
                    t.PageNumberWithinSection("chapter1");
                    t.Span("/");
                    t.TotalPagesWithinSection("chapter1");
                }));
                page.Content(content => content.Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Text(t => t.Span("Front matter (outside the section)").Bold().FontSize(16));
                    for (var i = 0; i < 10; i++)
                    {
                        column.Item().Height(28).Text($"Front matter, line {i + 1}. Not counted by the chapter's own page numbers.");
                    }

                    column.Item().BeginPageNumberOfSection("chapter1").Text(t => t.Span("Chapter 1").Bold().FontSize(16));
                    for (var i = 0; i < 24; i++)
                    {
                        var paragraph = column.Item().Height(28);
                        if (i == 23) paragraph = paragraph.EndPageNumberOfSection("chapter1");
                        paragraph.Text($"Chapter 1, paragraph {i + 1}.");
                    }

                    column.Item().Text(t => t.Span("Appendix (outside the section)").Bold().FontSize(16));
                    for (var i = 0; i < 10; i++)
                    {
                        column.Item().Height(28).Text($"Appendix, line {i + 1}. Also not counted by the chapter's own page numbers.");
                    }
                }));
            });
        });
    });

const string declarativeStylesheetAndHtmlSource =
    """""
    var generator = new PdfGenerator();
    var stylesheet = await generator.ParseStyleSheet(
        """"
        .invoice-title { color: #2C3E50; }
        .line-item.highlight { background-color: #FFF3CD; }
        @page { margin: 28pt; }
        """");

    var document = await generator.CreateDocument(doc =>
    {
        doc.Stylesheet(stylesheet);
        doc.Page(page =>
        {
            page.Size(PageSize.A4);
            page.Header(header => header.Text(t => t.Span("Invoice #1042").Bold()));
            page.Content(container =>
            {
                container.Column(column =>
                {
                    column.Spacing(14);

                    column.Item().Class("invoice-title").Text(t => t.Span("Acme Consulting Services").FontSize(16).Bold());

                    // A document-level stylesheet lets a caller's own class/id selector target a
                    // declaratively-built container, and a compound class like "line-item highlight"
                    // works exactly like it would in HTML.
                    column.Item().Table(table =>
                    {
                        table.Columns(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Text(t => t.Span("Item").Bold());
                            header.Cell().Text(t => t.Span("Amount").Bold());
                        });
                        table.Row(row =>
                        {
                            row.Cell().Class("line-item").Text("Consulting hours");
                            row.Cell().Class("line-item").Text("$4,200");
                        });
                        table.Row(row =>
                        {
                            row.Cell().Class("line-item highlight").Text("Rush delivery fee");
                            row.Cell().Class("line-item highlight").Text("$350");
                        });
                    });

                    // IContainer.Html(...) splices a real parsed HTML fragment into the tree - here, a
                    // terms paragraph with a <slot> filled in from C# with the customer's own name.
                    column.Item().Html(
                        """"
                        <p>These terms apply to <strong><slot name="customer">this customer</slot></strong>
                        until the invoice is settled in full.</p>
                        """",
                        onSlot: (slot, slotContainer) =>
                        {
                            if (slot.Name == "customer")
                            {
                                slotContainer.Text("Example Retail Co.");
                            }
                        });
                });
            });
        });
    });

    var stream = new MemoryStream();
    document.Save(stream);
    """"";

await SaveDeclarativeShowcaseAsync("declarative_stylesheet_and_html", "Document Building", "Stylesheet, Class/Id, and HTML Fragments",
    "IDocumentBuilder.Stylesheet attaches a document-level stylesheet so IContainer.Class/Id/Tag can target declaratively-built containers with real CSS selectors (including compound classes like \"line-item highlight\"), a base @page rule's margin merges with the page's own Header, and IContainer.Html(...) splices a real parsed HTML fragment into the tree with a <slot> filled in from C#.",
    declarativeStylesheetAndHtmlSource,
    async gen =>
    {
        var stylesheet = await gen.ParseStyleSheet(
            """
            .invoice-title { color: #2C3E50; }
            .line-item.highlight { background-color: #FFF3CD; }
            @page { margin: 28pt; }
            """);

        return await gen.CreateDocument(doc =>
        {
            doc.Stylesheet(stylesheet);
            doc.Page(page =>
            {
                page.Size(PageSize.A4);
                page.Header(header => header.Text(t => t.Span("Invoice #1042").Bold()));
                page.Content(container =>
                {
                    container.Column(column =>
                    {
                        column.Spacing(14);

                        column.Item().Class("invoice-title").Text(t => t.Span("Acme Consulting Services").FontSize(16).Bold());

                        column.Item().Table(table =>
                        {
                            table.Columns(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Text(t => t.Span("Item").Bold());
                                header.Cell().Text(t => t.Span("Amount").Bold());
                            });
                            table.Row(row =>
                            {
                                row.Cell().Class("line-item").Text("Consulting hours");
                                row.Cell().Class("line-item").Text("$4,200");
                            });
                            table.Row(row =>
                            {
                                row.Cell().Class("line-item highlight").Text("Rush delivery fee");
                                row.Cell().Class("line-item highlight").Text("$350");
                            });
                        });

                        column.Item().Html(
                            """
                            <p>These terms apply to <strong><slot name="customer">this customer</slot></strong>
                            until the invoice is settled in full.</p>
                            """,
                            onSlot: (slot, slotContainer) =>
                            {
                                if (slot.Name == "customer")
                                {
                                    slotContainer.Text("Example Retail Co.");
                                }
                            });
                    });
                });
            });
        });
    });

if (benchmarkMode)
{
    PrintBenchmarkReport();
}
else
{
    // The manifest that drives the website's /showcase page (see docs/showcase.html and
    // .github/workflows/pages.yml). Field names are camelCased for Liquid (site.data.showcases).
    var manifestJson = JsonSerializer.Serialize(showcaseManifest,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    File.WriteAllText(Path.Combine(outputDir, "showcases.json"), manifestJson);
    Console.WriteLine($"Saved showcases.json ({showcaseManifest.Count} showcases)");
}

// SourceKind says what the "source" file (Html) holds, so docs/showcase.html can label the link
// accurately: "html" for the markup given to GeneratePdf, "csharp" for the code a declarative-API showcase
// runs (that file is a minimal HTML page wrapping the C#, purely so the manifest schema stays one shape).
record ShowcaseEntry(string Slug, string Category, string Title, string Description, string Pdf, string Html, string SourceKind = ShowcaseSourceKind.Html);

static class ShowcaseSourceKind
{
    public const string Html = "html";
    public const string CSharp = "csharp";
}

/// <summary>One showcase's --benchmark measurements: one wall-time (ms) and one allocated-bytes sample per iteration.</summary>
record BenchmarkResult(string Slug, double[] WallTimesMs, long[] AllocatedBytes)
{
    public double MeanWallTimeMs => WallTimesMs.Average();
    public double MedianWallTimeMs => Median(WallTimesMs);
    public double MeanAllocatedBytes => AllocatedBytes.Average(b => (double)b);
    public double MedianAllocatedBytes => Median(AllocatedBytes.Select(b => (double)b));

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}
