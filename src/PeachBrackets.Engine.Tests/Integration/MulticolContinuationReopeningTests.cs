using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;
using System.Text;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A paragraph that continues into the next column or page is moved with a <c>Location</c> write to where its remainder
/// starts. That is not a change to what it already placed in the earlier fragmentainer, but the write was treated like any
/// other relocation and reopened the earlier page, which the emitter then had to emit again with a walk of the whole
/// document - once per page, so a long multi-column document paid for the whole document once per page.
/// </summary>
public class MulticolContinuationReopeningTests
{
    private static string Document(int paragraphs)
    {
        var words = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua"
            .Split(' ');
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>.cols { column-count: 2; column-gap: 24pt } p { margin: 0 0 7pt 0 }</style></head><body><div class=cols>");
        var seed = 777u;
        for (var i = 0; i < paragraphs; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            var count = 40 + (int)(seed >> 16) % 71;
            sb.Append("<p>");
            for (var w = 0; w < count; w++)
            {
                seed = seed * 1664525u + 1013904223u;
                sb.Append(words[(int)(seed >> 16) % words.Length]).Append(' ');
            }

            sb.Append("</p>");
        }

        return sb.Append("</div></body></html>").ToString();
    }

    private static async Task<HtmlContainerInt> Layout(string html)
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
        return container;
    }

    [Fact]
    public async Task ContinuingIntoTheNextColumn_DoesNotReEmitTheEarlierPages()
    {
        // 300 paragraphs is ~30 pages. Before the change nearly every page was re-emitted once (28 of them);
        // what is left is the occasional genuine relocation.
        var container = await Layout(Document(300));

        Assert.True(container.FragmentainerPasses >= 20, $"expected a long document, got {container.FragmentainerPasses} passes");
        Assert.True(container.CatchUpSlotEmits < 8, $"{container.CatchUpSlotEmits} stale slots were re-emitted over {container.FragmentainerPasses} passes");
    }

    [Fact]
    public async Task ReEmissionWork_GrowsLinearlyWithPages()
    {
        var small = await Layout(Document(150));
        var large = await Layout(Document(600));

        // Each re-emitted slot is a walk of the document, so the count has to stay flat for the cost to stay linear.
        Assert.True(large.CatchUpSlotEmits <= small.CatchUpSlotEmits + 8,
            $"150 paragraphs: {small.CatchUpSlotEmits} slots re-emitted, 600 paragraphs: {large.CatchUpSlotEmits}");
    }
}
