using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #1049: a captioned table's own background lives on a separate leaf box
    /// (<see cref="CssBox.TableGridDecorationBox"/>, see <c>CssLayoutEngineTable.EnsureGridDecorationBoxStructure</c>),
    /// and a repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> is read through a
    /// <c>FragmentEmitter</c> <c>CapturedInstance</c> whose <c>DetachedSourceRoot</c> used to be yielded
    /// ahead of every other child regardless of where the decoration box actually sits in the table's own
    /// child list - so the background painted over the header/footer text it should sit behind. This
    /// exercises the case the single-page regression test in <c>CssLayoutEngineTableTests</c> does not:
    /// a repeating group on <i>every</i> page it repeats on, and a repeating <c>&lt;thead&gt;</c> and
    /// <c>&lt;tfoot&gt;</c> present at the same time (two separate <c>DetachedSourceRoot</c> captures
    /// recorded for the same table at the same slot).
    /// </summary>
    public class RepeatingHeaderFooterCaptionBackgroundPaintOrderTests
    {
        private const double PageHeight = 200;
        private const double Margin = 10;
        private const string BackgroundColor = "rgb(238, 238, 238)";

        private static string RepeatingHeaderAndFooterTable() => LayoutHarness.Wrap(
            "<table style='width:100%;background-color:" + BackgroundColor + "'>" +
            "<caption>Caption</caption>" +
            "<thead><tr><th>HEADERMARKER</th></tr></thead>" +
            "<tfoot><tr><td>FOOTERMARKER</td></tr></tfoot>" +
            "<tbody>" +
            string.Join("", System.Linq.Enumerable.Range(1, 30).Select(i =>
                $"<tr><td style='height:14pt;padding:0'>Row {i}</td></tr>")) +
            "</tbody></table>");

        [Fact]
        public async Task RepeatingHeaderAndFooter_BackgroundPaintsBeforeEitherOnEveryPageTheyAppearOn()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(RepeatingHeaderAndFooterTable(),
                pageHeight: PageHeight, margin: Margin);

            var pages = container.FragmentTree!.Fragmentainers.Count;
            Assert.True(pages >= 3, $"fixture must span several pages, got {pages}");

            // The header is repeated on every page it applies to; the footer's own repetition is
            // conditional (css-tables-3 §6.2), so only assert about it on pages it actually shows up on.
            var headerPages = 0;
            var footerPages = 0;

            for (var page = 0; page < pages; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, g, page);

                var backgroundIndex = g.Log.FindIndex(c =>
                    c is TestRecordingGraphics.DrawRectCall r && r.Color is { R: 238, G: 238, B: 238, A: 255 });
                var headerIndex = g.Log.FindIndex(c =>
                    c is TestRecordingGraphics.DrawStringCall s && s.Text.Contains("HEADERMARKER"));
                var footerIndex = g.Log.FindIndex(c =>
                    c is TestRecordingGraphics.DrawStringCall s && s.Text.Contains("FOOTERMARKER"));

                Assert.True(backgroundIndex >= 0, $"table's own background was never painted on page {page}");

                if (headerIndex >= 0)
                {
                    headerPages++;
                    Assert.True(backgroundIndex < headerIndex,
                        $"page {page}: background (index {backgroundIndex}) must paint before the header " +
                        $"(index {headerIndex}), or it covers the header on the raster");
                }

                if (footerIndex >= 0)
                {
                    footerPages++;
                    Assert.True(backgroundIndex < footerIndex,
                        $"page {page}: background (index {backgroundIndex}) must paint before the footer " +
                        $"(index {footerIndex}), or it covers the footer on the raster");
                }
            }

            Assert.True(headerPages >= 3, $"expected the header to repeat on every page, got {headerPages}");

            // Confirms this really goes through the repeating-group DetachedSourceRoot mechanism the fix
            // targets, not some other path that happens not to trigger the bug.
            var proxies = LayoutHarness.Descendants(root).OfType<CssProxyBox>().ToList();
            Assert.True(proxies.Count >= headerPages, $"expected one header proxy per repeating page, got {proxies.Count}");
        }

        /// <summary>
        /// No caption (so no decoration box) and markup order <c>&lt;thead&gt;</c>, <c>&lt;tbody&gt;</c>,
        /// <c>&lt;tfoot&gt;</c> - the shape that puts the repeating <c>&lt;tfoot&gt;</c>'s own
        /// <see cref="CssProxyBox.SourceIndex"/> past every other child still left in the table's own
        /// child list, so <c>FragmentEmitter.ChildrenOf</c>'s merge never finds a later box.Boxes entry to
        /// yield it ahead of and has to fall back to its own trailing "still owed" loop - unlike the
        /// sibling test above, whose fixture puts <c>&lt;tfoot&gt;</c> before <c>&lt;tbody&gt;</c> in
        /// markup and a caption after everything, so its own footer is always caught mid-walk instead.
        /// </summary>
        [Fact]
        public async Task RepeatingFooterWithNothingAfterItInMarkup_StillPaintsAfterTheBodyRows()
        {
            var html = LayoutHarness.Wrap(
                "<table style='width:100%'>" +
                "<thead><tr><th>HEADERMARKER</th></tr></thead>" +
                "<tbody>" +
                string.Join("", Enumerable.Range(1, 20).Select(i =>
                    $"<tr><td style='height:14pt;padding:0'>Row {i}</td></tr>")) +
                "</tbody>" +
                "<tfoot><tr><td>FOOTERMARKER</td></tr></tfoot>" +
                "</table>");

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var pages = container.FragmentTree!.Fragmentainers.Count;
            Assert.True(pages >= 2, $"fixture must span at least two pages, got {pages}");

            var footerPages = 0;

            for (var page = 0; page < pages; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, g, page);

                var bodyIndex = g.Log.FindIndex(c =>
                    c is TestRecordingGraphics.DrawStringCall s && s.Text.Contains("Row"));
                var footerIndex = g.Log.FindIndex(c =>
                    c is TestRecordingGraphics.DrawStringCall s && s.Text.Contains("FOOTERMARKER"));

                if (footerIndex < 0) continue;
                footerPages++;

                Assert.True(bodyIndex >= 0, $"page {page}: footer painted but no body row did");
                Assert.True(bodyIndex < footerIndex,
                    $"page {page}: body row (index {bodyIndex}) must paint before the footer " +
                    $"(index {footerIndex}), matching <tbody> preceding <tfoot> in the markup");
            }

            Assert.True(footerPages > 0, "expected the footer to repeat on at least one page");
        }
    }
}
