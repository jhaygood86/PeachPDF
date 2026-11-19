using PeachDrawing.Core;

namespace PeachDrawing.PDF.PdfiumRunner;

internal enum Outcome { Pass, Fail, Blank, Skipped }

internal sealed record TestResult(Outcome Outcome, string? Reason = null);

/// <summary>How close a page must be to PDFium's to count: a pixel differs when a channel is off by more than <see cref="Threshold"/>, and a page passes when no more than <see cref="MaxDifferingFraction"/> of its pixels differ.</summary>
internal sealed record Tolerance(int Threshold = 48, double MaxDifferingFraction = 0.02);

/// <summary>Renders each page of a test's PDF as PDFium's test tool does (one pixel per point, white background, annotations and form fields drawn) and compares it with the expectation.</summary>
internal sealed class Evaluator(Tolerance tolerance, string? dumpDirectory = null)
{
    /// <summary>PDFium's ink below which a page is not judged by it: a pixel or two (a dot at a page corner) is noise, not content.</summary>
    private const int MinimumInk = 16;

    public TestResult Evaluate(CorpusTest test)
    {
        byte[] pdf;
        try
        {
            pdf = Corpus.LoadPdf(test);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyNotFoundException or FormatException)
        {
            return new TestResult(Outcome.Skipped, "template: " + e.Message);
        }

        PdfPageSource source;
        try
        {
            source = PdfPageSource.Open(pdf);
        }
        catch (PdfPasswordException)
        {
            return new TestResult(Outcome.Skipped, "needs a password");
        }
        catch (Exception e)
        {
            return new TestResult(Outcome.Fail, "error: open: " + Short(e));
        }

        var blank = false;
        var blankWhy = "";
        for (var page = 0; page < test.ExpectedPages.Count; page++)
        {
            var expected = RgbaImage.FromPng(File.ReadAllBytes(test.ExpectedPages[page]));
            if (expected is null)
                return new TestResult(Outcome.Skipped, $"page {page}: expectation is not a readable PNG");
            if (page >= source.PageCount)
                return new TestResult(Outcome.Fail, $"page {page}: the document has {source.PageCount} pages");

            RgbaImage actual;
            IReadOnlyCollection<string> unsupported;
            try
            {
                (actual, unsupported) = Render(source, page);
            }
            catch (Exception e)
            {
                return new TestResult(Outcome.Fail, $"error: page {page}: {Short(e)}");
            }

            Dump(test, page, expected, actual);
            if (actual.Compare(expected, tolerance.Threshold) is not { } d)
                return new TestResult(Outcome.Fail, $"page {page}: size {actual.Width}x{actual.Height}, expected {expected.Width}x{expected.Height}");

            var fraction = d.Differing / (double)Math.Max(1, actual.Width * actual.Height);
            if (fraction > tolerance.MaxDifferingFraction)
                return new TestResult(Outcome.Fail, $"page {page}: {fraction:P2} of pixels differ (mean {d.MeanDifference:0.0}){Unsupported(unsupported)}");
            // A page can sit inside the pixel tolerance and still have lost what PDFium drew (a check mark on a small page), so a page with
            // under a quarter of PDFium's ink is blank, not a pass.
            int expectedInk = expected.InkPixels(tolerance.Threshold);
            if (expectedInk >= MinimumInk && actual.InkPixels(tolerance.Threshold) * 4 < expectedInk)
            {
                blank = true;
                blankWhy = Unsupported(unsupported);
            }
        }

        return blank ? new TestResult(Outcome.Blank, "nothing was painted" + blankWhy) : new TestResult(Outcome.Pass);
    }

    /// <summary>One pixel per point, as <c>pdfium_test</c> at its default scale: the size is the page's size in points, truncated.</summary>
    internal static (RgbaImage Image, IReadOnlyCollection<string> Unsupported) Render(PdfPageSource source, int page)
    {
        var (w, h) = source.GetPageSize(page);
        int width = Math.Max(1, (int)w), height = Math.Max(1, (int)h);
        using var canvas = new RasterRenderContext().CreateCanvas(width, height);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, width, height);
        var unsupported = source.Paint(page, canvas, PdfAnnotationPaint.All);

        // pdfium_test runs with a form-fill environment whose highlight color (0xFFE4DD, alpha 100, BGR) tints every field but push buttons, signatures
        // and read-only fields; the expected images carry it, so the same tint is drawn here rather than counted as a difference.
        var highlight = canvas.GetSolidBrush(PaintColor.FromArgb(100, 221, 228, 255));
        foreach (var field in source.GetFormFields(page, 1))
        {
            if (field.Kind is PdfFormFieldKind.PushButton or PdfFormFieldKind.Signature || field.ReadOnly)
                continue;
            canvas.DrawRectangle(highlight, field.Bounds.X, field.Bounds.Y, field.Bounds.Width, field.Bounds.Height);
        }

        var buffer = canvas.ToPixelBuffer();
        return (new RgbaImage(buffer.Width, buffer.Height, buffer.PremultipliedRgba.ToArray()), unsupported);
    }

    private void Dump(CorpusTest test, int page, RgbaImage expected, RgbaImage actual)
    {
        if (string.IsNullOrEmpty(dumpDirectory))
            return;
        var stem = Path.Combine(dumpDirectory, test.Id.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(stem)!);
        File.WriteAllBytes($"{stem}.{page}.peach.png", actual.ToPng());
        File.WriteAllBytes($"{stem}.{page}.pdfium.png", expected.ToPng());
    }

    /// <summary>The features the importer reported it could not draw, so failures can be grouped by cause.</summary>
    private static string Unsupported(IReadOnlyCollection<string> unsupported) =>
        unsupported.Count == 0 ? "" : "; unsupported: " + string.Join(", ", unsupported.Order(StringComparer.Ordinal));

    private static string Short(Exception e)
    {
        var text = $"{e.GetType().Name}: {e.Message}".ReplaceLineEndings(" ");
        return text.Length > 140 ? text[..140] : text;
    }
}
