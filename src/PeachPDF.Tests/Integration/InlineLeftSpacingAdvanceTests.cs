using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An inline box's left padding and border are charged to the line exactly once.
    ///
    /// <c>CssLayoutEngine.FinalizeFlowBoxExit</c>'s "actual width handling" branch compared the advance
    /// the box's content had used (<c>CurrentX - startX</c>, measured from the box's <i>content</i>-box
    /// left edge, since FlowBox's per-child dispatch applies a child's leftSpacing before recursing)
    /// against <c>CssBox.ActualWidth</c>, which is the <i>border</i>-box width. Any inline carrying left
    /// padding or a left border therefore looked narrower than it was by exactly that padding and
    /// border, and the correction added them to the line a second time - pushing everything after the
    /// box right by one padding+border (issue #1093).
    ///
    /// Measured against Chromium at <c>font: 16px</c> with a bundled font embedded for both engines.
    /// The values below are the spec-exact ones; Chromium agrees to within its own rounding of a
    /// <c>1pt</c> border to a device pixel (~0.33px), and matches exactly on the border-free fixtures.
    /// </summary>
    public class InlineLeftSpacingAdvanceTests
    {
        private const string Pad = "padding-left:20pt; border-left:1pt solid red";

        /// <summary>padding-left 20pt + border-left 1pt.</summary>
        private const double PadWidthPt = 21;

        [Fact]
        public async Task EmptyInlineWithLeftPaddingAndBorder_ChargesThemOnce()
        {
            // The headline case: no white space anywhere in the markup, so nothing about white-space
            // processing is involved. Chromium puts BB at 27.656px = 20.742pt (its own rounding of the
            // 1pt border); the exact answer is 21pt. It used to come out at 42pt - precisely double.
            var words = await PlaceAsync($"<span style='{Pad}'></span><span>BB</span>");

            Assert.Equal(PadWidthPt, words["BB"].Left, 3);
        }

        [Fact]
        public async Task EmptyInlineWithLeftPaddingAndBorder_ChargesThemOnce_WithASourceSpaceAfterIt()
        {
            // Same box, now with a collapsible space after it. The space begins the line - the padded
            // inline holds no content - so css-text-3 phase II removes it and the answer is unchanged.
            // Chromium agrees, putting BB at the same 27.656px it gives the space-free form above.
            var words = await PlaceAsync($"<span style='{Pad}'></span> <span>BB</span>");

            Assert.Equal(PadWidthPt, words["BB"].Left, 3);
        }

        [Fact]
        public async Task InlineWithLeftPaddingAndContent_PlacesWhatFollowsRightAfterThatContent()
        {
            // The box's own content is positioned correctly even before the fix - it is the advance
            // PAST the box that was wrong - so this states the relationship the defect actually broke:
            // with no source white space between them, BB begins exactly where X ends.
            var words = await PlaceAsync($"<span style='{Pad}'>X</span><span>BB</span>");

            Assert.Equal(PadWidthPt, words["X"].Left, 3);
            Assert.Equal(words["X"].Right, words["BB"].Left, 3);
        }

        [Fact]
        public async Task InlineWithPaddingOnBothSides_ChargesEachSideOnce()
        {
            // The right side was never doubled - the parent's dispatch applies it after this branch
            // runs - so this is the guard that the fix left it alone. Chromium: 36.266px against
            // PeachPDF's 36.267px, the closest agreement of any fixture here (no border to round).
            var words = await PlaceAsync("<span style='padding:0 10pt'>X</span><span>BB</span>");

            Assert.Equal(10, words["X"].Left, 3);
            Assert.Equal(words["X"].Right + 10, words["BB"].Left, 3);
        }

        [Fact]
        public async Task InlineWithNoPaddingOrBorder_IsUnaffected()
        {
            // The control that localises the defect to padding/border: an empty inline with neither
            // was always correct, in both engines, at 0.
            var words = await PlaceAsync("<span></span><span>BB</span>");

            Assert.Equal(0, words["BB"].Left, 3);
        }

        [Fact]
        public async Task AnInlineLevelBoxWiderThanItsContent_StillReservesItsDeclaredWidth()
        {
            // What the branch is actually FOR, and the case that must keep working: a declared width
            // larger than the content it holds is still reserved, so the next box clears it. Chromium
            // and PeachPDF agree exactly here (80px = 60pt), there being no border to round.
            var words = await PlaceAsync(
                "<span style='display:inline-block; width:60pt'>X</span><span>BB</span>");

            Assert.Equal(60, words["BB"].Left, 3);
        }

        /// <summary>
        /// Every word in the fixture by its text, positioned relative to the container's own
        /// content-box left edge rather than to the page - the harness lays out inside a page margin,
        /// and none of these assertions are about where that margin falls.
        /// </summary>
        private static async Task<Dictionary<string, (double Left, double Right)>> PlaceAsync(string body)
        {
            // A monospace font keeps the fixtures' own glyph advances stable across whatever font the
            // host machine resolves for a generic family.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='font:16px monospace'>{body}</div>"));

            var container = LayoutHarness.FindById(root, "d")!;
            var origin = container.ClientLeft;

            return LayoutHarness.Descendants(container)
                .SelectMany(box => box.Words)
                .Where(word => !string.IsNullOrEmpty(word.Text))
                .ToDictionary(word => word.Text!, word => (word.Left - origin, word.Right - origin));
        }
    }
}
