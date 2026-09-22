using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The legacy <c>&lt;hr&gt;</c> attributes whose rendering-section mapping is not the generic one:
    /// <c>align</c> maps to margins, and <c>size</c> to <c>height: size - 2</c> (or, at 1, to a zero bottom
    /// border), per the
    /// <see href="https://html.spec.whatwg.org/multipage/rendering.html#the-hr-element">HTML Standard's
    /// hr element</see> rules. <c>align</c> on a <c>&lt;div&gt;</c> or <c>&lt;p&gt;</c> really is
    /// <c>text-align</c>, which is why the generic translation is right everywhere else.
    /// </summary>
    /// <remarks>
    /// Every literal was measured in Chrome 153 (1px = 0.75pt), against real <c>&lt;hr&gt;</c> elements.
    /// Two of them differ from a straight reading of the issue that filed this and are pinned on purpose:
    /// <c>align</c> matches the exact value, ASCII case-insensitively, with no trimming, and a <c>size</c>
    /// that is invalid, empty or valueless behaves like <c>size=1</c> in Chrome rather than being ignored.
    /// No test here declares a margin on the rule - an author margin legitimately overrides the
    /// <c>align</c> hint, so it would measure nothing.
    /// </remarks>
    public class HrLegacyAttributeTests
    {
        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string body, string css = "") =>
            LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>body { margin: 0; font-size: 10pt } "
                + $"#c {{ width: 200pt }} {css}</style></head><body><div id='c'>{body}</div></body></html>");

        private static async Task<double> LeftEdgeAsync(string hrAttributes, string css = "")
        {
            var (root, _) = await LayoutAsync($"<hr id='h' width='50%' {hrAttributes}>", css);
            var hr = LayoutHarness.FindById(root, "h")!;
            var c = LayoutHarness.FindById(root, "c")!;

            return hr.Location.X - c.ClientLeft;
        }

        // ── align ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("align='left'", 0)]
        [InlineData("align='center'", 49.25)]
        [InlineData("align='right'", 98.5)]
        [InlineData("align='LEFT'", 0)]
        [InlineData("align='Right'", 98.5)]
        [InlineData("align='CENTER'", 49.25)]
        // An unrecognised value maps to nothing, so the UA's own `margin-inline: auto` centres the rule.
        [InlineData("align='middle'", 49.25)]
        [InlineData("align='foo'", 49.25)]
        [InlineData("align=''", 49.25)]
        [InlineData("align", 49.25)]
        // The match is on the exact value: padding it with whitespace does not match, in Chrome or the spec.
        [InlineData("align=' right '", 49.25)]
        public async Task Align_MapsToMargins(string attribute, double expectedLeft)
        {
            Assert.Equal(expectedLeft, await LeftEdgeAsync(attribute), 2);
        }

        [Theory]
        [InlineData("align='left'")]
        [InlineData("align='right'")]
        [InlineData("align='center'")]
        public async Task Align_LosesToAnAuthorMargin(string attribute)
        {
            // Presentational hints sit at the start of the author origin at zero specificity, so any author
            // rule beats them: an `hr { margin: 0 }` reset pins the rule to the start edge whatever align says.
            Assert.Equal(0, await LeftEdgeAsync(attribute, "hr { margin: 0 }"), 3);
        }

        [Fact]
        public async Task Align_OnAnRtlRule_IsStillPhysical()
        {
            // `align=left` is margin-left: 0, not "start": the rule sits at the physical left in an rtl block.
            Assert.Equal(0, await LeftEdgeAsync("align='left'", "#c { direction: rtl }"), 3);
            Assert.Equal(98.5, await LeftEdgeAsync("align='right'", "#c { direction: rtl }"), 2);
        }

        [Fact]
        public async Task Align_OnAnOrdinaryBlock_IsStillTextAlign()
        {
            var (root, _) = await LayoutAsync("<div id='d' align='center'>x</div><p id='p' align='right'>x</p>");

            Assert.Equal("center", LayoutHarness.FindById(root, "d")!.TextAlignAll.ToString());
            Assert.Equal("right", LayoutHarness.FindById(root, "p")!.TextAlignAll.ToString());
        }

        // ── size ──────────────────────────────────────────────────────────────────

        [Theory]
        // total border-box height = the size in px, at 0.75pt a px: the two 1px borders count toward it
        [InlineData("size='3'", 2.25)]
        [InlineData("size='10'", 7.5)]
        [InlineData("size='2'", 1.5)]
        // size 1 drops the bottom border, leaving one 1px line
        [InlineData("size='1'", 0.75)]
        // `ToInt()`-style leading-integer parsing, as Chrome does: sign, whitespace and trailing junk are fine
        [InlineData("size='3px'", 2.25)]
        [InlineData("size=' 3'", 2.25)]
        [InlineData("size='+3'", 2.25)]
        [InlineData("size='3.7'", 2.25)]
        // Anything that does not parse to more than 1 behaves like size=1 in Chrome (the spec calls an invalid
        // value "no hint"; Chrome measured, and the pinned behaviour).
        [InlineData("size='0'", 0.75)]
        [InlineData("size='-1'", 0.75)]
        [InlineData("size='abc'", 0.75)]
        [InlineData("size=''", 0.75)]
        [InlineData("size", 0.75)]
        public async Task Size_MapsToTheRulesTotalHeight(string attribute, double expectedHeight)
        {
            var (root, _) = await LayoutAsync($"<hr id='h' {attribute}>");
            var hr = LayoutHarness.FindById(root, "h")!;

            Assert.Equal(expectedHeight, hr.ActualBottom - hr.Location.Y, 3);
        }

        [Theory]
        [InlineData("size='10' noshade", 7.5)]
        [InlineData("size='10' color='red'", 7.5)]
        [InlineData("size='1' noshade", 0.75)]
        public async Task Size_MeansTheSameWithColorOrNoshade(string attributes, double expectedHeight)
        {
            var (root, _) = await LayoutAsync($"<hr id='h' {attributes}>");
            var hr = LayoutHarness.FindById(root, "h")!;

            Assert.Equal(expectedHeight, hr.ActualBottom - hr.Location.Y, 3);
        }

        [Fact]
        public async Task Size_LosesToAnAuthorHeight()
        {
            var (root, _) = await LayoutAsync("<hr id='h' size='10'>", "hr { height: 20px }");
            var hr = LayoutHarness.FindById(root, "h")!;

            Assert.Equal(16.5, hr.ActualBottom - hr.Location.Y, 3);
        }

        // ── color and noshade fill the rule ───────────────────────────────────────

        [Theory]
        // The colour attribute is a presentational hint for `color` and is stored as written, so it reads
        // back as `red`; the UA sheet's own gray is normalized at parse time. Either way the fill is
        // `currentcolor`, which is what is being pinned.
        [InlineData("color='red'", "red")]
        [InlineData("noshade", "rgb(128, 128, 128)")]
        [InlineData("size='10' color='red' noshade", "red")]
        public async Task ColorAndNoshade_FillTheRulesContentBox(string attributes, string expectedBackground)
        {
            // Chrome maps `color` to background-color as well as the border colours, so a thick rule is a solid
            // bar rather than two coloured lines with a white gap between them.
            var (root, _) = await LayoutAsync($"<hr id='h' {attributes}>");

            Assert.Equal(expectedBackground, LayoutHarness.FindById(root, "h")!.BackgroundColor);
        }

        [Fact]
        public async Task APlainRule_HasNoBackground()
        {
            var (root, _) = await LayoutAsync("<hr id='h' size='10'>");
            var hr = LayoutHarness.FindById(root, "h")!;

            Assert.True(hr.BackgroundColor is "transparent" or "" or null,
                $"expected no background, got '{hr.BackgroundColor}'");
        }
    }
}
