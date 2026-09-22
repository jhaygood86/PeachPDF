using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The <c>&lt;hr&gt;</c> user-agent margin, <c>margin-block: 0.5em; margin-inline: auto</c>
    /// (<see href="https://html.spec.whatwg.org/multipage/rendering.html#the-hr-element">HTML Standard, the
    /// hr element</see>). It is a declaration in the UA sheet, so an author can override it in the ordinary way.
    /// </summary>
    /// <remarks>
    /// This used to be a hard-coded substitution in margin collapsing that replaced any near-zero collapsed
    /// margin before a rule with 1.1em <i>after</i> the cascade had decided - so <c>hr { margin: 0 }</c> was
    /// silently discarded, and the rule had no bottom margin at all. Every literal here was measured in
    /// Chrome 153 with a 10pt font, where 0.5em is 5pt.
    /// </remarks>
    public class HrUaMarginTests
    {
        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string body, string css = "") =>
            LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>body { margin: 0; font-size: 10pt } "
                + $"#c {{ width: 200pt }} {css}</style></head><body><div id='c'>{body}</div></body></html>");

        [Fact]
        public async Task ADefaultRule_HasHalfAnEmAboveAndBelow()
        {
            var (root, _) = await LayoutAsync("<div id='a' style='height: 20pt'></div><hr id='h'><div id='n' style='height: 10pt'></div>");
            var hr = LayoutHarness.FindById(root, "h")!;
            var a = LayoutHarness.FindById(root, "a")!;
            var n = LayoutHarness.FindById(root, "n")!;

            Assert.Equal(5, hr.ActualMarginTop, 3);
            Assert.Equal(5, hr.ActualMarginBottom, 3);
            Assert.Equal(5, hr.Location.Y - a.ActualBottom, 3);
            Assert.Equal(5, n.Location.Y - hr.ActualBottom, 3);
        }

        [Fact]
        public async Task TheMarginIsHalfAnEmOfTheRulesOwnFontSize()
        {
            var (root, _) = await LayoutAsync("<hr id='h' style='font-size: 20pt'>");
            var hr = LayoutHarness.FindById(root, "h")!;

            Assert.Equal(10, hr.ActualMarginTop, 3);
            Assert.Equal(10, hr.ActualMarginBottom, 3);
        }

        [Fact]
        public async Task AFirstChildRule_StillHasItsTopMargin()
        {
            // Chrome measures 5pt above a rule that is its container's first child. The margin collapses
            // through the container's own top edge, so it is measured against a container that has one.
            var (root, _) = await LayoutAsync("<hr id='h'>", "#c { padding-top: 1pt }");
            var hr = LayoutHarness.FindById(root, "h")!;
            var c = LayoutHarness.FindById(root, "c")!;

            Assert.Equal(5, hr.Location.Y - c.ClientTop, 3);
        }

        [Fact]
        public async Task AuthorMarginZero_OverridesTheDefault()
        {
            // The issue's repro: `hr { margin: 0 }` used to be replaced by a 1.1em top margin whenever a
            // sibling preceded the rule, because the substitution ran after the cascade.
            var (root, _) = await LayoutAsync(
                "<div id='a' style='margin: 0; height: 20pt'></div><hr id='h'><div id='n' style='margin: 0; height: 10pt'></div>",
                "hr { margin: 0 }");
            var hr = LayoutHarness.FindById(root, "h")!;
            var a = LayoutHarness.FindById(root, "a")!;
            var n = LayoutHarness.FindById(root, "n")!;

            Assert.Equal(0, hr.Location.Y - a.ActualBottom, 3);
            Assert.Equal(0, n.Location.Y - hr.ActualBottom, 3);
        }

        [Fact]
        public async Task AnInlineMarginTopZero_LeavesTheBottomMargin()
        {
            var (root, _) = await LayoutAsync("<div id='a' style='height: 20pt'></div><hr id='h' style='margin-top: 0'>");
            var hr = LayoutHarness.FindById(root, "h")!;
            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(0, hr.Location.Y - a.ActualBottom, 3);
            Assert.Equal(5, hr.ActualMarginBottom, 3);
        }

        [Fact]
        public async Task ATinyAuthorMargin_IsHonouredRatherThanRoundedUpToTheDefault()
        {
            // The substitution fired for anything that collapsed to under 0.1, so a margin the author chose
            // small enough was replaced with 1.1em too.
            var (root, _) = await LayoutAsync(
                "<div id='a' style='margin: 0; height: 20pt'></div><hr id='h' style='margin-top: 0.05pt'>");
            var hr = LayoutHarness.FindById(root, "h")!;
            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(0.05, hr.Location.Y - a.ActualBottom, 3);
        }

        // ── margin-inline: auto ───────────────────────────────────────────────────

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public async Task ARuleNarrowerThanItsContainer_CentresByDefault(string direction)
        {
            // Chrome: a `width="50%"` rule with no align attribute at all sits at 49.24pt, its 101.5pt border
            // box centred in 200pt.
            var (root, _) = await LayoutAsync("<hr id='h' width='50%'>", $"#c {{ direction: {direction} }}");
            var hr = LayoutHarness.FindById(root, "h")!;
            var c = LayoutHarness.FindById(root, "c")!;

            Assert.Equal(49.25, hr.Location.X - c.ClientLeft, 2);
            Assert.Equal(101.5, hr.ActualRight - hr.Location.X, 3);
        }

        [Fact]
        public async Task ACssWidthRule_CentresToo()
        {
            var (root, _) = await LayoutAsync("<hr id='h'>", "hr { width: 100pt }");
            var hr = LayoutHarness.FindById(root, "h")!;
            var c = LayoutHarness.FindById(root, "c")!;

            Assert.Equal(49.25, hr.Location.X - c.ClientLeft, 2);
        }

        [Fact]
        public async Task ACentredRule_InABorderBoxContainer_CentresInTheContentBox()
        {
            // The centring basis is the container's CONTENT width, so padding and border on a border-box
            // container must not skew it: content box 150pt, so a 50% rule is 75pt plus 1.5pt of border.
            var (root, _) = await LayoutAsync(
                "<hr id='h' width='50%'>",
                "#c { box-sizing: border-box; width: 200pt; padding: 0 20pt; border: 5pt solid }");
            var hr = LayoutHarness.FindById(root, "h")!;
            var c = LayoutHarness.FindById(root, "c")!;

            Assert.Equal((150 - 76.5) / 2, hr.Location.X - c.ClientLeft, 2);
        }

        [Fact]
        public async Task AnAutoWidthRule_StillFillsItsContainer()
        {
            var (root, _) = await LayoutAsync("<hr id='h'>");
            var hr = LayoutHarness.FindById(root, "h")!;
            var c = LayoutHarness.FindById(root, "c")!;

            Assert.Equal(0, hr.Location.X - c.ClientLeft, 3);
            Assert.Equal(200, hr.ActualRight - hr.Location.X, 3);
        }
    }
}
