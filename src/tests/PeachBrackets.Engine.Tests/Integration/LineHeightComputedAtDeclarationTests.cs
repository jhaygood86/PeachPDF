using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A <c>line-height</c> that is a length, percentage or <c>calc()</c> computes to an absolute length at the element that declares it, and
/// descendants inherit that length (CSS 2.1 section 10.8.1, CSS Values 4 section 6.1.1); only a unitless number is inherited as a number.
/// </summary>
public class LineHeightComputedAtDeclarationTests
{
    private const double PointsPerPx = 0.75;

    private static async Task<CssBox> LayoutAsync(string body, string id)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageHeight: 800);
        return LayoutHarness.FindById(root, id)!;
    }

    private static double HeightPx(CssBox box) => (box.ActualBottom - box.Location.Y) / PointsPerPx;

    [Theory]
    [InlineData("1.5em", 21)]
    [InlineData("150%", 21)]
    [InlineData("calc(1em + 7px)", 21)]
    public async Task ARelativeLength_IsResolvedAtTheDeclaringElementAndInheritedAsALength(string lineHeight, double expectedPx)
    {
        // 14px parent: 1.5em / 150% = 21px. The h2 has a 40px font and must inherit 21px, not 1.5 * 40.
        var heading = await LayoutAsync(
            $"<div style='font-size:14px;line-height:{lineHeight}'><h2 id='h' style='font-size:40px;margin:0'>x</h2></div>", "h");

        Assert.Equal(expectedPx, HeightPx(heading), 1);
    }

    [Fact]
    public async Task AUnitlessNumber_IsStillInheritedAsANumber()
    {
        var heading = await LayoutAsync(
            "<div style='font-size:14px;line-height:1.5'><h2 id='h' style='font-size:40px;margin:0'>x</h2></div>", "h");

        Assert.Equal(60, HeightPx(heading), 1);
    }

    [Fact]
    public async Task LineHeightDeclaredBeforeFontSize_UsesTheFinalFontSize()
    {
        var box = await LayoutAsync("<div id='a' style='line-height:1.5em;font-size:20px'>x</div>", "a");

        Assert.Equal(30, HeightPx(box), 1);
    }

    [Fact]
    public async Task CalcWithLh_AddsItsLengthOnlyOnce()
    {
        // Parent 30px; child calc(1lh + 8px) = 38px; the grandchild inherits 38px, not 46px.
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            "<div style='line-height:30px'><div id='c' style='line-height:calc(1lh + 8px);margin:0'><div id='g'>x</div></div></div>"), pageHeight: 800);

        Assert.Equal(38, HeightPx(LayoutHarness.FindById(root, "c")!), 1);
        Assert.Equal(38, HeightPx(LayoutHarness.FindById(root, "g")!), 1);
    }

    [Fact]
    public async Task CalcWithLhScaled_DoesNotCompound()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
            "<div style='line-height:20px'><div style='line-height:calc(1lh * 1.5)'><div id='g' style='line-height:inherit'>x</div></div></div>"), pageHeight: 800);

        Assert.Equal(30, HeightPx(LayoutHarness.FindById(root, "g")!), 1);
    }

    [Fact]
    public async Task BareLh_StillResolvesAgainstTheParent()
    {
        var box = await LayoutAsync("<div style='line-height:20px'><div id='a' style='line-height:2lh'>x</div></div>", "a");

        Assert.Equal(40, HeightPx(box), 1);
    }

    [Theory]
    [InlineData("-0.5")]
    [InlineData("-8px")]
    [InlineData("-50%")]
    public async Task ANegativeLineHeight_IsInvalidAndTheInheritedValueApplies(string negative)
    {
        // Invalid at parse time: the declaration is dropped, so the 30px inherited from the parent applies.
        var box = await LayoutAsync($"<div style='line-height:30px'><div id='a' style='line-height:{negative}'>x</div></div>", "a");

        Assert.Equal(30, HeightPx(box), 1);
    }

    [Fact]
    public async Task AZeroLineHeight_IsValid()
    {
        var box = await LayoutAsync("<div style='line-height:30px'><div id='a' style='line-height:0'>x</div></div>", "a");

        Assert.Equal(0, HeightPx(box), 1);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("select")]
    [InlineData("textarea")]
    [InlineData("button")]
    public async Task AFormControl_HasLineHeightNormalWhateverItsContainerSets(string tag)
    {
        var plain = await LayoutAsync($"<div><{tag} id='c'>x</{tag}></div>", "c");
        var inContainer = await LayoutAsync($"<div style='line-height:300%'><{tag} id='c'>x</{tag}></div>", "c");

        Assert.Equal(plain.DerivedStyle.ActualLineHeight, inContainer.DerivedStyle.ActualLineHeight, 3);
        Assert.Equal(HeightPx(plain), HeightPx(inContainer), 1);
    }
}
