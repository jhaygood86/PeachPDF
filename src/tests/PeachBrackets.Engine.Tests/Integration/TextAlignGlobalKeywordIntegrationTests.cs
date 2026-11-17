using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS Cascade &amp; Inheritance 4 section 7.3: a CSS-wide keyword (<c>inherit</c>/<c>initial</c>/
/// <c>unset</c>) applies to a shorthand as a whole. <c>text-align</c>'s own shorthand converter
/// (<c>PeachBrackets.Css.TextAlignProperty</c>) had no notion of this - unlike a generated longhand
/// property class, it is hand-written and never gets the generic <c>.OrDefault()</c> global-keyword
/// wrapping, so a declaration like <c>text-align: inherit</c> failed to parse at all and was dropped
/// outright, leaving whatever the property held before untouched.
/// </summary>
public class TextAlignGlobalKeywordIntegrationTests
{
    [Fact]
    public async Task TextAlignInherit_CopiesPastAnUnconditionalOverride()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<div style='text-align:right'>" +
                "<div id='child' style='text-align:center'>" +
                "<div id='grandchild' style='text-align:inherit'>A</div>" +
                "</div></div>"),
            pageWidth: 600, pageHeight: 600);
        var grandchild = LayoutHarness.FindById(root, "grandchild")!;

        Assert.Equal(HorizontalAlignment.Center, grandchild.ComputedStyle.Text.TextAlignAll.Value);
    }

    [Fact]
    public async Task TextAlignInitial_ResetsPastAnInheritedValue()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<div style='text-align:right'><div id='child' style='text-align:initial'>A</div></div>"),
            pageWidth: 600, pageHeight: 600);
        var child = LayoutHarness.FindById(root, "child")!;

        Assert.Equal(HorizontalAlignment.Start, child.ComputedStyle.Text.TextAlignAll.Value);
    }

    [Fact]
    public async Task TextAlignUnset_InheritsSinceTextAlignIsAnInheritedProperty()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<div style='text-align:right'><div id='child' style='text-align:unset'>A</div></div>"),
            pageWidth: 600, pageHeight: 600);
        var child = LayoutHarness.FindById(root, "child")!;

        Assert.Equal(HorizontalAlignment.Right, child.ComputedStyle.Text.TextAlignAll.Value);
    }
}
