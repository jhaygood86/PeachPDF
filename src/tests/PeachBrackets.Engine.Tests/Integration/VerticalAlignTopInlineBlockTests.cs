using System.Linq;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// <c>vertical-align: top</c> aligns the top of the box with the top of the line box (CSS 2.1 section 10.8.1), whatever the strut's font is.
/// An inline-block holding text used to sit a few tenths of a point below the line top.
/// </summary>
public class VerticalAlignTopInlineBlockTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("")]
    [InlineData("<p style='margin:0'>text</p>")]
    public async Task ATopAlignedInlineBlock_SitsAtTheLineTop(string content)
    {
        var html = "<html><body style=\"margin:0;font-family:'Liberation Sans';font-size:12pt\"><div id='line' style='width:375pt'>" +
                   $"<div id='box' style='display:inline-block;vertical-align:top;width:225pt;height:60pt'>{content}</div></div></body></html>";
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var box = LayoutHarness.FindById(root, "box")!;

        var top = box.Rectangles.Count > 0 ? box.Rectangles.Values.First().Top : box.Location.Y;
        Assert.Equal(LayoutHarness.FindById(root, "line")!.Location.Y, top, 3);
    }

    [Fact]
    public async Task ATopAlignedInlineBlock_WithTextInADifferentFont_StillSitsAtTheLineTop()
    {
        var html = "<html><body style=\"margin:0;font-family:'Liberation Sans';font-size:12pt\"><div id='line' style='width:375pt'>" +
                   "<div id='box' style='display:inline-block;vertical-align:top;width:225pt;height:60pt;font-size:30pt;line-height:2'>text</div></div></body></html>";
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var box = LayoutHarness.FindById(root, "box")!;
        var top = box.Rectangles.Count > 0 ? box.Rectangles.Values.First().Top : box.Location.Y;
        Assert.Equal(LayoutHarness.FindById(root, "line")!.Location.Y, top, 3);
    }
}
