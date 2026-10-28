using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;
using System.Text;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A paragraph moved by <c>orphans</c>/<c>widows</c> is laid out again, and re-opens the fragmentainers already emitted
/// from where its geometry starts. Its words past the cut still awaited the next fragmentainer, with no position yet,
/// and those unplaced words used to count as geometry at the top of the document - so every such paragraph re-opened
/// every page from the first, and each re-opened page was emitted again with a walk of the whole document. The cost grew
/// with the cube of the page count: 131 pages took 17s, 1,300 pages did not finish in 50 minutes.
/// </summary>
public class StaleSlotReopeningCostTests
{
    // Same shape as a long report: paragraphs of 40-110 words with inline emphasis, default orphans/widows.
    private static string Document(int paragraphs)
    {
        var words = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua"
            .Split(' ');
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>p { text-align: justify; margin: 0 0 7pt 0 }</style></head><body>");
        var seed = 12345u;
        for (var i = 0; i < paragraphs; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            var count = 40 + (int)(seed >> 16) % 71;
            sb.Append("<p>");
            for (var w = 0; w < count; w++)
            {
                seed = seed * 1664525u + 1013904223u;
                var word = words[(int)(seed >> 16) % words.Length];
                sb.Append(w == count / 2 ? $"<strong>{word}</strong> " : $"{word} ");
            }

            sb.Append("</p>");
        }

        return sb.Append("</body></html>").ToString();
    }

    private static async Task<(CssBox Root, HtmlContainerInt Container)> Layout(string html)
    {
        var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(renderContext);
        await container.SetHtml(html, null);

        var size = new Size(595, 842);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);

        Assert.NotNull(container.Root);
        return (container.Root!, container);
    }

    [Fact]
    public async Task EmissionWork_GrowsLinearlyWithPages_UnderDefaultWidowsAndOrphans()
    {
        // The number of boxes the emitter visits is deterministic, so this asserts the complexity directly instead of
        // timing a render. Four times the content is ~4x the work when linear; the defect made it ~80x. Visiting a
        // finished paragraph to find its mark is still O(paragraphs) per page, so the real figure is a little above
        // linear (8.5x measured here, 6.7x before the per-paragraph reopening scope; 600 paragraphs went from 50,423
        // visits to 38,101) - hence 12x rather than 8x, which still sits far below the cubic 64x+ the defect gave,
        // and an absolute ceiling at the old figure so the work can only go down.
        var (_, small) = await Layout(Document(150));
        var (_, large) = await Layout(Document(600));

        Assert.True(small.BuildDraftCalls > 0);
        Assert.True(large.BuildDraftCalls < small.BuildDraftCalls * 12,
            $"150 paragraphs: {small.BuildDraftCalls} box visits, 600 paragraphs: {large.BuildDraftCalls}");
        Assert.True(large.BuildDraftCalls < 50_000, $"600 paragraphs took {large.BuildDraftCalls} box visits");
    }

    // A flat list of paragraphs, page height varied: only the break positions differ.
    private static string Paragraphs(int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            sb.Append("<p>");
            for (var w = 0; w < 60; w++)
                sb.Append("lorem").Append((w * 7 + i) % 97).Append(w % 9 == 0 ? ", " : " ");

            sb.Append("</p>");
        }

        return sb.ToString();
    }

    private static async Task<(long Allocated, HtmlContainerInt Container)> LayoutAtHeight(string body, double height)
    {
        var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(renderContext) { MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };
        var sheet = new Size(595, height);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(sheet, 1.0);
        await container.SetHtml($"<html><body style='margin:0'>{body}</body></html>", null);

        var band = new Size(555, height - 40);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(band, 1.0);
        container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(band, 1.0);
        container.Location = PeachBrackets.Utilities.Utils.ToPixels(new PaintPoint(20, 20), 1.0);

        using var graphics = PdfCanvas.CreateMeasuring(renderContext, sheet);
        var before = GC.GetAllocatedBytesForCurrentThread();
        await container.PerformLayout(graphics);
        return (GC.GetAllocatedBytesForCurrentThread() - before, container);
    }

    [Fact]
    public async Task ReopeningEveryPage_DoesNotMakeEachEmissionWalkTheWholeDocument()
    {
        // The same 1000 paragraphs at two page heights: where the default orphans/widows move a paragraph after its first lines were emitted at
        // nearly every page break (109 reopenings at 842pt with Windows' metrics), and where they almost never do (2 at 820pt). When every reopening
        // retired the pruning marks of the whole document (one scope for everything under <html>), the first layout allocated 8x what the second did
        // (172 MB against 21 MB) and visited the whole tree once per page.
        //
        // Which heights those are depends on the font the platform falls back to, so they are found, on a shorter document, instead of assumed.
        var probe = Paragraphs(200);
        await LayoutAtHeight(Paragraphs(20), 842); // warm-up: JIT and font loading are not what is measured
        double busyHeight = 0, quietHeight = 0;
        var most = -1L;
        var fewest = long.MaxValue;
        for (var height = 790; height <= 880; height += 3)
        {
            var (_, container) = await LayoutAtHeight(probe, height);
            if (container.CatchUpSlotEmits > most) { most = container.CatchUpSlotEmits; busyHeight = height; }
            if (container.CatchUpSlotEmits < fewest) { fewest = container.CatchUpSlotEmits; quietHeight = height; }
        }

        var body = Paragraphs(1000);
        var (quiet, _) = await LayoutAtHeight(body, quietHeight);
        var (busy, busyContainer) = await LayoutAtHeight(body, busyHeight);

        Assert.True(busyContainer.CatchUpSlotEmits > 50, $"no page height reopens its pages ({busyContainer.CatchUpSlotEmits} at {busyHeight}pt); the test measures nothing");
        Assert.True(busy < quiet * 3, $"{busyHeight}pt allocated {busy / 1048576} MB, {quietHeight}pt {quiet / 1048576} MB");
    }

    private static CssBox? FirstBoxWithPlacedWords(CssBox box)
    {
        if (box.Rectangles.Count > 0 && box.Words.Count > 1) return box;
        foreach (var child in box.Boxes)
        {
            if (FirstBoxWithPlacedWords(child) is { } found) return found;
        }

        return null;
    }

    [Fact]
    public async Task OwnGeometryTop_IgnoresWordsAwaitingTheNextFragmentainer()
    {
        var (root, _) = await Layout("<!DOCTYPE html><html><body><p style='margin:0;padding:0'>one two three four five</p></body></html>");
        var box = FirstBoxWithPlacedWords(root)!;
        Assert.NotNull(box);

        var placedTop = box.OwnGeometryTop();

        // A word cut off by a break keeps no coordinate; its stale Top must not pull the box's top away.
        var last = box.Words[^1];
        last.AwaitsTheNextFragmentainer = true;
        last.Rectangle = new Rect(0, 0, last.Width, last.Height);

        Assert.Equal(placedTop, box.OwnGeometryTop());
    }

    [Fact]
    public async Task OwnGeometryTop_StillUsesWordPositions_ForABoxWithNoRectangles()
    {
        var (root, _) = await Layout("<!DOCTYPE html><html><body><p style='margin:0;padding:0'>one two three</p></body></html>");
        var box = FirstBoxWithPlacedWords(root)!;
        var wordTop = box.Words.Min(w => w.Top);

        box.Rectangles.Clear();

        Assert.Equal(wordTop, box.OwnGeometryTop());
    }

    [Fact]
    public async Task OwnGeometryTop_WithNothingPlaced_FallsBackToTheWordsTops()
    {
        var (root, _) = await Layout("<!DOCTYPE html><html><body><p style='margin:0;padding:0'>one two three</p></body></html>");
        var box = FirstBoxWithPlacedWords(root)!;
        var wordTop = box.Words.Min(w => w.Top);

        box.Rectangles.Clear();
        foreach (var word in box.Words) word.AwaitsTheNextFragmentainer = true;

        Assert.Equal(wordTop, box.OwnGeometryTop());
    }
}
