namespace PeachBrackets.Tests;

/// <summary>
/// Behavior that changes what is drawn, checked through the public API by comparing two renders that must be identical: the same page written two ways, or a
/// page and the same page with one declaration the spec says has no effect.
/// </summary>
public class RenderedBehaviorTests
{
    private static async Task AssertSameAsync(string a, string b, int width = 200, int height = 120)
    {
        using var first = await PageRaster.RenderAsync(a, width, height);
        using var second = await PageRaster.RenderAsync(b, width, height);

        Assert.True(first.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(second.ToPixelBuffer().PremultipliedRgba.Span), "the two pages render differently");
    }

    private static async Task AssertDifferentAsync(string a, string b, int width = 200, int height = 120)
    {
        using var first = await PageRaster.RenderAsync(a, width, height);
        using var second = await PageRaster.RenderAsync(b, width, height);

        Assert.False(first.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(second.ToPixelBuffer().PremultipliedRgba.Span), "the two pages render the same");
    }

    [Fact]
    public Task AReversedListStartsWhereItsFirstValueOverrideNeedsIt() => AssertSameAsync(
        "<ol reversed style='font-size:16px'><li>x<li value=6>x<li>x</ol>",
        "<ol style='font-size:16px'><li value=7>x<li value=6>x<li value=5>x</ol>");

    [Fact]
    public Task AReversedListWithExplicitIncrementsCountsThemToo() => AssertSameAsync(
        "<ol reversed style='font-size:16px'><li>x<li value=3 style='counter-increment: list-item -2'>x<li>x</ol>",
        "<ol style='font-size:16px'><li value=5>x<li value=3>x<li value=2>x</ol>");

    [Fact]
    public Task AnAuthorReversedCounterResetCountsDown() => AssertSameAsync(
        "<style>ol{counter-reset:reversed(foo);list-style:none} li{counter-increment:foo -1} li::before{content:counter(foo) '. '}</style><ol><li>x<li>x<li>x</ol>",
        "<style>ol{list-style:none} li::before{content:attr(data-n) '. '}</style><ol><li data-n=3>x<li data-n=2>x<li data-n=1>x</ol>");

    [Theory]
    [InlineData("table-row-group")]
    [InlineData("table-row")]
    [InlineData("table-cell")]
    public Task MarginDoesNotApplyToAnInternalTableBox(string display) => AssertSameAsync(
        $"<div style='display:table'><div style='display:{display};margin:20px;width:30px;height:30px;background:rgb(0,128,0)'></div></div>",
        $"<div style='display:table'><div style='display:{display};width:30px;height:30px;background:rgb(0,128,0)'></div></div>");

    [Fact]
    public Task PaddingDoesNotApplyToATableRow() => AssertSameAsync(
        "<div style='display:table'><div style='display:table-row;padding:20px'><div style='display:table-cell;width:30px;height:30px;background:rgb(0,128,0)'></div></div></div>",
        "<div style='display:table'><div style='display:table-row'><div style='display:table-cell;width:30px;height:30px;background:rgb(0,128,0)'></div></div></div>");

    [Fact]
    public Task PaddingStillAppliesToATableCell() => AssertDifferentAsync(
        "<div style='display:table'><div style='display:table-cell;padding:10px;width:30px;height:30px;background:rgb(0,128,0)'></div></div>",
        "<div style='display:table'><div style='display:table-cell;width:30px;height:30px;background:rgb(0,128,0)'></div></div>");

    [Theory]
    [InlineData("p")]
    [InlineData("blockquote")]
    [InlineData("ul")]
    public Task TheDefaultMarginOfFlowContentIsOneEm(string tag) => AssertSameAsync(
        $"<div style='font-size:16px'>a<{tag} style='background:rgb(0,128,0)'>x</{tag}>b</div>",
        $"<div style='font-size:16px'>a<{tag} style='background:rgb(0,128,0);margin-top:1em;margin-bottom:1em'>x</{tag}>b</div>");

    [Theory]
    [InlineData("h2", "0.83em", "1.5em")]
    [InlineData("h3", "1em", "1.17em")]
    [InlineData("h4", "1.33em", "1em")]
    [InlineData("h5", "1.67em", "0.83em")]
    [InlineData("h6", "2.33em", "0.67em")]
    public Task TheDefaultHeadingMarginsAndSizesAreTheHtmlStandardsOnes(string tag, string margin, string size) => AssertSameAsync(
        $"<div style='font-size:16px'>a<{tag}>x</{tag}>b</div>",
        $"<div style='font-size:16px'>a<{tag} style='margin:{margin} 0;font-size:{size}'>x</{tag}>b</div>");

    [Fact]
    public Task AFormHasNoDefaultMargin() => AssertSameAsync(
        "<div style='font-size:16px'>a<form>x</form>b</div>",
        "<div style='font-size:16px'>a<form style='margin:0'>x</form>b</div>");

    [Fact]
    public Task ContentWiderThanAZeroWidthInlineBlockDoesNotPushWhatFollows() => AssertSameAsync(
        "<div style='font-size:16px'><span style='display:inline-block;width:0'>wwwwwwwwww</span>item</div>",
        "<div style='font-size:16px'><span style='display:inline-block;width:0;overflow:visible;height:1em'>wwwwwwwwww</span>item</div>");

    [Theory]
    [InlineData("rgb(0% 50% 0%)")]
    [InlineData("rgb(0%, 50%, 0%)")]
    [InlineData("rgb(0 128.0 0)")]
    [InlineData("rgb(0, 127.5, 0)")]
    [InlineData("rgba(0%, 50%, 0%, 100%)")]
    public Task AnRgbComponentIsRoundedToTheNearestByte(string color) => AssertSameAsync(
        $"<div style='width:40px;height:40px;background:{color}'></div>",
        "<div style='width:40px;height:40px;background:rgb(0,128,0)'></div>");

    [Theory]
    [InlineData("ActiveBorder", "ButtonBorder")]
    [InlineData("Window", "Canvas")]
    [InlineData("WindowText", "CanvasText")]
    [InlineData("ThreeDFace", "ButtonFace")]
    [InlineData("InactiveCaptionText", "GrayText")]
    public Task ADeprecatedSystemColorIsTheSameAsItsCurrentName(string deprecated, string current) => AssertSameAsync(
        $"<div style='width:40px;height:40px;background:{deprecated}'></div>",
        $"<div style='width:40px;height:40px;background:{current}'></div>");
}
