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
        // timing a render. Four times the content is ~4x the work when linear; the defect made it ~80x.
        var (_, small) = await Layout(Document(150));
        var (_, large) = await Layout(Document(600));

        Assert.True(small.BuildDraftCalls > 0);
        Assert.True(large.BuildDraftCalls < small.BuildDraftCalls * 8,
            $"150 paragraphs: {small.BuildDraftCalls} box visits, 600 paragraphs: {large.BuildDraftCalls}");
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
