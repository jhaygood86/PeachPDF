using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// Painting a part of a page (<c>PaintPageRegionAsync</c>, what a scrolling host asks for) has to give the same pixels as the page has there, and cost
/// what the part costs. A canvas's tracked clip bound is the last rectangle pushed, not the intersection of those pushed, so the painter's own page-wide
/// clip used to replace the region the caller pushed (the engine tests count the words drawn; these check that what is drawn is right).
/// </summary>
public class RegionPaintTests
{
    private const int Paragraphs = 400;

    private static string Body()
    {
        var body = new System.Text.StringBuilder();
        for (var p = 0; p < Paragraphs; p++)
            body.Append($"<p id='p{p}' style='margin:0;padding:4pt;border-bottom:1pt solid #888;box-shadow:0 6pt 8pt rgba(0,0,0,.35)'>Paragraph {p}: the quick brown fox jumps over the lazy dog, again and again.</p>");

        return body.ToString();
    }

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync() =>
        await TestDocument.LoadAsync(TestDocument.Wrap(Body()), new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 600 });

    private static byte[] Rows(RasterCanvas canvas, int fromY, int toY)
    {
        var buffer = canvas.ToPixelBuffer();
        return buffer.PremultipliedRgba.Span.Slice(fromY * buffer.Width * 4, (toY - fromY) * buffer.Width * 4).ToArray();
    }

    [Fact]
    public async Task AStrip_HasThePixelsThePageHasThere_IncludingWhatOverhangsIntoIt()
    {
        var (context, document) = await LoadAsync();
        using var full = await TestDocument.PaintAsync(context, document);

        // A strip that starts in the middle of a paragraph, below a box whose shadow reaches into it.
        var bounds = document.GetElementBounds("p200")!.Value;
        var top = (int)(bounds.Y + 3);
        var height = 120;
        var page = document.Pages[0];
        using var strip = context.CreateCanvas((int)Math.Ceiling(page.WidthPoints), (int)Math.Ceiling(page.HeightPoints));
        strip.DrawRectangle(strip.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, page.WidthPoints, page.HeightPoints);
        await document.PaintPageRegionAsync(0, strip, new Rect(0, top, page.WidthPoints, height));

        Assert.True(Rows(full, top, top + height).AsSpan().SequenceEqual(Rows(strip, top, top + height)), "the strip differs from the page");
    }

    [Fact]
    public async Task APageStillPaintsWhole_AfterAStripWasPainted()
    {
        var (context, document) = await LoadAsync();
        using var before = await TestDocument.PaintAsync(context, document);
        var page = document.Pages[0];
        using var strip = context.CreateCanvas((int)Math.Ceiling(page.WidthPoints), (int)Math.Ceiling(page.HeightPoints));
        await document.PaintPageRegionAsync(0, strip, new Rect(0, 100, page.WidthPoints, 80));

        using var after = await TestDocument.PaintAsync(context, document);

        Assert.True(before.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(after.ToPixelBuffer().PremultipliedRgba.Span));
    }
}
