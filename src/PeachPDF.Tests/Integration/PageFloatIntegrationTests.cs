using PeachPDF.Html.Core.Dom;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Covers CSS Page Floats' <c>float: top/bottom/top-bottom/snap/inside/outside</c> (issue #699):
    /// keyword parsing, the dynamic band-start/band-end reservation
    /// (<see cref="Html.Core.HtmlContainerInt.TopFloatAreaHeightsBySlot"/>/
    /// <see cref="Html.Core.HtmlContainerInt.BottomFloatAreaHeightsBySlot"/>) that shrinks the usable
    /// content band for ordinary flow content on the float's landing page, the <c>top-bottom</c>/
    /// <c>snap</c> edge-choosing heuristics, and <c>inside</c>/<c>outside</c>'s page-parity-aware
    /// left/right resolution.
    /// </summary>
    public class PageFloatIntegrationTests
    {
        [Fact]
        public async Task FloatTop_SandwichedBetweenTwoInFlowSiblings_IsSkippedAsPreviousInFlowSibling()
        {
            // Regression for a real bug this feature's own showcase caught: DomUtils.GetPreviousSibling's
            // "in-flow sibling" walk (includeFloats: false) excluded ordinary left/right floats by name
            // (IsFloated) but not IsPageFloated - so a paragraph following a float: top box read that
            // float itself as its previous in-flow sibling instead of skipping it to reach the paragraph
            // before it, landing p2 on top of h/p1 instead of after p1.
            var html = Wrap(@"
                <h1 id='h' style='font-size:20pt; margin:0 0 14pt;'>Heading</h1>
                <p id='p1' style='line-height:1.6; margin:0 0 10pt;'>First paragraph, before the float.</p>
                <div id='f' style='float:top; border:1pt solid; padding:10pt; margin:0 0 12pt; font-size:9pt;'>A float: top box sitting between the two paragraphs in document order.</div>
                <p id='p2' style='line-height:1.6; margin:0 0 10pt;'>Second paragraph, after the float.</p>");

            var (root, _) = await LayoutAsync(html);
            var h = FindById(root, "h")!;
            var p1 = FindById(root, "p1")!;
            var p2 = FindById(root, "p2")!;

            Assert.True(p1.Location.Y >= h.ActualBottom, "p1 must start at or after h's bottom");
            Assert.True(p2.Location.Y >= p1.ActualBottom, "p2 must start at or after p1's bottom, not overlap h/p1");
        }

        [Theory]
        [InlineData("top", "Top")]
        [InlineData("bottom", "Bottom")]
        [InlineData("top-bottom", "TopBottom")]
        [InlineData("snap", "Snap")]
        [InlineData("inside", "Inside")]
        [InlineData("outside", "Outside")]
        public async Task Float_ParsesEachPageFloatKeyword(string keyword, string expectedEnumName)
        {
            var html = Wrap($"<div id='f' style='float:{keyword}; width:50pt; height:20pt;'></div>");

            var (root, _) = await LayoutAsync(html);
            var box = FindById(root, "f")!;

            Assert.Equal(expectedEnumName, box.Float.Value.ToString());
        }

        [Fact]
        public async Task FloatTop_IsPageFloated_AndOutOfFlow_ButNotIsFloated()
        {
            var html = Wrap("<div id='f' style='float:top; width:50pt; height:20pt;'></div>");

            var (root, _) = await LayoutAsync(html);
            var box = FindById(root, "f")!;

            Assert.True(box.IsPageFloated);
            Assert.True(box.IsOutOfFlow);
            Assert.False(box.IsFloated);
        }

        [Fact]
        public async Task FloatInsideOutside_AreIsFloated_NotIsPageFloated()
        {
            var html = Wrap(@"
                <div id='i' style='float:inside; width:50pt; height:20pt;'></div>
                <div id='o' style='float:outside; width:50pt; height:20pt;'></div>");

            var (root, _) = await LayoutAsync(html);

            var inside = FindById(root, "i")!;
            var outside = FindById(root, "o")!;

            Assert.True(inside.IsFloated);
            Assert.False(inside.IsPageFloated);
            Assert.True(outside.IsFloated);
            Assert.False(outside.IsPageFloated);
        }

        [Fact]
        public async Task FloatTop_ReservesTopSpaceOnItsLandingPage()
        {
            var html = Wrap(@"
                <div id='f' style='float:top; width:100pt; height:60pt;'></div>
                <p>Body text.</p>");

            var (_, container) = await LayoutAsync(html);

            var reservation = Assert.Single(container.TopFloatAreaHeightsBySlot);
            Assert.Equal(0, reservation.Key);
            Assert.True(reservation.Value >= 59.9, $"expected ~60pt reserved, was {reservation.Value}");
        }

        [Fact]
        public async Task FloatBottom_ReservesBottomSpaceOnItsLandingPage()
        {
            var html = Wrap(@"
                <p>Body text.</p>
                <div id='f' style='float:bottom; width:100pt; height:40pt;'></div>");

            var (_, container) = await LayoutAsync(html);

            var reservation = Assert.Single(container.BottomFloatAreaHeightsBySlot);
            Assert.Equal(0, reservation.Key);
            Assert.True(reservation.Value >= 39.9, $"expected ~40pt reserved, was {reservation.Value}");
        }

        [Fact]
        public async Task FloatTop_MovesToTheTrueTopOfItsLandingPage()
        {
            // The float appears after a spacer in document order, but float:top must still land flush
            // with the page's own content-band top, not at the spacer's natural in-flow position.
            var html = Wrap(@"
                <div style='height:300pt;'></div>
                <div id='f' style='float:top; width:100pt; height:30pt;'></div>");

            var (root, container) = await LayoutAsync(html);
            var box = FindById(root, "f")!;

            Assert.Equal(container.PageTopOf(0), box.Location.Y, 0.5);
        }

        [Fact]
        public async Task FloatBottom_MovesToTheTrueBottomOfItsLandingPage()
        {
            var html = Wrap(@"
                <div id='f' style='float:bottom; width:100pt; height:30pt;'></div>");

            var (root, container) = await LayoutAsync(html);
            var box = FindById(root, "f")!;

            Assert.Equal(container.PageBottomOf(0) - 30, box.Location.Y, 0.5);
            Assert.Equal(container.PageBottomOf(0), box.ActualBottom, 0.5);
        }

        [Fact]
        public async Task FloatTop_FollowingFlowContent_LandsBelowTheReservedStrip()
        {
            var withFloat = Wrap(@"
                <div id='f' style='float:top; width:100pt; height:80pt;'></div>
                <div id='after' style='height:10pt;'></div>");
            var withoutFloat = Wrap(@"
                <div id='after' style='height:10pt;'></div>");

            var (rootWith, containerWith) = await LayoutAsync(withFloat, pageHeight: 842);
            var (rootWithout, _) = await LayoutAsync(withoutFloat, pageHeight: 842);

            var afterWith = FindById(rootWith, "after")!;
            var afterWithout = FindById(rootWithout, "after")!;

            // Without the reservation, "after" would sit at the content-band top (same Y in both
            // documents' coordinate space) - the float must push it down by (at least) its own height.
            Assert.True(afterWith.Location.Y >= afterWithout.Location.Y + 79,
                $"expected 'after' to move down by ~80pt, moved by {afterWith.Location.Y - afterWithout.Location.Y}");
            Assert.Equal(containerWith.PageTopOf(0) + 80, afterWith.Location.Y, 0.5);
        }

        [Fact]
        public async Task FloatBottom_ForcesEarlierPageBreakThanWithoutIt()
        {
            // Enough content to almost fill one page; a bottom float's reservation should be the
            // difference between a wrapping paragraph's last line fitting on page 0 and moving to page 1.
            // A childless block (unlike a line box) isn't relocated just because it slightly overflows a
            // band - line boxes are the content PeachPDF actually treats as monolithic-and-relocatable
            // (css-break-3 §4.1), so the probe here is a paragraph, not a bare div.
            const string tailText =
                "<p id='tail'>alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango uniform victor whiskey xray yankee zulu</p>";
            var withFloat = Wrap($@"
                <div id='filler' style='height:750pt;'></div>
                <div id='f' style='float:bottom; width:100pt; height:60pt;'></div>
                {tailText}");
            var withoutFloat = Wrap($@"
                <div id='filler' style='height:750pt;'></div>
                {tailText}");

            var (rootWith, containerWith) = await LayoutAsync(withFloat, pageHeight: 842);
            var (rootWithout, containerWithout) = await LayoutAsync(withoutFloat, pageHeight: 842);

            var tailWith = FindById(rootWith, "tail")!;
            var tailWithout = FindById(rootWithout, "tail")!;

            Assert.Equal(0, containerWithout.PageIndexOf(tailWithout.Location.Y));
            Assert.Equal(1, containerWith.PageIndexOf(tailWith.LineBoxes[^1].LineTop));
        }

        [Fact]
        public async Task FloatTop_NestedInsideInlineContent_StillFloatsToThePageTop()
        {
            // A page float authored inside a run of inline text (an inline source, e.g. <span>) is
            // blockified (DerivedStyle.ActualDisplay) the same way float: left/right already is, so it
            // must reach the same out-of-flow placement even when discovered via inline dispatch rather
            // than the ordinary block-children loop.
            var html = Wrap(@"
                <p>Some text <span id='f' style='float:top; width:80pt; height:20pt;'>floated</span> more text after it.</p>");

            var (root, container) = await LayoutAsync(html);
            var f = FindById(root, "f")!;

            Assert.Equal(container.PageTopOf(0), f.Location.Y, 0.5);
        }

        [Fact]
        public async Task FloatTopBottom_PlacedAtTop_WhenItFits()
        {
            var html = Wrap("<div id='f' style='float:top-bottom; width:100pt; height:30pt;'></div>");

            var (_, container) = await LayoutAsync(html);

            Assert.True(container.TopFloatAreaHeightsBySlot.ContainsKey(0));
            Assert.False(container.BottomFloatAreaHeightsBySlot.ContainsKey(0));
        }

        [Fact]
        public async Task FloatTopBottom_FallsBackToBottom_WhenTopHasNoRoomLeft()
        {
            // A first float:top already claims almost the whole band; a second, later float:top-bottom
            // cannot fit at the top any more and must fall back to the bottom edge.
            var html = Wrap(@"
                <div id='first' style='float:top; width:100pt; height:790pt;'></div>
                <div id='second' style='float:top-bottom; width:100pt; height:40pt;'></div>");

            var (_, container) = await LayoutAsync(html, pageHeight: 842);

            Assert.True(container.BottomFloatAreaHeightsBySlot.ContainsKey(0),
                "the second float must have fallen back to the bottom edge");
        }

        [Fact]
        public async Task FloatSnap_ChoosesTheNearerEdge()
        {
            var nearTop = Wrap(@"
                <div style='height:10pt;'></div>
                <div id='f' style='float:snap; width:100pt; height:20pt;'></div>");
            var nearBottom = Wrap(@"
                <div style='height:780pt;'></div>
                <div id='f' style='float:snap; width:100pt; height:20pt;'></div>");

            var (_, containerNearTop) = await LayoutAsync(nearTop, pageHeight: 842);
            var (_, containerNearBottom) = await LayoutAsync(nearBottom, pageHeight: 842);

            Assert.True(containerNearTop.TopFloatAreaHeightsBySlot.ContainsKey(0));
            Assert.True(containerNearBottom.BottomFloatAreaHeightsBySlot.ContainsKey(0));
        }

        [Fact]
        public async Task FloatTop_MultipleOnOnePage_StackInDocumentOrder()
        {
            var html = Wrap(@"
                <div id='a' style='float:top; width:100pt; height:20pt;'></div>
                <div id='b' style='float:top; width:100pt; height:30pt;'></div>");

            var (root, container) = await LayoutAsync(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(container.PageTopOf(0), a.Location.Y, 0.5);
            Assert.Equal(container.PageTopOf(0) + 20, b.Location.Y, 0.5);

            var reservation = Assert.Single(container.TopFloatAreaHeightsBySlot);
            Assert.True(reservation.Value >= 49.9, $"expected ~50pt total, was {reservation.Value}");
        }

        [Theory]
        [InlineData(1, "left", "right")] // page 1 (1-based) is a right/recto page
        [InlineData(2, "right", "left")] // page 2 is a left/verso page
        public async Task FloatInsideOutside_ResolveByPageParity(int pageNumber, string expectedInsideSide, string expectedOutsideSide)
        {
            var pagesBefore = pageNumber - 1;
            // LayoutAsync's default margin is 20pt on every side, so each page's own usable content
            // band is 842 - 40 = 802pt tall, not the full 842pt sheet - a spacer sized off the sheet
            // height alone would land a page short of where this test expects. +5pt clears the boundary
            // itself, since a box flush ON it is (deliberately) still counted as the earlier page.
            var spacer = pagesBefore > 0 ? $"<div style='height:{pagesBefore * (842 - 40) + 5}pt;'></div>" : string.Empty;
            var html = Wrap($@"
                {spacer}
                <div id='i' style='float:inside; width:50pt; height:20pt;'></div>
                <div id='o' style='float:outside; width:50pt; height:20pt;'></div>");

            var (root, container) = await LayoutAsync(html, pageHeight: 842);
            var inside = FindById(root, "i")!;
            var outside = FindById(root, "o")!;

            var pageLeft = container.MarginLeft;
            var pageRight = container.PageContentRightOf(container.PageTopOf(pageNumber - 1));

            if (expectedInsideSide == "left")
            {
                Assert.Equal(pageLeft, inside.Location.X, 1.0);
            }
            else
            {
                Assert.True(inside.ActualRight > (pageLeft + pageRight) / 2, "inside float should sit on the right");
            }

            if (expectedOutsideSide == "left")
            {
                Assert.Equal(pageLeft, outside.Location.X, 1.0);
            }
            else
            {
                Assert.True(outside.ActualRight > (pageLeft + pageRight) / 2, "outside float should sit on the right");
            }
        }

        [Fact]
        public async Task NoPageFloats_DictionariesStayEmpty()
        {
            var html = Wrap("<p>Ordinary content with no page floats at all.</p>");

            var (_, container) = await LayoutAsync(html);

            Assert.False(container.HasPageFloats);
            Assert.Empty(container.TopFloatAreaHeightsBySlot);
            Assert.Empty(container.BottomFloatAreaHeightsBySlot);
            Assert.Empty(container.PageFloatPlacements);
        }

        // ─── Inside/outside interacting with other floats (regression: EffectiveFloatSide) ───

        [Fact]
        public async Task FloatInside_ResolvedToLeft_StacksBelowAnOtherwiseTooWidePlainLeftFloat()
        {
            // Regression: CssLayoutEngine.FloatBoxLeft's collision scan (DomUtils.
            // GetFirstIntersectingFloatBox) and its own boundary-narrowing switch both keyed on the raw
            // Float.Value, which stays Inside/Outside forever - so a plain float:left already occupying
            // the row was never recognized as an obstacle by a later float:inside float that resolves to
            // the same (left) side, and CssBox.EffectiveFloatSide (the resolved Left/Right) is what both
            // now read instead. Two floats sharing one resolved side and together too wide for the row is
            // the same "stacks below" shape ordinary float:left + float:left already proves elsewhere.
            var html = Wrap(@"
                <div id='lft' style='float:left; width:350pt; height:40pt;'></div>
                <div id='i' style='float:inside; width:300pt; height:20pt;'></div>");

            var (root, _) = await LayoutAsync(html);
            var lft = FindById(root, "lft")!;
            var i = FindById(root, "i")!;

            // On page 1 (odd/recto), inside resolves to left - 350pt + 300pt = 650pt, wider than the
            // ~555pt content band (595 - 2*20 margin), so the second one must drop below the first
            // instead of overlapping it.
            Assert.True(i.Location.Y >= lft.ActualBottom - 0.5,
                $"the inside float (resolved to left) must stack below the plain left float instead of overlapping it, i.Y={i.Location.Y}, lft.bottom={lft.ActualBottom}");
        }

        [Fact]
        public async Task FloatInside_WithClear_ReResolvesEffectiveSideAtTheClearedPosition()
        {
            // Regression: FloatBox's post-clear re-placement only recognized literal Floating.Left/Right,
            // so an inside/outside float with a clear value kept ClearBox's clearance Y (flush at
            // ClientLeft) instead of being re-floated to its resolved side.
            var html = Wrap(@"
                <div id='lft' style='float:left; width:100pt; height:40pt;'></div>
                <div id='i' style='float:inside; clear:left; width:80pt; height:20pt;'></div>");

            var (root, container) = await LayoutAsync(html);
            var i = FindById(root, "i")!;

            // Page 1 is recto (odd) - inside resolves to left, i.e. flush with the content's own left
            // edge, not merely "not overlapping the left float" (clearance alone would also achieve that
            // by dropping below it, which would NOT prove the re-placement ran).
            var pageLeft = container.MarginLeft;
            Assert.Equal(pageLeft, i.Location.X, 1.0);
        }

        // ─── Bottom floats stacking outside an existing footnote area ───

        [Fact]
        public async Task FloatBottom_StacksAboveAnExistingFootnoteArea_NotFlushWithThePhysicalEdge()
        {
            var html = Wrap(@"
                <p>Text with a note<sup style='float:footnote'>A footnote body with some real content in it.</sup>.</p>
                <div id='f' style='float:bottom; width:100pt; height:40pt;'></div>");

            var (root, container) = await LayoutAsync(html);
            var f = FindById(root, "f")!;

            var footnoteHeight = Assert.Single(container.FootnoteAreaHeightsBySlot).Value;
            Assert.True(footnoteHeight > 0);

            // The float's own bottom must land above the footnote area's own top (pageBottom -
            // footnoteHeight), not flush with the physical page bottom underneath/overlapping it.
            Assert.Equal(container.PageBottomOf(0) - footnoteHeight, f.ActualBottom, 0.5);
        }

        [Fact]
        public async Task FloatTopBottom_FallsBackToBottom_WhenAnExistingBottomFloatLeavesNoRoomAtTop()
        {
            // Regression: the top-bottom "does it fit at top" check compared the candidate against the
            // whole band height, ignoring floats/footnotes already claiming the BOTTOM edge of the same
            // slot - so a bottom float tall enough to leave no real room anywhere still let a later
            // top-bottom float land at the top, overlapping it.
            var html = Wrap(@"
                <div id='bottom' style='float:bottom; width:100pt; height:750pt;'></div>
                <div id='tb' style='float:top-bottom; width:100pt; height:60pt;'></div>");

            var (_, container) = await LayoutAsync(html, pageHeight: 842);

            Assert.True(container.BottomFloatAreaHeightsBySlot.TryGetValue(0, out var bottomHeight));
            Assert.True(bottomHeight >= 809.9, $"expected both floats' heights (750+60) reserved at the bottom, was {bottomHeight}");
            Assert.False(container.TopFloatAreaHeightsBySlot.ContainsKey(0),
                "the top-bottom float must have fallen back to the bottom edge, not claimed the top");
        }

        // ─── Flex/grid items: float has no effect (css-flexbox-1 §4 / css-grid-2 §6) ───

        [Theory]
        [InlineData("top")]
        [InlineData("inside")]
        public async Task PageFloatValue_OnAFlexItem_IsCoercedToNone_NotDroppedOrDoublyTreated(string keyword)
        {
            // Regression: DomParser's flex/grid item normalization only coerced float: left/right to none;
            // float: top/inside (and the other new values) kept IsFloated/IsPageFloated true, which the
            // flex engine's own item-collection filter (IsExcludedFromFlow) then silently dropped instead
            // of laying out as an ordinary item per spec.
            var html = Wrap($@"
                <div style='display:flex; width:200pt;'>
                    <div id='item' style='float:{keyword}; width:50pt; height:20pt;'></div>
                </div>");

            var (root, _) = await LayoutAsync(html);
            var item = FindById(root, "item")!;

            Assert.Equal(PeachPDF.CSS.Floating.None, item.Float.Value);
            Assert.True(item.ActualRight - item.Location.X > 0, "the item must have real, laid-out geometry, not be dropped");
        }
    }
}
