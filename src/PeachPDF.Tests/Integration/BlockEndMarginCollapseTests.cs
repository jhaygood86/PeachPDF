using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 <see href="https://www.w3.org/TR/CSS21/box.html#collapsing-margins">§8.3.1</see>'s
    /// <b>block-end</b> half: "the bottom margin of an in-flow block box with a <c>height</c> of
    /// <c>auto</c> and a <c>min-height</c> of zero collapses with its last in-flow block-level child's
    /// bottom margin, if the box has no bottom padding or border".
    ///
    /// <para>
    /// Two outcomes follow from that one sentence, and the engine used to get a third, wrong one. When the
    /// margins <b>do</b> collapse, the collapsed value belongs to the gap <i>after</i> the box - it is not
    /// part of the box's own height, and it keeps escaping outward through as many ancestors as also
    /// collapse. When they <b>do not</b> (a bottom border or padding, a new block formatting context), the
    /// child's margin has nowhere to escape to and is <i>contained</i> in the box's auto height. What used
    /// to happen instead is that a box with a following sibling dropped its last child's bottom margin
    /// entirely - neither escaping nor contained - which is what put Acid2's <c>&lt;ul&gt;</c> (the last
    /// row of the face) on top of <c>.parser</c> instead of below it.
    /// </para>
    ///
    /// <para>
    /// Every expectation here was measured in headless Chrome on the same markup first, then asserted;
    /// the per-test comments give Chrome's number where it is not simply the value being asserted.
    /// </para>
    /// </summary>
    public class BlockEndMarginCollapseTests
    {
        private const string InnerBlock = "<div class='inner' style='height:10pt; margin:0 0 12pt'></div>";

        [Fact]
        public async Task CollapsedMargin_EscapesTheWrapper_IntoTheGapAfterIt()
        {
            // The base case: nothing of #outer's own separates its bottom margin from #inner's, so #outer
            // is its content's 10pt and the 12pt lands in the gap before #next.
            var (outer, next) = await MeasurePairAsync("", "");

            Assert.Equal(10, outer.Height, precision: 6);
            Assert.Equal(12, next.Gap, precision: 6);
        }

        [Fact]
        public async Task CollapsedMargin_JoinsTheFollowingSiblingsOwnTopMarginInOneSet()
        {
            // The escaped 12pt and #next's -12pt are one adjoining set, so they collapse to
            // max(12) + min(-12) = 0 rather than being applied one after the other.
            var (outer, next) = await MeasurePairAsync("", "margin-top:-12pt");

            Assert.Equal(10, outer.Height, precision: 6);
            Assert.Equal(0, next.Gap, precision: 6);
        }

        [Theory]
        // The wrapper's own bottom margin joins the same set, so the larger of the two wins...
        [InlineData("margin-bottom:4pt", 12)]
        [InlineData("margin-bottom:20pt", 20)]
        // ...and a negative one is added to the maximum positive, per §8.3.1's collapse rule.
        [InlineData("margin-bottom:-4pt", 8)]
        public async Task WrappersOwnBottomMargin_CollapsesWithItsChilds_AsOneSet(string outerStyle, double expectedGap)
        {
            var (outer, next) = await MeasurePairAsync(outerStyle, "");

            Assert.Equal(10, outer.Height, precision: 6);
            Assert.Equal(expectedGap, next.Gap, precision: 6);
        }

        [Fact]
        public async Task CollapsedMargin_KeepsEscaping_ThroughEveryAncestorThatAlsoCollapses()
        {
            // A second wrapper changes nothing: both levels collapse, so the margin escapes both and
            // neither box's height grows. Reading only the immediate child's margin - or baking the
            // collapsed value into the inner wrapper's height, which the old code did whenever a box was
            // its own parent's last child - leaves the outer wrapper 22pt tall instead of 10pt.
            var (outer, next) = await MeasurePairAsync("", "", innerMarkup: $"<div>{InnerBlock}</div>");

            Assert.Equal(10, outer.Height, precision: 6);
            Assert.Equal(12, next.Gap, precision: 6);
        }

        [Theory]
        // A bottom border blocks collapsing, so the child's margin stays inside: 10 + 12 + 1.
        [InlineData("border-bottom:1pt solid black", 23)]
        // So does bottom padding: 10 + 12 + 5.
        [InlineData("padding-bottom:5pt", 27)]
        // ...and so does a new block formatting context, with nothing of its own to add: 10 + 12.
        [InlineData("overflow:hidden", 22)]
        public async Task BlockedCollapse_ContainsTheChildsMargin_RatherThanDiscardingIt(
            string outerStyle, double expectedHeight)
        {
            // "Blocked" means the margin cannot get out, not that it stops existing - the engine used to
            // read it the second way and lose the margin from both sides of the boundary at once.
            var (outer, next) = await MeasurePairAsync(outerStyle, "");

            Assert.Equal(expectedHeight, outer.Height, precision: 6);
            Assert.Equal(0, next.Gap, precision: 6);
        }

        [Theory]
        [InlineData("float:left")]
        [InlineData("position:absolute; top:0; left:0")]
        [InlineData("display:inline-block")]
        public async Task FormattingContextRoot_ContainsItsLastChildsMargin(string outerStyle)
        {
            var (outer, _) = await MeasurePairAsync(outerStyle, "");

            Assert.Equal(22, outer.Height, precision: 6);
        }

        [Fact]
        public async Task NonAutoHeight_BlocksCollapse_ButIsStillTheHeight()
        {
            // The one blocking reason that does not hand the margin to the box's height: a declared
            // height IS the height, and the contained margin overflows it rather than growing it.
            // Chrome measures 20pt here, not 22pt.
            var (outer, next) = await MeasurePairAsync("height:20pt", "");

            Assert.Equal(20, outer.Height, precision: 6);
            Assert.Equal(0, next.Gap, precision: 6);
        }

        [Fact]
        public async Task SelfCollapsingLastChild_ContributesItsWholeAdjoiningSet()
        {
            // A self-collapsing last child's margins join the chain's set rather than being a break in
            // it, so its 3pt reaches the gap after #outer even though the child itself has no height.
            //
            // #outer needs real content of its own ahead of that child, or #outer becomes
            // self-collapsing too and the whole set escapes ABOVE it instead of landing in this gap -
            // Chrome puts the gap at 0 for that shape, which is the separate self-collapsing
            // pass-through rule rather than the block-end chain under test here. Chrome measures
            // 10pt / 3pt for the shape used. Deliberately no margin-top on the self-collapsing child:
            // this engine positions such a child below its own collapsed-away top margin, which inflates
            // #outer (18pt where Chrome says 10pt) - a separate defect in self-collapsing box placement,
            // not in the block-end chain, and one this test would otherwise be silently asserting.
            var (outer, next) = await MeasurePairAsync("", "",
                innerMarkup: "<div style='height:10pt'></div><div style='margin-bottom:3pt'></div>");

            Assert.Equal(10, outer.Height, precision: 6);
            Assert.Equal(3, next.Gap, precision: 6);
        }

        [Theory]
        // Blocked, so the margin that gets contained must be the last RENDERED child's 12pt, not the
        // hidden box's 40pt: 10 + 12 + 1pt border. Chrome measures 22.75pt, the 0.25 being its own
        // sub-pixel rounding of a 1pt border.
        [InlineData("border-bottom:1pt solid black", 23d, 0d)]
        // Unblocked, so the same 12pt escapes into the gap and the hidden box contributes nothing.
        [InlineData("", 10d, 12d)]
        public async Task DisplayNoneLastChild_IsNotTheChildWhoseMarginCounts(
            string outerStyle, double expectedHeight, double expectedGap)
        {
            // A `display: none` box generates no box at all (CSS 2.1 §9.2.4), so it can be neither the
            // last in-flow child a margin collapses with nor the thing the parent's height is measured
            // to. The chain walk filtered it out from the start; the box the contained-margin arm reads
            // did not, and the two disagreeing made a hidden element's 40pt margin real.
            var (outer, next) = await MeasurePairAsync(outerStyle, "",
                innerMarkup: InnerBlock + "<div style='display:none; margin-bottom:40pt'></div>");

            Assert.Equal(expectedHeight, outer.Height, precision: 6);
            Assert.Equal(expectedGap, next.Gap, precision: 6);
        }

        [Fact]
        public async Task DisplayNoneOnlyChild_ContributesNeitherHeightNorMargin()
        {
            var (outer, next) = await MeasurePairAsync("border-bottom:1pt solid black", "",
                innerMarkup: "<div style='display:none; margin-bottom:40pt'></div>");

            Assert.Equal(1, outer.Height, precision: 6);
            Assert.Equal(0, next.Gap, precision: 6);
        }

        [Fact]
        public async Task InlineChildsBlockAxisMargin_DoesNotEnterTheCollapseChain()
        {
            var (outer, next) = await MeasurePairAsync("", "",
                innerMarkup: "<span style='margin-bottom:100pt'>x</span>");

            Assert.True(outer.Height > 0);
            Assert.Equal(0, next.Gap, precision: 6);
        }

        /// <summary>
        /// Lays out <c>#outer</c> (carrying <paramref name="outerStyle"/>, wrapping
        /// <paramref name="innerMarkup"/>) followed by <c>#next</c> (carrying
        /// <paramref name="nextStyle"/>), and returns each box's height and the gap between them, in
        /// points.
        /// </summary>
        private static async Task<((double Height, double Gap) Outer, (double Height, double Gap) Next)>
            MeasurePairAsync(string outerStyle, string nextStyle, string? innerMarkup = null)
        {
            var html = $"""
                <!DOCTYPE html>
                <html><body style="margin: 0">
                  <div id="outer" style="{outerStyle}">{innerMarkup ?? InnerBlock}</div>
                  <div id="next" style="height:10pt; {nextStyle}"></div>
                </body></html>
                """;

            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter)
            {
                MarginTop = 0,
                MarginLeft = 0,
                MarginRight = 0,
                MarginBottom = 0
            };

            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);

            var byId = new Dictionary<string, CssBox>();
            Collect(container.Root!, byId);

            var outer = byId["outer"];
            var next = byId["next"];

            return (
                (outer.ActualBottom - outer.Location.Y, 0),
                (next.ActualBottom - next.Location.Y, next.Location.Y - outer.ActualBottom));
        }

        private static void Collect(CssBox box, Dictionary<string, CssBox> into)
        {
            if (box.HtmlTag?.TryGetAttribute("id") is { } id) into[id] = box;
            foreach (var child in box.Boxes) Collect(child, into);
        }
    }
}
