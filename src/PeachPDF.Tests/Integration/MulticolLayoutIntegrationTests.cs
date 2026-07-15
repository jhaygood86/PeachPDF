using PeachPDF.Adapters;
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

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
