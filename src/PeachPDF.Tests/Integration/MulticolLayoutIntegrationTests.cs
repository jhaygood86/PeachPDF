using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        public async Task ColumnCount1_DegeneratesToOrdinaryBlockFlow()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:1; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
        public async Task ColumnWidth_ProducesMultipleColumnsAutomatically()
        {
            var html = Wrap(@"
                <div id='mc' style='column-width:100px; column-gap:0; width:320px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);
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
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            var spy = new DrawLineSpyGraphics();
            await mc.Paint(spy);

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
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            var spy = new DrawLineSpyGraphics();
            await mc.Paint(spy);

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
                <div id='mc' style='columns:3; column-gap:0; width:300px'>
                    <div class='item' style='height:50px'></div>
                    <div class='item' style='height:50px'></div>
                    <div class='item' style='height:50px'></div>
                    <div class='item' style='height:40px'></div>
                    <div class='item' style='height:40px'></div>
                    <div class='item' style='height:40px'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
                <div id='mc' style='columns:3; column-gap:0; width:300px'>
                    <div class='item' style='height:50px'></div>
                    <div class='item' style='height:50px'></div>
                    <div class='item' style='height:50px'></div>
                    <div class='item' style='height:40px'></div>
                    <div class='item' style='height:40px'></div>
                    <div class='item' style='height:40px'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 1000);
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
        public async Task NamedString_Y_TracksRealPositionAfterRebanding()
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
        public async Task NamedPageElement_Y_TracksRealPositionAfterRebanding()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' id='i2' style='height:10px; page:chapter'></div>
                    <div class='item' style='height:10px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;

            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be re-banded into column 2");

            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
            Assert.Equal(i2.Location.Y, registered.Y, 1);
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
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override RSize MeasureString(string str, RFont font) => new(0, 12);
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = 0;
            }
            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0) { }
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
            var (root, _) = await BuildAndLayout(Wrap(fragment), pageHeight: 1000);
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
