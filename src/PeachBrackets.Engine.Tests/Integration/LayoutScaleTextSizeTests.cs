using PeachBrackets;
using PeachDrawing.PDF;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// Lengths a document reports are in points and a layout unit is <c>1 / PixelsPerPoint</c> of a point, so changing the layout scale
/// must not change the measured size of text (it used to be built 1/PixelsPerPoint too small).
/// </summary>
public class LayoutScaleTextSizeTests
{
    private const double Scaled = 96d / 72d;

    private static async Task<(double TextWidth, double BoxWidth)> MeasureAsync(string body, double pixelsPerPoint)
    {
        var context = new PdfRenderContext { PixelsPerPoint = pixelsPerPoint };
        var options = new HtmlDocumentOptions { PageWidthPoints = 480, PageHeightPoints = 100000, Media = "screen", RecordTextRuns = true };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync($"<!doctype html><body style=\"margin:0\">{body}</body>", context, options);
        await document.LayoutAsync(PdfCanvas.CreateMeasuring(context, new PeachDrawing.Core.Size(1, 1)));
        var textWidth = document.TextRuns[0].Bounds.Width;
        return (textWidth, document.Pages[0].WidthPoints);
    }

    [Theory]
    [InlineData("font-size:15px")]
    [InlineData("font-size:11.25pt")]
    [InlineData("font-size:0.9375rem")]
    [InlineData("font-size:medium")]
    [InlineData("font-size:15px;font-family:Arial")]
    [InlineData("font-size:2em")]
    [InlineData("font-size:15px;font-variant:small-caps")]
    public async Task TextWidthInPoints_DoesNotDependOnPixelsPerPoint(string style)
    {
        var body = $"<p style=\"margin:0;{style}\">Stream pixel queue service.</p>";
        var (unit, _) = await MeasureAsync(body, 1);
        var (scaled, _) = await MeasureAsync(body, Scaled);

        Assert.Equal(unit, scaled, 2);
    }

    [Fact]
    public async Task RelativeFontSizes_DoNotDependOnPixelsPerPoint()
    {
        const string body = "<div style=\"font-size:20px\"><p style=\"margin:0;font-size:0.5em\">Stream pixel queue service.</p></div>";
        var (unit, _) = await MeasureAsync(body, 1);
        var (scaled, _) = await MeasureAsync(body, Scaled);

        Assert.Equal(unit, scaled, 2);
    }

    [Fact]
    public async Task EmLengths_AndLineHeight_DoNotDependOnPixelsPerPoint()
    {
        const string body = "<p style=\"margin:0;font-size:15px;letter-spacing:0.2em\">Stream pixel queue service.</p>";
        var (unit, _) = await MeasureAsync(body, 1);
        var (scaled, _) = await MeasureAsync(body, Scaled);

        Assert.Equal(unit, scaled, 2);
    }
}

/// <summary>The same invariance through PDF generation, for the content that builds its own fonts (inline SVG, MathML).</summary>
public class LayoutScalePdfTextSizeTests
{
    private static async Task<string> PdfAtAsync(string body, double pixelsPerInch)
    {
        var generator = new PeachPDF.PdfGenerator();
        var config = new PeachPDF.PdfGenerateConfig { PageSize = PeachPDF.PageSize.A4, CompressContentStreams = false, PixelsPerInch = pixelsPerInch };
        var document = await generator.GeneratePdf($"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>", config);
        using var stream = new MemoryStream();
        document.Save(stream);
        return System.Text.Encoding.Latin1.GetString(stream.ToArray());
    }

    /// <summary>
    /// Each text object's first font size and position, in the units of the page: the scale of every <c>cm</c> since the start of its stream
    /// is applied, because content may be drawn in a nested user space (an SVG's) and reach the page through it.
    /// </summary>
    private static List<(double Size, double X, double Y)> EffectiveText(string pdf)
    {
        var result = new List<(double, double, double)>();
        var number = System.Globalization.CultureInfo.InvariantCulture;
        foreach (System.Text.RegularExpressions.Match text in System.Text.RegularExpressions.Regex.Matches(
                     pdf, @"BT\r?\n(?:[^\n]*\n)*?/\S+ ([\d.]+) Tf\r?\n([\d.\-]+) ([\d.\-]+) Td"))
        {
            var streamStart = pdf.LastIndexOf("stream", text.Index, StringComparison.Ordinal);
            var scale = 1.0;
            foreach (System.Text.RegularExpressions.Match cm in System.Text.RegularExpressions.Regex.Matches(
                         pdf[streamStart..text.Index], @"([\d.]+) 0 0 [\d.]+ 0 0 cm"))
                scale *= double.Parse(cm.Groups[1].Value, number);

            result.Add((Math.Round(double.Parse(text.Groups[1].Value, number) * scale, 2),
                        Math.Round(double.Parse(text.Groups[2].Value, number) * scale, 2),
                        Math.Round(double.Parse(text.Groups[3].Value, number) * scale, 2)));
        }

        return result;
    }

    [Theory]
    [InlineData("<svg width='300' height='60'><text x='10' y='40' font-size='30'>Stream pixel</text></svg>")]
    [InlineData("<svg width='300' height='60'><text x='10' y='40' font-size='30' text-decoration='underline'>Stream pixel</text></svg>")]
    [InlineData("<p style='margin:0;font-size:20px'>Stream <math><mi>x</mi><mo>+</mo><mn>12</mn></math></p>")]
    public async Task FontSizesInPdfPoints_DoNotDependOnPixelsPerInch(string body)
    {
        var at72 = EffectiveText(await PdfAtAsync(body, 72));
        var at96 = EffectiveText(await PdfAtAsync(body, 96));

        Assert.NotEmpty(at72);
        Assert.Equal(at72.Count, at96.Count);

        // Ascent and line height round to a whole layout unit, so a baseline may differ by under a point between scales.
        for (var i = 0; i < at72.Count; i++)
        {
            Assert.Equal(at72[i].Size, at96[i].Size, 2);
            Assert.Equal(at72[i].X, at96[i].X, 0.75);
            Assert.Equal(at72[i].Y, at96[i].Y, 0.75);
        }
    }
}
