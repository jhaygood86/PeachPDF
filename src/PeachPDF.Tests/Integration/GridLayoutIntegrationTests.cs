using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for the CSS Grid layout engine (issue #232), built stage by stage on the same
    /// <c>HtmlContainerInt</c> + <c>PdfSharpAdapter</c> harness as <see cref="FlexboxIntegrationTests"/>:
    /// build, <c>PerformLayout</c>, walk the box tree, and assert <c>Location</c>/<c>ActualRight</c>/
    /// <c>ActualBottom</c> geometry.
    /// </summary>
    public class GridLayoutIntegrationTests
    {
        // ─── Stage 1: dispatch + trivial grid ────────────────────────────────────

        [Fact]
        public async Task DisplayGrid_PropertyApplied_ToBox()
        {
            var box = await FindByTagAsync("<div style='display:grid'></div>", "div");
            Assert.Equal("grid", box.Display);
        }

        [Fact]
        public async Task InlineGrid_IsInline_ForParent()
        {
            var box = await FindByTagAsync("<div style='display:inline-grid'></div>", "div");
            Assert.True(box.IsInline);
        }

        [Fact]
        public async Task Grid_NoTemplate_StacksItemsVertically_AndContainerFits()
        {
            // With no grid-template yet (Stage 1), items lay out as a single implicit column: each below
            // the previous, and the container grows to hold them all.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:100pt;'>
                    <div class='item' style='height:20pt;'>A</div>
                    <div class='item' style='height:20pt;'>B</div>
                    <div class='item' style='height:20pt;'>C</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            Assert.True(items[0].Location.Y < items[1].Location.Y, "A should be above B");
            Assert.True(items[1].Location.Y < items[2].Location.Y, "B should be above C");
            Assert.True(container.ActualBoxSizingHeight >= 58,
                $"Container should be at least 60pt tall, was {container.ActualBoxSizingHeight}");
        }

        // ─── Stage 2: fixed track sizing + row-major placement ───────────────────

        [Fact]
        public async Task TwoFixedColumns_ItemsTakeTrackWidths_AndSitSideBySide()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:100pt 200pt;'>
                    <div id='a' style='height:20pt;'></div>
                    <div id='b' style='height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // a fills the 100pt track, b the 200pt track; both on the same row.
            Assert.Equal(100, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(200, b.ActualBoxSizingWidth, 1.0);
            Assert.Equal(a.Location.Y, b.Location.Y, 1.0);
            // b starts one track-width to the right of a.
            Assert.Equal(a.Location.X + 100, b.Location.X, 1.0);
        }

        [Fact]
        public async Task RepeatFixedColumns_ExpandsToEqualTracks()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:repeat(3, 100pt);'>
                    <div class='item' style='height:10pt;'></div>
                    <div class='item' style='height:10pt;'></div>
                    <div class='item' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            foreach (var item in items)
                Assert.Equal(100, item.ActualBoxSizingWidth, 1.0);
            Assert.Equal(items[0].Location.X + 100, items[1].Location.X, 1.0);
            Assert.Equal(items[1].Location.X + 100, items[2].Location.X, 1.0);
        }

        [Fact]
        public async Task MoreItemsThanColumns_WrapToNextRow()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:200pt; grid-template-columns:100pt 100pt;'>
                    <div id='a' style='height:20pt;'></div>
                    <div id='b' style='height:20pt;'></div>
                    <div id='c' style='height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            // a,b on row 0; c wraps to row 1, first column (same X as a, below it).
            Assert.Equal(a.Location.Y, b.Location.Y, 1.0);
            Assert.Equal(a.Location.X, c.Location.X, 1.0);
            Assert.True(c.Location.Y > a.Location.Y, "third item should wrap to the next row");
        }

        [Fact]
        public async Task PercentageColumns_ResolveAgainstContainerWidth()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:200pt; grid-template-columns:25% 75%;'>
                    <div id='a' style='height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(50, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(150, b.ActualBoxSizingWidth, 1.0);
        }

        [Fact]
        public async Task ColumnGap_SpacesTracksApart()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:220pt; grid-template-columns:100pt 100pt; column-gap:20pt;'>
                    <div id='a' style='height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(20, b.Location.X - a.ActualRight, 1.0);
        }

        [Fact]
        public async Task ExplicitRowHeights_AreUsed()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:100pt; grid-template-columns:100pt; grid-template-rows:40pt 60pt;'>
                    <div id='a'>A</div>
                    <div id='b'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(40, a.ActualBoxSizingHeight, 1.0);
            Assert.Equal(60, b.ActualBoxSizingHeight, 1.0);
            Assert.Equal(a.Location.Y + 40, b.Location.Y, 1.0);
        }

        // ─── Stage 3: fr distribution + minmax ───────────────────────────────────

        [Fact]
        public async Task FrTracks_DistributeSpaceProportionally()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:1fr 2fr;'>
                    <div id='a' style='height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(100, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(200, b.ActualBoxSizingWidth, 1.0);
        }

        [Fact]
        public async Task FixedAndFrTracks_FrAbsorbsRemainder()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:100pt 1fr;'>
                    <div id='a' style='height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(100, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(200, b.ActualBoxSizingWidth, 1.0);
        }

        [Fact]
        public async Task Minmax_FloorIsRespected_WhenFractionWouldBeSmaller()
        {
            // Two minmax(120pt, 1fr) tracks in a 200pt container: the 1fr share (100pt) is below the
            // 120pt floor, so each track is pinned to its 120pt minimum.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:200pt; grid-template-columns:minmax(120pt,1fr) minmax(120pt,1fr);'>
                    <div id='a' style='height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(120, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(120, b.ActualBoxSizingWidth, 1.0);
        }

        [Fact]
        public async Task Minmax_ZeroFloorOneFr_BehavesLikeFr()
        {
            // The Tailwind grid-cols-2 idiom: repeat(2, minmax(0, 1fr)).
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:repeat(2, minmax(0, 1fr)); column-gap:20pt;'>
                    <div id='a' style='height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // (300 - 20 gap) / 2 = 140 each.
            Assert.Equal(140, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(140, b.ActualBoxSizingWidth, 1.0);
        }

        // ─── Stage 4: explicit placement + spanning ──────────────────────────────

        [Fact]
        public async Task GridColumnSpan_MakesItemSpanTwoTracks()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:100pt 100pt 100pt;'>
                    <div id='a' style='grid-column:1 / 3; height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // a spans columns 1-2 (200pt); b auto-places into column 3.
            Assert.Equal(200, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(100, b.ActualBoxSizingWidth, 1.0);
            Assert.Equal(a.Location.X + 200, b.Location.X, 1.0);
            Assert.Equal(a.Location.Y, b.Location.Y, 1.0);
        }

        [Fact]
        public async Task SpanKeyword_SpansMultipleTracks()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:repeat(3, 100pt);'>
                    <div id='a' style='grid-column:span 2; height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(200, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(100, b.ActualBoxSizingWidth, 1.0);
        }

        [Fact]
        public async Task ExplicitColumnStart_PlacesItemInThatTrack()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:repeat(3, 100pt);'>
                    <div id='a' style='grid-column-start:3; height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            // Placed in the third column: X offset 200pt from the container content-left.
            Assert.Equal(container.ClientLeft + 200, a.Location.X, 1.0);
            Assert.Equal(100, a.ActualBoxSizingWidth, 1.0);
        }

        [Fact]
        public async Task ExplicitRowPlacement_PositionsItemOnThatRow()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:100pt; grid-template-columns:100pt; grid-template-rows:40pt 60pt;'>
                    <div id='a' style='grid-row:2;'>A</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            // On the second row: Y offset by the first (40pt) row.
            Assert.Equal(container.ClientTop + 40, a.Location.Y, 1.0);
            Assert.Equal(60, a.ActualBoxSizingHeight, 1.0);
        }

        [Fact]
        public async Task GridArea_SpansRowsAndColumns()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:200pt; grid-template-columns:100pt 100pt; grid-template-rows:50pt 50pt;'>
                    <div id='a' style='grid-area:1 / 1 / 3 / 3;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            // Spans both columns (200pt) and both rows (100pt).
            Assert.Equal(200, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(100, a.ActualBoxSizingHeight, 1.0);
        }

        // ─── Stage 5: auto-placement (column/dense) + implicit tracks ────────────

        [Fact]
        public async Task GridAutoFlowColumn_FillsDownColumnsFirst()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; grid-auto-flow:column; width:200pt; grid-template-rows:20pt 20pt; grid-template-columns:100pt 100pt;'>
                    <div id='a' style='height:20pt;'></div>
                    <div id='b' style='height:20pt;'></div>
                    <div id='c' style='height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;
            // Column flow: a top-left, b below a (same column), c top of the next column.
            Assert.Equal(a.Location.X, b.Location.X, 1.0);
            Assert.True(b.Location.Y > a.Location.Y, "b should be below a in column flow");
            Assert.True(c.Location.X > a.Location.X, "c should start a new column");
            Assert.Equal(a.Location.Y, c.Location.Y, 1.0);
        }

        [Fact]
        public async Task GridAutoRows_SizesImplicitRows()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:100pt; grid-template-columns:100pt; grid-auto-rows:30pt;'>
                    <div id='a'>A</div>
                    <div id='b'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // Both rows are implicit and sized by grid-auto-rows: 30pt each.
            Assert.Equal(30, a.ActualBoxSizingHeight, 1.0);
            Assert.Equal(30, b.ActualBoxSizingHeight, 1.0);
            Assert.Equal(a.Location.Y + 30, b.Location.Y, 1.0);
        }

        [Fact]
        public async Task GridAutoFlowDense_BackfillsEarlierHoles()
        {
            // a spans 2 cols starting at col 2 (leaving col 1 of row 1 empty); a 1-wide item then
            // dense-backfills that hole instead of flowing after a.
            var html = Wrap(@"
                <div id='container' style='display:grid; grid-auto-flow:row dense; width:300pt; grid-template-columns:repeat(3, 100pt);'>
                    <div id='a' style='grid-column:2 / 4; height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // b backfills column 1 of the first row (same Y as a, at the container's left edge).
            Assert.Equal(container.ClientLeft, b.Location.X, 1.0);
            Assert.Equal(a.Location.Y, b.Location.Y, 1.0);
        }

        // ─── Stage 6: alignment ──────────────────────────────────────────────────

        [Fact]
        public async Task JustifyItemsStart_ItemsUseContentWidth_NotStretched()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; justify-items:start; width:200pt; grid-template-columns:200pt;'>
                    <div id='a' style='height:20pt; padding:0 10pt;'>X</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            // Not stretched to the 200pt track; sits at the track's left edge.
            Assert.True(a.ActualBoxSizingWidth < 100, $"item should shrink to content, was {a.ActualBoxSizingWidth}");
            Assert.Equal(container.ClientLeft, a.Location.X, 1.0);
        }

        [Fact]
        public async Task JustifySelfEnd_PositionsItemAtCellEnd()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:200pt; grid-template-columns:200pt;'>
                    <div id='a' style='justify-self:end; width:40pt; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            // 40pt item right-aligned in a 200pt track → starts at left+160.
            Assert.Equal(container.ClientLeft + 160, a.Location.X, 1.5);
        }

        [Fact]
        public async Task JustifyContentCenter_CentersTracksInContainer()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; justify-content:center; width:300pt; grid-template-columns:100pt;'>
                    <div id='a' style='height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            // Single 100pt track centered in 300pt → offset 100 from the content-left.
            Assert.Equal(container.ClientLeft + 100, a.Location.X, 1.5);
        }

        [Fact]
        public async Task PlaceItemsCenter_AppliesToBothAxes()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; place-items:center; width:200pt; grid-template-columns:200pt; grid-template-rows:100pt;'>
                    <div id='a' style='width:40pt; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            // Centered horizontally (200-40)/2=80 and vertically (100-20)/2=40.
            Assert.Equal(container.ClientLeft + 80, a.Location.X, 1.5);
            Assert.Equal(container.ClientTop + 40, a.Location.Y, 1.5);
        }

        // ─── Stage 7: auto-fill/auto-fit + fit-content + intrinsic ───────────────

        [Fact]
        public async Task RepeatAutoFill_CreatesAsManyTracksAsFit()
        {
            // 620pt container, repeat(auto-fill, 100pt) with 20pt gap: (620+20)/(100+20)=5 tracks.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:620pt; column-gap:20pt; grid-template-columns:repeat(auto-fill, 100pt);'>
                    <div class='item' style='height:10pt;'></div>
                    <div class='item' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            // Two items, each in a 100pt track; the second sits one track+gap to the right.
            Assert.Equal(100, items[0].ActualBoxSizingWidth, 1.0);
            Assert.Equal(100, items[1].ActualBoxSizingWidth, 1.0);
            Assert.Equal(items[0].Location.X + 120, items[1].Location.X, 1.0);
        }

        [Fact]
        public async Task RepeatAutoFillMinmax_TracksExpandToFill()
        {
            // The Tailwind responsive-card idiom: repeat(auto-fill, minmax(200pt, 1fr)) in 640pt (20pt gap):
            // (640+20)/(200+20)=3 tracks, each 1fr → (640-40)/3=200pt.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:640pt; column-gap:20pt; grid-template-columns:repeat(auto-fill, minmax(200pt, 1fr));'>
                    <div class='item' style='height:10pt;'></div>
                    <div class='item' style='height:10pt;'></div>
                    <div class='item' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(3, items.Count);
            foreach (var item in items)
                Assert.Equal(200, item.ActualBoxSizingWidth, 1.5);
        }

        [Fact]
        public async Task AutoFit_CollapsesEmptyTracks_PopulatedTracksFillWidth()
        {
            // repeat(auto-fit, minmax(100pt,1fr)) in 640pt yields room for several tracks, but with one item
            // the empty tracks collapse and the single populated track fills the whole width.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:640pt; grid-template-columns:repeat(auto-fit, minmax(100pt, 1fr));'>
                    <div id='a' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            Assert.Equal(640, a.ActualBoxSizingWidth, 2.0);
        }

        [Fact]
        public async Task AutoColumn_SizesToContent()
        {
            // An auto column takes its width from its content; the fr column absorbs the rest.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:auto 1fr;'>
                    <div id='a' style='height:10pt; width:60pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(60, a.ActualBoxSizingWidth, 2.0);
            Assert.Equal(240, b.ActualBoxSizingWidth, 2.0);
        }

        // ─── Additional coverage: sizing edges + property parsing ────────────────

        [Fact]
        public async Task DefiniteHeight_RowsResolveAgainstIt_AndPercentRowWorks()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:100pt; height:200pt; grid-template-columns:100pt; grid-template-rows:25% 75%;'>
                    <div id='a'></div>
                    <div id='b'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(50, a.ActualBoxSizingHeight, 1.5);
            Assert.Equal(150, b.ActualBoxSizingHeight, 1.5);
        }

        [Fact]
        public async Task RowSpanningItem_GrowsAutoRowToFitTallContent()
        {
            // Auto rows (no grid-auto-rows) so the span can grow them; a's 70pt content exceeds its two
            // spanned rows' natural size, so the last spanned row grows and pushes row 3 down.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:100pt; grid-template-columns:100pt;'>
                    <div id='a' style='grid-row:span 2; height:70pt;'></div>
                    <div id='b' style='grid-row:3;'>b</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.True(b.Location.Y >= a.Location.Y + 70 - 1, $"b should sit below the grown span, a.Y={a.Location.Y} b.Y={b.Location.Y}");
        }

        [Fact]
        public async Task EmptyGrid_ProducesNoError()
        {
            var box = await FindByTagAsync("<div style='display:grid; width:100pt; grid-template-columns:50pt 50pt;'></div>", "div");
            Assert.Equal("grid", box.Display);
        }

        [Fact]
        public async Task MinContentColumn_SizesToLongestWord()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:400pt; grid-template-columns:min-content 1fr;'>
                    <div id='a' style='font:12pt Arial;'>Hi</div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // The min-content column is narrow (just fits "Hi"); the 1fr column takes the rest.
            Assert.True(a.ActualBoxSizingWidth < 80, $"min-content column should be narrow, was {a.ActualBoxSizingWidth}");
            Assert.True(b.ActualBoxSizingWidth > 300, $"1fr column should take the remainder, was {b.ActualBoxSizingWidth}");
        }

        [Fact]
        public async Task FitContentColumn_CapsAtArgument()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:400pt; grid-template-columns:fit-content(50pt) 1fr;'>
                    <div id='a' style='width:200pt; height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var b = FindById(root, "b")!;
            // The item wants 200pt but fit-content(50pt) caps the track at 50pt, so the 1fr column (b)
            // starts at the content-left + 50pt.
            Assert.Equal(container.ClientLeft + 50, b.Location.X, 2.0);
        }

        [Fact]
        public async Task PlaceContentAndPlaceSelf_Parse()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; place-content:center; width:300pt; grid-template-columns:100pt;'>
                    <div id='a' style='place-self:end; width:40pt; height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            // place-content:center centers the single 100pt track (offset 100); place-self:end right-aligns
            // the 40pt item in that track (+60).
            var container = FindById(root, "container")!;
            Assert.Equal(container.ClientLeft + 100 + 60, a.Location.X, 2.0);
        }

        [Fact]
        public async Task GridAutoColumns_SizesImplicitColumn_InColumnFlow()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; grid-auto-flow:column; grid-template-rows:20pt; grid-auto-columns:70pt; width:400pt;'>
                    <div id='a' style='height:20pt;'></div>
                    <div id='b' style='height:20pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // Column flow with no explicit columns: each item makes an implicit 70pt column.
            Assert.Equal(70, a.ActualBoxSizingWidth, 2.0);
            Assert.Equal(70, b.ActualBoxSizingWidth, 2.0);
            Assert.Equal(a.Location.X + 70, b.Location.X, 2.0);
        }

        // ─── Post-review regression coverage ─────────────────────────────────────

        [Fact]
        public async Task AutoAutoColumns_StretchToFillContainer_ByDefault()
        {
            // Two auto columns with the default justify-content (normal → stretch) share the container.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:400pt; grid-template-columns:auto auto;'>
                    <div id='a' style='height:10pt;'>A</div>
                    <div id='b' style='height:10pt;'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(200, a.ActualBoxSizingWidth, 3.0);
            Assert.Equal(200, b.ActualBoxSizingWidth, 3.0);
        }

        [Fact]
        public async Task MultiTrackAutoFill_CountsRepetitionsCorrectly()
        {
            // repeat(auto-fill, 30pt 30pt 30pt) with 10pt gap in 720pt: (720+10)/(90+10)=7 -> but the
            // repeated group's own internal gaps are already counted, so the real fit is 6 repetitions
            // (18 tracks). Two items land in the first two tracks, one track+gap apart.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:720pt; column-gap:10pt; grid-template-columns:repeat(auto-fill, 30pt 30pt 30pt);'>
                    <div id='a' style='height:10pt;'></div>
                    <div id='b' style='height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(30, a.ActualBoxSizingWidth, 1.0);
            Assert.Equal(a.Location.X + 40, b.Location.X, 1.0);
        }

        [Fact]
        public async Task DefiniteRow_MoreItemsThanColumns_DoesNotHang()
        {
            // Three items all locked to row 1 of a 2-column grid: the third cannot fit the full row, but
            // placement must terminate (previously an infinite loop) rather than hang.
            var html = Wrap(@"
                <div id='container' style='display:grid; width:200pt; grid-template-columns:100pt 100pt;'>
                    <div id='a' style='grid-row:1; height:10pt;'></div>
                    <div id='b' style='grid-row:1; height:10pt;'></div>
                    <div id='c' style='grid-row:1; height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            // The key assertion is that BuildAndLayout returned at all (no hang); all three items exist.
            Assert.NotNull(FindById(root, "a"));
            Assert.NotNull(FindById(root, "b"));
            Assert.NotNull(FindById(root, "c"));
        }

        // ─── Pagination ──────────────────────────────────────────────────────────

        [Fact]
        public async Task GridTallerThanPage_PaginatesWithoutError()
        {
            // A grid whose rows exceed one A4 page must render across pages without throwing.
            var rows = string.Concat(Enumerable.Range(0, 40)
                .Select(i => $"<div class='item' style='height:40pt;'>row {i}</div>"));
            var html = Wrap($@"
                <div style='display:grid; grid-template-columns:1fr 1fr;'>{rows}</div>");
            var (root, container) = await BuildAndLayout(html);
            var items = FindAllByClass(root, "item");
            Assert.Equal(40, items.Count);
            // The grid extends well past a single A4 page (842pt) in total height.
            Assert.True(container.ActualSize.Height > 842, $"grid should span multiple pages, was {container.ActualSize.Height}");
        }

        // ─── Named lines (#261) ──────────────────────────────────────────────────

        [Fact]
        public async Task NamedLine_PlacesItemAtThatLine()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:[c1] 100pt [c2] 100pt [c3] 100pt [c4];'>
                    <div id='a' style='grid-column-start:c3; height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var a = FindById(root, "a")!;
            // Line c3 is the 3rd column line (index 2) → the item starts at content-left + 200pt.
            Assert.Equal(container.ClientLeft + 200, a.Location.X, 1.5);
        }

        [Fact]
        public async Task NamedLineRange_SpansBetweenTwoNamedLines()
        {
            var html = Wrap(@"
                <div id='container' style='display:grid; width:300pt; grid-template-columns:[start] 100pt 100pt [mid] 100pt [end];'>
                    <div id='a' style='grid-column-start:start; grid-column-end:mid; height:10pt;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            // start = line 1, mid = line 3 → spans columns 1-2 = 200pt.
            Assert.Equal(200, a.ActualBoxSizingWidth, 1.5);
        }

        // ─── grid-template-areas (#261) ──────────────────────────────────────────

        [Fact]
        public async Task GridTemplateAreas_EstablishesTrackCounts_AndAreaEdgeLinesPlaceItems()
        {
            // A 2-row × 3-col area grid. Explicit track sizes are given so the test exercises area
            // placement (the #261 feature) against known geometry; an item placed by an area's
            // -start/-end edges (via the suffix rule on the longhands) fills exactly that area.
            var html = Wrap(@"
                <div id='container' style=""display:grid; width:300pt;
                     grid-template-columns:100pt 100pt 100pt; grid-template-rows:60pt 100pt;
                     grid-template-areas: 'header header header' 'nav main main';"">
                    <div id='m' style=""grid-row-start:main; grid-row-end:main;
                         grid-column-start:main; grid-column-end:main;""></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var m = FindById(root, "m")!;
            // main occupies row 1 (cols 1-2): 2 columns = 200pt wide, the 100pt second row tall,
            // starting at column 1 (100pt) and row 1 (60pt).
            Assert.Equal(200, m.ActualBoxSizingWidth, 2.0);
            Assert.Equal(100, m.ActualBoxSizingHeight, 2.0);
            Assert.Equal(container.ClientLeft + 100, m.Location.X, 2.0);
            Assert.Equal(container.ClientTop + 60, m.Location.Y, 2.0);
        }

        [Fact]
        public async Task GridTemplateAreas_WithNoExplicitColumns_DerivesColumnCountFromAreas()
        {
            // No grid-template-columns: the 3-column area grid drives the column count, and the columns
            // (auto, default stretch) share the container width — so each area cell is 100pt wide.
            var html = Wrap(@"
                <div id='container' style=""display:grid; width:300pt; grid-template-rows:40pt;
                     grid-template-areas: 'a b c';"">
                    <div id='b' style=""grid-column-start:b; grid-column-end:b; height:10pt;""></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var container = FindById(root, "container")!;
            var b = FindById(root, "b")!;
            // b area is the middle column → starts at content-left + 100pt, one column (100pt) wide.
            Assert.Equal(container.ClientLeft + 100, b.Location.X, 2.0);
            Assert.Equal(100, b.ActualBoxSizingWidth, 2.0);
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
