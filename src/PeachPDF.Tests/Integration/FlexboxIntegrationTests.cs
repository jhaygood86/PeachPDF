using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    public class FlexboxIntegrationTests
    {
        // ─── Property application ───────────────────────────────────────────────

        [Fact]
        public async Task DisplayFlex_PropertyApplied_ToBox()
        {
            var box = await FindByTagAsync("<div style='display:flex'></div>", "div");
            Assert.Equal("flex", box.Display);
        }

        [Fact]
        public async Task InlineFlex_IsInline_ForParent()
        {
            var box = await FindByTagAsync("<div style='display:inline-flex'></div>", "div");
            Assert.True(box.IsInline);
        }

        // ─── Row layout ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Row_ItemsArrangedHorizontally()
        {
            var html = Wrap(@"
                <div style='display:flex; width:300px;'>
                    <div class='item' style='width:50px; height:20px;'></div>
                    <div class='item' style='width:50px; height:20px;'></div>
                    <div class='item' style='width:50px; height:20px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            Assert.True(items[0].Location.X < items[1].Location.X);
            Assert.True(items[1].Location.X < items[2].Location.X);
            Assert.Equal(items[0].Location.Y, items[1].Location.Y, 1.0);
            Assert.Equal(items[1].Location.Y, items[2].Location.Y, 1.0);
        }

        // ─── Column layout ───────────────────────────────────────────────────────

        [Fact]
        public async Task Column_ItemsArrangedVertically()
        {
            var html = Wrap(@"
                <div style='display:flex; flex-direction:column; width:100px;'>
                    <div class='item' style='height:20px;'></div>
                    <div class='item' style='height:20px;'></div>
                    <div class='item' style='height:20px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            Assert.True(items[0].Location.Y < items[1].Location.Y);
            Assert.True(items[1].Location.Y < items[2].Location.Y);
        }

        // ─── flex-grow ───────────────────────────────────────────────────────────

        [Fact]
        public async Task FlexGrow_DistributesSpaceProportionally()
        {
            var html = Wrap(@"
                <div style='display:flex; width:300pt;'>
                    <div id='a' style='flex-grow:1; height:20pt;'></div>
                    <div id='b' style='flex-grow:2; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.InRange(b.ActualBoxSizingWidth / a.ActualBoxSizingWidth, 1.9, 2.1);
            Assert.InRange(a.ActualBoxSizingWidth + b.ActualBoxSizingWidth, 295, 305);
        }

        // ─── flex-shrink ─────────────────────────────────────────────────────────

        [Fact]
        public async Task FlexShrink_ReducesItemsOnOverflow()
        {
            var html = Wrap(@"
                <div style='display:flex; width:100px; flex-wrap:nowrap;'>
                    <div class='item' style='flex-basis:80px; flex-shrink:1; height:20px;'></div>
                    <div class='item' style='flex-basis:80px; flex-shrink:1; height:20px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(2, items.Count);
            Assert.True(items[0].ActualBoxSizingWidth + items[1].ActualBoxSizingWidth <= 102);
        }

        // ─── flex-basis ──────────────────────────────────────────────────────────

        [Fact]
        public async Task FlexBasis_OverridesNaturalSize()
        {
            var html = Wrap(@"
                <div style='display:flex; width:300pt;'>
                    <div id='item' style='flex-basis:100pt; flex-grow:0; flex-shrink:0; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingWidth, 98, 102);
        }

        // ─── justify-content ─────────────────────────────────────────────────────

        [Fact]
        public async Task JustifyContent_Center_OffsetsByHalfFreeSpace()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt; justify-content:center;'>
                    <div id='item' style='width:100pt; height:20pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            double expectedX = container.ClientLeft + 100; // (300 - 100) / 2 = 100pt offset
            Assert.InRange(item.Location.X, expectedX - 2, expectedX + 2);
        }

        [Fact]
        public async Task JustifyContent_Center_PaddedItems_AllThreeVisible()
        {
            // Items have explicit width:50pt + padding:4pt 6pt → outer = 62pt each.
            // 3 × 62 + 2 × 4 (gap) = 194pt in 240pt container → freeSpace = 46pt, startOffset = 23pt.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:240pt; justify-content:center; gap:4pt;'>
                    <div id='a' style='width:50pt; height:20pt; padding:4pt 6pt; flex-shrink:0;'></div>
                    <div id='b' style='width:50pt; height:20pt; padding:4pt 6pt; flex-shrink:0;'></div>
                    <div id='c' style='width:50pt; height:20pt; padding:4pt 6pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            // All items must be within the container
            Assert.True(a.Location.X >= container.ClientLeft - 1, "A must be within container");
            Assert.True(b.Location.X >= container.ClientLeft - 1, "B must be within container");
            Assert.True(c.ActualRight <= container.ClientRight + 1, "C must not overflow container");
            // Items must not overlap
            Assert.True(b.Location.X >= a.ActualRight - 1, "B must not overlap A");
            Assert.True(c.Location.X >= b.ActualRight - 1, "C must not overlap B");
            // Items must be equal outer width
            Assert.InRange(a.ActualBoxSizingWidth, 60, 64);
            Assert.InRange(b.ActualBoxSizingWidth, 60, 64);
            Assert.InRange(c.ActualBoxSizingWidth, 60, 64);
            // Center offset: A should start ~23pt from container left
            Assert.InRange(a.Location.X - container.ClientLeft, 20, 26);
        }

        [Fact]
        public async Task JustifyContent_FlexEnd_PaddedItems_AllThreeVisible()
        {
            // Items have explicit width:50pt + padding:4pt 6pt → outer = 62pt each.
            // freeSpace = 240 - 194 = 46pt, so startOffset = 46pt.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:240pt; justify-content:flex-end; gap:4pt;'>
                    <div id='a' style='width:50pt; height:20pt; padding:4pt 6pt; flex-shrink:0;'></div>
                    <div id='b' style='width:50pt; height:20pt; padding:4pt 6pt; flex-shrink:0;'></div>
                    <div id='c' style='width:50pt; height:20pt; padding:4pt 6pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            // C must be at the right edge of the container
            Assert.InRange(c.ActualRight, container.ClientRight - 2, container.ClientRight + 2);
            // All items visible
            Assert.True(a.Location.X >= container.ClientLeft - 1, "A must be within container");
            // Items must not overlap
            Assert.True(b.Location.X >= a.ActualRight - 1, "B must not overlap A");
            Assert.True(c.Location.X >= b.ActualRight - 1, "C must not overlap B");
        }

        [Fact]
        public async Task JustifyContent_SpaceBetween_FirstAndLastAtEdges()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt; justify-content:space-between;'>
                    <div class='item' style='width:60pt; height:20pt; flex-shrink:0;'></div>
                    <div class='item' style='width:60pt; height:20pt; flex-shrink:0;'></div>
                    <div class='item' style='width:60pt; height:20pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var items = FindAllByClass(root, "item");
            Assert.InRange(items[0].Location.X, container.ClientLeft - 1, container.ClientLeft + 1);
            Assert.InRange(items[2].ActualRight, container.ClientRight - 1, container.ClientRight + 1);
        }

        // ─── align-items ─────────────────────────────────────────────────────────

        [Fact]
        public async Task AlignItems_Center_VerticallyCenter()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; height:100pt; align-items:center;'>
                    <div id='item' style='width:50pt; height:40pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            double expectedY = container.ClientTop + (100 - 40) / 2.0;
            Assert.InRange(item.Location.Y, expectedY - 2, expectedY + 2);
        }

        [Fact]
        public async Task AlignItems_Stretch_FillsCrossAxis()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; height:100pt; align-items:stretch;'>
                    <div id='item' style='width:50pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingHeight, 98, 102);
        }

        // ─── align-self ──────────────────────────────────────────────────────────

        [Fact]
        public async Task AlignSelf_OverridesAlignItems()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; height:100pt; align-items:flex-start;'>
                    <div id='item' style='width:50pt; height:20pt; align-self:flex-end;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            double expectedY = container.ClientTop + 100 - 20;
            Assert.InRange(item.Location.Y, expectedY - 2, expectedY + 2);
        }

        // ─── align-items: baseline ───────────────────────────────────────────────

        [Fact]
        public async Task AlignItems_Baseline_LargerFontStaysAtTop_SmallerFontPushedDown()
        {
            // With true baseline alignment, the item with the larger font (larger ascent) stays
            // at the line's top, while the smaller-font item is pushed down so their text
            // baselines line up. Under the old flex-start fallback both would sit at Y=0.
            var html = Wrap(@"
                <div id='container' style='display:flex; align-items:baseline;'>
                    <div id='small' style='font:10pt Arial;'>Small</div>
                    <div id='big' style='font:30pt Arial;'>Big</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var small = FindById(root, "small")!;
            var big = FindById(root, "big")!;
            Assert.True(big.Location.Y < small.Location.Y,
                $"Larger-font item should stay higher: big.Y={big.Location.Y}, small.Y={small.Location.Y}");
        }

        [Fact]
        public async Task AlignSelfBaseline_OverridesAlignItems()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; align-items:flex-start;'>
                    <div id='small' style='font:10pt Arial; align-self:baseline;'>Small</div>
                    <div id='big' style='font:30pt Arial; align-self:baseline;'>Big</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var small = FindById(root, "small")!;
            var big = FindById(root, "big")!;
            Assert.True(big.Location.Y < small.Location.Y,
                $"align-self:baseline should override align-items:flex-start: big.Y={big.Location.Y}, small.Y={small.Location.Y}");
        }

        [Fact]
        public async Task AlignItems_Baseline_ItemWithNoContent_FallsBackToFlexStart()
        {
            // An item with no line-box content anywhere (no discoverable baseline) must not
            // throw, and falls back to flex-start per spec's synthesized-baseline allowance.
            var html = Wrap(@"
                <div id='container' style='display:flex; align-items:baseline;'>
                    <div id='empty' style='width:20px; height:20px;'></div>
                    <div id='text' style='font:14pt Arial;'>Text</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var empty = FindById(root, "empty")!;
            Assert.InRange(empty.Location.Y, container.ClientTop - 2, container.ClientTop + 2);
        }

        [Fact]
        public async Task AlignItems_Baseline_ColumnDirection_FallsBackToFlexStart()
        {
            // Column-direction flex has no vertical text-baseline concept on its (horizontal)
            // cross axis, so align-items:baseline falls back to flex-start per spec §8.5.
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-direction:column; align-items:baseline;'>
                    <div id='a' style='width:30px; height:20px;'></div>
                    <div id='b' style='width:60px; height:20px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.InRange(a.Location.X, container.ClientLeft - 2, container.ClientLeft + 2);
            Assert.InRange(b.Location.X, container.ClientLeft - 2, container.ClientLeft + 2);
        }

        // ─── flex-wrap ───────────────────────────────────────────────────────────

        [Fact]
        public async Task FlexWrap_WrapsToNewLine()
        {
            var html = Wrap(@"
                <div style='display:flex; flex-wrap:wrap; width:100px;'>
                    <div class='item' style='width:60px; height:20px;'></div>
                    <div class='item' style='width:60px; height:20px;'></div>
                    <div class='item' style='width:60px; height:20px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            Assert.True(items[2].Location.Y > items[0].Location.Y);
        }

        // ─── order ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Order_ReordersItems()
        {
            var html = Wrap(@"
                <div style='display:flex; width:300px;'>
                    <div id='first'  style='width:50px; height:20px; order:2;'></div>
                    <div id='second' style='width:50px; height:20px; order:1;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var first  = FindById(root, "first")!;
            var second = FindById(root, "second")!;
            Assert.True(second.Location.X < first.Location.X);
        }

        // ─── row-reverse ─────────────────────────────────────────────────────────

        [Fact]
        public async Task RowReverse_ItemsArrangedRightToLeft()
        {
            var html = Wrap(@"
                <div style='display:flex; flex-direction:row-reverse; width:300px;'>
                    <div class='item' style='width:50px; height:20px;'></div>
                    <div class='item' style='width:50px; height:20px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.True(items[0].Location.X > items[1].Location.X);
        }

        // ─── Auto-width items (no explicit width, no flex-grow) ──────────────────

        [Fact]
        public async Task AutoWidth_NoFlexGrow_ItemsAreVisible()
        {
            // Items with text content and no explicit width must not collapse to zero.
            var html = Wrap(@"
                <div style='display:flex; width:300px;'>
                    <div class='item' style='height:20px; padding:4px;'>A</div>
                    <div class='item' style='height:20px; padding:4px;'>B</div>
                    <div class='item' style='height:20px; padding:4px;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            foreach (var item in items)
                Assert.True(item.ActualBoxSizingWidth > 0, "Item should have positive width");
        }

        [Fact]
        public async Task AutoWidth_NoFlexGrow_ItemsArrangedHorizontally()
        {
            // Auto-width items should be placed side by side, not all at the same position.
            var html = Wrap(@"
                <div style='display:flex; width:300px;'>
                    <div class='item' style='height:20px; padding:4px;'>A</div>
                    <div class='item' style='height:20px; padding:4px;'>B</div>
                    <div class='item' style='height:20px; padding:4px;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            Assert.True(items[0].Location.X < items[1].Location.X, "A should be left of B");
            Assert.True(items[1].Location.X < items[2].Location.X, "B should be left of C");
        }

        [Fact]
        public async Task AutoWidth_NoFlexGrow_ItemsUseContentWidth()
        {
            // Auto-width items with no flex-grow should use their max-content width (not fill the container).
            // Items with identical content should be equal width, and together narrower than the container.
            var html = Wrap(@"
                <div style='display:flex; width:300px;'>
                    <div id='a' style='height:20px; padding:0 10px;'>X</div>
                    <div id='b' style='height:20px; padding:0 10px;'>X</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // Items should be visible
            Assert.True(a.ActualBoxSizingWidth > 0 && b.ActualBoxSizingWidth > 0, "Both items should be visible");
            // Items should be equal width (same content)
            Assert.InRange(a.ActualBoxSizingWidth, b.ActualBoxSizingWidth - 2, b.ActualBoxSizingWidth + 2);
            // Items together should be much less than the 300px container (not filling it)
            Assert.True(a.ActualBoxSizingWidth + b.ActualBoxSizingWidth < 200,
                $"Auto-width items should not fill container: {a.ActualBoxSizingWidth} + {b.ActualBoxSizingWidth} >= 200");
        }

        [Fact]
        public async Task Column_AutoHeight_ContainerExpandsToFitAllItems()
        {
            // Column flex with auto height must expand to hold all items (not collapse to zero).
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-direction:column; width:100pt;'>
                    <div class='item' style='height:20pt;'>A</div>
                    <div class='item' style='height:20pt;'>B</div>
                    <div class='item' style='height:20pt;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            // All items must have distinct Y positions
            Assert.True(items[0].Location.Y < items[1].Location.Y, "A should be above B");
            Assert.True(items[1].Location.Y < items[2].Location.Y, "B should be above C");
            // Container must be tall enough to contain all items
            Assert.True(container.ActualBoxSizingHeight >= 58, // 3*20=60, allow small margin error
                $"Container should be at least 60pt tall, was {container.ActualBoxSizingHeight}");
        }

        [Fact]
        public async Task ColumnReverse_ItemsArrangedBottomToTop()
        {
            var html = Wrap(@"
                <div style='display:flex; flex-direction:column-reverse; width:100px;'>
                    <div class='item' style='height:20px;'>A</div>
                    <div class='item' style='height:20px;'>B</div>
                    <div class='item' style='height:20px;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            // In column-reverse: A (first in DOM) appears at the bottom, C at top
            Assert.True(items[0].Location.Y > items[1].Location.Y, "A should be below B in column-reverse");
            Assert.True(items[1].Location.Y > items[2].Location.Y, "B should be below C in column-reverse");
        }

        [Fact]
        public async Task AutoWidth_MinWidth_EnforcedAsHypotheticalSize()
        {
            // Items with min-width:40pt and short text should all be at least 40pt wide.
            var html = Wrap(@"
                <div style='display:flex; width:300pt;'>
                    <div class='item' style='min-width:40pt; height:20pt; padding:0 5pt;'>A</div>
                    <div class='item' style='min-width:40pt; height:20pt; padding:0 5pt;'>B</div>
                    <div class='item' style='min-width:40pt; height:20pt; padding:0 5pt;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            foreach (var item in items)
                Assert.True(item.ActualBoxSizingWidth >= 38, // allow small rounding
                    $"Item should be at least 40pt wide, was {item.ActualBoxSizingWidth}");
            // All items should be equal width since all have same min-width and same content
            Assert.InRange(items[0].ActualBoxSizingWidth, items[1].ActualBoxSizingWidth - 2, items[1].ActualBoxSizingWidth + 2);
            Assert.InRange(items[1].ActualBoxSizingWidth, items[2].ActualBoxSizingWidth - 2, items[2].ActualBoxSizingWidth + 2);
        }

        // ─── Gap support ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ColumnGap_SpacesItemsApart()
        {
            // With column-gap:20pt, each adjacent pair of items should be 20pt apart.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt; column-gap:20pt;'>
                    <div class='item' style='width:60pt; height:20pt; flex-shrink:0;'></div>
                    <div class='item' style='width:60pt; height:20pt; flex-shrink:0;'></div>
                    <div class='item' style='width:60pt; height:20pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            double gap01 = items[1].Location.X - items[0].ActualRight;
            double gap12 = items[2].Location.X - items[1].ActualRight;
            Assert.InRange(gap01, 18, 22);
            Assert.InRange(gap12, 18, 22);
        }

        [Fact]
        public async Task Gap_Shorthand_SpacesItemsApart()
        {
            // gap shorthand should apply to column-gap for row flex.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt; gap:15pt;'>
                    <div class='item' style='width:80pt; height:20pt; flex-shrink:0;'></div>
                    <div class='item' style='width:80pt; height:20pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(2, items.Count);
            double gap = items[1].Location.X - items[0].ActualRight;
            Assert.InRange(gap, 13, 17);
        }

        // ─── flex-wrap: wrap-reverse ─────────────────────────────────────────────

        [Fact]
        public async Task WrapReverse_LastItemAtTop_FirstItemAtBottom()
        {
            // 3 items each 150pt wide in a 200pt container → each wraps to its own line.
            // wrap-reverse reverses line stacking: C (last DOM, last line) should be at top, A at bottom.
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-wrap:wrap-reverse; width:200pt; gap:4pt;'>
                    <div id='a' style='width:150pt; height:30pt; flex-shrink:0;'></div>
                    <div id='b' style='width:150pt; height:30pt; flex-shrink:0;'></div>
                    <div id='c' style='width:150pt; height:30pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            Assert.True(c.Location.Y < b.Location.Y, $"C should be above B: C.Y={c.Location.Y}, B.Y={b.Location.Y}");
            Assert.True(b.Location.Y < a.Location.Y, $"B should be above A: B.Y={b.Location.Y}, A.Y={a.Location.Y}");
            // C should sit at the container's top edge
            Assert.InRange(c.Location.Y, container.ClientTop - 1, container.ClientTop + 1);
            // Lines are 30pt high with 4pt gap → 34pt between line starts
            Assert.InRange(b.Location.Y - c.Location.Y, 32, 36);
            Assert.InRange(a.Location.Y - b.Location.Y, 32, 36);
        }

        [Fact]
        public async Task WrapReverse_ContainerHeightFitsAllLines()
        {
            // Container height must expand to include all 3 reversed lines (3×30 + 2×4 = 98pt).
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-wrap:wrap-reverse; width:200pt; gap:4pt;'>
                    <div id='a' style='width:150pt; height:30pt; flex-shrink:0;'></div>
                    <div id='b' style='width:150pt; height:30pt; flex-shrink:0;'></div>
                    <div id='c' style='width:150pt; height:30pt; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            Assert.InRange(container.ActualBoxSizingHeight, 96, 100);
        }

        [Fact]
        public async Task WrapReverse_ItemsWithinLinePreserveDOMOrder()
        {
            // A and B (60px each) fit on line 1 in a 160px container; C wraps to line 2.
            // wrap-reverse puts C's line at top, A/B's line at bottom.
            // Within line 1, A must still be left of B.
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-wrap:wrap-reverse; width:160px; gap:4px;'>
                    <div id='a' style='width:60px; height:30px; flex-shrink:0;'></div>
                    <div id='b' style='width:60px; height:30px; flex-shrink:0;'></div>
                    <div id='c' style='width:60px; height:30px; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            // C on reversed top line, A/B on reversed bottom line
            Assert.True(c.Location.Y < a.Location.Y, $"C should be above A: C.Y={c.Location.Y}, A.Y={a.Location.Y}");
            Assert.True(c.Location.Y < b.Location.Y, $"C should be above B: C.Y={c.Location.Y}, B.Y={b.Location.Y}");
            // A and B are on the same line; A precedes B in DOM so A is left
            Assert.True(a.Location.X < b.Location.X, $"A must be left of B: A.X={a.Location.X}, B.X={b.Location.X}");
            Assert.Equal(a.Location.Y, b.Location.Y, 1.0);
        }

        // ─── Showcase scenario regression ────────────────────────────────────────

        [Fact]
        public async Task WrapReverse_ShowcaseItems_AllThreeVisible()
        {
            // Mirrors the showcase section 6 wrap-reverse HTML, with the box lengths in pt so the
            // hand-authored expectations below stay unit-exact (1px = 0.75pt would scale them):
            // items: width:90pt; height:24pt; padding:4pt 6pt; font:bold 7pt Arial
            // outer = 90+12=102pt; container=200pt → each item on own line.
            // wrap-reverse: C at top (y≈containerTop), B in middle, A at bottom.
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-wrap:wrap-reverse; width:200pt; gap:4pt;'>
                    <div id='a' style='background:#e74c3c;color:#fff;font:bold 7pt Arial;padding:4pt 6pt;min-width:28pt;text-align:center;width:90pt;height:24pt;'>A</div>
                    <div id='b' style='background:#3498db;color:#fff;font:bold 7pt Arial;padding:4pt 6pt;min-width:28pt;text-align:center;width:90pt;height:24pt;'>B</div>
                    <div id='c' style='background:#27ae60;color:#fff;font:bold 7pt Arial;padding:4pt 6pt;min-width:28pt;text-align:center;width:90pt;height:24pt;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;

            // C (last DOM, last line) should be at the top, A (first DOM, first line) at bottom
            Assert.True(c.Location.Y < b.Location.Y, $"C should be above B: C.Y={c.Location.Y}, B.Y={b.Location.Y}");
            Assert.True(b.Location.Y < a.Location.Y, $"B should be above A: B.Y={b.Location.Y}, A.Y={a.Location.Y}");

            // C should be at container top
            Assert.InRange(c.Location.Y, container.ClientTop - 1, container.ClientTop + 1);

            // Lines are 32pt high (24pt content + 4+4 padding) with 4pt gap → 36pt between line starts
            Assert.InRange(b.Location.Y - c.Location.Y, 34, 38);
            Assert.InRange(a.Location.Y - b.Location.Y, 34, 38);

            // Container must accommodate all 3 lines: 3×32 + 2×4 = 104 content + 2 borders
            Assert.InRange(container.ActualBoxSizingHeight, 102, 110);
        }

        [Fact]
        public async Task ShowcaseScenario_ExplicitWidthWithFontAndText_CorrectOuterSize()
        {
            // Mirrors the showcase FItem (box lengths in pt to keep the hand-authored numbers
            // unit-exact): font:bold 7pt Arial; padding:4pt 6pt; min-width:28pt; width:50pt; text content.
            // Width is explicit so hypothetical = 50 + 12(padding) = 62. All 3 items (194pt) fit in 240pt container.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:240pt; justify-content:center; gap:4pt;'>
                    <div id='a' style='background:#e74c3c;color:#fff;font:bold 7pt Arial;padding:4pt 6pt;min-width:28pt;text-align:center;width:50pt;'>A</div>
                    <div id='b' style='background:#3498db;color:#fff;font:bold 7pt Arial;padding:4pt 6pt;min-width:28pt;text-align:center;width:50pt;'>B</div>
                    <div id='c' style='background:#27ae60;color:#fff;font:bold 7pt Arial;padding:4pt 6pt;min-width:28pt;text-align:center;width:50pt;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            // Each item: width:50pt + padding 6+6 = 62pt outer
            Assert.InRange(a.ActualBoxSizingWidth, 60, 65);
            Assert.InRange(b.ActualBoxSizingWidth, 60, 65);
            Assert.InRange(c.ActualBoxSizingWidth, 60, 65);
            // All 3 items must be within the container bounds (no overflow)
            Assert.True(a.Location.X >= container.ClientLeft - 1, $"A left={a.Location.X} < container.ClientLeft={container.ClientLeft}");
            Assert.True(c.ActualRight <= container.ClientRight + 1, $"C right={c.ActualRight} > container.ClientRight={container.ClientRight}");
            // No overlap between items
            Assert.True(b.Location.X >= a.ActualRight - 1, $"B overlaps A: B.X={b.Location.X} < A.Right={a.ActualRight}");
            Assert.True(c.Location.X >= b.ActualRight - 1, $"C overlaps B: C.X={c.Location.X} < B.Right={b.ActualRight}");
        }

        // ─── Inline-flex ─────────────────────────────────────────────────────────

        [Fact]
        public async Task InlineFlex_Items_ArrangedHorizontally_WithCorrectSize()
        {
            // Inline items with explicit width inside an inline-flex container.
            // The flex engine must blockify inline items so width:20pt is applied,
            // and must arrange them in a horizontal row at the correct positions.
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <span id='container' style='display:inline-flex;'>
                        <span id='r' style='width:20pt; height:15pt;'>R</span>
                        <span id='g' style='width:20pt; height:15pt;'>G</span>
                        <span id='b' style='width:20pt; height:15pt;'>B</span>
                    </span>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var r = FindById(root, "r")!;
            var g = FindById(root, "g")!;
            var b = FindById(root, "b")!;

            Assert.InRange(r.ActualBoxSizingWidth, 18, 22);
            Assert.InRange(g.ActualBoxSizingWidth, 18, 22);
            Assert.InRange(b.ActualBoxSizingWidth, 18, 22);

            Assert.True(r.Location.X < g.Location.X, $"R should be left of G: R.X={r.Location.X}, G.X={g.Location.X}");
            Assert.True(g.Location.X < b.Location.X, $"G should be left of B: G.X={g.Location.X}, B.X={b.Location.X}");

            Assert.InRange(g.Location.Y - r.Location.Y, -1.0, 1.0);
            Assert.InRange(b.Location.Y - r.Location.Y, -1.0, 1.0);
        }

        [Fact]
        public async Task InlineFlex_Gap_AppliedBetweenItems()
        {
            // gap:6pt must separate items; without flex layout the gap is not applied.
            var html = Wrap(@"
                <div style='width:300pt;'>
                    <span id='container' style='display:inline-flex; gap:6pt;'>
                        <span id='r' style='width:20pt; height:15pt;'>R</span>
                        <span id='g' style='width:20pt; height:15pt;'>G</span>
                        <span id='b' style='width:20pt; height:15pt;'>B</span>
                    </span>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var r = FindById(root, "r")!;
            var g = FindById(root, "g")!;
            var b = FindById(root, "b")!;

            var rgGap = g.Location.X - (r.Location.X + r.ActualBoxSizingWidth);
            var gbGap = b.Location.X - (g.Location.X + g.ActualBoxSizingWidth);

            Assert.InRange(rgGap, 4.0, 8.0);
            Assert.InRange(gbGap, 4.0, 8.0);
        }

        [Fact]
        public async Task InlineFlex_AutoWidth_Items_ArrangedHorizontally()
        {
            // Items with auto width (sized by text content + padding) should be placed
            // side-by-side, and the container should shrink to fit its content.
            var html = Wrap(@"
                <div style='width:300px;'>
                    <span id='container' style='display:inline-flex; gap:3px; padding:2px; border:1px solid #bbb;'>
                        <span id='r' style='padding:2px 4px;'>R</span>
                        <span id='g' style='padding:2px 4px;'>G</span>
                        <span id='b' style='padding:2px 4px;'>B</span>
                    </span>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var r = FindById(root, "r")!;
            var g = FindById(root, "g")!;
            var b = FindById(root, "b")!;

            // All items should have the same width (each holds one character + same padding)
            Assert.InRange(Math.Abs(r.ActualBoxSizingWidth - g.ActualBoxSizingWidth), 0, 2);
            Assert.InRange(Math.Abs(g.ActualBoxSizingWidth - b.ActualBoxSizingWidth), 0, 2);

            // Items should be arranged left-to-right
            Assert.True(r.Location.X < g.Location.X, $"R.X={r.Location.X} G.X={g.Location.X}");
            Assert.True(g.Location.X < b.Location.X, $"G.X={g.Location.X} B.X={b.Location.X}");

            // Container should shrink to fit content (not span full parent width)
            Assert.True(container.ActualBoxSizingWidth < 100,
                $"Container is too wide ({container.ActualBoxSizingWidth}px); inline-flex should be content-sized");
        }

        [Fact]
        public async Task InlineFlex_InDivTextFlow_ItemWidthsAreContentSized()
        {
            // Same as the table test but using a plain div with surrounding text
            var html = Wrap(@"
                <div style='width:400px; font:8pt Arial;'>
                    Text before
                    <span id='container' style='display:inline-flex;gap:3px;border:1px solid #bbb;padding:2px;'>
                        <span id='r' style='background:#e74c3c;color:#fff;font:6pt Arial;padding:2px 4px;'>R</span>
                        <span id='g' style='background:#3498db;color:#fff;font:6pt Arial;padding:2px 4px;'>G</span>
                        <span id='b' style='background:#27ae60;color:#fff;font:6pt Arial;padding:2px 4px;'>B</span>
                    </span>
                    text after
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var g = FindById(root, "g")!;
            Assert.True(g.ActualBoxSizingWidth < 50, $"G should be narrow, was {g.ActualBoxSizingWidth}");
        }

        [Fact]
        public async Task InlineFlex_InTextFlow_ItemWidthsAreContentSized()
        {
            // Mirrors the test harness section 10: inline-flex inside a table cell with surrounding text.
            // Items must be content-sized (narrow), not fill the container.
            var html = Wrap(@"
                <table style='width:400px;'><tr>
                <td style='padding:2px;font:8pt Arial'>
                    Text before
                    <span id='container' style='display:inline-flex;gap:3px;vertical-align:middle;border:1px solid #bbb;padding:2px;'>
                        <span id='r' style='background:#e74c3c;color:#fff;font:6pt Arial;padding:2px 4px;'>R</span>
                        <span id='g' style='background:#3498db;color:#fff;font:6pt Arial;padding:2px 4px;'>G</span>
                        <span id='b' style='background:#27ae60;color:#fff;font:6pt Arial;padding:2px 4px;'>B</span>
                    </span>
                    text after — inline-flex sits in the text flow.
                </td>
                </tr></table>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var r = FindById(root, "r")!;
            var g = FindById(root, "g")!;
            var b = FindById(root, "b")!;

            // Items are content-sized: each holds one char + padding:2px 4px → outer ≈ char_width + 8
            Assert.True(r.ActualBoxSizingWidth < 50, $"R should be narrow, was {r.ActualBoxSizingWidth}");
            Assert.True(g.ActualBoxSizingWidth < 50, $"G should be narrow, was {g.ActualBoxSizingWidth}");
            Assert.True(b.ActualBoxSizingWidth < 50, $"B should be narrow, was {b.ActualBoxSizingWidth}");

            // Items arranged left-to-right
            Assert.True(r.Location.X < g.Location.X, $"R.X={r.Location.X} G.X={g.Location.X}");
            Assert.True(g.Location.X < b.Location.X, $"G.X={g.Location.X} B.X={b.Location.X}");

            // Container should be much narrower than the cell (content-sized)
            Assert.True(container.ActualBoxSizingWidth < 100,
                $"Container too wide: {container.ActualBoxSizingWidth}px");
        }

        // ─── max-width / max-height clamping ────────────────────────────────────

        [Fact]
        public async Task MaxWidth_ClampsGrownItem_Row()
        {
            var html = Wrap(@"
                <div style='display:flex; width:300pt;'>
                    <div id='a' style='flex-grow:1; max-width:80pt; height:20pt;'></div>
                    <div id='b' style='flex-grow:1; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.InRange(a.ActualBoxSizingWidth, 76, 84);
            Assert.True(b.ActualBoxSizingWidth > a.ActualBoxSizingWidth,
                $"B should absorb more than clamped A: A={a.ActualBoxSizingWidth}, B={b.ActualBoxSizingWidth}");
        }

        [Fact]
        public async Task MaxHeight_ClampsGrownItem_Column()
        {
            var html = Wrap(@"
                <div style='display:flex; flex-direction:column; height:200pt;'>
                    <div id='a' style='flex-grow:1; max-height:50pt; width:50pt;'></div>
                    <div id='b' style='flex-grow:1; width:50pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.InRange(a.ActualBoxSizingHeight, 46, 54);
            Assert.True(b.ActualBoxSizingHeight > a.ActualBoxSizingHeight,
                $"B should absorb more than clamped A: A={a.ActualBoxSizingHeight}, B={b.ActualBoxSizingHeight}");
        }

        [Fact]
        public async Task MinWidth_OverridesShrink_EvenBeyondContainer()
        {
            // flex-shrink would otherwise reduce the item to container width (50pt);
            // min-width:80pt must win, causing the item to overflow the container.
            var html = Wrap(@"
                <div style='display:flex; width:50pt;'>
                    <div id='item' style='flex-basis:200pt; flex-shrink:1; min-width:80pt; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingWidth, 78, 82);
        }

        // ─── Percentage flex-basis ───────────────────────────────────────────────

        [Fact]
        public async Task FlexBasis_Percentage_ResolvesAgainstDefiniteContainerWidth()
        {
            var html = Wrap(@"
                <div style='display:flex; width:300pt;'>
                    <div id='item' style='flex-basis:50%; flex-grow:0; flex-shrink:0; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingWidth, 148, 152);
        }

        [Fact]
        public async Task FlexBasis_Percentage_OnIndefiniteMainAxis_FallsBackToContentSize()
        {
            // Column direction with auto container height: the main axis is indefinite, so a
            // percentage flex-basis must behave like "auto" (content-based) instead of resolving to 0.
            var html = Wrap(@"
                <div style='display:flex; flex-direction:column; width:100px;'>
                    <div id='item' style='flex-basis:50%;'>Some content that needs height</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.True(item.ActualBoxSizingHeight > 5,
                $"Item should size to its content, not collapse to ~0: {item.ActualBoxSizingHeight}");
        }

        // ─── flex-basis: content ─────────────────────────────────────────────────

        [Fact]
        public async Task FlexBasis_Content_IgnoresExplicitWidth()
        {
            // flex-basis:content must ignore the conflicting explicit width and size to content instead.
            var html = Wrap(@"
                <div style='display:flex; width:300px;'>
                    <div id='item' style='flex-basis:content; width:250px; flex-grow:0; flex-shrink:0; padding:0 5px;'>Hi</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.True(item.ActualBoxSizingWidth < 50,
                $"Item should be content-sized (narrow), not use the explicit 250px width: {item.ActualBoxSizingWidth}");
        }

        // ─── Auto margins (main axis) ────────────────────────────────────────────

        [Fact]
        public async Task AutoMargin_MarginLeft_PushesItemToEnd()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt;'>
                    <div id='item' style='width:50pt; height:20pt; margin-left:auto;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualRight, container.ClientRight - 2, container.ClientRight + 2);
        }

        [Fact]
        public async Task AutoMargin_ShorthandLeftRight_CentersSingleItem()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt;'>
                    <div id='item' style='width:50pt; height:20pt; margin:0 auto;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            double expectedX = container.ClientLeft + (300 - 50) / 2.0;
            Assert.InRange(item.Location.X, expectedX - 2, expectedX + 2);
        }

        [Fact]
        public async Task AutoMargin_OnSecondItem_PushesItApartFromFirst()
        {
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt;'>
                    <div id='a' style='width:50pt; height:20pt;'></div>
                    <div id='b' style='width:50pt; height:20pt; margin-left:auto;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.InRange(a.Location.X, container.ClientLeft - 2, container.ClientLeft + 2);
            Assert.InRange(b.ActualRight, container.ClientRight - 2, container.ClientRight + 2);
        }

        [Fact]
        public async Task AutoMargin_TakesPrecedenceOverJustifyContent()
        {
            // Even with justify-content:center, an auto margin must absorb the free space instead.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300pt; justify-content:center;'>
                    <div id='item' style='width:50pt; height:20pt; margin-left:auto;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualRight, container.ClientRight - 2, container.ClientRight + 2);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // ─── Column cross-axis (horizontal) alignment — issue #133 ────────────────

        [Fact]
        public async Task Column_AlignItemsCenter_ShrinkWrapsAndCentersItem()
        {
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:300px; align-items:center;'>
                    <div id='chip'>Hi</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var chip = FindById(root, "chip")!;

            // Shrink-wrapped to content, not stretched to the full container width.
            Assert.True(chip.ActualWidth < c.ActualWidth - 1,
                $"expected shrink-wrap; chip={chip.ActualWidth} container={c.ActualWidth}");
            // Centered: equal gaps on both sides, and a real (non-zero) left gap.
            var leftGap = chip.Location.X - c.Location.X;
            var rightGap = (c.Location.X + c.ActualWidth) - (chip.Location.X + chip.ActualWidth);
            Assert.Equal(leftGap, rightGap, 1.0);
            Assert.True(leftGap > 1, $"expected a centered left gap, got {leftGap}");
        }

        [Fact]
        public async Task Column_AlignItemsFlexEnd_ShrinkWrapsAndRightAligns()
        {
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:300px; align-items:flex-end;'>
                    <div id='chip'>End</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var chip = FindById(root, "chip")!;

            Assert.True(chip.ActualWidth < c.ActualWidth - 1, "expected shrink-wrap");
            // Right edge aligns to the container's right edge; not at the left edge.
            Assert.Equal(c.Location.X + c.ActualWidth, chip.Location.X + chip.ActualWidth, 1.0);
            Assert.True(chip.Location.X > c.Location.X + 1, "expected right-alignment, not left edge");
        }

        [Fact]
        public async Task Column_AlignSelf_OverridesContainerAlignItems()
        {
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:300px; align-items:center;'>
                    <div id='a' style='align-self:flex-end;'>A</div>
                    <div id='b'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            // 'a' aligns to the right edge (its own align-self:flex-end wins over the container's center).
            Assert.Equal(c.Location.X + c.ActualWidth, a.Location.X + a.ActualWidth, 1.0);
            // 'b' inherits the container's center.
            var bLeft = b.Location.X - c.Location.X;
            var bRight = (c.Location.X + c.ActualWidth) - (b.Location.X + b.ActualWidth);
            Assert.Equal(bLeft, bRight, 1.0);
            Assert.True(bLeft > 1, "expected 'b' centered");
        }

        [Fact]
        public async Task Column_DefaultAlign_StretchesItemsFullWidth_Regression()
        {
            // No align-items => default 'normal' (≡ stretch): items keep the full container width and the
            // left edge (unchanged behavior). Guards against the fix over-shrinking the common default.
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:300px;'>
                    <div id='chip'>Full</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var chip = FindById(root, "chip")!;

            Assert.Equal(c.ActualWidth, chip.ActualWidth, 1.0);
            Assert.Equal(c.Location.X, chip.Location.X, 1.0);
        }

        [Fact]
        public async Task Column_AlignItemsStretch_ExplicitlyFullWidth_Regression()
        {
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:300px; align-items:stretch;'>
                    <div id='chip'>Full</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var chip = FindById(root, "chip")!;

            Assert.Equal(c.ActualWidth, chip.ActualWidth, 1.0);
            Assert.Equal(c.Location.X, chip.Location.X, 1.0);
        }

        [Fact]
        public async Task Column_NonStretch_MinWidth_GrowsShrunkItem()
        {
            // min-width raises the fit-content cross size: 'Hi' shrink-wraps small, but min-width:120px
            // (=90pt) grows it back up while it stays centered (still narrower than the 225pt container).
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:300px; align-items:center;'>
                    <div id='chip' style='min-width:120px;'>Hi</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var chip = FindById(root, "chip")!;

            Assert.Equal(90.0, chip.ActualWidth, 1.0);   // 120px = 90pt
            Assert.True(chip.ActualWidth < c.ActualWidth - 1, "still narrower than the container");
            var leftGap = chip.Location.X - c.Location.X;
            var rightGap = (c.Location.X + c.ActualWidth) - (chip.Location.X + chip.ActualWidth);
            Assert.Equal(leftGap, rightGap, 1.0);
        }

        [Fact]
        public async Task Column_NonStretch_MaxWidth_CapsShrunkItem()
        {
            // max-width caps the fit-content cross size: long content would be wide, but max-width:80px
            // (=60pt) caps it, and it stays centered.
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:300px; align-items:center;'>
                    <div id='chip' style='max-width:80px;'>A much longer label than fits</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var chip = FindById(root, "chip")!;

            Assert.Equal(60.0, chip.ActualWidth, 1.0);   // 80px = 60pt
            var leftGap = chip.Location.X - c.Location.X;
            var rightGap = (c.Location.X + c.ActualWidth) - (chip.Location.X + chip.ActualWidth);
            Assert.Equal(leftGap, rightGap, 1.0);
            Assert.True(leftGap > 1, "expected centered");
        }

        [Fact]
        public async Task Column_NonStretch_OverflowingItemWithCrossMargins_FitsWithinContainer()
        {
            // An overflowing non-stretch column item with horizontal margins is capped at the container's
            // inner width MINUS its margins, so it stays inside the container rather than re-expanding to the
            // full width and pushing the margins into overflow. Container 200px=150pt, margins 20px=15pt each
            // => available 120pt.
            var html = Wrap(@"
                <div id='c' style='display:flex; flex-direction:column; width:200px; align-items:center;'>
                    <div id='chip' style='margin:0 20px;'>A much longer label than fits the container</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var chip = FindById(root, "chip")!;

            Assert.Equal(120.0, chip.ActualWidth, 1.5);            // container 150pt − 2×15pt margins
            Assert.True(chip.Location.X >= c.Location.X - 0.5, "stays within the container left edge");
            Assert.True(chip.Location.X + chip.ActualWidth <= c.Location.X + c.ActualWidth + 0.5,
                "stays within the container right edge");
        }

        [Fact]
        public async Task Column_AlignItemsCenter_ReplacedItem_Centers()
        {
            // The #131 cover case: a fixed-size replaced item (96x48 SVG => 72x36pt) centers horizontally
            // on a column-flex container instead of sitting at the left edge.
            const string svg = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";
            var html = Wrap($@"
                <div id='c' style='display:flex; flex-direction:column; width:300px; align-items:center;'>
                    <img id='img' src=""{svg}"" />
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var c = FindById(root, "c")!;
            var img = FindById(root, "img")!;

            Assert.True(img.ActualWidth < c.ActualWidth - 1, $"expected intrinsic width; img={img.ActualWidth}");
            var leftGap = img.Location.X - c.Location.X;
            var rightGap = (c.Location.X + c.ActualWidth) - (img.Location.X + img.ActualWidth);
            Assert.Equal(leftGap, rightGap, 1.0);
            Assert.True(leftGap > 1, $"expected centered image, left gap {leftGap}");
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private async Task<CssBox> FindByTagAsync(string fragment, string tag)
        {
            var (root, _) = await BuildAndLayout(Wrap(fragment));
            return FindByTag(root, tag)!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            // Match the production PdfGenerator setting: PixelsPerInch=72 → pixelsPerPoint=1.0.
            // Without this, the default PixelsPerPoint=72 makes font heights ≈72px (integer-rounded
            // from a sub-point XFont), which prevents explicit CSS heights from constraining items.
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize  = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, System.StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
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
    }
}
