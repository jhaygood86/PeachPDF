using PeachBrackets.Html.Dom;
using System.Linq;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// <c>text-box-trim</c> and <c>text-box-edge</c> (CSS Inline 3 section 5) cut the leading above the first and below the last formatted line of
/// a block container off, down to the edge metric of the font.
/// </summary>
public class TextBoxTrimTests
{
    private static async Task<CssBox> LayoutAsync(string style, string content = "Hxg", string extra = "")
    {
        var html = LayoutHarness.Wrap(
            $"<div id='a' style=\"margin:0;font-family:'Liberation Sans';font-size:20px;line-height:40px;{style}\">{content}</div>{extra}");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 800);
        return LayoutHarness.FindById(root, "a")!;
    }

    private static double Height(CssBox box) => box.ActualBottom - box.Location.Y;

    [Fact]
    public async Task NoTrimByDefault()
    {
        Assert.Equal(40 * 0.75, Height(await LayoutAsync("")), 1);
    }

    [Fact]
    public async Task TrimBoth_TextEdge_RemovesTheHalfLeadingOnBothSides()
    {
        var plain = await LayoutAsync("");
        var trimmed = await LayoutAsync("text-box-trim:trim-both;text-box-edge:text");
        var font = trimmed.ActualFont;

        Assert.Equal(font.Height, Height(trimmed), 1);
        Assert.True(Height(trimmed) < Height(plain));
    }

    [Fact]
    public async Task TrimStart_MovesTheContentUpAndTrimEndOnlyShortensTheBox()
    {
        var plain = await LayoutAsync("");
        var start = await LayoutAsync("text-box-trim:trim-start;text-box-edge:text");
        var end = await LayoutAsync("text-box-trim:trim-end;text-box-edge:text");
        var halfLeading = (Height(plain) - plain.ActualFont.Height) / 2;

        Assert.Equal(Height(plain) - halfLeading, Height(start), 1);
        Assert.Equal(Height(plain) - halfLeading, Height(end), 1);
        Assert.Equal(plain.LineBoxes[0].BaselineY!.Value - halfLeading, start.LineBoxes[0].BaselineY!.Value, 1);
        Assert.Equal(plain.LineBoxes[0].BaselineY!.Value, end.LineBoxes[0].BaselineY!.Value, 1);
    }

    [Fact]
    public async Task CapAndAlphabeticTrimToTheCapHeightAndTheBaseline()
    {
        var box = await LayoutAsync("text-box-trim:trim-both;text-box-edge:cap alphabetic");
        var font = box.ActualFont;
        var capHeight = PeachBrackets.Html.Utils.FontMetricMeasurement.Ratio(font, PeachBrackets.Css.FontMetric.Cap) * font.Size;

        Assert.Equal(capHeight, Height(box), 1);
    }

    [Fact]
    public async Task ABoxWithAnExplicitHeightKeepsIt()
    {
        var box = await LayoutAsync("text-box-trim:trim-both;text-box-edge:text;height:100px");

        Assert.Equal(75, Height(box), 1);
    }

    [Fact]
    public async Task TheShorthandAndNormal()
    {
        var both = await LayoutAsync("text-box:trim-both cap alphabetic");
        var normal = await LayoutAsync("text-box:trim-both cap alphabetic;text-box:normal");

        Assert.Equal("trim-both", both.ComputedStyle.TextDecoration.TextBoxTrim);
        Assert.Equal("cap alphabetic", both.ComputedStyle.Text.TextBoxEdge);
        Assert.Equal("none", normal.ComputedStyle.TextDecoration.TextBoxTrim);
        Assert.Equal("auto", normal.ComputedStyle.Text.TextBoxEdge);
    }

    [Fact]
    public async Task TheFirstAndLastLineOfNestedBlocksAreTrimmed()
    {
        var plain = await LayoutAsync("", "<p style='margin:0'>one</p><p style='margin:0'>two</p>");
        var trimmed = await LayoutAsync("text-box-trim:trim-both;text-box-edge:text", "<p style='margin:0'>one</p><p style='margin:0'>two</p>");
        var halfLeading = (40 * 0.75 - plain.ActualFont.Height) / 2;

        Assert.Equal("none", trimmed.Boxes[0].ComputedStyle.TextDecoration.TextBoxTrim);
        Assert.Equal(Height(plain) - 2 * halfLeading, Height(trimmed), 1);
    }
}
