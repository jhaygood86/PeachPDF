using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// <c>line-height</c> computation and the line-box properties built on it (<c>line-height-step</c>, <c>block-step</c>,
/// <c>text-box-trim</c>/<c>text-box-edge</c>), seen only through the public API: the height a laid-out page reports.
/// </summary>
public class LineBoxPropertiesTests
{
    private static async Task<double> HeightAsync(string body, string head = "")
    {
        var options = new HtmlDocumentOptions();
        options.SetMargins(0);
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>html,body{margin:0;padding:0}</style>" + head), options);
        return document.GetContentHeight(0);
    }

    private const string Font = "font-family:'Liberation Sans',Arial,sans-serif;margin:0;";

    [Fact]
    public async Task AnEmLineHeight_IsInheritedAsALength()
    {
        // 14px parent, 1.5em = 21px; the h2 has a 40px font and still gets the 21px.
        var height = await HeightAsync($"<div style=\"{Font}font-size:14px;line-height:1.5em\"><h2 style='margin:0;font-size:40px'>x</h2></div>");
        var explicitLength = await HeightAsync($"<div style=\"{Font}font-size:14px;line-height:21px\"><h2 style='margin:0;font-size:40px'>x</h2></div>");
        var number = await HeightAsync($"<div style=\"{Font}font-size:14px;line-height:1.5\"><h2 style='margin:0;font-size:40px'>x</h2></div>");

        Assert.Equal(explicitLength, height, 2);
        Assert.True(number > height + 8);
    }

    [Fact]
    public async Task ACalcWithLh_IsNotCompounded()
    {
        var height = await HeightAsync(
            $"<div style=\"{Font}line-height:30px\"><div style='line-height:calc(1lh + 8px)'><div>x</div></div></div>");

        var explicitLength = await HeightAsync($"<div style=\"{Font}line-height:38px\"><div><div>x</div></div></div>");

        Assert.Equal(explicitLength, height, 2);
    }

    [Fact]
    public async Task ANegativeLineHeight_IsIgnored()
    {
        var height = await HeightAsync($"<div style=\"{Font}line-height:30px\"><div style='line-height:-8px'>x</div></div>");

        var inherited = await HeightAsync($"<div style=\"{Font}line-height:30px\"><div>x</div></div>");

        Assert.Equal(inherited, height, 2);
    }

    [Fact]
    public async Task AFormControl_IgnoresTheContainersLineHeight()
    {
        var plain = await HeightAsync($"<div style=\"{Font}\"><input></div>");
        var tall = await HeightAsync($"<div style=\"{Font}line-height:300%\"><input></div>");

        Assert.True(tall - plain < 2 * 0.75 * 20, "the control must not be stretched by the container's line height");
    }

    [Theory]
    [InlineData("line-height:13px;line-height-step:20px", "line-height:20px")]
    [InlineData("line-height:21px;line-height-step:20px", "line-height:40px")]
    [InlineData("line-height:13px;line-height-step:none", "line-height:13px")]
    public async Task LineHeightStep_RoundsEachLineUp(string style, string equivalent)
    {
        var height = await HeightAsync($"<div style=\"{Font}font-size:10px;{style}\">a<br>b<br>c</div>");
        var expected = await HeightAsync($"<div style=\"{Font}font-size:10px;{equivalent}\">a<br>b<br>c</div>");

        Assert.Equal(expected, height, 0);
    }

    [Fact]
    public async Task BlockStep_PadsTheBoxOrItsMargin()
    {
        var margin = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px;block-step:25px\">a<br>b<br>c</div><div style='height:1px'></div>");
        var padding = await HeightAsync($"<div style=\"{Font}font-size:10px;line-height:10px;block-step:25px padding-box\">a<br>b<br>c</div>");

        Assert.Equal((50 + 1) * 0.75, margin, 1);
        Assert.Equal(50 * 0.75, padding, 1);
    }

    [Fact]
    public async Task TextBoxTrim_CutsTheLeadingOffBothEnds()
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\">Hxg</div>");
        var both = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box:trim-both text\">Hxg</div>");
        var cap = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box:trim-both cap alphabetic\">Hxg</div>");
        var start = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box-trim:trim-start;text-box-edge:text\">Hxg</div>");

        Assert.Equal(40 * 0.75, plain, 1);
        Assert.True(both < start && start < plain);
        Assert.True(cap < both);
    }

    [Fact]
    public async Task TextBoxTrim_ReachesTheFirstAndLastLineOfNestedBlocks()
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\"><p style='margin:0'>a</p><p style='margin:0'>b</p></div>");
        var trimmed = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box:trim-both text\"><p style='margin:0'>a</p><p style='margin:0'>b</p></div>");

        Assert.True(trimmed < plain);
        Assert.True(plain - trimmed > 5);
    }

    [Fact]
    public async Task TextBoxNormal_TrimsNothing()
    {
        var plain = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px\">Hxg</div>");
        var normal = await HeightAsync($"<div style=\"{Font}font-size:20px;line-height:40px;text-box:trim-both cap alphabetic;text-box:normal\">Hxg</div>");

        Assert.Equal(plain, normal, 1);
    }
}
