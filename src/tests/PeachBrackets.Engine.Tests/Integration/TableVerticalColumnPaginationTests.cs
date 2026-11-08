using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachBrackets.Engine.Tests.TestSupport;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Real per-column pagination of a vertical table (the columns run down the page, so a page break falls
    /// between two of them) for the shapes beyond a plain grid: <c>rowspan</c>, <c>colspan</c>,
    /// <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>, a <c>&lt;caption&gt;</c> and collapsed borders. Every case lays
    /// 8 columns of 40pt (320pt) against a 260pt page band (page height 300, margin 20): columns 0-5 fit on
    /// page 1, and a break falls before column 6. pt fixtures so expected values read literally.
    /// </summary>
    public class TableVerticalColumnPaginationTests
    {
        private static string Cells(string prefix, int count = 8, string extra = "") =>
            string.Concat(Enumerable.Range(0, count)
                .Select(i => $"<td id='{prefix}{i}' style='height:40pt; width:20pt; padding:0; {extra}'>{i}</td>"));

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string table) =>
            LayoutHarness.LayoutAsync(LayoutHarness.Wrap(table), pageHeight: 300, margin: 20);

        private static CssBox Find(CssBox root, string id) => LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, id)!;

        // ─── rowspan ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task ARowspanCellIsRelocatedWithItsColumnAndItsPlaceholdersFollow()
        {
            // Column 6's cell in row 0 spans both rows; row 1 has no cell of its own there.
            var row0 = string.Concat(Enumerable.Range(0, 8).Select(i =>
                $"<td id='a{i}' {(i == 6 ? "rowspan='2'" : "")} style='height:40pt; width:20pt; padding:0'>{i}</td>"));
            var row1 = string.Concat(Enumerable.Range(0, 8).Where(i => i != 6).Select(i =>
                $"<td id='b{i}' style='height:40pt; width:20pt; padding:0'>{i}</td>"));
            var (root, container) = await LayoutAsync(
                $"<table id='t' style='writing-mode:vertical-rl; border-spacing:0'><tr>{row0}</tr><tr>{row1}</tr></table>");

            var table = Find(root, "t");
            Assert.NotNull(table.PageBreakBottoms);

            // Columns 0-5 stay on page 1 in both rows; columns 6-7 are at the top of page 2 in both.
            foreach (var id in new[] { "a0", "a5", "b0", "b5" })
                Assert.Equal(0, container.SlotStartingAt(Find(root, id).Location.Y));
            foreach (var id in new[] { "a6", "a7", "b7" })
                Assert.Equal(1, container.SlotStartingAt(Find(root, id).Location.Y));

            Assert.Equal(container.PageTopOf(1), Find(root, "a6").Location.Y, 0.01);
            Assert.Equal(container.PageTopOf(1) + 40, Find(root, "a7").Location.Y, 0.01);
            Assert.Equal(container.PageTopOf(1) + 40, Find(root, "b7").Location.Y, 0.01);

            // The spanning cell still covers both rows along the row axis.
            var span = Find(root, "a6");
            Assert.True(span.ActualRight - span.Location.X > 35, "the rowspan cell is two rows thick");
            Assert.Equal(Find(root, "b7").Location.X, span.Location.X, 0.5);
            Assert.Equal(Find(root, "a7").ActualRight, span.ActualRight, 0.5);
        }

        // ─── colspan ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task AColspanCellStaysInOnePieceAndMovesTheColumnsItSpansTogether()
        {
            // Row 0: a0..a4, then one cell over columns 5-6 (80pt), then a7. Columns 5 and 6 are one unit.
            var row0 = Cells("a", 5) + "<td id='wide' colspan='2' style='height:80pt; width:20pt; padding:0'>w</td>" +
                       "<td id='a7' style='height:40pt; width:20pt; padding:0'>7</td>";
            var (root, container) = await LayoutAsync(
                $"<table id='t' style='writing-mode:vertical-rl; border-spacing:0'><tr>{row0}</tr><tr>{Cells("b")}</tr></table>");

            // 5 columns (200pt) fit; the wide cell (two columns, taller than the room left) does not, so
            // the unit moves whole instead of being cut by the boundary.
            var wide = Find(root, "wide");
            var wideLength = wide.ActualBottom - wide.Location.Y;
            Assert.True(wideLength >= 80, "the wide cell spans both of its columns");
            Assert.Equal(0, container.SlotStartingAt(Find(root, "a4").Location.Y));
            Assert.Equal(container.PageTopOf(1), wide.Location.Y, 0.01);
            // Row 1's cells under the wide one move with it, each keeping its own slice of its extent.
            Assert.Equal(wide.Location.Y, Find(root, "b5").Location.Y, 0.01);
            Assert.Equal(wide.Location.Y + 40, Find(root, "b6").Location.Y, 0.01);
            Assert.Equal(wide.ActualBottom, Find(root, "a7").Location.Y, 0.01);
            Assert.Equal(0, container.SlotStartingAt(Find(root, "b4").Location.Y));

            Assert.True(Find(root, "t").PageBreakBottoms!.ContainsKey(0));
        }

        // ─── thead / tfoot ────────────────────────────────────────────────────────

        [Fact]
        public async Task TheHeaderAndFooterCellsOfARelocatedColumnMoveWithItAndTheirSnapshotsAgree()
        {
            var (root, container) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-rl; border-spacing:0'>" +
                $"<thead><tr>{Cells("h")}</tr></thead><tbody><tr>{Cells("b")}</tr></tbody>" +
                $"<tfoot><tr>{Cells("f")}</tr></tfoot></table>");

            var table = Find(root, "t");
            Assert.NotNull(table.PageBreakBottoms);

            foreach (var prefix in new[] { "h", "b", "f" })
            {
                Assert.Equal(0, container.SlotStartingAt(Find(root, prefix + "5").Location.Y));
                Assert.Equal(container.PageTopOf(1), Find(root, prefix + "6").Location.Y, 0.01);
                Assert.Equal(container.PageTopOf(1) + 40, Find(root, prefix + "7").Location.Y, 0.01);
            }

            // What the fragment tree paints for the repeated groups is their proxies' snapshots, which must
            // have been moved too - not left where the unpaginated layout put the cells.
            foreach (var proxy in table.Boxes.OfType<CssProxyBox>())
            {
                var prefix = proxy.SourceBox.DerivedStyle.ActualDisplay == "table-header-group" ? "h" : "f";
                var cell = Find(root, prefix + "6");
                Assert.True(proxy.SourceGeometry!.TryGetGeometry(cell, out var snapshot));
                Assert.Equal(cell.Location.Y, snapshot.Location.Y, 0.01);
                Assert.Equal(cell.ActualBottom, snapshot.ActualBottom, 0.01);
                // The group's own extent grew to the last relocated column.
                Assert.True(proxy.SourceGeometry.TryGetGeometry(proxy.SourceBox, out var group));
                Assert.Equal(Find(root, prefix + "7").ActualBottom, group.ActualBottom, 0.01);
                Assert.Equal(group.ActualBottom, proxy.ActualBottom, 0.01);
            }

            // And the fragment tree really holds the relocated header cell on the second page.
            var onSecondPage = container.FragmentTree!.Fragmentainers[1].Root;
            Assert.Contains(Flatten(onSecondPage), f => ReferenceEquals(f.Box, Find(root, "h6")));
        }

        [Fact]
        public async Task ATableWithOnlyAHeaderPaginatesAcrossColumns()
        {
            var (root, container) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-lr; border-spacing:0'>" +
                $"<thead><tr>{Cells("h")}</tr></thead><tbody><tr>{Cells("b")}</tr></tbody></table>");

            Assert.Equal(container.PageTopOf(1), Find(root, "h6").Location.Y, 0.01);
            Assert.Equal(container.PageTopOf(1), Find(root, "b6").Location.Y, 0.01);
            Assert.Equal(Find(root, "b7").ActualBottom, Find(root, "t").ActualBottom, 0.5);
        }

        // ─── ragged rows ──────────────────────────────────────────────────────────

        [Fact]
        public async Task ARaggedRowIsRelocatedByTheColumnEachOfItsCellsIsIn()
        {
            var (root, container) = await LayoutAsync(
                $"<table id='t' style='writing-mode:vertical-rl; border-spacing:0'><tr>{Cells("a")}</tr><tr>{Cells("b", 7)}</tr></table>");

            Assert.Equal(container.PageTopOf(1), Find(root, "a6").Location.Y, 0.01);
            Assert.Equal(container.PageTopOf(1), Find(root, "b6").Location.Y, 0.01);
            Assert.Equal(0, container.SlotStartingAt(Find(root, "b5").Location.Y));
        }

        // ─── caption ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task ATopCaptionIsLaidOutAgainstTheFirstSliceAloneAndNotRepeated()
        {
            var (root, container) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-rl; border-spacing:0'>" +
                $"<caption id='c' style='padding:0'>A caption that is long enough to wrap in the first slice</caption><tr>{Cells("a")}</tr></table>");

            var table = Find(root, "t");
            var caption = Find(root, "c");
            Assert.NotNull(table.PageBreakBottoms);

            // The columns paginate as without a caption.
            Assert.Equal(0, container.SlotStartingAt(Find(root, "a5").Location.Y));
            Assert.Equal(container.PageTopOf(1), Find(root, "a6").Location.Y, 0.01);

            // The caption spans the first slice (the six columns that fit, 240pt), not the whole 320pt the
            // table is long, and sits entirely on the first page.
            Assert.Equal(table.Location.Y, caption.Location.Y, 0.01);
            Assert.Equal(table.PageBreakBottoms![0], caption.ActualBottom, 0.5);
            Assert.True(caption.ActualBottom <= container.PageBottomOf(0) + 0.01);

            // It is in the fragment tree of the first page and of no other.
            var onPage = container.FragmentTree!.Fragmentainers
                .Select(f => Flatten(f.Root).Any(x => ReferenceEquals(x.Box, caption))).ToList();
            Assert.True(onPage[0]);
            Assert.All(onPage.Skip(1), x => Assert.False(x));

            // The grid's decoration box covers the columns on both pages.
            var decoration = table.TableGridDecorationBox!;
            Assert.Equal(Find(root, "a7").ActualBottom, decoration.ActualBottom, 0.5);
            Assert.Equal(table.ActualBottom, decoration.ActualBottom, 0.5);
        }

        [Fact]
        public async Task ABottomCaptionBelongsToTheLastSlice()
        {
            var (root, container) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-lr; border-spacing:0'>" +
                $"<caption id='c' style='caption-side:bottom; padding:0'>Bottom caption</caption><tr>{Cells("a")}</tr></table>");

            var caption = Find(root, "c");
            Assert.Equal(container.PageTopOf(1), caption.Location.Y, 0.5);
            Assert.Equal(Find(root, "a7").ActualBottom, caption.ActualBottom, 0.5);
            Assert.Equal(1, container.SlotStartingAt(caption.Location.Y));

            var onPage = container.FragmentTree!.Fragmentainers
                .Select(f => Flatten(f.Root).Any(x => ReferenceEquals(x.Box, caption))).ToList();
            Assert.False(onPage[0]);
            Assert.True(onPage[1]);
        }

        [Fact]
        public async Task ACaptionedTableThatFitsOnePageIsLeftAlone()
        {
            var (root, _) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-rl; border-spacing:0'>" +
                $"<caption id='c' style='padding:0'>Short</caption><tr>{Cells("a", 3)}</tr></table>");

            Assert.Null(Find(root, "t").PageBreakBottoms);
            Assert.Equal(Find(root, "t").Location.Y, Find(root, "c").Location.Y, 0.01);
            Assert.Equal(120, Find(root, "c").ActualBottom - Find(root, "c").Location.Y, 1);
        }

        // ─── collapsed borders ────────────────────────────────────────────────────

        [Fact]
        public async Task CollapsedBorderSegmentsAreCutAtTheBreakAndEachPartGoesWithItsPage()
        {
            var (root, container) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-rl; border-collapse:collapse'>" +
                $"<tr>{Cells("a", extra: "border:4pt solid black;")}</tr></table>");

            var table = Find(root, "t");
            Assert.NotNull(table.PageBreakBottoms);

            // Borders make the columns 44pt long, so five fit and the break falls before column 5.
            Assert.Equal(container.PageTopOf(1), Find(root, "a5").Location.Y, 0.01);

            var segments = table.CollapsedBorderSegments!;

            // Every segment lies within one page's band: none straddles the break.
            foreach (var segment in segments)
            {
                var top = segment.Rect.Y;
                var bottom = segment.Rect.Y + segment.Rect.Height;
                var page = container.SlotStartingAt(top + 0.01);
                Assert.True(bottom <= container.PageBottomOf(page) + 0.01, $"segment {segment.Rect} crosses the end of page {page}");
            }

            // The lines between columns (thin along Y) on page 2 are where the relocated columns are: one
            // at the page's content top (the half of the line the break cut) and one after each column.
            var secondPage = segments.Where(s => s.Rect.Y >= container.PageTopOf(1) - 0.01).ToList();
            var firstPage = segments.Where(s => s.Rect.Y < container.PageTopOf(1) - 0.01).ToList();
            Assert.NotEmpty(firstPage);
            Assert.NotEmpty(secondPage);

            // The column-axis lines (IsHorizontal here) that end page 1 stop where the slice stops.
            var lastOnFirst = firstPage.Where(s => s.IsHorizontal).Max(s => s.Rect.Y + s.Rect.Height);
            Assert.Equal(table.PageBreakBottoms![0], lastOnFirst, 0.5);

            // Row-axis lines (tall along Y) run the length of each slice: on page 2 they span the two
            // relocated columns from the page top.
            var tall = secondPage.Where(s => !s.IsHorizontal).ToList();
            Assert.NotEmpty(tall);
            Assert.All(tall, s => Assert.Equal(container.PageTopOf(1), s.Rect.Y, 0.5));
            Assert.All(tall, s => Assert.Equal(Find(root, "a7").ActualBottom, s.Rect.Y + s.Rect.Height, 2.5));
        }

        [Fact]
        public async Task CollapsedBordersOfARepeatedHeaderAreCutAtTheBreakToo()
        {
            var (root, container) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-rl; border-collapse:collapse'>" +
                $"<thead><tr>{Cells("h", extra: "border:4pt solid black;")}</tr></thead>" +
                $"<tbody><tr>{Cells("b", extra: "border:4pt solid black;")}</tr></tbody></table>");

            var table = Find(root, "t");
            Assert.NotNull(table.PageBreakBottoms);

            var header = table.Boxes.OfType<CssProxyBox>().Select(p => p.SourceBox)
                .First(x => x.DerivedStyle.ActualDisplay == "table-header-group");
            foreach (var segment in header.CollapsedBorderSegments ?? [])
            {
                var page = container.SlotStartingAt(segment.Rect.Y + 0.01);
                Assert.True(segment.Rect.Y + segment.Rect.Height <= container.PageBottomOf(page) + 0.01,
                    $"header segment {segment.Rect} crosses the end of page {page} ({container.PageBottomOf(page)}); breaks {string.Join(',', table.PageBreakBottoms!.Select(kv => kv.Value))}");
            }
        }

        [Fact]
        public async Task ACaptionedCollapsedTableWithRowspanAndAHeaderPaginates()
        {
            var (root, container) = await LayoutAsync(
                "<table id='t' style='writing-mode:vertical-rl; border-collapse:collapse'>" +
                "<caption id='c' style='padding:0'>Everything at once</caption>" +
                $"<thead><tr>{Cells("h", extra: "border:2pt solid black;")}</tr></thead>" +
                $"<tbody><tr>{Cells("b", extra: "border:2pt solid black;")}</tr></tbody></table>");

            var table = Find(root, "t");
            Assert.NotNull(table.PageBreakBottoms);
            Assert.Equal(1, container.SlotStartingAt(Find(root, "b7").Location.Y));
            Assert.True(Find(root, "c").ActualBottom <= container.PageBottomOf(0) + 0.01);
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
