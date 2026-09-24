using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// How <c>FragmentPainter</c> drives the raster backend for CSS <c>filter</c>: driven through a real page layout,
    /// once with a graphics that cannot rasterize (the paint must fall back to what it did before the raster backend
    /// existed) and once with a raster graphics as the outer surface (no PDF anywhere - the standalone seam).
    /// </summary>
    public class FilterRasterPaintTests
    {
        private const string Box = "width:40pt;height:30pt;background:#ff0000;";

        private static string Html(string style) =>
            LayoutHarness.Wrap($"<div style=\"margin:0;{Box}{style}\"></div>");

        [Fact]
        public async Task WithoutRasterSupport_TheElementStillPaintsAsBefore()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Html("filter:blur(3pt) grayscale(1);"), margin: 0);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, recording);

            // No raster surface was available, so the box paints its own background rather than vanishing.
            Assert.Contains(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        private static RasterGraphics NewPage(int width, int height, PdfSharpAdapter adapter)
        {
            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            return new RasterGraphics(adapter, surface, 1);
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        [Fact]
        public async Task Grayscale_TurnsTheRedBoxGrey_WhenPaintedIntoARasterPage()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Html("filter:grayscale(1);"), margin: 0);
            var page = NewPage(80, 60, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            var p = Pixel(page, 20, 15);
            Assert.Equal(p[0], p[1]);
            Assert.Equal(p[1], p[2]);
            Assert.InRange(p[0], 50, 58);
            Assert.Equal(255, p[3]);
        }

        [Fact]
        public async Task Blur_SpreadsTheBoxBeyondItsEdges_AndSoftensThem()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Html("margin:20pt;filter:blur(3pt);"), margin: 0);
            var page = NewPage(120, 100, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            // The box sits at (20, 20) to (60, 50). Inside, well away from the edge, it is solid; just outside it there
            // is a halo, and far outside there is nothing.
            Assert.True(Pixel(page, 40, 35)[3] > 245);
            Assert.InRange(Pixel(page, 19, 35)[3], 20, 200);
            Assert.InRange(Pixel(page, 61, 35)[3], 20, 200);
            Assert.Equal(0, Pixel(page, 5, 5)[3]);
        }

        [Fact]
        public async Task FilteredElementInsideAFilteredAncestor_IsFilteredTwice()
        {
            const string html = """
                <div style="filter:grayscale(1);margin:0;width:80pt;height:60pt;background:#00ff00">
                  <div style="filter:blur(2pt);margin:10pt;width:30pt;height:20pt;background:#ff0000"></div>
                </div>
                """;
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), margin: 0);
            var page = NewPage(100, 80, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            // Everything is grey (the ancestor's grayscale ran over the nested, blurred red box too), and the nested
            // box's edge is soft.
            var inner = Pixel(page, 25, 20);
            Assert.Equal(inner[0], inner[1]);
            Assert.Equal(inner[1], inner[2]);
            var outer = Pixel(page, 70, 50);
            Assert.Equal(outer[0], outer[1]);
            Assert.NotEqual(inner[0], outer[0]);
            var edge = Pixel(page, 10, 20);
            // The red box greys to a darker value than the green ground; its blurred edge is in between.
            Assert.InRange(edge[0], inner[0] + 1, outer[0] - 1);
        }

        [Fact]
        public async Task OpacityAndBlendMode_ApplyAfterTheFilterList()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Html("filter:grayscale(1);opacity:0.5;"), margin: 0);
            var page = NewPage(80, 60, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            var p = Pixel(page, 20, 15);
            Assert.InRange(p[3], 126, 129);
            Assert.InRange(p[0], 25, 30);
        }

        [Fact]
        public async Task OpacityFunction_NextToABlur_RunsInTheSameBitmapPass()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Html("margin:10pt;filter:opacity(0.5) blur(1pt);"), margin: 0);
            var page = NewPage(80, 70, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            Assert.InRange(Pixel(page, 30, 25)[3], 120, 135);
        }

        [Fact]
        public async Task DropShadowInTheList_IsLeftToTheDecorationPainter()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Html("margin:10pt;filter:drop-shadow(4pt 4pt 0 #0000ff) grayscale(1);"), margin: 0);
            var page = NewPage(90, 80, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            // The element is greyed; the shadow below-right of it was painted (and greyed with it).
            Assert.True(Pixel(page, 30, 30)[3] > 200);
            Assert.True(Pixel(page, 52, 42)[3] > 100);
        }

        [Fact]
        public async Task ABoxShadow_IsIncludedInTheBitmapAndFiltered()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                Html("margin:20pt;box-shadow:8pt 8pt 0 #0000ff;filter:grayscale(1);"), margin: 0);
            var page = NewPage(110, 100, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            // Below-right of the box (which ends at 60, 50) the hard shadow is present and neutral grey.
            var shadow = Pixel(page, 65, 55);
            Assert.True(shadow[3] > 200);
            Assert.Equal(shadow[0], shadow[1]);
            Assert.Equal(shadow[1], shadow[2]);
        }

        [Fact]
        public async Task ElementOutsideTheClip_IsNotRasterized()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Html("margin:200pt 0 0 0;filter:blur(2pt);"), margin: 0);
            var page = NewPage(80, 60, new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, page);

            Assert.All(Enumerable.Range(0, 60).SelectMany(y => Enumerable.Range(0, 80).Select(x => Pixel(page, x, y)[3])), a => Assert.Equal(0, a));
        }
    }
}
