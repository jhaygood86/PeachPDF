using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.TestRecordingGraphics;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A repeating table <c>&lt;thead&gt;</c> is one source subtree shown at a different place on every
    /// page, so the live boxes carry only whichever page positioned them last. Paint takes its geometry
    /// from the fragment tree, and must take the <c>overflow: hidden</c> clip from there too — resolving
    /// it off the live boxes lands it a page away and culls the whole repeated row.
    /// </summary>
    public class RepeatedTableHeaderClipIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 10;

        /// <summary>
        /// A table long enough to repeat its header on several pages, whose header cell clips its own
        /// content. Two things about the shape are load-bearing. The clipping box has to be
        /// <i>inside</i> the repeated subtree: the source row-group's <c>ParentBox</c> is detached by
        /// <c>CssLayoutEngineTable.RemoveHeaderFooterFromTree</c>, so the containing-block walk stops
        /// there and never sees the table or the body. And the clipped text has to sit in a box of its
        /// own below it, since the walk starts at the painted box's <i>containing block</i> — text held
        /// directly by the clipping box never asks about it.
        /// </summary>
        private static string ClippingHeaderTable() => LayoutHarness.Wrap(
            "<table style='width:300pt;border-collapse:collapse;font:10pt Arial'>" +
            "<thead><tr><th style='padding:0;text-align:left'>" +
            "<div id='h' style='overflow:hidden;height:14pt'><span>HEADERMARKER</span></div>" +
            "</th></tr></thead><tbody>" +
            string.Join("", Enumerable.Range(1, 30).Select(i =>
                $"<tr><td style='height:14pt;padding:0'>Row {i}</td></tr>")) +
            "</tbody></table>");

        [Fact]
        public async Task ClippedRepeatedHeader_IsPaintedOnEveryPageItRepeatsOn()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(ClippingHeaderTable(),
                pageHeight: PageHeight, margin: Margin);

            var pages = container.FragmentTree!.Fragmentainers.Count;
            Assert.True(pages >= 3, $"fixture must span several pages, got {pages}");

            // Including the intermediate pages, which is where a clip resolved from another page's
            // geometry would cull the row outright.
            for (var page = 0; page < pages; page++)
            {
                var recording = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, recording, page);

                Assert.Contains(recording.DrawStringCalls, w => w.Text.Contains("HEADERMARKER"));
            }
        }

        [Fact]
        public async Task ClippedRepeatedHeader_ClipsAtItsOwnPagesPosition()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(ClippingHeaderTable(),
                pageHeight: PageHeight, margin: Margin);

            var pages = container.FragmentTree!.Fragmentainers.Count;
            Assert.True(pages >= 3, $"fixture must span several pages, got {pages}");

            var bandHeight = PageHeight - 2 * Margin;

            for (var page = 0; page < pages; page++)
            {
                var recording = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, recording, page);

                // Fragment coordinates are fragmentainer-local, so every clip pushed while painting a
                // page belongs to that page's own band. A clip resolved from the shared live boxes
                // instead carries whichever page positioned them last - hundreds of points down the
                // document, so it admits nothing at all on any other page.
                Assert.All(recording.Log.OfType<PushClipCall>(),
                    c => Assert.InRange(c.Rect.Top, -bandHeight, 2 * bandHeight));
            }

            // And the header really is repeated from one shared subtree, so the coupling this guards
            // against is present in the fixture at all.
            var proxies = LayoutHarness.Descendants(root).OfType<CssProxyBox>().ToList();
            Assert.True(proxies.Count >= 3, $"expected one header proxy per page, got {proxies.Count}");
        }

        [Fact]
        public async Task PaintingAPage_DoesNotMoveTheLiveSourceBoxes()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(ClippingHeaderTable(),
                pageHeight: PageHeight, margin: Margin);

            var source = LayoutHarness.Descendants(root).OfType<CssProxyBox>().First().SourceBox;
            var before = Geometry(source);

            // Paint is a read of the fragment tree. It used to write a page's geometry back onto the
            // shared source subtree first, which is the last layout-state mutation left on the paint
            // path - and the reason painting one page changed what the next one would have painted.
            FragmentPaintHarness.PaintPage(container, new TestRecordingGraphics(), page: 0);

            Assert.Equal(before, Geometry(source));
        }

        /// <summary>Every position <c>BoxGeometrySnapshot.Apply</c> would have written.</summary>
        private static string Geometry(CssBox root) =>
            string.Join("|", LayoutHarness.Descendants(root).Select(b =>
                $"{b.Location.X:F3},{b.Location.Y:F3},{b.ActualRight:F3},{b.ActualBottom:F3}," +
                string.Join(";", b.Words.Select(w => $"{w.Left:F3},{w.Top:F3}"))));
    }
}
