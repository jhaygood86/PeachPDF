using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;
using System.Text;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// Filling a column asks how far down the content reaches. It used to ask every child of the container, including every
/// one already placed on earlier pages, for every column - so a long multi-column document measured its finished pages
/// again once per column: quadratic in pages.
/// </summary>
public class ColumnBottomMeasureTests
{
    private static string Document(int paragraphs)
    {
        var words = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore"
            .Split(' ');
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>body { font-size: 14pt } .cols { column-count: 2; column-gap: 24pt } p { margin: 0 0 7pt 0 }</style></head><body><div class=cols>");
        var seed = 99u;
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
        container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);

        Assert.NotNull(container.Root);
        return container;
    }

    [Fact]
    public async Task TheChildrenMeasured_GrowLinearlyWithPages()
    {
        var small = await Layout(Document(150));
        var large = await Layout(Document(600));

        Assert.True(small.ColumnBottomChildrenMeasured > 0);

        // Four times the content is ~4x the measuring when each column measures the pages near it, ~16x when it measures all.
        Assert.True(large.ColumnBottomChildrenMeasured < small.ColumnBottomChildrenMeasured * 7,
            $"150 paragraphs: {small.ColumnBottomChildrenMeasured} children measured, 600 paragraphs: {large.ColumnBottomChildrenMeasured}");
    }
}
