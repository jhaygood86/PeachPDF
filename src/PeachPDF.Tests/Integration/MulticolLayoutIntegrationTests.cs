using PeachPDF.Text;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    public class MulticolLayoutIntegrationTests
    {
        // ─── Establishing a multicol context ───────────────────────────────────────

        [Fact]
        public async Task ColumnCount_EstablishesMultiColumnContext()
        {
            var box = await FindByIdAsync("<div id='mc' style='column-count:2'></div>", "mc");
            Assert.True(box.EstablishesMultiColumnContext);
        }

        [Fact]
        public async Task ColumnWidth_EstablishesMultiColumnContext()
        {
            var box = await FindByIdAsync("<div id='mc' style='column-width:100px'></div>", "mc");
            Assert.True(box.EstablishesMultiColumnContext);
        }

        [Fact]
        public async Task NoColumnProperties_DoesNotEstablishMultiColumnContext()
        {
            var box = await FindByIdAsync("<div id='mc'></div>", "mc");
            Assert.False(box.EstablishesMultiColumnContext);
        }

        [Fact]
        public async Task NoChildren_ActualBottomEqualsLocationPlusBoxSizeIncludedHeight()
        {
            // A genuinely childless box (Boxes.Count == 0) never reaches CssLayoutEngineColumns at all -
            // the CssBox.PerformLayoutImp dispatch gate itself requires Boxes.Count > 0. To reach
            // Layout's own internal "no substantive children" branch, the box needs a child that passes
            // the outer dispatch gate but gets filtered out by Layout's own Display/IsOutOfFlow/
            // IsSpaceOrEmpty check - a display:none child does exactly that.
            var box = await FindByIdAsync(
                "<div id='mc' style='columns:2; padding:5px'><span style='display:none'>hidden</span></div>", "mc");

            Assert.Equal(box.Location.Y + box.ActualBoxSizeIncludedHeight, box.ActualBottom);
        }

        [Fact]
        public async Task ImageWithBlockSibling_InMulticolContainer_ActuallyPaints()
        {
            // CssLayoutEngineColumns.Layout uses the exact same (b.HtmlTag != null || !b.IsSpaceOrEmpty)
            // item filter as CssLayoutEngineFlex - a replaced element sharing a multicol container with a
            // block-level sibling gets wrapped in an anonymous box (DomParser.CorrectInlineBoxesParent,
            // per CSS2.1 §9.2.1.1's "block container either contains only block-level boxes, or
            // establishes an inline formatting context") and was dropped by the same shallow
            // IsSpaceOrEmpty check the flex fix addressed - see FlexReplacedElementIntegrationTests for
            // the full root-cause writeup.
            const string pngDataUri =
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";
            var html = Wrap($"""
                <div id='mc' style='columns:2; width:200px'>
                    <img id='i' src='{pngDataUri}' width='40' height='30'>
                    <div class='title'>Title</div>
                </div>
                """);
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);

            var g = new PeachPDF.Tests.TestSupport.TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var call = Assert.Single(g.DrawImageCalls);
            // HTML width/height attributes are px lengths (TranslateAttributes -> "40px"/"30px"),
            // and 1px = 0.75pt, so the painted destination rect is 40*0.75 x 30*0.75 layout units.
            Assert.Equal(30, call.DestRect.Width, 1);
            Assert.Equal(22.5, call.DestRect.Height, 1);
        }

        [Fact]
        public async Task ColumnCount1_DegeneratesToOrdinaryBlockFlow()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:1; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            Assert.Equal(2, items.Count);
            // Ordinary block flow: both items stack vertically at the same X, second directly below first.
            Assert.Equal(items[0].Location.X, items[1].Location.X);
            Assert.True(items[1].Location.Y > items[0].Location.Y);
            Assert.Equal(mc.ActualBottom, items[1].ActualBottom);
        }

        [Fact]
        public async Task ColumnCountAndWidthBothSpecified_CountActsAsMaximum()
        {
            // column-count is a maximum: never more columns than fit at >= column-width, so a
            // wide column-width here caps the actual column count below the requested count.
            var html = Wrap(@"
                <div id='mc' style='column-count:5; column-width:80px; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            // 200px / 80px => at most 2 columns fit, even though column-count asked for 5.
            var distinctX = items.Select(i => System.Math.Round(i.Location.X)).Distinct().ToList();
            Assert.True(distinctX.Count <= 2);
        }

        // ─── Basic column geometry ──────────────────────────────────────────────────

        [Fact]
        public async Task ColumnCount2_SplitsChildrenAcrossTwoXPositions()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px'>
                    <div class='item' style='height:400px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            Assert.Equal(4, items.Count);
            // First item fills column 1 alone (too tall for anything else to join it there);
            // remaining items must land in column 2, at a greater X than column 1.
            var distinctX = items.Select(i => System.Math.Round(i.Location.X)).Distinct().ToList();
            Assert.Equal(2, distinctX.Count);
            Assert.Contains(items, i => System.Math.Abs(i.Location.X - mc.ClientLeft) < 0.5);
            Assert.Contains(items, i => i.Location.X > mc.ClientLeft + 50);
        }

        [Fact]
        public async Task ColumnGapUnset_ResolvesToOneEmGap()
        {
            // No column-gap declared: the shared field's initial value "normal" (CSS Box Alignment
            // Module Level 3 §8.3) resolves to a UA-convention ~1em gap for multicol, matching
            // real-world browser behavior.
            var html = Wrap(@"
                <div id='mc' style='columns:2; width:200pt; font-size:16px'>
                    <div class='item' style='height:400px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            // 1em = the box's own font-size, eagerly resolved to layout points (16px * 0.75pt/px = 12pt).
            const double expectedGap = 12;
            var expectedColumnWidth = (200 - expectedGap) / 2;
            Assert.Equal(mc.ClientLeft + expectedColumnWidth + expectedGap, items[1].Location.X, 1);
        }

        [Fact]
        public async Task ColumnGapExplicitZero_ProducesNoGapBetweenColumns()
        {
            // Regression: before this fix, an explicit "column-gap: 0" was indistinguishable from the
            // unset default (both stored as the literal string "0") and was incorrectly substituted
            // with ~1em by CssLayoutEngineColumns.Layout's old sentinel check.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200pt; font-size:16px'>
                    <div class='item' style='height:400px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            var expectedColumnWidth = 200 / 2.0;
            Assert.Equal(mc.ClientLeft + expectedColumnWidth, items[1].Location.X, 1);
        }

        [Fact]
        public async Task ColumnWidth_ProducesMultipleColumnsAutomatically()
        {
            var html = Wrap(@"
                <div id='mc' style='column-width:100px; column-gap:0; width:320px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            // 320px / 100px column-width ≈ 3 columns fit, so each item should land in its own column
            var distinctX = items.Select(i => System.Math.Round(i.Location.X)).Distinct().ToList();
            Assert.True(distinctX.Count >= 2, "expected column-width to produce more than one column");
        }

        [Fact]
        public async Task ColumnRule_ProducesOneSegmentPerGap()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:3; column-rule: 2px solid black; width:300px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            Assert.NotNull(mc.ColumnRuleSegments);
            // 3 columns -> 2 internal gaps, one row used -> 2 segments
            Assert.Equal(2, mc.ColumnRuleSegments!.Count);
        }

        [Fact]
        public async Task ColumnRuleNone_HasZeroActualWidth()
        {
            // Column-rule geometry is still computed (so painting logic has segments to skip based on
            // width), but the default column-rule-style is "none", which must resolve to zero actual
            // width - the same convention CssBox already uses for border-*-style: none.
            var html = Wrap(@"
                <div id='mc' style='columns:2; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            Assert.Equal(0, mc.ActualColumnRuleWidth);
        }

        // ─── column-fill: balance ───────────────────────────────────────────────────

        [Fact]
        public async Task ShortContent_BalancesAcrossAllColumns()
        {
            // Regression: content short enough to fit a single column on one page must still be
            // spread across every column (column-fill defaults to "balance"), not left piled into
            // column 1 with the rest sitting empty.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            Assert.Contains(items, i => i.Location.X > mc.ClientLeft + 50);
        }

        // ─── Fragmentation correctness (no overlap) ─────────────────────────────────

        [Fact]
        public async Task OversizedForcedChild_DoesNotOverlapSubsequentContent()
        {
            // Regression for a real bug: when the first child dropped into a column is taller than
            // the column's remaining page budget, it's still placed in full (children are never
            // split) - but every subsequent child must be pushed at or past that child's actual
            // bottom, not the column's nominal page boundary, or the two visibly overlap.
            var html = Wrap(@"
                <div style='height:70px'></div>
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                </div>");
            // Page content height of 100 leaves only ~30px on page 0 for the .mc container to start
            // in - far less than the first item's 80px height.
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var items = FindAllByClass(root, "item");

            Assert.Equal(5, items.Count);
            AssertNoOverlaps(items);
        }

        [Fact]
        public async Task OversizedForcedChild_ColumnRuleDoesNotOverlapContent()
        {
            var html = Wrap(@"
                <div style='height:70px'></div>
                <div id='mc' style='columns:2; column-rule:1px solid black; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var items = FindAllByClass(root, "item");

            Assert.NotNull(mc.ColumnRuleSegments);
            foreach (var (_, top, bottom) in mc.ColumnRuleSegments!)
            {
                Assert.True(bottom >= top);
            }
            AssertNoOverlaps(items);
        }

        [Fact]
        public async Task ColumnRule_ActuallyPainted_DrawsALine()
        {
            // Regression coverage per this repo's painting-test convention: a passing layout-level
            // assertion on ColumnRuleSegments alone doesn't prove PaintColumnRules ever runs or issues a
            // real draw call - drive the real Paint() pipeline and record what actually reached RGraphics.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-rule:1px solid black; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            var spy = new DrawLineSpyGraphics();
            FragmentPaintHarness.PaintBox(container, mc, spy);

            Assert.True(spy.DrawLineCallCount > 0);
        }

        [Theory]
        [InlineData("dashed")]
        [InlineData("dotted")]
        public async Task ColumnRule_DashedOrDotted_StillPaintsALine(string style)
        {
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-rule:1px {style} black; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            var spy = new DrawLineSpyGraphics();
            FragmentPaintHarness.PaintBox(container, mc, spy);

            Assert.True(spy.DrawLineCallCount > 0);
        }

        // ─── column-fill: balance precision (binary-search solver) ─────────────────

        [Fact]
        public async Task ColumnFillBalance_FindsTighterHeightThanNaiveEvenSplitEstimate()
        {
            // 6 items [50,50,50,40,40,40] (total 270) into 3 columns. The old single-formula estimate
            // (total/columnCount = 90) is provably too short: sequential first-fit at height 90 can only
            // place 4 of the 6 items (items 5/6 don't fit in any of the 3 columns at that height), forcing
            // the remaining 2 into a synthetic next "row" - which, since row height here is the page
            // height (1000), means they'd land ~1000 units below the rest. The true minimum height that
            // fits all 6 in 3 columns is 100 (verified by hand: col0=[50,50], col1=[50,40], col2=[40,40]).
            // The improved binary-search solver must find that and keep everything on the first row.
            var html = Wrap(@"
                <div id='mc' style='columns:3; column-gap:0; width:300pt'>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            Assert.Equal(6, items.Count);
            // None of the 6 items should have been pushed onto a synthetic next "row" (which, at this
            // page height, would put them ~1000 units below the first row) - the old naive estimate
            // would fail this assertion for items 5 and 6.
            Assert.All(items, i => Assert.True(i.ActualBottom < 200,
                $"expected all items to stay within the first balanced row, but one ended at Y={i.ActualBottom}"));
        }

        [Fact]
        public async Task ColumnFillBalance_StillNeverSplitsAWholeChild_Regression()
        {
            // The binary-search solver must preserve the existing "whole child, never split" model -
            // every item keeps its full natural height intact regardless of which column it lands in.
            var html = Wrap(@"
                <div id='mc' style='columns:3; column-gap:0; width:300pt'>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            var expectedHeights = new[] { 50.0, 50.0, 50.0, 40.0, 40.0, 40.0 };
            for (var i = 0; i < items.Count; i++)
            {
                Assert.Equal(expectedHeights[i], items[i].ActualBottom - items[i].Location.Y, 1);
            }
            AssertNoOverlaps(items);
        }

        // ─── Phase-1/Phase-2 side-effect tracking (regression for stale Y after re-banding) ─

        [Fact]
        public async Task NamedString_Y_TracksTheColumnItLandsIn()
        {
            // Same re-banding shape as OversizedForcedChild_DoesNotOverlapSubsequentContent: item i1
            // alone claims column 1 (too tall to share), forcing i2/i3 into column 2 - a real move away
            // from their Phase-1 virtual (single-tall-column) position.
            var html = Wrap(@"
                <style>.item { string-set: entry content(text); }</style>
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='i1' style='height:80px'>one</div>
                    <div class='item' id='i2' style='height:10px'>two</div>
                    <div class='item' id='i3' style='height:10px'>three</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;

            // Confirm the fixture actually forces re-banding before trusting the Y assertion below.
            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be re-banded into column 2");

            var namedString = Assert.Single(i2.NamedStrings.Values);
            Assert.Equal(i2.Location.Y, namedString.Y, 1);

            var documentEntry = container.NamedStrings.Single(ns => ns.Name == "entry" && ns.Value == "two");
            Assert.Equal(i2.Location.Y, documentEntry.Y, 1);
        }

        [Fact]
        public async Task NamedPageElement_RegistersOnceForThePageItLandsIn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' id='i2' style='height:10px; page:chapter'></div>
                    <div class='item' style='height:10px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var i2 = FindById(root, "i2")!;

            // A named-page transition forces a *page* break (CSS Paged Media 3 §3), and a page break is
            // not this container's to satisfy - none of its columns is on another page. It used to arrive
            // one column over instead.
            Assert.True(container.PageIndexOf(i2.Location.Y) > 0, "expected i2 to open a page of its own");

            // Exactly one entry is the regression guard. The box is laid out twice - once by the
            // measurement pass that sizes the fill, once for real - and once more again if a break
            // re-places it, and each placement registers. Its Y is the top of the pagination slot
            // the box lands on, which is the attribution NamedPageRegistrationY defines rather than the
            // box's own coordinate.
            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
            Assert.Equal(
                container.PageTopOf(container.SlotStartingAt(i2.Location.Y)),
                registered.Y, 1);
        }

        [Fact]
        public async Task NamedString_InlineTarget_DoesNotAccumulateStaleEntriesAcrossReflow()
        {
            // Same re-banding shape as NamedString_Y_TracksTheColumnItLandsIn, but string-set now
            // targets an inline <b> reached only through FlowBox, not a block box's own
            // PerformLayoutPrologue - the one path that already withdraws a stale registration before
            // re-registering. Without the same guard in FlowBox, each Phase-1/Phase-2 re-layout (and
            // any column-fill retry) leaves one more orphaned NamedString behind.
            var html = Wrap(@"
                <style>.item b { string-set: entry content(text); }</style>
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='i1' style='height:80px'>one</div>
                    <div class='item' id='i2' style='height:10px'><b id='b2'>two</b></div>
                    <div class='item' id='i3' style='height:10px'>three</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;
            var b2 = FindById(root, "b2")!;

            // Confirm the fixture actually forces re-banding before trusting the assertions below.
            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be re-banded into column 2");

            var namedString = Assert.Single(b2.NamedStrings.Values);
            Assert.Equal(i2.Location.Y, namedString.Y, 1);

            // The regression guard: without unregistering a stale entry before re-registering, this
            // accumulates one orphaned "entry"/"two" per re-layout instead of staying at exactly one.
            var documentEntry = Assert.Single(container.NamedStrings, ns => ns.Name == "entry" && ns.Value == "two");
            Assert.Equal(i2.Location.Y, documentEntry.Y, 1);
        }

        [Fact]
        public async Task NamedPageElement_InlineTarget_DoesNotAccumulateStaleEntriesAcrossReflow()
        {
            // Same re-banding fixture as NamedString_InlineTarget_DoesNotAccumulateStaleEntriesAcrossReflow,
            // exercising the `page` registration's twin unregister-before-register gap in the same
            // FlowBox code path. (Unlike the block-level page property, an inline target's `page`
            // value does not force a break of its own - css2.1 §13.2's page-transition break check
            // runs against block-level boxes - so this only asserts against re-registration, not a
            // page transition.)
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='i1' style='height:80px'>one</div>
                    <div class='item' id='i2' style='height:10px'><b id='b2' style='page:chapter'>two</b></div>
                    <div class='item' id='i3' style='height:10px'>three</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;

            // Confirm the fixture actually forces re-banding before trusting the assertion below.
            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be re-banded into column 2");

            // Exactly one entry is the regression guard - the inline path used to leave one orphaned
            // NamedPageElement behind per re-layout instead of withdrawing the earlier one first.
            Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
        }

        [Fact]
        public async Task NamedString_InlineFlexTarget_DoesNotAccumulateStaleEntriesAcrossReflow()
        {
            // Same re-banding fixture as NamedString_InlineTarget_DoesNotAccumulateStaleEntriesAcrossReflow,
            // but through FlowBox's separate InlineFlex branch (CssLayoutEngine.cs), which finalizes
            // string-set eagerly rather than splitting it across entry/exit like the plain-inline case -
            // and has the identical missing unregister-before-register guard.
            var html = Wrap(@"
                <style>.item span { string-set: entry content(text); }</style>
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='i1' style='height:80px'>one</div>
                    <div class='item' id='i2' style='height:10px'><span id='b2' style='display:inline-flex'>two</span></div>
                    <div class='item' id='i3' style='height:10px'>three</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;

            // Confirm the fixture actually forces re-banding before trusting the assertions below.
            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be re-banded into column 2");

            // The regression guard: without unregistering a stale entry before re-registering, this
            // accumulates one orphaned "entry"/"two" per re-layout instead of staying at exactly one.
            Assert.Single(container.NamedStrings, ns => ns.Name == "entry" && ns.Value == "two");
        }

        [Fact]
        public async Task NamedPageElement_InlineFlexTarget_DoesNotAccumulateStaleEntriesAcrossReflow()
        {
            // Same re-banding fixture, exercising the `page` registration's twin gap in the same
            // InlineFlex branch of FlowBox.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='i1' style='height:80px'>one</div>
                    <div class='item' id='i2' style='height:10px'><span id='b2' style='display:inline-flex; page:chapter'>two</span></div>
                    <div class='item' id='i3' style='height:10px'>three</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;

            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be re-banded into column 2");

            // Exactly one entry is the regression guard - the InlineFlex branch used to leave one
            // orphaned NamedPageElement behind per re-layout instead of withdrawing the earlier one first.
            Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
        }

        [Fact]
        public async Task ActualSize_MatchesRealFinalGeometry_NotInflatedPhase1Height()
        {
            // 4 items of 80px each into 2 columns at a 100px page height. Phase 1's un-banded virtual
            // pass would stack all 4 directly (4*80=320); the real, re-banded/paginated result is far
            // shorter. Since this multicol container is the only content in the document, nothing
            // subsequent can paper over an inflated Phase-1 contribution to ActualSize the way later
            // real content does for a non-last chapter in the real dictionary document.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:80px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;

            // ActualSize must cover the real content (mc's own re-banded/paginated bottom, plus whatever
            // legitimate margin collapse the surrounding body contributes) but must NOT be inflated all
            // the way up toward Phase 1's un-banded virtual height (stacking all 4 80px items directly
            // would give 320) - that's the actual defect under test here.
            Assert.True(container.ActualSize.Height >= mc.ActualBottom - root.Location.Y,
                $"expected ActualSize.Height to cover real content ({mc.ActualBottom - root.Location.Y}), got {container.ActualSize.Height}");
            Assert.True(container.ActualSize.Height < 250,
                $"expected ActualSize.Height well under Phase 1's un-banded 320, got {container.ActualSize.Height}");
        }

        // ─── column-span: all (#602) ────────────────────────────────────────────────

        [Fact]
        public async Task ColumnSpanAll_SpansTheFullContainerWidth_BetweenTwoColumnRuns()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='pre' style='height:20px'></div>
                    <div class='pre' style='height:20px'></div>
                    <div id='span' style='column-span:all; height:15px'></div>
                    <div class='post' style='height:20px'></div>
                    <div class='post' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;
            var span = FindById(root, "span")!;
            var pre = FindAllByClass(root, "pre");
            var post = FindAllByClass(root, "post");

            Assert.Equal(2, pre.Count);
            Assert.Equal(2, post.Count);

            // The span sits at the container's own left edge and reaches its own right edge - the full
            // content width, not one column's - rather than at some column's own narrower position.
            Assert.Equal(mc.ClientLeft, span.Location.X, 1);
            Assert.Equal(mc.ActualRight, span.ActualRight, 1);

            // Both runs still split across the two columns on either side of the span.
            Assert.Contains(pre, i => i.Location.X > mc.ClientLeft + 50);
            Assert.Contains(post, i => i.Location.X > mc.ClientLeft + 50);

            // The span sits below both pre-run items and above both post-run items.
            Assert.All(pre, i => Assert.True(i.ActualBottom <= span.Location.Y + 0.5));
            Assert.All(post, i => Assert.True(i.Location.Y >= span.ActualBottom - 0.5));

            AssertNoOverlaps([.. pre, span, .. post]);
        }

        [Fact]
        public async Task ColumnSpanAll_EachRunBalancesIndependently()
        {
            // 4 equal items before the span, 4 equal items after - column-fill: balance (the default)
            // must spread each run's own 4 items 2-and-2 across the container's two columns, not treat
            // the whole 8-item flow as one balancing problem that happens to have a full-width box in
            // the middle of it.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='pre' style='height:20px'></div>
                    <div class='pre' style='height:20px'></div>
                    <div class='pre' style='height:20px'></div>
                    <div class='pre' style='height:20px'></div>
                    <div id='span' style='column-span:all; height:15px'></div>
                    <div class='post' style='height:20px'></div>
                    <div class='post' style='height:20px'></div>
                    <div class='post' style='height:20px'></div>
                    <div class='post' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;
            var pre = FindAllByClass(root, "pre");
            var post = FindAllByClass(root, "post");

            Assert.Equal(2, pre.Count(i => System.Math.Abs(i.Location.X - mc.ClientLeft) < 0.5));
            Assert.Equal(2, pre.Count(i => i.Location.X > mc.ClientLeft + 50));
            Assert.Equal(2, post.Count(i => System.Math.Abs(i.Location.X - mc.ClientLeft) < 0.5));
            Assert.Equal(2, post.Count(i => i.Location.X > mc.ClientLeft + 50));
        }

        [Fact]
        public async Task ColumnSpanAll_ColumnRule_OneSegmentPerRun_NeverThroughTheSpan()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-rule:1px solid black; column-gap:0; width:200px'>
                    <div class='pre' style='height:20px'></div>
                    <div class='pre' style='height:20px'></div>
                    <div id='span' style='column-span:all; height:15px'></div>
                    <div class='post' style='height:20px'></div>
                    <div class='post' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;
            var span = FindById(root, "span")!;

            Assert.NotNull(mc.ColumnRuleSegments);
            // 2 columns -> 1 internal gap per run; pre-run and post-run -> 2 segments total, none of
            // them contributed by the span itself.
            Assert.Equal(2, mc.ColumnRuleSegments!.Count);

            Assert.All(mc.ColumnRuleSegments!, seg =>
                Assert.False(seg.Top < span.ActualBottom - 0.5 && span.Location.Y < seg.Bottom - 0.5,
                    $"rule segment [{seg.Top},{seg.Bottom}] overlaps the span [{span.Location.Y},{span.ActualBottom}]"));
        }

        [Fact]
        public async Task ColumnSpanAll_LeadingSpan_StartsAtTheContainersOwnTop()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div id='span' style='column-span:all; height:15px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;
            var span = FindById(root, "span")!;
            var items = FindAllByClass(root, "item");

            Assert.Equal(mc.ClientTop, span.Location.Y, 1);
            Assert.Equal(mc.ClientLeft, span.Location.X, 1);
            Assert.Equal(mc.ActualRight, span.ActualRight, 1);
            Assert.All(items, i => Assert.True(i.Location.Y >= span.ActualBottom - 0.5));
            AssertNoOverlaps([span, .. items]);
        }

        [Fact]
        public async Task ColumnSpanAll_TrailingSpan_EndsTheColumnFlow()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div id='span' style='column-span:all; height:15px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;
            var span = FindById(root, "span")!;
            var items = FindAllByClass(root, "item");

            Assert.Equal(mc.ClientLeft, span.Location.X, 1);
            Assert.Equal(mc.ActualRight, span.ActualRight, 1);
            Assert.All(items, i => Assert.True(i.ActualBottom <= span.Location.Y + 0.5));
            Assert.Equal(mc.ActualBottom, span.ActualBottom, 1);
            AssertNoOverlaps([.. items, span]);
        }

        [Fact]
        public async Task ColumnSpanAll_TwoConsecutiveSpans_BothFullWidthStackedVertically()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div id='span1' style='column-span:all; height:15px'></div>
                    <div id='span2' style='column-span:all; height:12px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;
            var span1 = FindById(root, "span1")!;
            var span2 = FindById(root, "span2")!;

            Assert.Equal(mc.ClientLeft, span1.Location.X, 1);
            Assert.Equal(mc.ClientLeft, span2.Location.X, 1);
            Assert.Equal(mc.ActualRight, span1.ActualRight, 1);
            Assert.Equal(mc.ActualRight, span2.ActualRight, 1);
            Assert.True(span2.Location.Y >= span1.ActualBottom - 0.5, "expected span2 stacked below span1");
            AssertNoOverlaps([span1, span2]);
        }

        [Fact]
        public async Task ColumnSpanAll_WithColumnCount1_DegeneratesToOrdinaryBlockFlow()
        {
            // With only one column region already, spanning is a no-op - the columnCount <= 1 branch
            // never partitions into segments at all, so this must behave exactly like an ordinary
            // block-flow sibling.
            var html = Wrap(@"
                <div id='mc' style='columns:1; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div id='span' style='column-span:all; height:15px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var span = FindById(root, "span")!;

            Assert.Equal(2, items.Count);
            Assert.Equal(items[0].Location.X, span.Location.X, 1);
            Assert.Equal(items[0].Location.X, items[1].Location.X, 1);
            Assert.True(span.Location.Y > items[0].Location.Y);
            Assert.True(items[1].Location.Y > span.Location.Y);
        }

        [Fact]
        public async Task ColumnSpanAll_RetryInThePostSpanRun_DoesNotEraseThePreSpanRunsFragments()
        {
            // Regression for the _nested fragmentainer-bookkeeping hazard the segmented design
            // introduced: FragmentEmitter._nested is keyed by (columnsBox, slot), and before this fix a
            // later run's own balance-retry cleared the whole key - including an earlier run's
            // already-finished, correct columns sharing it. An oversized first child in the post-span
            // run (too tall for the remaining page budget, forced whole per this engine's atomic-child
            // model - the same shape as OversizedForcedChild_ColumnRuleDoesNotOverlapContent) reliably
            // drives that run's own target-growth retry.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-rule:1px solid black; column-gap:0; width:200px'>
                    <div class='pre' id='p1' style='height:2px'></div>
                    <div id='span' style='column-span:all; height:2px'></div>
                    <div class='post' style='height:80px'></div>
                    <div class='post' style='height:10px'></div>
                    <div class='post' style='height:10px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 30);
            var mc = FindById(root, "mc")!;
            var p1 = FindById(root, "p1")!;
            var span = FindById(root, "span")!;
            var post = FindAllByClass(root, "post");

            Assert.Equal(3, post.Count);
            AssertNoOverlaps([p1, span, .. post]);

            // The pre-span run's own column was recorded before the post-span run's retry - if the
            // retry had cleared the whole (mc, slot) entry instead of just its own tail, this box would
            // have no fragment at all rather than a real, non-degenerate one.
            var fragment = FragmentPaintHarness.FragmentOf(container, p1, page: 0);
            Assert.True(fragment.Rect.Width > 0 && fragment.Rect.Height > 0);

            Assert.NotNull(mc.ColumnRuleSegments);
            Assert.NotEmpty(mc.ColumnRuleSegments!);
        }

        [Fact]
        public async Task ColumnSpanAll_PostSpanRunOverflowingThePage_ResumesOnlyThatRun()
        {
            // Real text in each item, so every page it lands on carries printable content and is
            // materialized (see ContentBeyondTheLastColumn_ResumesOnTheNextPage above for why).
            var postItems = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='post' id='p{i}' style='height:40px'>Item {i}</div>"));
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div id='pre' style='height:20px'>Pre</div>
                    <div id='span' style='column-span:all; height:20px'>Span</div>
                    {postItems}
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 120);

            var pre = FindById(root, "pre")!;
            var span = FindById(root, "span")!;
            var post = FindAllByClass(root, "post");

            Assert.Equal(12, post.Count);
            AssertNoOverlaps([pre, span, .. post]);

            Assert.True(container.FragmentainerPasses > 1, "expected the post-span run to have resumed");

            // The pre-span run and the span itself both finished on the first page - only the post-span
            // run's overflow should have carried on to a later one.
            Assert.Equal(0, container.PageIndexOf(pre.Location.Y));
            Assert.Equal(0, container.PageIndexOf(span.Location.Y));
            Assert.Contains(post, i => container.PageIndexOf(i.Location.Y) > 0);

            // Two columns maintained across every page the post-span run occupies.
            var xs = post.Select(b => System.Math.Round(b.Location.X)).Distinct().ToList();
            Assert.Equal(2, xs.Count);
        }

        [Fact]
        public async Task ColumnSpanAll_SpanTallerThanOnePage_ContinuesRatherThanDroppingContent()
        {
            // Regression: the break-after check that ends a span's own fill right after it (so a run
            // opens fresh for whatever follows) must not fire while the span itself still has a pending
            // continuation of its own - a span whose content does not fit one page already carries a
            // record naming where *it* resumes, and overwriting that with "resume at whatever follows
            // the span" instead of falling through to the ordinary propagation silently dropped
            // everything from the overflow point on.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div id='pre' style='height:10px'>Pre</div>
                    <div id='span' style='column-span:all'>
                        <p id='s1' style='height:60px'>S1</p>
                        <p id='s2' style='height:60px'>S2</p>
                        <p id='s3' style='height:60px'>S3</p>
                        <p id='s4' style='height:60px'>S4</p>
                    </div>
                    <div class='post' id='post1' style='height:20px'>Post1</div>
                    <div class='post' id='post2' style='height:20px'>Post2</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);

            var pre = FindById(root, "pre")!;
            var s1 = FindById(root, "s1")!;
            var s2 = FindById(root, "s2")!;
            var s3 = FindById(root, "s3")!;
            var s4 = FindById(root, "s4")!;
            var post = FindAllByClass(root, "post");

            Assert.Equal(2, post.Count);

            // None of the span's own children were silently dropped - each kept real, distinct geometry.
            Assert.All([s1, s2, s3, s4], s => Assert.True(s.ActualBottom > s.Location.Y));
            AssertNoOverlaps([pre, s1, s2, s3, s4, .. post]);

            // The span's own content spilled across more than one page.
            Assert.True(container.FragmentainerPasses > 1, "expected the span itself to have resumed");

            // The post-span run still lands after all of the span's content, wherever that ended up.
            Assert.All(post, p => Assert.True(p.Location.Y >= s4.ActualBottom - 0.5));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // Minimal RGraphics spy recording only DrawLine calls - per this repo's painting-test
        // convention (see SpyGraphics in TransformIntegrationTests.cs), used to prove PaintColumnRules
        // actually issues a real draw call rather than trusting a layout-level assertion alone.
        private sealed class DrawLineSpyGraphics : RGraphics
        {
            public int DrawLineCallCount { get; private set; }

            public DrawLineSpyGraphics() : base(new PdfSharpAdapter(), new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) => DrawLineCallCount++;

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, TextShapingFeatures? features = null) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override RSize MeasureString(string str, RFont font, TextShapingFeatures? features = null) => new(0, 12);
            public override int CountShapedGlyphs(string str, RFont font, TextShapingFeatures? features = null) => str?.Length ?? 0;
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = 0;
            }
            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, TextShapingFeatures? features = null) { }
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
            public override void DrawImage(RImage image, RRect destRect) { }
            public override void DrawPath(RPen pen, RGraphicsPath path) { }
            public override void DrawPath(RBrush brush, RGraphicsPath path) { }
            public override void DrawPolygon(RBrush brush, RPoint[] points) { }
            public override void Dispose() { }
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private async Task<CssBox> FindByIdAsync(string fragment, string id)
        {
            var (root, container) = await BuildAndLayout(Wrap(fragment), pageHeight: 1000);
            return FindById(root, id)!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html, double pageHeight)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            container.MarginTop = 0;
            container.MarginLeft = 0;
            container.MarginRight = 0;
            container.MarginBottom = 0;
            await container.SetHtml(html, null);

            var size = new XSize(400, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize  = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }

        private static List<CssBox> FindAllByClass(CssBox box, string className)
        {
            var results = new List<CssBox>();
            FindAllByClassRecursive(box, className, results);
            return results;
        }

        private static void FindAllByClassRecursive(CssBox box, string className, List<CssBox> results)
        {
            var val = box.HtmlTag?.TryGetAttribute("class", "");
            if (val != null && val.Split(' ').Contains(className, System.StringComparer.OrdinalIgnoreCase))
                results.Add(box);
            foreach (var child in box.Boxes)
                FindAllByClassRecursive(child, className, results);
        }

        // ─── A column as a real fragmentainer (css-break-3 §2) ─────────────────────

        // The whole point of the change: a column is a fragmentainer, so the break machinery inside one
        // asks the column rather than the page. Without that, break-inside had nothing to avoid a break
        // *in* - a column boundary was not a break at all - and the box was placed wherever the packing
        // loop put it.
        [Theory]
        [InlineData("break-inside: avoid")]
        [InlineData("break-inside: avoid-page")]
        public async Task BreakInsideAvoid_IsHonouredAtAColumnBoundary(string declaration)
        {
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:60px'></div>
                    <div class='item' id='keep' style='height:60px; {declaration}'>
                        <div style='height:30px'></div>
                        <div style='height:30px'></div>
                    </div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var mc = FindById(root, "mc")!;
            var keep = FindById(root, "keep")!;

            // It could not stay in column 1 without straddling its bottom, so it starts column 2 whole.
            Assert.True(keep.Location.X > mc.ClientLeft + 50,
                $"expected the avoid box to start the next column, it is at x={keep.Location.X}");
        }

        // §2 monolithic content - a scroll container - may not be broken by any user agent, and a column
        // boundary is a break like any other.
        [Fact]
        public async Task MonolithicContent_MovesWholeToTheNextColumn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:60px'></div>
                    <div class='item' id='card' style='height:60px; overflow:hidden'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var mc = FindById(root, "mc")!;
            var card = FindById(root, "card")!;

            Assert.True(card.Location.X > mc.ClientLeft + 50,
                $"expected the scroll container to move whole to the next column, it is at x={card.Location.X}");
        }

        // Every column shares one band, so a child starting a later column starts at the same top the
        // first column did rather than continuing below where the previous column ended.
        [Fact]
        public async Task AChildStartingALaterColumn_StartsAtTheColumnTop()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='a' style='height:60px'></div>
                    <div class='item' id='b' style='height:60px'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 1);
            Assert.True(b.Location.X > a.Location.X, "expected the second child in the next column");
        }

        // What the last column cannot hold is not dropped: it travels up the ordinary chain and the page
        // driver opens the next page, where the container resumes rather than starting over.
        [Fact]
        public async Task ContentBeyondTheLastColumn_ResumesOnTheNextPage()
        {
            // Real text in each, so every page they land on carries printable content and is
            // materialized - an empty box makes its page content-empty and CSS Paged Media 3 §3.2 drops
            // it, which says nothing about whether the column driver placed it there.
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='i{i}' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px'>{items}</div>");

            var (root, container) = await BuildAndLayout(html, pageHeight: 120);

            var placed = FindAllByClass(root, "item");
            Assert.Equal(12, placed.Count);
            AssertNoOverlaps(placed);

            // More than one page, and every item still somewhere in the tree with real geometry.
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "expected the container to span more than one page");
            Assert.True(container.FragmentainerPasses > 1, "expected it to have resumed rather than overflowed");

            // Two columns on every page it occupies, not one tall column on a later one.
            var xs = placed.Select(b => System.Math.Round(b.Location.X)).Distinct().ToList();
            Assert.Equal(2, xs.Count);
            Assert.All(placed, b => Assert.True(b.ActualBottom > b.Location.Y, "every item keeps a height"));
        }

        // ─── A balance retry inside a resumed pass (#374) ──────────────────────────

        // The fill is attempted more than once whenever `column-fill: balance` has to grow or re-derive
        // its target, and between attempts everything the abandoned one placed has to be undone. Skipping
        // that wholesale on a resumed pass - which is what "return if resumed" did, for the real reason
        // that children below the resume point hold earlier pages' geometry - also skipped the children at
        // and above it, which this pass is laying out for the first time. So the retry re-finalized the
        // abandoned attempt's own line boxes and CssLineBox.AssignRectanglesToBoxes threw
        // "An item with the same key has already been added".
        //
        // Ordinary auto-height paragraphs, because the shape needs the balance search to want a second
        // attempt on a *resumed* fragment, which is a function of the content's own heights - fixed-height
        // children pack too predictably to reach it.
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task BalanceRetryOnAResumedFragment_LaysOutRatherThanThrowing(int columnCount)
        {
            var (root, container) = await BuildAndLayout(ResumedBalanceRetryFixture(columnCount), pageHeight: 100);

            var items = FindAllByClass(root, "item");

            Assert.Equal(12, items.Count);
            Assert.True(container.FragmentainerPasses > 1, "expected the container to have resumed");
            Assert.All(items, item => Assert.True(item.ActualBottom > item.Location.Y,
                "every paragraph keeps a height of its own"));
            AssertNoOverlaps(items);
        }

        // The structural companion: whatever the undo does, it has to leave the box tree self-consistent.
        // A per-line rectangle outlives its line box unless the two are discarded together, and one that
        // does describes a fill that no longer exists - which the emitter reads as readily as the real
        // thing.
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task BalanceRetryOnAResumedFragment_LeavesNoRectangleKeyedByADiscardedLineBox(int columnCount)
        {
            var (root, _) = await BuildAndLayout(ResumedBalanceRetryFixture(columnCount), pageHeight: 100);

            foreach (var box in LayoutHarness.Descendants(root))
            {
                foreach (var line in box.Rectangles.Keys)
                {
                    Assert.True(line.OwnerBox.LineBoxes.Contains(line),
                        $"'{box}' still carries a rectangle for the discarded line '{line}'");
                }
            }
        }

        // The workhorse, because a word says both things at once. Claimed twice means the undo left a
        // word where the abandoned attempt put it and a later attempt that did not reach it again could
        // not say so - which is what marks every word of a discarded fill as awaiting placement again.
        // Claimed not at all means the undo went too far: the child at the resume point is *continuing*,
        // so discarding its earlier fragmentainers' lines along with this attempt's blanks a fragment an
        // earlier page already emitted. Each of the three guards fails one half or the other.
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task BalanceRetryOnAResumedFragment_KeepsEveryWordExactlyOnce(int columnCount)
        {
            var (root, container) = await BuildAndLayout(ResumedBalanceRetryFixture(columnCount), pageHeight: 100);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.Equal(claimed.Count, claimed.Distinct().Count());

            var authored = FindAllByClass(root, "item")
                .SelectMany(LayoutHarness.Descendants)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            Assert.All(authored, word => Assert.Contains(word, claimed));
        }

        /// <summary>
        /// Twelve auto-height paragraphs against a page too short to hold them, so the container spans
        /// several pages and resumes — the shape from
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/374">#374</see>.
        /// </summary>
        private static string ResumedBalanceRetryFixture(int columnCount)
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='i{i}'>Item {i} with a little text of its own</div>"));

            return Wrap($"<div id='mc' style='columns:{columnCount}; column-gap:0; width:200px'>{items}</div>");
        }

        // ─── A whole remaining tail reset across many pages (#573) ─────────────────

        // PassRewind.RollBackTo resets every one of a container's *remaining* children on every page it
        // resumes into, not just the handful that page's fill reaches - so a container with far more
        // children than fit on one page redundantly re-resets the same distant, untouched children again
        // on every earlier page before the fill finally reaches them (CssBox.ResetForRefill now skips a
        // child already sitting in that reset-and-untouched state - see its remarks). This is the
        // large-container shape that makes the redundancy actually add up: many more children than any
        // single page holds, spanning many resumed pages, so most children get reset - and, before the
        // fix, re-reset - several times over before the fill ever reaches them.
        [Fact]
        public async Task ManyChildrenAcrossManyResumedPages_KeepsEveryWordExactlyOnceAndOverlapsNone()
        {
            const int itemCount = 60;
            var items = string.Concat(Enumerable.Range(1, itemCount)
                .Select(i => $"<div class='item' id='i{i}'>Entry {i} with a little text of its own.</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px'>{items}</div>");

            var (root, container) = await BuildAndLayout(html, pageHeight: 100);

            // Confirms the fixture actually exercises the shape the fix targets: many more resumed pages
            // than the earlier 12-item fixtures reach, so most children are reset several times over
            // before the fill reaches them.
            Assert.True(container.FragmentainerPasses > 4,
                $"expected many resumed pages, got {container.FragmentainerPasses}");

            var placed = FindAllByClass(root, "item");
            Assert.Equal(itemCount, placed.Count);
            AssertNoOverlaps(placed);

            // The same double-claim/dropped-word guard as BalanceRetryOnAResumedFragment_KeepsEveryWordExactlyOnce,
            // at a scale where a child can be redundantly reset many times before the fill actually reaches it.
            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.Equal(claimed.Count, claimed.Distinct().Count());

            var authored = placed
                .SelectMany(LayoutHarness.Descendants)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            Assert.All(authored, word => Assert.Contains(word, claimed));
        }

        // ─── A resumption top landing exactly on a page boundary (#573) ────────────

        // CssLayoutEngineColumns.Layout resolved which page slot a resumed column starts filling with
        // the raw, boundary-ambiguous HtmlContainerInt.PageIndexOf (a coordinate exactly on a page
        // boundary is "both the end of one band and the start of the next" per its own remarks) instead
        // of the boundary-aware SlotStartingAt every other "fresh content-top edge" call site uses. A
        // resumed column's own top is exactly that edge, and it lands precisely on a page boundary by
        // construction (a genuinely new page always starts at its own designated top) - so with a page
        // height whose arithmetic doesn't round back to a clean multiple of itself (e.g. a millimeter
        // page size converted to points), PageIndexOf(boxTop) can resolve to the page being LEFT rather
        // than the one being entered. That page's own remaining budget then computes to zero, and every
        // later pass recomputes the exact same (wrong, un-advanced) boxTop forever: the container never
        // reaches a fresh page and instead piles every remaining word onto the one it already filled, at
        // the same frozen Y - visually, hundreds of entries stacked directly on top of each other with
        // the rest of the page left blank. Reproduced directly against the real dictionary.mhtml-shaped
        // page format (160mm x 200mm, which floating-point-converts to points messily, unlike Letter's
        // already-integer 612x792pt) - page count stayed frozen at exactly 4 from 500 through 4000
        // paragraphs, with per-page text length exploding from ~2000 to over 65000 characters, before the
        // fix. A non-round pageHeight below reproduces the same boundary mismatch in miniature.
        [Fact]
        public async Task ManyChildrenAtANonRoundPageHeight_PageCountGrowsAndNoTwoItemsOverlap()
        {
            const int itemCount = 400;
            var items = string.Concat(Enumerable.Range(1, itemCount)
                .Select(i => $"<div class='item' id='i{i}'>Entry {i} with a little text of its own.</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px'>{items}</div>");

            // 150mm worth of points (150 / 25.4 * 72) - the same messy-binary-fraction shape as the real
            // document's @page { size: 160mm 200mm }, deliberately not a round number of layout units.
            // Verified (against the pre-fix code) to actually land a resumed column's top exactly on this
            // specific page height's boundary: without the fix this fixture is stuck at 11 pages rather
            // than scaling with content.
            var (root, container) = await BuildAndLayout(html, pageHeight: 150.0 / 25.4 * 72);

            var placed = FindAllByClass(root, "item");
            Assert.Equal(itemCount, placed.Count);

            // The frozen-page bug left the container on a small, fixed number of pages regardless of how
            // much content remained; 400 items at this page height must span well beyond that. (The
            // threshold sits a bit below the column width this fixture actually gets, since "column-gap:
            // 0" now resolves to a genuine zero gap - see CssLayoutEngineColumns.Layout - rather than the
            // ~1em the pre-fix sentinel bug substituted, giving each column a little more width and so a
            // little more text per line.)
            var pageCount = placed.Select(b => container.PageIndexOf(b.Location.Y)).Distinct().Count();
            Assert.True(pageCount > 15, $"expected the container to span many pages, landed on {pageCount}");

            AssertNoOverlaps(placed);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.Equal(claimed.Count, claimed.Distinct().Count());

            var authored = placed
                .SelectMany(LayoutHarness.Descendants)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            Assert.All(authored, word => Assert.Contains(word, claimed));
        }

        private static IEnumerable<BoxFragment> FlattenFragments(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in FlattenFragments(child))
                {
                    yield return descendant;
                }
            }
        }

        // The invariant this was drafted as, and characterized as a boundary until a fragment carried
        // geometry of its own: a child continues into the next column rather than moving to it whole. What
        // used to make that impossible is that a box carries one Location, and columns sit side by side
        // inside a single page band - so both halves had the same document Y *and* the same X, with the
        // continuation's lines laid out over the ones already there.
        [Fact]
        public async Task AChildSplitsAcrossColumns_ItsLinesLandingInEach()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <p id='long'>One two three four five six seven eight nine ten eleven twelve thirteen
                    fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two
                    twenty-three twenty-four twenty-five twenty-six twenty-seven twenty-eight twenty-nine
                    thirty thirty-one thirty-two thirty-three thirty-four thirty-five thirty-six.</p>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            var mc = FindById(root, "mc")!;
            var longBox = FindById(root, "long")!;
            var words = LayoutHarness.Descendants(longBox).SelectMany(b => b.Words).ToList();

            var inFirstColumn = words.Where(w => w.Left < mc.ClientLeft + 100).ToList();
            var inSecondColumn = words.Where(w => w.Left >= mc.ClientLeft + 100).ToList();

            Assert.NotEmpty(inFirstColumn);
            Assert.NotEmpty(inSecondColumn);

            // The continuation starts at the top of its column rather than below where the first fragment
            // ended - each column is a fragmentainer, and a resumed fragment begins at its content edge.
            Assert.True(inSecondColumn.Min(w => w.Top) < inFirstColumn.Max(w => w.Top),
                "expected the continuation to start above the point the first column reached");
        }

        // The other half of splitting: the box itself moves to the column it continues in, and keeps the
        // inline size it was measured at (css-break-3 §2 gives a box one inline size across its fragments).
        [Fact]
        public async Task AChildContinuingIntoTheNextColumn_IsPlacedAtThatColumnsOrigin()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:20px; width:300px; column-fill:auto'>
                    <p id='long'>One two three four five six seven eight nine ten eleven twelve thirteen
                    fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two.</p>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            var mc = FindById(root, "mc")!;
            var longBox = FindById(root, "long")!;

            // columns:2 over a 225pt content box with a 15pt gap: 105pt columns, the second at +120pt.
            Assert.Equal(mc.ClientLeft + 120, longBox.Location.X, 1);
            Assert.Equal(105, longBox.ActualWidth, 1);
            Assert.Equal(mc.ClientTop, longBox.Location.Y, 1);
        }

        // A container that spans pages keeps the top it was placed at on the page it began, so its own
        // content edge names a coordinate the columns of a later page do not reach. Read as the destination
        // for a box continuing into the next column, it sent that continuation back to the container's very
        // first column - landing it on top of whatever legitimately sits there, which is what makes this an
        // overlap rather than a merely-misplaced box.
        [Fact]
        public async Task AChildContinuingIntoTheNextColumn_OnAPageAfterTheContainersFirst_StaysInThatPagesBand()
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='i{i}'>Item {i} with a little text of its own</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px'>{items}</div>");

            var (root, container) = await BuildAndLayout(html, pageHeight: 100);

            var placed = FindAllByClass(root, "item");
            Assert.Equal(12, placed.Count);
            AssertNoOverlaps(placed);

            // Every item sits inside the band of the page it was laid out on - the check the overlap above
            // is a symptom of, stated directly, so a future regression names the cause rather than a
            // coincidence of which two boxes happened to collide.
            Assert.All(placed, box =>
            {
                var slot = container.PageIndexOf(box.Location.Y);
                Assert.InRange(box.Location.Y,
                    container.PageTopOf(slot) - 0.5, container.PageBottomOf(slot) + 0.5);
            });
        }

        // column-fill: auto fills each column before starting the next, so the first column runs to the
        // page budget rather than to an even share - the branch that skips the balance estimate entirely.
        [Fact]
        public async Task ColumnFillAuto_FillsTheFirstColumnBeforeStartingTheNext()
        {
            var items = string.Concat(Enumerable.Range(1, 6)
                .Select(i => $"<div class='item' id='a{i}' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>{items}</div>");

            var (root, _) = await BuildAndLayout(html, pageHeight: 200);
            var placed = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            // Filling means column 1 takes as many as its budget allows, so it holds strictly more than
            // an even split of six would.
            var inFirstColumn = placed.Count(b => System.Math.Abs(b.Location.X - mc.ClientLeft) < 0.5);
            Assert.True(inFirstColumn > 3, $"expected the first column to be filled, it holds {inFirstColumn}");
        }

        // The estimator searches for the tightest height that still packs as many children as the full
        // budget would. With unevenly-sized children an even split of the total is not that height, so
        // this pins that the search runs rather than a closed form.
        [Fact]
        public async Task ColumnFillBalance_WithUnevenChildren_UsesNoMoreColumnsThanTheBudgetWould()
        {
            var heights = new[] { 70, 70, 70, 30, 30, 30 };
            var items = string.Concat(heights.Select((h, i) =>
                $"<div class='item' id='u{i}' style='height:{h}px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:3; column-gap:0; width:300px'>{items}</div>");

            var (root, _) = await BuildAndLayout(html, pageHeight: 400);
            var placed = FindAllByClass(root, "item");

            AssertNoOverlaps(placed);
            Assert.Equal(6, placed.Count);

            // Balanced across all three columns rather than poured into the first.
            var xs = placed.Select(b => System.Math.Round(b.Location.X)).Distinct().ToList();
            Assert.Equal(3, xs.Count);
        }

        // A child taller than any column claims one on its own and overflows it, rather than being split
        // - the case the estimator's monotonicity argument turns on.
        [Fact]
        public async Task AChildTallerThanItsColumn_ClaimsOneAndOverflows()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='tall' style='height:300px'>Tall</div>
                    <div class='item' id='after' style='height:20px'>After</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var tall = FindById(root, "tall")!;
            var after = FindById(root, "after")!;

            // It is not split: one box, its full height, in the first column.
            Assert.Equal(225, tall.ActualBottom - tall.Location.Y, 1);
            Assert.Equal(mc.ClientLeft, tall.Location.X, 1);
            Assert.True(after.Location.X > mc.ClientLeft + 50, "the next child starts the next column");
        }

        // The multicol dispatch no longer routes through LayoutMonolithicContent, which used to be what
        // ran the out-of-flow children of every engine container. The columns engine lays out its own, so
        // an absolutely-positioned child must still be placed and keep its content.
        [Fact]
        public async Task OutOfFlowChild_IsStillLaidOut()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; position:relative'>
                    <div class='item' style='height:40px'>One</div>
                    <div class='item' style='height:40px'>Two</div>
                    <div id='abs' style='position:absolute; top:5px; left:5px'>Absolute</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 400);

            var abs = FindById(root, "abs")!;
            Assert.True(abs.ActualBottom > abs.Location.Y,
                $"expected the absolutely-positioned child to have been laid out, it is {abs.Location.Y}..{abs.ActualBottom}");
            Assert.NotEmpty(LayoutHarness.Descendants(abs).SelectMany(b => b.Words));
        }

        // A multi-column container inside another engine's container is being *measured*, not paginated -
        // that engine will translate it afterwards. Establishing a fragmentation context here anyway
        // would let the column driver record a resumption record the enclosing engine never reads, and
        // the content that record names is simply dropped. Measured at five of twelve items lost.
        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid")]
        [InlineData("display:table")]
        public async Task InsideAnotherEngine_NoContentIsDropped(string outerStyle)
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='k{i}' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div style='{outerStyle}'><div id='mc' style='columns:2; width:200px'>{items}</div></div>");

            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var placed = FindAllByClass(root, "item");
            Assert.Equal(12, placed.Count);
            AssertNoOverlaps(placed);
            Assert.All(placed, b => Assert.True(b.ActualBottom > b.Location.Y, "every item keeps a height"));
        }

        // Issue #430's own fixture: a column-count container taller than one page, nested inside
        // another engine's container, used to lose most of its content - measured at 176/640 words
        // for display:flex before the fix chain landed (issue's own numbers, a different page band);
        // the <td> half was already fixed by PR #488. Re-measures both halves together, per that PR's
        // own lesson about a two-part issue's `Fixes` keyword being claimed on the strength of one.
        [Theory]
        [InlineData(null)]      // top-level control
        [InlineData("display:table")]
        [InlineData("display:flex")]
        public async Task AColumnContainer_InsideAnotherEngine_EmitsEveryWordExactlyOnce(string? outerStyle)
        {
            var words = string.Join(" ", Enumerable.Range(1, 16).Select(i => $"word{i}"));
            var paragraphs = string.Concat(Enumerable.Range(1, 40)
                .Select(i => $"<p class='item' id='p{i}'>{words}</p>"));
            var mcHtml = $"<div id='mc' style='columns:2; width:300px'>{paragraphs}</div>";
            var html = outerStyle switch
            {
                null => Wrap(mcHtml),
                "display:table" => Wrap($"<table><tr><td>{mcHtml}</td></tr></table>"),
                _ => Wrap($"<div style='{outerStyle}'>{mcHtml}</div>"),
            };

            var (root, container) = await BuildAndLayout(html, pageHeight: 260);

            const int authoredWords = 40 * 16;
            var mc = FindById(root, "mc")!;
            Assert.Equal(authoredWords, LayoutHarness.Descendants(mc).SelectMany(b => b.Words).Count());

            // Every word claimed by exactly one fragmentainer in the emitted tree - not merely laid
            // out somewhere, which the assertion above already covers, but actually painted, since a
            // word can be positioned and still fall outside every emitted fragment's band.
            var claims = new Dictionary<PeachPDF.Html.Core.Dom.CssRect, int>();
            void CountClaims(PeachPDF.Html.Core.Fragments.BoxFragment f)
            {
                foreach (var w in f.Words)
                    claims[w.Word] = claims.GetValueOrDefault(w.Word) + 1;
                foreach (var c in f.Children) CountClaims(c);
            }
            var tree = container.FragmentTree;
            Assert.NotNull(tree);
            foreach (var frag in tree!.Fragmentainers)
                CountClaims(frag.Root);

            Assert.Equal(authoredWords, claims.Count);
            Assert.All(claims.Values, count => Assert.Equal(1, count));
        }

        // Two items sharing one row-direction line - a parallel-flows shape (css-break-3 §2.1's own
        // example is "the contents of each flex item in a flex layout row"), each with its own
        // multicol descendant. Both must fragment for real and independently: this is the multi-item
        // generalization of the fixture above, not just "does it avoid crashing."
        [Fact]
        public async Task TwoColumnContainers_InSeparateFlexItemsOnOneLine_BothEmitEveryWordExactlyOnce()
        {
            string Words(string prefix) => string.Join(" ", Enumerable.Range(1, 12).Select(i => $"{prefix}{i}"));
            string Paragraphs(string prefix) => string.Concat(Enumerable.Range(1, 20)
                .Select(i => $"<p>{Words(prefix)}</p>"));

            var html = Wrap($"""
                <div style='display:flex'>
                  <div id='mc1' style='columns:2; width:200px'>{Paragraphs("a")}</div>
                  <div id='mc2' style='columns:2; width:200px'>{Paragraphs("b")}</div>
                </div>
                """);

            var (root, container) = await BuildAndLayout(html, pageHeight: 260);

            const int authoredWordsPerContainer = 20 * 12;
            var mc1 = FindById(root, "mc1")!;
            var mc2 = FindById(root, "mc2")!;
            Assert.Equal(authoredWordsPerContainer, LayoutHarness.Descendants(mc1).SelectMany(b => b.Words).Count());
            Assert.Equal(authoredWordsPerContainer, LayoutHarness.Descendants(mc2).SelectMany(b => b.Words).Count());

            var claims = new Dictionary<PeachPDF.Html.Core.Dom.CssRect, int>();
            void CountClaims(PeachPDF.Html.Core.Fragments.BoxFragment f)
            {
                foreach (var w in f.Words)
                    claims[w.Word] = claims.GetValueOrDefault(w.Word) + 1;
                foreach (var c in f.Children) CountClaims(c);
            }
            var tree = container.FragmentTree;
            Assert.NotNull(tree);
            foreach (var frag in tree!.Fragmentainers)
                CountClaims(frag.Root);

            Assert.Equal(authoredWordsPerContainer * 2, claims.Count);
            Assert.All(claims.Values, count => Assert.Equal(1, count));
        }

        // Scope boundaries the commit pass deliberately does not cover yet (a wrapped container's
        // second line, and flex-direction:column, whose items are sequential rather than parallel
        // flows) - content must still not be *lost*, matching the pre-existing, still-accepted
        // "no content dropped" behaviour these shapes already had, even though it does not fully
        // fragment across pages the way the row/single-line case above now does.
        [Theory]
        [InlineData("display:flex; flex-wrap:wrap; width:150px")]  // forces a second line
        [InlineData("display:flex; flex-direction:column")]
        public async Task ColumnContainer_OutsideTheCommitPassScope_StillLosesNoContent(string outerStyle)
        {
            var words = string.Join(" ", Enumerable.Range(1, 16).Select(i => $"word{i}"));
            var paragraphs = string.Concat(Enumerable.Range(1, 40)
                .Select(i => $"<p class='item' id='p{i}'>{words}</p>"));
            var html = Wrap($"""
                <div style='{outerStyle}'>
                  <div style='height:20px'>Filler</div>
                  <div id='mc' style='columns:2; width:150px'>{paragraphs}</div>
                </div>
                """);

            var (root, _) = await BuildAndLayout(html, pageHeight: 260);

            var mc = FindById(root, "mc")!;
            Assert.Equal(40 * 16, LayoutHarness.Descendants(mc).SelectMany(b => b.Words).Count());
        }

        // No-progress backstop: a much larger document than the fixtures above, verifying the driver's
        // pass-count/no-progress guards (FlexBreakToken's contents-based equality among them) keep this
        // bounded rather than spinning - the exact defect class a table cell's own resumability hit
        // before FlexBreakToken/TableBreakToken's equality override existed (100,000 passes, 1m52s).
        //
        // The bound is a hang guard, not a complexity guard (see
        // .claude/invariants/testing-a-complexity-guard-asserts-a-count-not-the-clock.md): this
        // normally completes in ~1s in isolation, so 60s is generous headroom for a CI job running both
        // target frameworks' suites under coverage instrumentation with parallel xUnit collections -
        // measured tripping a 15s bound at 15.2-25.3s on ubuntu-latest and windows-latest alike with no
        // code change involved - while staying well under the 1m52s the guarded-against regression took.
        [Fact]
        public async Task LargeMulticolInFlexItem_CompletesQuicklyWithoutSpinning()
        {
            var words = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"word{i}"));
            var paragraphs = string.Concat(Enumerable.Range(1, 400)
                .Select(i => $"<p>{words}</p>"));
            var html = Wrap($"<div style='display:flex'><div id='mc' style='columns:3; width:400px'>{paragraphs}</div></div>");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (root, _) = await BuildAndLayout(html, pageHeight: 300);
            sw.Stop();

            var mc = FindById(root, "mc")!;
            Assert.Equal(400 * 20, LayoutHarness.Descendants(mc).SelectMany(b => b.Words).Count());
            Assert.True(sw.ElapsedMilliseconds < 60000, $"expected well under 60s, took {sw.ElapsedMilliseconds}ms");
        }

        // The container is laid out once per page fragment, so a rule list that is *assigned* rather than
        // accumulated keeps only the last fragment's - and the first page draws no rule at all.
        [Fact]
        public async Task ColumnRules_AreKeptForEveryPageTheContainerSpans()
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-rule:1px solid #000; width:200px'>{items}</div>");

            var (root, container) = await BuildAndLayout(html, pageHeight: 120);
            var mc = FindById(root, "mc")!;

            Assert.NotNull(mc.ColumnRuleSegments);

            // One per page the container occupies, each spanning only that page's own content.
            var slots = mc.ColumnRuleSegments!
                .Select(seg => container.PageIndexOf(seg.Top + HtmlContainerInt.PageBoundaryEpsilon))
                .Distinct()
                .ToList();

            Assert.True(slots.Count > 1,
                $"expected a rule on every page the container spans, got segments on slot(s) {string.Join(",", slots)}");
        }

        // An out-of-flow child's containing block is the multi-column container, not a column - and the
        // column loop narrows the container to one column at a time while filling it.
        [Fact]
        public async Task OutOfFlowChild_ResolvesAgainstTheContainerNotAColumn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; position:relative'>
                    <div class='item' style='height:40px'>One</div>
                    <div class='item' style='height:40px'>Two</div>
                    <div id='abs' style='position:absolute; top:0; left:0; width:100%'>Absolute</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var abs = FindById(root, "abs")!;

            // 200px = 150pt of container, not 75pt of column.
            Assert.Equal(mc.ClientRight - mc.ClientLeft, abs.ActualRight - abs.Location.X, 1);
        }

        // A known boundary, pre-existing and characterized here rather than left silent. CssBox's own
        // content dispatch tests ContainsInlinesOnly *before* EstablishesMultiColumnContext, and
        // ContainsInlinesOnly is "every child is inline" - vacuously true of a box whose children are all
        // text. So a multi-column container holding nothing but text takes the inline branch and never
        // reaches this engine at all. Columnizing it needs its inline content wrapped in an anonymous
        // block first, which is a box-generation question rather than a fragmentation one.
        [Fact]
        public async Task InlineOnlyContent_DoesNotColumnize_KnownBoundary()
        {
            var html = Wrap("<div id='mc' style='columns:2; column-gap:0; width:200px'>"
                            + "Plain text with no block child of its own, long enough that two columns "
                            + "would be visibly different from one.</div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var xs = LayoutHarness.Descendants(mc).SelectMany(b => b.Words)
                .Select(w => System.Math.Round(w.Left)).DefaultIfEmpty(0).ToList();

            // The text uses the container's whole width rather than a column's - two columns of a 150pt
            // container would each be ~75pt wide. And no rule is drawn, because no column was made.
            Assert.True(xs.Max() - xs.Min() > 100,
                $"expected the text to span the whole container rather than one column, got X {xs.Min()}..{xs.Max()}");
            Assert.Null(mc.ColumnRuleSegments);
        }

        // ─── Break values in the column context (css-break-3 §3.1 `column`, §3.2 `avoid-column`) ──────

        /// <summary>
        /// The column an author asks for. A forced column break cannot be realized the way a page break
        /// is — by placing the box lower — because every column of a container shares one block-axis
        /// band, so it is a break decision instead.
        /// </summary>
        [Theory]
        [InlineData("break-before: column", "")]
        [InlineData("", "break-after: column")]
        public async Task ForcedColumnBreak_StartsTheNextColumn(string onSecond, string onFirst)
        {
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px; {onFirst}'>A</div>
                    <div id='b' style='height:20px; {onSecond}'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.True(a.Location.X < mc.ClientLeft + 50,
                $"the first box must stay in column 1, it is at x={a.Location.X}");
            Assert.True(b.Location.X > mc.ClientLeft + 50,
                $"expected the forced break to start the next column, the box is at x={b.Location.X}");

            // Every column shares one band, so the box begins where the first column began rather than
            // below where its content ended.
            Assert.Equal(a.Location.Y, b.Location.Y, 1);
        }

        /// <summary>
        /// Both boxes fit column 1 with room to spare, so without the break there is no boundary at all —
        /// which is what makes the assertion above about the break rather than about the packing.
        /// </summary>
        [Fact]
        public async Task WithoutTheForcedBreak_BothBoxesShareOneColumn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>A</div>
                    <div id='b' style='height:20px'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;

            Assert.True(FindById(root, "b")!.Location.X < mc.ClientLeft + 50);
        }

        /// <summary>
        /// <c>column</c> names a fragmentation context, and outside one there is no column boundary for it
        /// to force. Honouring it as a page break would be a hint about columns silently changing
        /// pagination — the defect the context-aware predicate exists to prevent.
        /// </summary>
        [Theory]
        [InlineData("break-before: column")]
        [InlineData("break-inside: avoid-column")]
        public async Task ColumnBreakValues_OutsideAMulticolContainer_ChangeNothing(string declaration)
        {
            var html = Wrap($@"
                <div id='a' style='height:20px'>A</div>
                <div id='b' style='height:20px; {declaration}'>B</div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 200);

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(container.PageIndexOf(a.Location.Y), container.PageIndexOf(b.Location.Y));
            Assert.True(b.Location.Y > a.Location.Y, "the second box must still follow the first");
        }

        /// <summary>
        /// A value that forces a <i>page</i> break says nothing about columns, and none of this
        /// container's columns is on another page — so it escapes the container rather than being answered
        /// by it. It used to arrive one column over instead of one page over, because the page vehicle is
        /// realized by placement and placement cannot leave a container whose engine decides which
        /// fragmentainer content lands in.
        /// </summary>
        [Fact]
        public async Task AForcedPageBreak_InsideAColumn_StartsTheNextPage()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>A</div>
                    <div id='b' style='height:20px; break-before:page'>B</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(0, container.PageIndexOf(a.Location.Y));
            Assert.True(container.PageIndexOf(b.Location.Y) > container.PageIndexOf(a.Location.Y),
                $"expected the box to start the next page, it is at y={b.Location.Y}");

            // And it opens that page in the first column, rather than carrying the column it left.
            Assert.True(b.Location.X < mc.ClientLeft + 50,
                $"expected the box to begin the next page's first column, it is at x={b.Location.X}");
        }

        /// <summary>
        /// A forced break carried by a box that is not the container's own child is honoured exactly as its
        /// direct-child sibling above is — no longer lost entirely, which is what
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/395">#395</see> characterized: the break
        /// is one-shot (<c>CssBox.PlacedByForcedBreak</c>, cleared only by a prologue that re-decides it),
        /// and the multi-column engine's measurement pass — which lays every child out once to size the
        /// fill, then again for real — used to spend that one shot on a descendant nothing re-opened the
        /// prologue for, since <c>PassRewind.RollBackTo</c> re-opens it directly only for the container's
        /// own children.
        /// </summary>
        /// <remarks>
        /// Fixed incidentally by <see href="https://github.com/jhaygood86/PeachPDF/issues/434">#434</see>'s
        /// own fix, not by work scoped to #395 directly: both the page driver's pass re-entry and this
        /// engine's measurement-pass retry share the same <c>CssBox.ResetForRefill</c>/<c>PassRewind</c>
        /// primitive, so widening <c>ResetForRefill</c> to let a reset box's whole subtree retake a forced
        /// break (rather than only the box itself) closes this shape too. #395's own text names flex, grid
        /// and table as the same shape for their own measure-then-place engines; those are unverified here
        /// and #395 stays open for them until checked.
        /// </remarks>
        [Fact]
        public async Task AForcedPageBreak_BelowTheContainersOwnChild_StartsTheNextPage()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='wrap'>
                        <div id='a' style='height:20px'>A</div>
                        <div id='b' style='height:20px; break-before:page'>B</div>
                    </div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(0, container.PageIndexOf(a.Location.Y));
            Assert.True(container.PageIndexOf(b.Location.Y) > container.PageIndexOf(a.Location.Y),
                $"expected the box to start the next page, it is at y={b.Location.Y}");
            Assert.True(b.Location.X < mc.ClientLeft + 50,
                $"expected the box to begin the next page's first column, it is at x={b.Location.X}");
        }

        /// <summary>
        /// §3.1's "one or two page breaks". The parity step used to be suppressed inside a column, because
        /// reserving a page while the box merely moved to the next column honoured half of a decision.
        /// Now that the break reaches the page it names, the side it names is honoured too.
        /// </summary>
        [Theory]
        [InlineData("right")]
        [InlineData("left")]
        public async Task ADirectionalBreak_InsideAColumn_LandsOnAPageOfThatSide(string side)
        {
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>A</div>
                    <div id='b' style='height:20px; break-before:{side}'>B</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 200);

            var b = FindById(root, "b")!;
            var landing = container.PageIndexOf(b.Location.Y);

            Assert.True(landing > 0, $"expected the box to leave the first page, it is at y={b.Location.Y}");
            Assert.True(
                BreakValues.SlotIsOn(landing, side == "right" ? PageSide.Right : PageSide.Left),
                $"expected a {side}-hand page, the box landed in slot {landing}");

            // The slot is only half of it, and the half a reader never sees. Every slot the break stepped
            // over has to be *materialized* as a blank page, or the side the value names and the page the
            // reader counts to disagree — which the escape used to do, by settling the reservation on the
            // pass that declined to place the box and losing it before the pass that did.
            var materialized = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToList();

            Assert.Equal(Enumerable.Range(0, landing + 1), materialized);
        }

        /// <summary>
        /// A forced page break ends the flow for this fragment as surely as running out of content does,
        /// so it is the fragment that balances — over what precedes the break, not over the content the
        /// balance estimate was made for. Without this the four boxes before the break pile into the first
        /// column and the second is left empty, because the estimate was taken over a flow sixteen boxes
        /// longer than the one this fragment actually holds.
        /// </summary>
        [Fact]
        public async Task AForcedPageBreak_LeavesTheColumnsBeforeItBalanced()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='before' style='height:20px'>1</div>
                    <div class='before' style='height:20px'>2</div>
                    <div class='before' style='height:20px'>3</div>
                    <div class='before' style='height:20px'>4</div>
                    <div id='rest' style='height:240px; break-before:page'>rest</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var before = FindAllByClass(root, "before");

            Assert.Equal(4, before.Count);
            Assert.Equal(2, before.Count(b => b.Location.X < mc.ClientLeft + 50));
            Assert.Equal(2, before.Count(b => b.Location.X > mc.ClientLeft + 50));
            Assert.True(container.PageIndexOf(FindById(root, "rest")!.Location.Y) > 0);
        }

        /// <summary>
        /// A page break is no nested fragmentainer's to satisfy, so a container nested inside another hands
        /// the record up marked rather than answering it with a column of its own — and the column of the
        /// container enclosing it does not answer it either.
        /// </summary>
        [Fact]
        public async Task AForcedPageBreak_InsideANestedContainer_EscapesBothOfThem()
        {
            var html = Wrap(@"
                <div id='outer' style='columns:2; column-gap:0; width:400px; column-fill:auto'>
                    <div id='inner' style='columns:2; column-gap:0; column-fill:auto'>
                        <div id='a' style='height:20px'>A</div>
                        <div id='b' style='height:20px; break-before:page'>B</div>
                    </div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 200);

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(0, container.PageIndexOf(a.Location.Y));
            Assert.True(container.PageIndexOf(b.Location.Y) > 0,
                $"expected the break to leave both containers, the box is at y={b.Location.Y}");
        }

        /// <summary>
        /// The invariant that catches both directions of getting an escape wrong: a ghost left in the
        /// column the content escaped from, or content dropped along with the columns the container
        /// stopped opening.
        /// </summary>
        [Fact]
        public async Task AForcedPageBreak_InsideAColumn_ClaimsEveryWordExactlyOnce()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>alpha</div>
                    <div id='b' style='height:20px; break-before:page'>beta</div>
                    <div id='c' style='height:20px'>gamma</div>
                </div>");
            var (_, container) = await BuildAndLayout(html, pageHeight: 200);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.Equal(3, claimed.Count);
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        /// <summary>
        /// §3.1's break point before a container's first in-flow child <i>is</i> the break point before the
        /// container, so a <c>break-before: column</c> there moves the container — the same propagation the
        /// page vehicle applies, asked in the column context.
        /// </summary>
        [Fact]
        public async Task ForcedColumnBreak_OnAContainersFirstChild_MovesTheContainer()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>A</div>
                    <div id='wrap'><div id='b' style='height:20px; break-before:column'>B</div></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var wrap = FindById(root, "wrap")!;
            var b = FindById(root, "b")!;

            Assert.True(wrap.Location.X > mc.ClientLeft + 50,
                $"the container must travel with the break, it is at x={wrap.Location.X}");
            Assert.True(b.Location.X > mc.ClientLeft + 50);
        }

        /// <summary>
        /// §3.2 in the column context. Only observable at all because a child can genuinely split across a
        /// column: until it could, "do not break me by a column" named nothing that happened.
        /// </summary>
        [Theory]
        [InlineData("avoid-column")]
        [InlineData("avoid")]
        public async Task AvoidColumnBreak_MovesTheWholeBoxToTheNextColumn(string value)
        {
            var (root, mc, longBox, _) = await SplittingParagraphAsync($"break-inside:{value}");

            var words = LayoutHarness.Descendants(longBox).SelectMany(b => b.Words).ToList();

            Assert.NotEmpty(words);
            Assert.All(words, w => Assert.True(w.Left >= mc.ClientLeft + 100,
                $"every word must be in the second column, one is at x={w.Left}"));
            Assert.NotNull(FindById(root, "top"));
        }

        /// <summary>
        /// The control, and the point of the context: <c>avoid-page</c> names the page context and must not
        /// suppress a <i>column</i> break, so the same paragraph still splits.
        /// </summary>
        [Theory]
        [InlineData("break-inside:avoid-page")]
        [InlineData("break-inside:auto")]
        public async Task APageContextAvoidValue_DoesNotSuppressAColumnBreak(string declaration)
        {
            var (_, mc, longBox, _) = await SplittingParagraphAsync(declaration);

            var words = LayoutHarness.Descendants(longBox).SelectMany(b => b.Words).ToList();

            Assert.Contains(words, w => w.Left < mc.ClientLeft + 100);
            Assert.Contains(words, w => w.Left >= mc.ClientLeft + 100);
        }

        /// <summary>
        /// §4.3's fourth tier: a box that asks not to be broken and fits no column is split rather than
        /// walked from column to column. The <c>i &gt; start</c> guard is what expresses it — a box moved to
        /// the next column becomes the child that column's fill starts at, so it is not asked again.
        /// </summary>
        [Fact]
        public async Task ABoxThatAvoidsButFitsNoColumn_IsStillSplit()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <div id='top' style='height:40pt'>Top</div>
                    <p id='long' style='break-inside:avoid-column; margin:0; font-size:10pt; line-height:20pt'>
                    L1<br>L2<br>L3<br>L4<br>L5<br>L6</p>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            var mc = FindById(root, "mc")!;
            var placed = LayoutHarness.Descendants(FindById(root, "long")!).SelectMany(b => b.Words).ToList();

            Assert.Contains(placed, w => w.Left < mc.ClientLeft + 100);
            Assert.Contains(placed, w => w.Left >= mc.ClientLeft + 100);
        }

        /// <summary>
        /// The workhorse invariant for anything that relocates content between fragmentainers: every word
        /// the document authored is claimed by exactly one fragment. It fails one way if the abandoned
        /// attempt leaves a ghost behind in the column the box left, and the other way if moving the box
        /// drops what it had already placed.
        /// </summary>
        [Fact]
        public async Task AvoidColumnBreak_KeepsEveryWordExactlyOnce()
        {
            var (_, _, _, container) = await SplittingParagraphAsync("break-inside:avoid-column");

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(claimed);
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        // ─── Keep-with-next at a column boundary (css-break-3 §3.1) ────────────────

        /// <summary>
        /// The stranded heading. A run chained to the moving box by <c>break-after: avoid</c> travels with
        /// it — but a column has no lower coordinate to move a run to, so the break is stated before the
        /// run's head and the next column's fill lays the whole run out there.
        /// </summary>
        [Theory]
        [InlineData("break-after:avoid", "")]
        [InlineData("break-after:avoid-column", "")]
        [InlineData("", "break-before:avoid")]
        public async Task KeepWithNext_AtAColumnBoundary_MovesTheHeadWithTheContent(string onHead, string onBody)
        {
            var (root, mc) = await StrandedHeadingAsync(onHead, onBody);

            var head = FindById(root, "head")!;
            var body = FindById(root, "body")!;

            Assert.True(body.Location.X > mc.ClientLeft + 50,
                $"the fixture must move the content to the next column, it is at x={body.Location.X}");
            Assert.True(head.Location.X > mc.ClientLeft + 50,
                $"the heading must travel with it, it is at x={head.Location.X}");
            Assert.True(head.Location.Y < body.Location.Y, "the heading must still precede its content");
        }

        /// <summary>
        /// The control. Without the chain the same fixture strands the heading, which is what makes the
        /// assertion above about the run rather than about the packing.
        /// </summary>
        [Fact]
        public async Task WithoutTheChain_TheHeadingStaysInTheColumnItsContentLeaves()
        {
            var (root, mc) = await StrandedHeadingAsync(string.Empty, string.Empty);

            Assert.True(FindById(root, "body")!.Location.X > mc.ClientLeft + 50);
            Assert.True(FindById(root, "head")!.Location.X < mc.ClientLeft + 50);
        }

        /// <summary>
        /// The context, asked of the run itself: <c>avoid-page</c> forbids nothing at a column boundary,
        /// so the pair is not kept together there. The counterpart to
        /// <see cref="APageContextAvoidValue_DoesNotSuppressAColumnBreak"/>, one level up.
        /// </summary>
        [Fact]
        public async Task APageContextAvoidValue_DoesNotChainARunAtAColumnBoundary()
        {
            var (root, mc) = await StrandedHeadingAsync("break-after:avoid-page", string.Empty);

            Assert.True(FindById(root, "body")!.Location.X > mc.ClientLeft + 50);
            Assert.True(FindById(root, "head")!.Location.X < mc.ClientLeft + 50);
        }

        /// <summary>
        /// §4.3's ladder: a run that does not fit a whole column with the content it is chained to is
        /// trimmed from its <i>front</i>, because the members nearest the breaking box are what the chain
        /// is about. Here only the nearer heading can come.
        /// </summary>
        [Fact]
        public async Task ARunTooTallForTheDestinationColumn_IsTrimmedFromItsFront()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <div id='filler' style='height:40pt'>F</div>
                    <div id='far' style='height:20pt; break-after:avoid'>Far</div>
                    <div id='near' style='height:10pt; break-after:avoid'>Near</div>
                    <div id='body' style='height:70pt'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            var mc = FindById(root, "mc")!;

            Assert.True(FindById(root, "body")!.Location.X > mc.ClientLeft + 50);
            Assert.True(FindById(root, "near")!.Location.X > mc.ClientLeft + 50,
                "the member nearest the breaking box is the one the chain is about");
            Assert.True(FindById(root, "far")!.Location.X < mc.ClientLeft + 50,
                "the whole run does not fit the destination column, so its front is left behind");
        }

        /// <summary>
        /// A run whose head is the very child this column's fill began at is left where it is: breaking
        /// before it would start the next column at the same place and put the identical question to it
        /// again. The index test states that outright; here the fit test reaches it first, since a head at
        /// the band top plus a box that overflows the band can never fit the band together. Both are
        /// wanted — the index test is what keeps the decision sound if a fit test ever becomes an estimate.
        /// </summary>
        [Fact]
        public async Task ARunHeadingTheColumnsOwnFill_IsNotMovedAgain()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <div id='head' style='height:20pt; break-after:avoid'>H</div>
                    <div id='body' style='height:50pt'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 60);

            var mc = FindById(root, "mc")!;

            Assert.True(FindById(root, "body")!.Location.X > mc.ClientLeft + 50);
            Assert.True(FindById(root, "head")!.Location.X < mc.ClientLeft + 50,
                "the head of this column's own fill has nowhere to travel to");
        }

        /// <summary>
        /// The other arm that raises a column break — a box that asks not to be broken by one — moves the
        /// content alone, and §4.3 is why: that box has a <i>pending record</i>, which is the statement
        /// that its epilogue has not run, so its height is not known and the fit question cannot be asked
        /// about it. Answering anyway reads as "the run always fits", and moves the heading into a column
        /// of its own while the box it is chained to breaks again in the next one — worse than leaving it,
        /// and a wasted column besides.
        /// </summary>
        [Fact]
        public async Task AtAnAvoidColumnBreak_TheContentMovesAlone_SinceItsHeightIsNotYetKnown()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:3; column-gap:0; width:450px; column-fill:auto'>
                    <div id='top' style='height:30pt'>Top</div>
                    <div id='head' style='height:10pt; break-after:avoid; margin:0; font-size:10pt; line-height:10pt'>Heading</div>
                    <p id='long' style='break-inside:avoid-column; margin:0; font-size:10pt; line-height:20pt'>
                    L1<br>L2<br>L3<br>L4</p>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 60);

            var mc = FindById(root, "mc")!;
            var head = FindById(root, "head")!;
            var top = FindById(root, "top")!;

            Assert.Equal(top.Location.X, head.Location.X, 1);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(claimed);
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
            Assert.True(mc.ClientLeft >= 0);
        }

        /// <summary>
        /// §5.2's forced-break governance survives the escape. The break is settled on the pass that
        /// declines to place the box and applied on the one that does, and in between the columns engine
        /// re-opens the box's prologue, which retracts it — so a collapse-through marker read as an
        /// ordinary self-collapsing box and the sibling after it anchored to the box <i>before</i> the
        /// marker, landing ahead of the break it was supposed to follow.
        /// </summary>
        [Fact]
        public async Task AnEscapingForcedBreak_StillGovernsTheMarginAfterIt()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:300pt; column-fill:auto'>
                    <div id='a' style='height:20pt; margin:0'>A</div>
                    <div id='mark' style='break-before:page; margin:0'></div>
                    <div id='c' style='height:20pt; margin-top:30pt'>C</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 200);

            var mark = FindById(root, "mark")!;
            var c = FindById(root, "c")!;

            Assert.True(mark.PlacedByForcedBreak, "the marker is placed by the break it carries");
            Assert.True(c.Location.Y > mark.Location.Y,
                $"the sibling after the break must follow it, it is at y={c.Location.Y} against the marker's {mark.Location.Y}");
            Assert.Equal(container.PageIndexOf(mark.Location.Y), container.PageIndexOf(c.Location.Y));
        }

        /// <summary>
        /// The invariant that catches both directions of getting an escape wrong: a ghost left in the
        /// column the content escaped from, or content dropped along with the columns the container
        /// stopped opening.
        /// </summary>

        private static async Task<(CssBox Root, CssBox Mc)> StrandedHeadingAsync(string onHead, string onBody)
        {
            // 40pt of filler, then a 20pt heading, then 60pt of content in a column that holds a little
            // under 100pt: the content cannot stay, and the heading plus the content fit a column of their
            // own - so whether the heading comes along says whether the chain was read, with no dependence
            // on font metrics.
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <div id='filler' style='height:40pt'>F</div>
                    <div id='head' style='height:20pt; {onHead}'>H</div>
                    <div id='body' style='height:60pt; {onBody}'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            return (root, FindById(root, "mc")!);
        }

        private static async Task<(CssBox Root, CssBox Mc, CssBox LongBox, HtmlContainerInt Container)>
            SplittingParagraphAsync(string declaration)
        {
            // 40pt of content above a paragraph that is 80pt tall, in a 100pt column: it cannot stay
            // where it is, and it fits a column of its own exactly - so which of the two happens says
            // which break value was honoured, with no dependence on font metrics.
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <div id='top' style='height:40pt'>Top</div>
                    <p id='long' style='{declaration}; margin:0; font-size:10pt; line-height:20pt'>
                    L1<br>L2<br>L3<br>L4</p>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);

            return (root, FindById(root, "mc")!, FindById(root, "long")!, container);
        }

        // A column is at least as tall as its unbreakable content (css-multicol-1 §3.3). The band a
        // column is *given* comes from balancing, and a child this engine cannot fragment - a table,
        // another engine's container - is placed whole and overflows it. Recording the target rather
        // than the band the column actually filled left everything below the target claimed by no
        // fragmentainer at all: laid out, and painted nowhere (issue #406). No break value is needed;
        // the plain nesting is enough.
        [Fact]
        public async Task AnUnbreakableChildTallerThanTheBalancedTarget_EmitsAllOfItsContent()
        {
            var blocks = string.Concat(Enumerable.Range(1, 4)
                .Select(i => $"<p class='item'>block {i} alpha beta gamma delta</p>"));

            var (root, container) = await BuildAndLayout(
                Wrap("<div id='mc' style='columns:2; column-gap:20px; width:300px'>"
                     + $"<table style='width:100%'><tr><td>{blocks}</td></tr></table></div>"),
                pageHeight: 400);

            var authored = FindAllByClass(root, "item")
                .SelectMany(LayoutHarness.Descendants)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(authored);
            Assert.All(authored, word => Assert.Contains(word, claimed));

            // And the widening cannot have let a neighbouring column claim the same word: columns are
            // told apart by the inline axis, which the band says nothing about.
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        // The same equality for a grid container, which reached it by a different route. Its items'
        // content used to be laid out at a width that ignored the column entirely - an `auto` track was
        // seeded at its max-content contribution and only ever grown - so it overflowed the column's
        // *inline* span, the one axis that must stay exact because it is what tells two columns of one
        // band apart. Fixing the track sizing (CSS Grid §12.6) is what makes the overflow not exist.
        [Fact]
        public async Task AGridChildOfAMulticol_KeepsItsContentInsideTheColumn()
        {
            var blocks = string.Concat(Enumerable.Range(1, 4)
                .Select(i => $"<p class='item'>block {i} alpha beta gamma delta</p>"));

            var (root, container) = await BuildAndLayout(
                Wrap("<div id='mc' style='columns:2; column-gap:20px; width:300px'>"
                     + $"<div id='g' style='display:grid'>{blocks}</div></div>"),
                pageHeight: 400);

            var grid = FindById(root, "g")!;

            var authored = FindAllByClass(root, "item")
                .SelectMany(LayoutHarness.Descendants)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(authored);

            // Nothing reaches past the column the grid is in, so nothing is refused for being outside it.
            Assert.All(authored, word =>
                Assert.True(word.Right <= grid.ActualRight + 0.5,
                    $"'{word.Text}' ends at {word.Right:F1}, past the grid's own right edge {grid.ActualRight:F1}"));

            Assert.All(authored, word => Assert.Contains(word, claimed));
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        // ─── A break raised below the container's own child (css-break-3 §2) ───────

        /// <summary>
        /// A wrapper holding <paramref name="rowCount"/> 20pt rows, with a 20pt lead sibling above it, in a
        /// two-column container. Nothing about the rows is special: the point is only that the break falls
        /// between two of them, which puts it one level below the container's own child loop — the loop
        /// whose record the columns engine reads.
        /// </summary>
        private static string NestedRowsFixture(int rowCount, string fill)
        {
            var rows = string.Concat(Enumerable.Range(0, rowCount)
                .Select(i => $"<div id='r{i}' class='row' style='height:20pt; margin:0'>r{i}</div>"));

            return Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300pt; {fill}'>
                    <div id='lead' style='height:20pt; margin:0'>lead</div>
                    <div id='wrap' style='margin:0'>{rows}</div>
                </div>");
        }

        /// <summary>
        /// The break falls between two rows of a wrapper rather than between two children of the
        /// container, and the content after it starts the next column at that column's own top. It used to
        /// neither move nor break: the record travelled up correctly, but the child it named was then
        /// placed after the previous sibling it had just left behind in the column being vacated, so it
        /// straddled the boundary and everything after it ran past the page.
        /// </summary>
        [Fact]
        public async Task ABreakBelowTheContainersOwnChild_StartsTheNextColumnAtItsTop()
        {
            // A 394pt band holds the 20pt lead plus eighteen 20pt rows exactly; the nineteenth cannot stay.
            var (root, container) = await BuildAndLayout(NestedRowsFixture(24, "column-fill:auto"), pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var rows = FindAllByClass(root, "row");
            var secondColumnLeft = FindById(root, "r18")!.Location.X;

            // Everything up to the break stays where the first column put it.
            Assert.Equal(mc.ClientLeft, FindById(root, "lead")!.Location.X, 1);
            for (var i = 0; i <= 17; i++)
            {
                Assert.Equal(mc.ClientLeft, rows[i].Location.X, 1);
                Assert.Equal(mc.ClientTop + 20 * (i + 1), rows[i].Location.Y, 1);
            }

            // Everything after it is in the next column, which begins at the band top rather than at the
            // bottom of the row it followed.
            Assert.True(secondColumnLeft > mc.ClientLeft + 100,
                $"the continuation is at x={secondColumnLeft}, still in the column it should have left");

            for (var i = 18; i <= 23; i++)
            {
                Assert.Equal(secondColumnLeft, rows[i].Location.X, 1);
                Assert.Equal(mc.ClientTop + 20 * (i - 18), rows[i].Location.Y, 1);
                Assert.Equal(rows[i].Location.Y + 20, rows[i].ActualBottom, 1);
            }

            // The wrapper itself is a fragment in each column, and the box carries the second one.
            var wrap = FindById(root, "wrap")!;
            Assert.Equal(secondColumnLeft, wrap.Location.X, 1);
            Assert.Equal(mc.ClientTop, wrap.Location.Y, 1);

            // And the container no longer reports a height past the page it is on: it ends where its
            // deepest column's content ends, and one page holds the lot.
            Assert.Equal(rows[17].ActualBottom, mc.ActualBottom, 1);
            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        /// <summary>
        /// The invariant over the fragment tree, which catches both directions at once: the rows after the
        /// break claimed by the column they left as well as by the one they moved to (they were captured
        /// into both), and rows the fill never reached claimed at the measurement pass's position, a whole
        /// virtual column down the document.
        /// </summary>
        [Theory]
        [InlineData("column-fill:auto", 24, 400)]
        [InlineData("column-fill:balance", 24, 400)]
        [InlineData("column-fill:auto", 60, 400)]
        [InlineData("column-fill:auto", 24, 200)]
        public async Task ABreakBelowTheContainersOwnChild_ClaimsEveryWordExactlyOnce(
            string fill, int rowCount, double pageHeight)
        {
            var (root, container) = await BuildAndLayout(NestedRowsFixture(rowCount, fill), pageHeight);

            var authored = FindAllByClass(root, "row")
                .SelectMany(LayoutHarness.Descendants)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.Equal(rowCount, authored.Count);
            Assert.All(authored, word => Assert.Contains(word, claimed));
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        /// <summary>
        /// A <i>resumed</i> container fills its second column too. Each link of the record was worked out
        /// against the page grid, and only the outermost was being restated in the next column's terms — so
        /// a deeper link carried a slot several pages on and a target down the document, and the second
        /// column of the resumed page was filled at that target instead of at its own top.
        /// </summary>
        [Fact]
        public async Task AResumedContainer_FillsItsSecondColumnWhenTheBreakIsNested()
        {
            // 60 rows against a 400pt page: two columns of the first page, two of the second, and nothing
            // left over.
            var (root, container) = await BuildAndLayout(NestedRowsFixture(60, "column-fill:auto"), pageHeight: 400);

            var rows = FindAllByClass(root, "row");
            var mc = FindById(root, "mc")!;
            var secondPageTop = container.PageTopOf(1);

            // The tail of the flow is in the second column of the second page, not on a third page.
            for (var i = 57; i <= 59; i++)
            {
                Assert.True(rows[i].Location.X > mc.ClientLeft + 100,
                    $"r{i} is at x={rows[i].Location.X}, in the first column of the resumed page");
                Assert.Equal(secondPageTop + 20 * (i - 57), rows[i].Location.Y, 1);
            }

            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
        }

        /// <summary>
        /// Two levels below the container, so the record's chain has an intermediate link that is itself
        /// <i>continuing</i>. The target for the box that begins the next column is resolved against its own
        /// containing block — which is why it is re-derived at placement time rather than carried on the
        /// record, since that block had not been re-placed in this column when the record was written.
        /// <para>
        /// The inline axis is the unambiguous half: the continuation begins at the inner block's own left
        /// edge, inside its containing block's <c>padding-left</c>. In the block axis it begins at the
        /// column's own content top under the default <c>box-decoration-break: slice</c>, because
        /// <see href="https://www.w3.org/TR/css-break-3/#break-decoration">§6.2</see> does not re-insert the
        /// inner block's <c>padding-top</c> at a fragmentation break. This test previously asserted
        /// <c>mc.ClientTop + 4</c> here, pinning a deviation rather than a regression: the block path
        /// consulted <c>box-decoration-break</c> nowhere, so it opened every continuation column with the
        /// continuation's own block-start padding and border while paint — correctly — drew no border in it.
        /// </para>
        /// </summary>
        [Fact]
        public async Task ABreakTwoLevelsBelowTheContainer_StartsFlushAtTheColumnTop()
        {
            var rows = string.Concat(Enumerable.Range(0, 24)
                .Select(i => $"<div id='r{i}' class='row' style='height:20pt; margin:0'>r{i}</div>"));

            var (root, container) = await BuildAndLayout(Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300pt; column-fill:auto'>
                    <div id='lead' style='height:20pt; margin:0'>lead</div>
                    <div id='wrap' style='margin:0; padding-left:5pt'>
                        <div id='inner' style='margin:0; padding-top:4pt'>{rows}</div>
                    </div>
                </div>"), pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var inner = FindById(root, "inner")!;
            var rowBoxes = FindAllByClass(root, "row");

            // The inline axis: the continuation begins at its own containing block's left edge, inside the
            // wrapper's padding, rather than at the column's.
            Assert.Equal(inner.ClientLeft, rowBoxes[18].Location.X, 1);
            Assert.True(rowBoxes[18].Location.X > mc.ClientLeft + 100);

            // The block axis: the column's own content top. `inner` is a continuation here, so under
            // `slice` its padding-top is not re-inserted - and its fragment in this column therefore opens
            // exactly where its content does.
            Assert.Equal(mc.ClientTop, rowBoxes[18].Location.Y, 1);
            Assert.Equal(inner.Location.Y, rowBoxes[18].Location.Y, 1);

            // The padding is genuinely there in the fragment the block *started* in, so this is a statement
            // about the break rather than about the padding being dropped.
            Assert.Equal(mc.ClientTop + 20 + 4, rowBoxes[0].Location.Y, 1);

            for (var i = 19; i <= 23; i++)
            {
                Assert.Equal(rowBoxes[18].Location.X, rowBoxes[i].Location.X, 1);
                Assert.Equal(rowBoxes[18].Location.Y + 20 * (i - 18), rowBoxes[i].Location.Y, 1);
            }

            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        /// <summary>
        /// <see href="https://www.w3.org/TR/css-break-3/#break-decoration">§6.2</see> at a column boundary,
        /// asked of the <i>block</i> axis: a continuation's own block-start border and padding are
        /// re-inserted under <c>clone</c> and not under <c>slice</c>.
        /// </summary>
        /// <remarks>
        /// The block path used to read the containing block's content edge unconditionally, so the two
        /// values were indistinguishable — every continuation column opened with 16pt of blank space and no
        /// border drawn in it, since <c>FragmentEmitter.ResumesAnEarlierFragment</c> clears the fragment's
        /// top edge. The inline axis has always got this right
        /// (<c>CssLayoutEngine.CreateLineBoxes</c>), which is what made it a block/inline inconsistency
        /// inside one feature rather than an unimplemented area.
        /// </remarks>
        [Fact]
        public async Task AContinuationsBlockStartDecorations_AreReInsertedAtAColumnBreakOnlyUnderClone()
        {
            async Task<(CssBox Mc, CssBox Wrap, IReadOnlyList<CssBox> Rows)> ColumnsWith(string decorationBreak)
            {
                var rows = string.Concat(Enumerable.Range(0, 24)
                    .Select(i => $"<div id='r{i}' class='row' style='height:20pt; margin:0'>r{i}</div>"));

                var (root, _) = await BuildAndLayout(Wrap($@"
                    <div id='mc' style='columns:2; column-gap:0; width:300pt; column-fill:auto'>
                        <div id='lead' style='height:20pt; margin:0'>lead</div>
                        <div id='wrap' style='margin:0; padding-top:10pt; border-top:6pt solid red;
                             box-decoration-break:{decorationBreak}'>{rows}</div>
                    </div>"), pageHeight: 400);

                return (FindById(root, "mc")!, FindById(root, "wrap")!, FindAllByClass(root, "row"));
            }

            var sliced = await ColumnsWith("slice");
            var cloned = await ColumnsWith("clone");

            // Same break point either way: the wrapper's own decorations are at its *top*, so nothing about
            // them changes how much of the band the first column holds. Both split at r17.
            var slicedRowsInFirstColumn = sliced.Rows.Count(r => r.Location.X <= sliced.Rows[0].Location.X + 0.5);
            var clonedRowsInFirstColumn = cloned.Rows.Count(r => r.Location.X <= cloned.Rows[0].Location.X + 0.5);

            Assert.Equal(17, slicedRowsInFirstColumn);
            Assert.Equal(17, clonedRowsInFirstColumn);

            // The wrapper's continuation fragment itself opens at the column's content top in both: it is
            // the container's own child, and the container is not fragmented by its own columns.
            Assert.Equal(sliced.Mc.ClientTop, sliced.Wrap.Location.Y, 1);
            Assert.Equal(cloned.Mc.ClientTop, cloned.Wrap.Location.Y, 1);

            // `slice` does not re-open the wrapper's border and padding, so its content resumes flush with
            // the column's content top; `clone` does, so it resumes 6pt + 10pt below it.
            Assert.Equal(sliced.Mc.ClientTop, sliced.Rows[17].Location.Y, 1);
            Assert.Equal(cloned.Mc.ClientTop + 16, cloned.Rows[17].Location.Y, 1);

            // Stated as the difference the spec requires, so a change that stopped reading the property at
            // all fails here whatever the two absolute values became.
            Assert.Equal(16, cloned.Rows[17].Location.Y - sliced.Rows[17].Location.Y, 1);

            // Everything after it follows, in both.
            for (var i = 18; i <= 23; i++)
            {
                Assert.Equal(sliced.Rows[17].Location.Y + 20 * (i - 17), sliced.Rows[i].Location.Y, 1);
                Assert.Equal(cloned.Rows[17].Location.Y + 20 * (i - 17), cloned.Rows[i].Location.Y, 1);
                Assert.Equal(sliced.Rows[17].Location.X, sliced.Rows[i].Location.X, 1);
            }
        }

        /// <summary>
        /// §6.2 clones the decorations of <i>every</i> box the break falls inside at once, so a nested pair
        /// of cloning blocks re-opens with the sum of both — and each level's own fragment opens inside its
        /// ancestors' re-inserted spacing rather than on top of it.
        /// </summary>
        [Fact]
        public async Task NestedCloningBlocks_ReOpenAColumnWithTheSumOfTheirBlockStartDecorations()
        {
            var rows = string.Concat(Enumerable.Range(0, 24)
                .Select(i => $"<div id='r{i}' class='row' style='height:20pt; margin:0'>r{i}</div>"));

            var (root, _) = await BuildAndLayout(Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300pt; column-fill:auto'>
                    <div id='lead' style='height:20pt; margin:0'>lead</div>
                    <div id='wrap' style='margin:0; padding-top:8pt; box-decoration-break:clone'>
                        <div id='inner' style='margin:0; padding-top:4pt; box-decoration-break:clone'>{rows}</div>
                    </div>
                </div>"), pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var wrap = FindById(root, "wrap")!;
            var inner = FindById(root, "inner")!;
            var rowBoxes = FindAllByClass(root, "row");

            var firstInSecondColumn = rowBoxes.First(r => r.Location.X > rowBoxes[0].Location.X + 100);

            // The container's own child opens at the column top; the block inside it opens below the
            // wrapper's re-inserted 8pt; the rows open below the inner block's further 4pt.
            Assert.Equal(mc.ClientTop, wrap.Location.Y, 1);
            Assert.Equal(mc.ClientTop + 8, inner.Location.Y, 1);
            Assert.Equal(mc.ClientTop + 12, firstInSecondColumn.Location.Y, 1);

            // And the re-opened inset is the same one the boxes really open with where they are not broken
            // at all: in the first column the rows sit below the 20pt lead sibling and then below both
            // levels' actual padding. That is what `clone` means - each fragment wrapped as the whole box
            // is - and it is independent of the three assertions above rather than restating them.
            Assert.Equal(mc.ClientTop + 20 + 12, rowBoxes[0].Location.Y, 1);
        }

        /// <summary>
        /// The multi-column container is <b>not</b> fragmented by its own columns: its border and padding
        /// wrap all of them at once, so §6.2 has nothing to say about it and a <c>clone</c> value on the
        /// container re-opens nothing at a column boundary.
        /// </summary>
        /// <remarks>
        /// The distinction the block path draws is between the container establishing the columns and every
        /// box below it, and this is the case that tells them apart: treating the container as a
        /// continuation too would add its own block-start border and padding to a coordinate
        /// (<c>ResumeContentTop</c>) that is already its content edge, indenting the second column by them
        /// twice over.
        /// </remarks>
        [Fact]
        public async Task AColumnBoundary_DoesNotReOpenTheContainersOwnClonedDecorations()
        {
            var rows = string.Concat(Enumerable.Range(0, 24)
                .Select(i => $"<div id='r{i}' class='row' style='height:20pt; margin:0'>r{i}</div>"));

            var (root, _) = await BuildAndLayout(Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300pt; column-fill:auto;
                     padding-top:9pt; border-top:5pt solid red; box-decoration-break:clone'>
                    <div id='lead' style='height:20pt; margin:0'>lead</div>
                    <div id='wrap' style='margin:0'>{rows}</div>
                </div>"), pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var wrap = FindById(root, "wrap")!;
            var rowBoxes = FindAllByClass(root, "row");

            var firstInSecondColumn = rowBoxes.First(r => r.Location.X > rowBoxes[0].Location.X + 100);

            // The container's own content edge, once - not once from ClientTop and again from its cloned
            // border and padding.
            Assert.Equal(mc.ClientTop, wrap.Location.Y, 1);
            Assert.Equal(mc.ClientTop, firstInSecondColumn.Location.Y, 1);

            // And the first column still begins there too, so both columns share one content top.
            Assert.Equal(mc.ClientTop, FindById(root, "lead")!.Location.Y, 1);
        }

        /// <summary>
        /// A continuation's own fragment rectangle begins where its content begins — asserted on the
        /// emitted <see cref="BoxFragment"/> rather than on the box, because that rectangle is what paint
        /// consumes and it is the only place this failure is visible.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is the test that separates a whole fix from half of one.</b> Two sites begin a column's
        /// content: the child laid out afresh there, and the box that <i>continues</i> into it
        /// (<c>CssBox.ResumeInTheNextFragmentainer</c>). Correcting only the first leaves a chain two or more
        /// levels deep placing the continuing block at its containing block's re-inserted content edge while
        /// placing that block's own children at the column's — so under <c>slice</c> the block's fragment
        /// rectangle sits 16pt <b>below</b> the rows it contains, and its background and border paint outside
        /// their own content. That reads as correct on every box-level assertion about the rows, which is why
        /// it needs a fragment-level one.
        /// </para>
        /// <para>
        /// Measured on the half-fixed build: the second column's fragment for <c>inner</c> was
        /// <c>(x, 22.00)</c> against a column content top of <c>6.00</c> and rows at <c>6.00</c>. Before
        /// either fix it was <c>(x, 22.00)</c> with rows at <c>22.00</c> — wrong, but at least self-consistent.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData("slice", 0)]
        [InlineData("clone", 16)]
        public async Task AContinuingBlocksFragment_BeginsWhereItsContentBegins(string decorationBreak, double inset)
        {
            var rows = string.Concat(Enumerable.Range(0, 24)
                .Select(i => $"<div id='r{i}' class='row' style='height:20pt; margin:0'>r{i}</div>"));

            // The decorations are on `wrap`, and the rows sit one level further down in `inner`. That is what
            // routes `inner` through the continuing-box site while the rows go through the laid-out-afresh
            // one, so the two must agree for the fragment to contain its own content.
            var (root, container) = await BuildAndLayout(Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300pt; column-fill:auto'>
                    <div id='lead' style='height:20pt; margin:0'>lead</div>
                    <div id='wrap' style='margin:0; padding-top:10pt; border-top:6pt solid red;
                         box-decoration-break:{decorationBreak}'>
                        <div id='inner' style='margin:0; background:#eef; border:1pt solid blue'>{rows}</div>
                    </div>
                </div>"), pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var inner = FindById(root, "inner")!;
            var rowBoxes = FindAllByClass(root, "row");
            var firstInSecondColumn = rowBoxes.First(r => r.Location.X > rowBoxes[0].Location.X + 100);

            // The box-level facts: the continuing block and the content it holds agree with each other, and
            // both sit at the column's content top plus whatever §6.2 re-opens.
            Assert.Equal(mc.ClientTop + inset, inner.Location.Y, 1);
            Assert.Equal(mc.ClientTop + inset, firstInSecondColumn.Location.Y, 1);

            // And the fact that actually reaches paint: `inner`'s fragment in the second column starts at
            // the same coordinate, so its border and background enclose the rows rather than beginning below
            // them.
            var innerFragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .Where(f => ReferenceEquals(f.Box, inner))
                .OrderBy(f => f.WholeBoxRect.X)
                .ToList();

            Assert.Equal(2, innerFragments.Count);

            var secondColumnFragment = innerFragments[1];

            Assert.Equal(mc.ClientTop + inset, secondColumnFragment.WholeBoxRect.Y, 1);
            Assert.True(secondColumnFragment.WholeBoxRect.Y <= firstInSecondColumn.Location.Y + 0.1,
                $"the fragment begins at y={secondColumnFragment.WholeBoxRect.Y} but its first row is at " +
                $"y={firstInSecondColumn.Location.Y}, so its decorations paint below their own content");
        }

        /// <summary>
        /// <see href="https://www.w3.org/TR/css-break-3/#break-decoration">§6.2</see>'s <c>clone</c> at a
        /// column boundary, for a box whose content is <i>blocks</i>. There was no break inside such a box
        /// to reserve room at, which is what bounded the multi-column half of the clone reservation; now
        /// that the break happens, the reservation the word path already makes is what the fill obeys —
        /// the first column stops a whole row short of where <c>slice</c> stops, leaving the cloned bottom
        /// border its room.
        /// </summary>
        [Fact]
        public async Task CloneDecorationsAtAColumnBreak_ReserveTheirRoomInABoxOfBlocks()
        {
            async Task<IReadOnlyList<CssBox>> RowsWith(string decorationBreak)
            {
                var rows = string.Concat(Enumerable.Range(0, 16)
                    .Select(i => $"<div id='r{i}' class='row' style='height:20pt; margin:0'>r{i}</div>"));

                var (root, _) = await BuildAndLayout(Wrap($@"
                    <div id='mc' style='columns:2; column-gap:0; width:300pt; column-fill:auto'>
                        <div id='wrap' style='margin:0; padding:0; border-top:12pt solid red;
                             border-bottom:12pt solid red; box-decoration-break:{decorationBreak}'>{rows}</div>
                    </div>"), pageHeight: 200);

                return FindAllByClass(root, "row");
            }

            var sliced = await RowsWith("slice");
            var cloned = await RowsWith("clone");

            // Both split at the boundary at all, which is the part that did not happen.
            Assert.True(sliced[15].Location.X > sliced[0].Location.X + 100);
            Assert.True(cloned[15].Location.X > cloned[0].Location.X + 100);

            // `slice` fills the band; `clone` stops one row earlier, by the 12pt border it has to close
            // with. Read off which row is the last one still in the first column.
            var lastSliced = sliced.Count(r => r.Location.X <= sliced[0].Location.X + 0.5);
            var lastCloned = cloned.Count(r => r.Location.X <= cloned[0].Location.X + 0.5);

            Assert.Equal(lastSliced - 1, lastCloned);
        }

        /// <summary>
        /// Asserts no two boxes' bounding rectangles overlap — the structural invariant a
        /// two-axis (page, column) fragmentation engine must never violate, since an overlap
        /// means two unrelated pieces of content paint on top of each other.
        /// </summary>
        private static void AssertNoOverlaps(IReadOnlyList<CssBox> boxes)
        {
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];

                    var xOverlap = a.Location.X < b.ActualRight - 0.5 && b.Location.X < a.ActualRight - 0.5;
                    var yOverlap = a.Location.Y < b.ActualBottom - 0.5 && b.Location.Y < a.ActualBottom - 0.5;

                    Assert.False(xOverlap && yOverlap,
                        $"Boxes overlap: [{a.Location.X},{a.Location.Y},{a.ActualRight},{a.ActualBottom}] vs [{b.Location.X},{b.Location.Y},{b.ActualRight},{b.ActualBottom}]");
                }
            }
        }
    }
}
