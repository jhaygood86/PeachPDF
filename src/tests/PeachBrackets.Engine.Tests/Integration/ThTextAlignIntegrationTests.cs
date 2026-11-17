namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// HTML Standard, rendering: a th is centered by the UA stylesheet only when its parent's own
/// computed text-align is still the initial value - not a flat UA rule, which would override an
/// ancestor's own explicit text-align the same way it overrides an unset one (wpt
/// css-tables/th-text-align).
/// </summary>
public class ThTextAlignIntegrationTests
{
    [Fact]
    public async Task AThInATableWithNoTextAlignAnywhere_IsCentered()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table><tr><th id='th'>A</th></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var th = LayoutHarness.FindById(root, "th")!;

        Assert.Equal(PeachBrackets.Css.HorizontalAlignment.Center, th.ActualTextAlignAll);
    }

    [Fact]
    public async Task AThUnderATableWhoseTextAlignIsStillTheInitialValue_IsCentered()
    {
        // The table's own "start" is written explicitly, but "start" is itself text-align's initial
        // value - the UA default still applies, same as if the table had never set it at all.
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table style='text-align:start'><tr><th id='th'>A</th></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var th = LayoutHarness.FindById(root, "th")!;

        Assert.Equal(PeachBrackets.Css.HorizontalAlignment.Center, th.ActualTextAlignAll);
    }

    [Fact]
    public async Task AThUnderATableWithAnExplicitNonInitialTextAlign_InheritsItInsteadOfCentering()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table style='text-align:end'><tr><th id='th'>A</th></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var th = LayoutHarness.FindById(root, "th")!;

        Assert.Equal(PeachBrackets.Css.HorizontalAlignment.End, th.ComputedStyle.Text.TextAlignAll.Value);
    }

    [Fact]
    public async Task AThWithItsOwnExplicitTextAlignInherit_InheritsRatherThanCentering()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table><tr><th id='th' style='text-align:inherit'>A</th></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var th = LayoutHarness.FindById(root, "th")!;

        Assert.Equal(PeachBrackets.Css.HorizontalAlignment.Start, th.ComputedStyle.Text.TextAlignAll.Value);
    }

    [Fact]
    public async Task AThWithItsOwnExplicitTextAlign_IsNotCentered()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table><tr><th id='th' style='text-align:left'>A</th></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var th = LayoutHarness.FindById(root, "th")!;

        Assert.Equal(PeachBrackets.Css.HorizontalAlignment.Left, th.ComputedStyle.Text.TextAlignAll.Value);
    }
}
