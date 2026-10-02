using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A table whose cells hold a <c>break-inside: avoid</c> block that does not fit the foot of its page is moved whole to the
/// next page, together with the heading kept with it. The cells had already been laid out once, with their content moved
/// past the page edge inside them, and the second run kept that height: the table came out as tall as the gap it had
/// left behind, and the table after it started that far below it.
/// </summary>
public class TableRelocationHeightTests
{
    private const string Style =
        "body { margin:0; font:10pt/12pt Arial } h2 { font-size:10pt; margin:.9em 0 .3em; break-after:avoid } " +
        "table { border-collapse:collapse; width:100% } td { padding:3px; vertical-align:top; width:50% }";

    private static string Cell(string text) =>
        $"<td><div style='page-break-inside:avoid'><div style='height:60pt'>{text}</div><div>caption</div></div></td>";

    private static async Task<(CssBox First, CssBox Second)> LayOutAsync(double fillerPt)
    {
        var html = $"<!DOCTYPE html><html><head><style>{Style}</style></head><body><div style='height:{fillerPt}pt'></div>" +
                   $"<h2>Heading</h2><table id='t1'><tr>{Cell("a")}{Cell("b")}</tr></table>" +
                   $"<table id='t2'><tr>{Cell("c")}{Cell("d")}</tr></table></body></html>";

        var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(adapter) { MarginTop = 0, MarginLeft = 0, MarginRight = 0, MarginBottom = 0 };
        await container.SetHtml(html, null);

        var size = new XSize(400, 300);
        container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

        var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
        using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
        await container.PerformLayout(graphics);

        return (FindById(container.Root!, "t1")!, FindById(container.Root!, "t2")!);
    }

    private static CssBox? FindById(CssBox box, string id)
    {
        if (box.HtmlTag?.TryGetAttribute("id", "") == id) return box;
        foreach (var child in box.Boxes)
        {
            if (FindById(child, id) is { } found) return found;
        }
        return null;
    }

    // The page is 300pt tall. At 204pt of filler the first table starts at 228pt and its row (72pt of avoid block plus the
    // cell padding) reaches past the foot; at 240pt the same happens with less room left on the page.
    [Theory]
    [InlineData(204)]
    [InlineData(222)]
    [InlineData(240)]
    public async Task TableMovedToTheNextPage_IsAsTallAsItsContent(double fillerPt)
    {
        var (first, second) = await LayOutAsync(fillerPt);

        Assert.True(first.Location.Y >= 300, $"the table starts on the next page, at {first.Location.Y}");
        Assert.InRange(first.ActualBottom - first.Location.Y, 76, 77);
        Assert.Equal(first.ActualBottom, second.Location.Y, 1);
    }

    [Fact]
    public async Task TableThatFitsItsPage_IsUnaffected()
    {
        var (first, second) = await LayOutAsync(100);

        Assert.InRange(first.ActualBottom - first.Location.Y, 76, 77);
        Assert.Equal(first.ActualBottom, second.Location.Y, 1);
    }
}
