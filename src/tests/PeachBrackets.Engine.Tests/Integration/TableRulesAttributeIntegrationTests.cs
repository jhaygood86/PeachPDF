using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// HTML Standard, rendering: the legacy <c>rules</c> attribute switches a table to the collapsed
/// border model, and <c>rules="groups"</c> also gives every <c>thead</c>/<c>tbody</c> a solid 1px
/// border-block-end so the collapsed model draws a line at each row-group boundary (wpt
/// css-tables/rules-groups).
/// </summary>
public class TableRulesAttributeIntegrationTests
{
    [Fact]
    public async Task ARulesAttribute_SwitchesTheTableToCollapse()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table id='t' rules='groups'><tr><td>a</td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(BorderCollapseMode.Collapse, t.BorderCollapse.Value);
    }

    [Fact]
    public async Task RulesGroups_GivesEveryTbodyABorderBlockEndButNotATfoot()
    {
        // Checked on two <tbody>s rather than thead+tbody+tfoot: a <thead> is detached and relaid out
        // through this engine's own repeating-header-group machinery, which replaces its CssBox by the
        // time layout finishes, so asserting on the post-layout instance found here would check the
        // wrong box. The actual wpt css-tables/rules-groups.html pixel test is what proves a thead's
        // own border-block-end renders correctly; this test only has to prove the hint reaches a
        // tbody and is withheld from a tfoot, which it can check reliably.
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<table rules='groups'>" +
                "<tbody id='body1'><tr><td>one</td></tr></tbody>" +
                "<tbody id='body2'><tr><td>two</td></tr></tbody>" +
                "<tfoot id='foot'><tr><td>foot</td></tr></tfoot>" +
                "</table>"),
            pageWidth: 600, pageHeight: 600);
        var body1 = LayoutHarness.FindById(root, "body1")!;
        var body2 = LayoutHarness.FindById(root, "body2")!;
        var foot = LayoutHarness.FindById(root, "foot")!;

        Assert.Equal("1px", body1.BorderBlockEndWidth);
        Assert.Equal("1px", body2.BorderBlockEndWidth);
        Assert.Null(foot.BorderBlockEndWidth);
    }

    [Fact]
    public async Task RulesNone_DoesNotSwitchToCollapse()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap("<table id='t' rules='none'><tr><td>a</td></tr></table>"),
            pageWidth: 600, pageHeight: 600);
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(BorderCollapseMode.Separate, t.BorderCollapse.Value);
    }

    [Fact]
    public async Task AnAuthorRule_StillOverridesTheRulesGroupsHint()
    {
        var (root, _) = await LayoutHarness.LayoutAsync(
            LayoutHarness.Wrap(
                "<style>tbody { border-block-end-width: 5px; }</style>" +
                "<table rules='groups'><tbody id='body'><tr><td>a</td></tr></tbody></table>"),
            pageWidth: 600, pageHeight: 600);
        var body = LayoutHarness.FindById(root, "body")!;

        Assert.Equal("5px", body.BorderBlockEndWidth);
    }
}
