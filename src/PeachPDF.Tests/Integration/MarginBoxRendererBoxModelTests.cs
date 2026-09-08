using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.ApplyBoxModel"/> and the outer-size
    /// allocation it pairs with in <see cref="MarginBoxRenderer.GetMarginBoxRect"/>. Per css-page-3
    /// §5.1 a page margin box is a block-level box that accepts the whole box model; §5.3.2/§5.3.3
    /// make its <c>width</c>/<c>height</c> the CONTENT-box dimension, with margin and padding
    /// additive on top of it.
    /// <para>
    /// Every expected value here is written as a literal derived from the fixture's own arithmetic,
    /// not read back off the rect under test.
    /// </para>
    /// </summary>
    public class MarginBoxRendererBoxModelTests
    {
        // A 600×800 sheet with 60pt left/right and 40pt top/bottom margins:
        //   contentWidth  = 600 - 60 - 60 = 480   (shared by a top/bottom row's three boxes)
        //   contentHeight = 800 - 40 - 40 = 720   (shared by a left/right column's three boxes)
        private static readonly XSize Page = new(600, 800);
        private const double ML = 60, MT = 40, MR = 60, MB = 40;
        private const double RemPt = 16;

        [Fact]
        public void AutoSizedBox_PaddingInsetsTheContentBoxOnEveryEdge()
        {
            // The PR's own repro: an auto-sized box's slot IS an outer allocation, so padding
            // insets it. An undeclared box in the row is still auto, so all three take an equal
            // third of the 480pt band: top-center's slot is 160pt wide starting at 60 + 160 = 220.
            var rect = Rect("@top-center { content: \"x\"; padding: 5pt 8pt; }", "top-center");

            Assert.Equal(220.0 + 8.0, rect.X, 3);
            Assert.Equal(0.0 + 5.0, rect.Y, 3);
            Assert.Equal(160.0 - 16.0, rect.Width, 3);
            Assert.Equal(40.0 - 10.0, rect.Height, 3);
        }

        [Fact]
        public void AutoSizedBox_MarginInsetsTheContentBoxTheSameWay()
        {
            var rect = Rect("@top-center { content: \"x\"; margin: 6pt 10pt; }", "top-center");

            Assert.Equal(220.0 + 10.0, rect.X, 3);
            Assert.Equal(0.0 + 6.0, rect.Y, 3);
            Assert.Equal(160.0 - 20.0, rect.Width, 3);
            Assert.Equal(40.0 - 12.0, rect.Height, 3);
        }

        [Fact]
        public void NegativeMargin_GrowsTheBoxPastItsBand()
        {
            // The other end of the same problem: a browser's print footer is an overlay that keeps
            // its inset even on a page margin shallower than that inset, which a margin box can only
            // follow by growing past its band. A negative margin is how it says so, so it must NOT
            // be clamped to zero the way padding is.
            var rect = Rect("@bottom-center { content: \"x\"; margin-top: -20pt; }", "bottom-center");

            // The bottom band starts at 800 - 40 = 760 and is 40pt tall.
            Assert.Equal(760.0 - 20.0, rect.Y, 3);
            Assert.Equal(40.0 + 20.0, rect.Height, 3);
        }

        [Fact]
        public void NegativePadding_IsClampedToZero()
        {
            // CSS 2.1 §8.4: a padding value may not be negative.
            var rect = Rect("@top-center { content: \"x\"; padding-left: -20pt; }", "top-center");

            Assert.Equal(220.0, rect.X, 3);
            Assert.Equal(160.0, rect.Width, 3);
        }

        [Fact]
        public void ExplicitWidth_IsTheContentWidth_NotChargedForItsOwnPaddingTwice()
        {
            // css-page-3 §5.3.2: a declared width is the CONTENT-box dimension and the box model is
            // additive on top of it. GetMarginBoxRect therefore has to distribute width + padding +
            // margin into the row, so that subtracting the box model back leaves the declared 200pt.
            // Subtracting from a slot that was only ever 200pt wide renders the box at 190pt.
            var rect = Rect("@top-center { content: \"x\"; width: 200pt; padding: 0 5pt; }", "top-center");

            Assert.Equal(200.0, rect.Width, 3);
        }

        [Fact]
        public void ExplicitHeight_IsTheContentHeight_InAColumnToo()
        {
            var rect = Rect("@left-middle { content: \"x\"; height: 100pt; padding: 7pt 0; }", "left-middle");

            Assert.Equal(100.0, rect.Height, 3);
        }

        [Fact]
        public void PercentagePadding_ResolvesAgainstTheMarginArea_NotTheBoxOwnShare()
        {
            // A LEFT or RIGHT percentage resolves against the containing block's width (css-page-3
            // §6). For a top-row box that is the 480pt content band, not the box's own
            // post-distribution share — otherwise the same percentage on two boxes in
            // one row means two different absolute lengths, and for an auto-width box the basis is
            // circular (a box-model-blind distribution feeding back into the box model).
            //
            // Two boxes share the row: 'top-left' is explicitly 120pt wide, so 'top-center' takes a
            // different share. 10% must still be 48pt in both.
            const string css = "@top-left { content: \"l\"; width: 120pt; padding-left: 10%; } "
                             + "@top-center { content: \"c\"; padding-left: 10%; }";

            var left = Rect(css, "top-left");
            var centre = Rect(css, "top-center");

            Assert.Equal(48.0, left.X - 60.0, 3);
            Assert.Equal(48.0, centre.X - (60.0 + left.Width + 48.0), 3);
        }

        [Fact]
        public void PercentagePaddingOnATopEdge_ResolvesAgainstTheContainingBlockHeight()
        {
            // css-page-3 §6 OVERRIDES CSS 2.1 §8.3/§8.4 in the page context: "For right and left
            // values, percentages are relative to the width of the containing block; for top and
            // bottom values, percentages are relative to the height of the containing block."
            //
            // A top row's containing block (§5.3.1) is the content band's width by the used page
            // margin's thickness — so a top/bottom percentage resolves against that 40pt thickness,
            // not the 480pt band. 5% of 40 is 2, not 5% of 480 (24).
            var rect = Rect("@top-center { content: \"x\"; padding-top: 5%; }", "top-center");

            Assert.Equal(2.0, rect.Y, 3);
        }

        [Fact]
        public void PercentagePaddingOnAVerticalEdgeInAColumn_ResolvesAgainstTheContentHeight()
        {
            // The mirror: a left/right column's containing block is the page margin's own width by
            // the content band's HEIGHT, so a vertical percentage there resolves against 720, not
            // against the 60pt column width. 5% of 720 = 36.
            //
            // The column's slot starts at contentTop (40) and a lone declared box still shares the
            // column three ways, so only the padding is asserted, as an offset from the slot's own top.
            var padded = Rect("@left-middle { content: \"x\"; padding-top: 5%; }", "left-middle");
            var bare = Rect("@left-middle { content: \"x\"; }", "left-middle");

            Assert.Equal(36.0, padded.Y - bare.Y, 3);
        }

        [Fact]
        public void PercentagePaddingInACorner_ResolvesAgainstTheTwoMarginsThatMeetThere()
        {
            // §5.3.1: a corner box's containing block is "the rectangle defined by the intersection
            // of the two page margins meeting at that corner" — 60pt wide by 40pt tall here. So the
            // horizontal basis is the left margin and the vertical one the top margin, and neither is
            // the content band.
            var rect = Rect("@top-left-corner { content: \"x\"; padding-left: 10%; padding-top: 10%; }",
                "top-left-corner");

            Assert.Equal(6.0, rect.X, 3);   // 10% of mL = 60
            Assert.Equal(4.0, rect.Y, 3);   // 10% of mT = 40
        }

        [Fact]
        public void PercentagePaddingInAColumn_ResolvesAgainstTheColumnWidth()
        {
            // For a left/right column the containing block is that column, whose width is the page
            // margin: 10% of the 60pt left margin = 6pt, not 10% of the 480pt content band.
            var rect = Rect("@left-middle { content: \"x\"; padding-left: 10%; }", "left-middle");

            Assert.Equal(6.0, rect.X, 3);
        }

        [Fact]
        public void PercentagePaddingInTheRightColumn_ResolvesAgainstTheRightMargin()
        {
            // The mirror of the left column: the containing block is the RIGHT page margin, so the
            // basis is mR. Same 60pt here as mL, so the fixture also pins that the box is placed
            // against the right band (x = 600 - 60 = 540) rather than the left one.
            var rect = Rect("@right-middle { content: \"x\"; padding-left: 10%; }", "right-middle");

            Assert.Equal(540.0 + 6.0, rect.X, 3);
        }

        [Fact]
        public void OverPaddedBox_CollapsesToZeroRatherThanANegativeExtent()
        {
            // XRect refuses a negative extent, and the render path's own `rect.Width <= 0` guard
            // then skips the box — which is what an over-padded box should do.
            var rect = Rect("@top-center { content: \"x\"; padding-left: 400pt; padding-right: 400pt; }", "top-center");

            Assert.Equal(0.0, rect.Width, 3);
        }

        [Fact]
        public void NoBoxModelDeclared_LeavesTheRectExactlyAsAllocated()
        {
            var css = "@top-center { content: \"x\"; }";
            var allocated = MarginBoxRenderer.GetMarginBoxRect("top-center", Page, ML, MT, MR, MB, Margins(css), null, RemPt);
            var applied = Rect(css, "top-center");

            Assert.Equal(allocated.X, applied.X, 3);
            Assert.Equal(allocated.Y, applied.Y, 3);
            Assert.Equal(allocated.Width, applied.Width, 3);
            Assert.Equal(allocated.Height, applied.Height, 3);
        }

        /// <summary>
        /// The two steps the render paths run back to back: allocate the slot, then reduce it to the
        /// content box.
        /// </summary>
        private static XRect Rect(string marginBoxCss, string boxName)
        {
            var margins = Margins(marginBoxCss);
            var rect = MarginBoxRenderer.GetMarginBoxRect(boxName, Page, ML, MT, MR, MB, margins, null, RemPt);
            var rule = margins.Single(m => (m.Selector?.Text?.Trim().ToLowerInvariant() ?? "") == boxName);
            return MarginBoxRenderer.ApplyBoxModel(rect, rule, null, RemPt,
                MarginBoxRenderer.MarginAreaWidth(boxName, Page, ML, MR),
                MarginBoxRenderer.MarginAreaHeight(boxName, Page, MT, MB));
        }

        private static IReadOnlyList<MarginStyleRule> Margins(string marginBoxCss) =>
            new StylesheetParser().Parse($"@page {{ {marginBoxCss} }}")
                .Rules.OfType<PageRule>().Single().Margins.ToList();
    }
}
