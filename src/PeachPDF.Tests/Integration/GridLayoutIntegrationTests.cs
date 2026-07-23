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
