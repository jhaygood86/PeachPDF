using PeachDrawing.Core;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// Painting a part of a page (<c>HtmlDocument.PaintPageRegionAsync</c>, what a scrolling host asks for) has to draw what is in the part and skip the rest.
/// A canvas's tracked clip bound is the last rectangle pushed, not the intersection of those pushed, so the painter's own page-wide clip used to replace the
/// region the caller pushed and every word of a long page was drawn (and then discarded by the clip) for each strip a view revealed.
/// </summary>
public class RegionPaintCullingTests
{
    private const int Paragraphs = 300;
    private const int WordsPerParagraph = 6;

    private static string Page()
    {
        var body = new System.Text.StringBuilder();
        for (var p = 0; p < Paragraphs; p++)
        {
            body.Append($"<p id='p{p}' style='margin:0;padding:4pt;border-bottom:1pt solid #888'>");
            for (var w = 0; w < WordsPerParagraph; w++)
                body.Append($"w{p}_{w} ");
            body.Append("</p>");
        }

        return $"<!DOCTYPE html><html><head><style>html, body {{ margin: 0 }} p {{ font: 12pt sans-serif }}</style></head><body>{body}</body></html>";
    }

    private static async Task<(HtmlDocument Document, RecordingGraphics Canvas)> LoadAsync()
    {
        var renderContext = new HtmlRenderContext();
        await BundledFonts.PinSansSerifAsync(renderContext);

        // One page tall enough for all of it, so a strip is a part of a single page.
        var options = new HtmlDocumentOptions { PageWidthPoints = 600, PageHeightPoints = 20000 };
        var document = await HtmlDocument.LoadAsync(Page(), renderContext, options);
        var area = document.ContentArea;
        using var measuring = PeachDrawing.PDF.PdfCanvas.CreateMeasuring(renderContext, area.Width, area.Height);
        await document.LayoutAsync(measuring);
        return (document, new RecordingGraphics(renderContext));
    }

    // Every word the canvas was asked to draw (a draw call may carry one word or several).
    private static List<string> Words(RecordingGraphics canvas) =>
        [.. canvas.Log.Where(op => op.Kind == PaintOpKind.DrawString).SelectMany(op => op.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries))];

    [Fact]
    public async Task AStripOfALongPage_DrawsItsOwnWords_AndNotTheRestOfThePage()
    {
        var (document, canvas) = await LoadAsync();
        Assert.Single(document.Pages);
        var first = document.GetElementBounds("p100")!.Value;
        var last = document.GetElementBounds("p104")!.Value;
        var region = new Rect(0, first.Y, 600, last.Bottom - first.Y);

        using (var whole = new RecordingGraphics(new HtmlRenderContext()))
        {
            await document.PaintPageAsync(0, whole);
            Assert.Equal(Paragraphs * WordsPerParagraph, Words(whole).Count(word => word.StartsWith('w')));
        }

        await document.PaintPageRegionAsync(0, canvas, region);

        var drawn = Words(canvas);
        for (var p = 100; p <= 104; p++)
        {
            for (var w = 0; w < WordsPerParagraph; w++)
                Assert.Contains($"w{p}_{w}", drawn);
        }

        // The strip and a little over: a handful of paragraphs, nowhere near the 300 of the page.
        Assert.InRange(drawn.Count, WordsPerParagraph * 5, WordsPerParagraph * 25);
        Assert.DoesNotContain("w0_0", drawn);
        Assert.DoesNotContain($"w{Paragraphs - 1}_0", drawn);
    }
}
