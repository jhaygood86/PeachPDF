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
                <div style='display:flex; width:300px;'>
                    <div id='a' style='flex-grow:1; height:20px;'></div>
                    <div id='b' style='flex-grow:2; height:20px;'></div>
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
                <div style='display:flex; width:300px;'>
                    <div id='item' style='flex-basis:100px; flex-grow:0; flex-shrink:0; height:20px;'></div>
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
                <div id='container' style='display:flex; width:300px; justify-content:center;'>
                    <div id='item' style='width:100px; height:20px; flex-shrink:0;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            double expectedX = container.ClientLeft + 100; // (300 - 100) / 2 = 100px offset
            Assert.InRange(item.Location.X, expectedX - 2, expectedX + 2);
        }

        [Fact]
        public async Task JustifyContent_Center_PaddedItems_AllThreeVisible()
        {
            // Items have explicit width:50px + padding:4px 6px → outer = 62px each.
            // 3 × 62 + 2 × 4 (gap) = 194px in 240px container → freeSpace = 46px, startOffset = 23px.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:240px; justify-content:center; gap:4px;'>
                    <div id='a' style='width:50px; height:20px; padding:4px 6px; flex-shrink:0;'></div>
                    <div id='b' style='width:50px; height:20px; padding:4px 6px; flex-shrink:0;'></div>
                    <div id='c' style='width:50px; height:20px; padding:4px 6px; flex-shrink:0;'></div>
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
            // Center offset: A should start ~23px from container left
            Assert.InRange(a.Location.X - container.ClientLeft, 20, 26);
        }

        [Fact]
        public async Task JustifyContent_FlexEnd_PaddedItems_AllThreeVisible()
        {
            // Items have explicit width:50px + padding:4px 6px → outer = 62px each.
            // freeSpace = 240 - 194 = 46px, so startOffset = 46px.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:240px; justify-content:flex-end; gap:4px;'>
                    <div id='a' style='width:50px; height:20px; padding:4px 6px; flex-shrink:0;'></div>
                    <div id='b' style='width:50px; height:20px; padding:4px 6px; flex-shrink:0;'></div>
                    <div id='c' style='width:50px; height:20px; padding:4px 6px; flex-shrink:0;'></div>
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
                <div id='container' style='display:flex; width:300px; justify-content:space-between;'>
                    <div class='item' style='width:60px; height:20px; flex-shrink:0;'></div>
                    <div class='item' style='width:60px; height:20px; flex-shrink:0;'></div>
                    <div class='item' style='width:60px; height:20px; flex-shrink:0;'></div>
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
                <div id='container' style='display:flex; height:100px; align-items:center;'>
                    <div id='item' style='width:50px; height:40px;'></div>
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
                <div id='container' style='display:flex; height:100px; align-items:stretch;'>
                    <div id='item' style='width:50px;'></div>
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
                <div id='container' style='display:flex; height:100px; align-items:flex-start;'>
                    <div id='item' style='width:50px; height:20px; align-self:flex-end;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var item = FindById(root, "item")!;
            double expectedY = container.ClientTop + 100 - 20;
            Assert.InRange(item.Location.Y, expectedY - 2, expectedY + 2);
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
                <div id='container' style='display:flex; flex-direction:column; width:100px;'>
                    <div class='item' style='height:20px;'>A</div>
                    <div class='item' style='height:20px;'>B</div>
                    <div class='item' style='height:20px;'>C</div>
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
                $"Container should be at least 60px tall, was {container.ActualBoxSizingHeight}");
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
            // Items with min-width:40px and short text should all be at least 40px wide.
            var html = Wrap(@"
                <div style='display:flex; width:300px;'>
                    <div class='item' style='min-width:40px; height:20px; padding:0 5px;'>A</div>
                    <div class='item' style='min-width:40px; height:20px; padding:0 5px;'>B</div>
                    <div class='item' style='min-width:40px; height:20px; padding:0 5px;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            foreach (var item in items)
                Assert.True(item.ActualBoxSizingWidth >= 38, // allow small rounding
                    $"Item should be at least 40px wide, was {item.ActualBoxSizingWidth}");
            // All items should be equal width since all have same min-width and same content
            Assert.InRange(items[0].ActualBoxSizingWidth, items[1].ActualBoxSizingWidth - 2, items[1].ActualBoxSizingWidth + 2);
            Assert.InRange(items[1].ActualBoxSizingWidth, items[2].ActualBoxSizingWidth - 2, items[2].ActualBoxSizingWidth + 2);
        }

        // ─── Gap support ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ColumnGap_SpacesItemsApart()
        {
            // With column-gap:20px, each adjacent pair of items should be 20px apart.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:300px; column-gap:20px;'>
                    <div class='item' style='width:60px; height:20px; flex-shrink:0;'></div>
                    <div class='item' style='width:60px; height:20px; flex-shrink:0;'></div>
                    <div class='item' style='width:60px; height:20px; flex-shrink:0;'></div>
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
                <div id='container' style='display:flex; width:300px; gap:15px;'>
                    <div class='item' style='width:80px; height:20px; flex-shrink:0;'></div>
                    <div class='item' style='width:80px; height:20px; flex-shrink:0;'></div>
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
            // 3 items each 150px wide in a 200px container → each wraps to its own line.
            // wrap-reverse reverses line stacking: C (last DOM, last line) should be at top, A at bottom.
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-wrap:wrap-reverse; width:200px; gap:4px;'>
                    <div id='a' style='width:150px; height:30px; flex-shrink:0;'></div>
                    <div id='b' style='width:150px; height:30px; flex-shrink:0;'></div>
                    <div id='c' style='width:150px; height:30px; flex-shrink:0;'></div>
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
            // Lines are 30px high with 4px gap → 34px between line starts
            Assert.InRange(b.Location.Y - c.Location.Y, 32, 36);
            Assert.InRange(a.Location.Y - b.Location.Y, 32, 36);
        }

        [Fact]
        public async Task WrapReverse_ContainerHeightFitsAllLines()
        {
            // Container height must expand to include all 3 reversed lines (3×30 + 2×4 = 98px).
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-wrap:wrap-reverse; width:200px; gap:4px;'>
                    <div id='a' style='width:150px; height:30px; flex-shrink:0;'></div>
                    <div id='b' style='width:150px; height:30px; flex-shrink:0;'></div>
                    <div id='c' style='width:150px; height:30px; flex-shrink:0;'></div>
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
            // Exact showcase HTML for section 6 wrap-reverse:
            // items: width:90px; height:24px; padding:4px 6px; font:bold 7pt Arial
            // outer = 90+12=102px; container=200px → each item on own line.
            // wrap-reverse: C at top (y≈containerTop), B in middle, A at bottom.
            var html = Wrap(@"
                <div id='container' style='display:flex; flex-wrap:wrap-reverse; width:200px; gap:4px;'>
                    <div id='a' style='background:#e74c3c;color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;width:90px;height:24px;'>A</div>
                    <div id='b' style='background:#3498db;color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;width:90px;height:24px;'>B</div>
                    <div id='c' style='background:#27ae60;color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;width:90px;height:24px;'>C</div>
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

            // Lines are 32px high (24px content + 4+4 padding) with 4px gap → 36px between line starts
            Assert.InRange(b.Location.Y - c.Location.Y, 34, 38);
            Assert.InRange(a.Location.Y - b.Location.Y, 34, 38);

            // Container must accommodate all 3 lines: 3×32 + 2×4 = 104 content + 2 borders
            Assert.InRange(container.ActualBoxSizingHeight, 102, 110);
        }

        [Fact]
        public async Task ShowcaseScenario_ExplicitWidthWithFontAndText_CorrectOuterSize()
        {
            // Mirrors the showcase FItem: font:bold 7pt Arial; padding:4px 6px; min-width:28px; width:50px; text content.
            // Width is explicit so hypothetical = 50 + 12(padding) = 62. All 3 items (194px) fit in 240px container.
            var html = Wrap(@"
                <div id='container' style='display:flex; width:240px; justify-content:center; gap:4px;'>
                    <div id='a' style='background:#e74c3c;color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;width:50px;'>A</div>
                    <div id='b' style='background:#3498db;color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;width:50px;'>B</div>
                    <div id='c' style='background:#27ae60;color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;width:50px;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            // Each item: width:50px + padding 6+6 = 62px outer
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
            // The flex engine must blockify inline items so width:20px is applied,
            // and must arrange them in a horizontal row at the correct positions.
            var html = Wrap(@"
                <div style='width:300px;'>
                    <span id='container' style='display:inline-flex;'>
                        <span id='r' style='width:20px; height:15px;'>R</span>
                        <span id='g' style='width:20px; height:15px;'>G</span>
                        <span id='b' style='width:20px; height:15px;'>B</span>
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
            // gap:6px must separate items; without flex layout the gap is not applied.
            var html = Wrap(@"
                <div style='width:300px;'>
                    <span id='container' style='display:inline-flex; gap:6px;'>
                        <span id='r' style='width:20px; height:15px;'>R</span>
                        <span id='g' style='width:20px; height:15px;'>G</span>
                        <span id='b' style='width:20px; height:15px;'>B</span>
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

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
