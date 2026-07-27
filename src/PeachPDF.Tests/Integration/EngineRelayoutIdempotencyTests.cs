using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Whether laying the same subtree out again reproduces the first result — the property a resumed
    /// fragmentainer pass depends on, since it re-runs an engine's measurement phases to rebuild the
    /// geometry it then resumes into.
    /// </summary>
    /// <remarks>
    /// Not theoretical. A repeating table <c>&lt;thead&gt;</c> is detached from the tree and replaced by
    /// one proxy per page, and nothing removes the proxies, so a second run over the same table finds no
    /// header group and measures taller. These pin the engines that a resumed pass may re-enter, so a
    /// regression shows up here rather than as content landing in the wrong fragmentainer.
    /// </remarks>
    public class EngineRelayoutIdempotencyTests
    {
        // Position and size of every box that carries an id, which is what a resumed pass re-derives.
        private static string GeometryOf(CssBox root, HtmlContainerInt container)
        {
            var parts = LayoutHarness.Descendants(root)
                .Where(b => !string.IsNullOrEmpty(b.HtmlTag?.TryGetAttribute("id")))
                .Select(b => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}@({1:F3},{2:F3})-({3:F3},{4:F3})",
                    b.HtmlTag!.TryGetAttribute("id"),
                    b.Location.X, b.Location.Y, b.ActualRight, b.ActualBottom));

            return string.Join("|", parts)
                   + string.Format(CultureInfo.InvariantCulture, "||size={0:F3}", container.ActualSize.Height);
        }

        private static async Task AssertStableAcrossLayouts(string body, double pageHeight = 842)
        {
            var snapshots = await LayoutHarness.LayoutRepeatedlyAsync(
                LayoutHarness.Wrap(body), passes: 3, GeometryOf, pageHeight: pageHeight);

            Assert.Equal(snapshots[0], snapshots[1]);
            Assert.Equal(snapshots[1], snapshots[2]);
        }

        private static string Items(int count, string extra = "") =>
            string.Concat(Enumerable.Range(1, count).Select(i =>
                $"<div id='i{i}' style='{extra}'>Item {i} with enough words in it to wrap onto more than "
                + "a single line when the column it sits in is narrow.</div>"));

        [Theory]
        [InlineData("display:flex; flex-wrap:wrap; gap:6pt")]
        [InlineData("display:flex; flex-direction:column")]
        [InlineData("display:grid; grid-template-columns:1fr 1fr; gap:6pt")]
        [InlineData("display:grid; grid-template-columns:repeat(3, 1fr)")]
        public async Task EngineContainer_LaidOutAgain_ReproducesItsGeometry(string containerStyle)
        {
            await AssertStableAcrossLayouts($"<div id='c' style='{containerStyle}'>{Items(6)}</div>");
        }

        // The shape that matters most for a resumed pass: content tall enough to cross a boundary, so the
        // second run starts from a tree the first one already paginated.
        [Theory]
        [InlineData("display:flex; flex-direction:column")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        [InlineData("column-count:2")]
        public async Task EngineContainer_TallerThanAPage_LaidOutAgain_ReproducesItsGeometry(string containerStyle)
        {
            await AssertStableAcrossLayouts(
                $"<div id='c' style='{containerStyle}'>{Items(24)}</div>", pageHeight: 300);
        }

        // A plain block container, as the control: whatever the engines do, ordinary flow is stable, so a
        // failure above is the engine's and not the harness's.
        [Fact]
        public async Task PlainBlockFlow_LaidOutAgain_ReproducesItsGeometry()
        {
            await AssertStableAcrossLayouts($"<div id='c'>{Items(24)}</div>", pageHeight: 300);
        }
        private static string TableRows(int count) =>
            string.Concat(Enumerable.Range(1, count).Select(i =>
                $"<tr><td>Row {i} cell one</td><td>Row {i} cell two</td></tr>"));

        private const string RepeatingHeaderTable =
            "<table id='t' style='width:100%'>"
            + "<thead><tr><th>Head A</th><th>Head B</th></tr></thead>"
            + "<tbody>{0}</tbody></table>";

        [Fact]
        public async Task ATableWithARepeatingHeader_LaidOutAgain_DoesNotThrow()
        {
            var body = string.Format(CultureInfo.InvariantCulture, RepeatingHeaderTable, TableRows(40));

            // The engine detaches the <thead> and puts one proxy per page in its place. Nothing removed
            // the proxies, so a second run found the markup's own group gone and — because a proxy
            // inherits the source's style, making its Display *table-header-group* — took the first stale
            // proxy as the header and classified the rest as body rows. A proxy has no cells, so
            // positioning such a "row" threw `Sequence contains no elements` on `row.Boxes.Min(...)`.
            var snapshots = await LayoutHarness.LayoutRepeatedlyAsync(
                LayoutHarness.Wrap(body), passes: 3, GeometryOf, pageHeight: 400);

            Assert.Equal(3, snapshots.Count);
        }

        [Fact]
        public async Task ATableWithARepeatingHeader_LaidOutAgain_ReproducesItsBodyRows()
        {
            var body = string.Format(CultureInfo.InvariantCulture, RepeatingHeaderTable, TableRows(40));

            var rowGeometry = await LayoutHarness.LayoutRepeatedlyAsync(
                LayoutHarness.Wrap(body), passes: 3,
                (root, _) => string.Join("|", LayoutHarness.Descendants(root)
                    .Where(b => b.Display == "table-row")
                    .Select(b => string.Format(CultureInfo.InvariantCulture,
                        "({0:F3},{1:F3})", b.Location.X, b.Location.Y))),
                pageHeight: 400);

            // The structure restore is what makes this hold: every body row lands where it did before,
            // and the repeated header still repeats.
            Assert.Equal(rowGeometry[0], rowGeometry[1]);
            Assert.Equal(rowGeometry[1], rowGeometry[2]);
        }

        [Fact]
        public async Task ATableWithARepeatingHeader_StillMeasuresOneHeaderTallerOnASecondRun()
        {
            var body = string.Format(CultureInfo.InvariantCulture, RepeatingHeaderTable, TableRows(40));

            var heights = await LayoutHarness.LayoutRepeatedlyAsync(
                LayoutHarness.Wrap(body), passes: 3,
                (root, _) =>
                {
                    var table = LayoutHarness.FindById(root, "t")!;
                    return table.ActualBottom - table.Location.Y;
                },
                pageHeight: 400);

            // Characterization of what is left of #353, not the invariant it should be. The body rows are
            // reproduced exactly (above) and the extra height is precisely one header row plus the
            // vertical spacing — the table's own reported height gains a header's worth on the second run
            // and stabilises there. Closing that turns these three into an equality.
            Assert.True(heights[1] > heights[0], $"expected drift, got {heights[0]} then {heights[1]}");
            Assert.Equal(heights[1], heights[2], 3);
        }
    }
}
