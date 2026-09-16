using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for #1027's <c>match-parent</c> support (css-text-3 §6.2/§6.3, confirmed
    /// against the actual spec text rather than the issue's own paraphrase): a value that behaves as
    /// <c>inherit</c>, except that an inherited <c>start</c>/<c>end</c> is interpreted against the
    /// <i>parent's</i> own <c>direction</c> rather than the box's own, recursing through a chain of
    /// <c>match-parent</c> ancestors and computing to <c>start</c> on the root element. See
    /// <see cref="DerivedStyle.ActualTextAlignAll"/>/<see cref="DerivedStyle.ActualTextAlignLast"/> for
    /// where the resolution actually lives.
    /// </summary>
    public class TextAlignMatchParentTests
    {
        private static async Task<(CssBox Target, CssBox Root)> LayoutAsync(string body)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageWidth: 300, pageHeight: 800, margin: 10);
            return (LayoutHarness.FindById(root, "target")!, root);
        }

        [Fact]
        public async Task MatchParent_ResolvesAgainstTheImmediateParentsDirection_NotItsOwn()
        {
            // target's own direction is ltr (where "start" would mean left) - match-parent must ignore
            // that and consult its rtl parent instead, landing on the physical right edge.
            var (target, _) = await LayoutAsync(
                "<div id='middle' style='direction:rtl'>" +
                "<p id='target' style='direction:ltr;text-align:match-parent'>w0 w1 w2</p>" +
                "</div>");

            Assert.Equal(HorizontalAlignment.Right, target.ActualTextAlignAll);
        }

        [Fact]
        public async Task MatchParent_ResolvesAnAncestorsLogicalEndAgainstTheParentsDirection()
        {
            // middle's own text-align:end (not inherited/defaulted start) resolves against middle's own
            // rtl direction to Left - exercising the End arm of the parent-direction switch, distinct
            // from the Start arm the other tests here exercise.
            var (target, _) = await LayoutAsync(
                "<div id='middle' style='direction:rtl;text-align:end'>" +
                "<p id='target' style='text-align:match-parent'>w0 w1 w2</p>" +
                "</div>");

            Assert.Equal(HorizontalAlignment.Left, target.ActualTextAlignAll);
        }

        [Fact]
        public async Task MatchParent_ChainsThroughMultipleMatchParentAncestors()
        {
            // grandparent (ltr, default text-align:start) -> middle (rtl, match-parent) -> target
            // (match-parent). middle's own match-parent resolves start against grandparent's ltr
            // direction to Left; target's match-parent then just inherits that already-physical Left
            // unchanged, regardless of middle's own rtl direction.
            var (target, _) = await LayoutAsync(
                "<div id='grandparent'>" +
                "<div id='middle' style='direction:rtl;text-align:match-parent'>" +
                "<p id='target' style='text-align:match-parent'>w0 w1 w2</p>" +
                "</div></div>");

            Assert.Equal(HorizontalAlignment.Left, target.ActualTextAlignAll);
        }

        [Fact]
        public async Task MatchParent_OnTextAlignLast_ResolvesAgainstTheParentsDirectionToo()
        {
            var (target, _) = await LayoutAsync(
                "<div id='middle' style='direction:rtl;text-align-last:end'>" +
                "<p id='target' style='direction:ltr;text-align-last:match-parent'>w0 w1 w2</p>" +
                "</div>");

            // middle's own text-align-last:end resolves against middle's own rtl direction to Left;
            // target's match-parent inherits that unchanged, ignoring target's own ltr direction.
            Assert.Equal(TextAlignLast.Left, target.ActualTextAlignLast);
        }

        [Fact]
        public async Task MatchParent_OnTextAlignLast_ResolvesAnAncestorsLogicalStartAgainstTheParentsDirection()
        {
            // middle's own text-align-last:start (explicit, not the default-unset Auto the other tests
            // here exercise) resolves against middle's own rtl direction to Right - the Start arm of the
            // switch, distinct from the End arm MatchParent_OnTextAlignLast_ResolvesAgainstTheParentsDirectionToo
            // exercises above.
            var (target, _) = await LayoutAsync(
                "<div id='middle' style='direction:rtl;text-align-last:start'>" +
                "<p id='target' style='text-align-last:match-parent'>w0 w1 w2</p>" +
                "</div>");

            Assert.Equal(TextAlignLast.Right, target.ActualTextAlignLast);
        }

        [Fact]
        public async Task MatchParent_OnTextAlignLast_DegradesToAutoWhenTheParentNeverDeclaredItsOwn()
        {
            var (target, _) = await LayoutAsync(
                "<div id='middle'>" +
                "<p id='target' style='text-align-last:match-parent'>w0 w1 w2</p>" +
                "</div>");

            Assert.Equal(TextAlignLast.Auto, target.ActualTextAlignLast);
        }

        [Fact]
        public async Task Root_MatchParent_ComputesToStart()
        {
            var (_, root) = await LayoutAsync("<p id='target'>w0 w1 w2</p>");
            Assert.Null(root.ParentBox);

            root.TextAlignAll = CssProperty<HorizontalAlignment>.FromValue(Keywords.MatchParent, HorizontalAlignment.MatchParent);

            Assert.Equal(HorizontalAlignment.Start, root.ActualTextAlignAll);
        }

        [Fact]
        public async Task TextAlignShorthand_MatchParent_SetsBothLonghandsToMatchParentAtTheRenderLayer()
        {
            var (target, _) = await LayoutAsync(
                "<div id='middle' style='direction:rtl'>" +
                "<p id='target' style='direction:ltr;text-align:match-parent'>w0 w1 w2</p>" +
                "</div>");

            Assert.Equal(HorizontalAlignment.MatchParent, target.TextAlignAll.Value);
            Assert.Equal(TextAlignLast.MatchParent, target.TextAlignLast.Value);
            Assert.Equal(HorizontalAlignment.Right, target.ActualTextAlignAll);
        }
    }
}
