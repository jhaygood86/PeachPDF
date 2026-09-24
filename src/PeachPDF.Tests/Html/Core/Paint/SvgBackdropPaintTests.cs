using PeachPDF.Adapters;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// <c>BackgroundImage</c> of an SVG filter in an HTML page: the page behind an inline <c>&lt;svg&gt;</c> is repainted into the filter's
    /// bitmap (over white paper), then the SVG's own earlier content. Driven through a real page layout into a raster page.
    /// </summary>
    public class SvgBackdropPaintTests
    {
        private static RasterGraphics NewPage(int width, int height) =>
            new(new PdfSharpAdapter(), new RasterSurface(width, height, 0, 0, 1, 1), 1);

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        // A blue div at x 0..40pt holding an SVG 60pt wide, whose one rectangle is filtered to show the backdrop 30pt to its right.
        private static string Page(string svgStyle = "", string offset = "dx=\"-40\"") => $"""
            <div style="margin:0;width:40pt;height:60pt;background:rgb(0,0,255)">
              <svg width="80" height="40" viewBox="0 0 80 40" style="display:block;{svgStyle}">
                <defs><filter id="f" color-interpolation-filters="sRGB"><feOffset in="BackgroundImage" {offset} dy="0"/></filter></defs>
                <rect width="80" height="40" fill="red" filter="url(#f)"/>
              </svg>
            </div>
            """;

        [Fact]
        public async Task TheSvgsBackdrop_IsThePageBehindIt_OverWhitePaper()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(), margin: 0);
            var page = NewPage(100, 80);

            FragmentPaintHarness.PaintPage(container, page);

            // 30pt to the left of x 5 is outside the SVG's region; x 5 sees x 35, which is blue. x 15 sees x 45: paper.
            Assert.Equal([0, 0, 255, 255], Pixel(page, 5, 20));
            Assert.Equal([255, 255, 255, 255], Pixel(page, 15, 20));
        }

        [Fact]
        public async Task AnIsolatingSvg_HasNoPageBehindIt()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(svgStyle: "opacity:0.99;"), margin: 0);
            var page = NewPage(100, 80);

            FragmentPaintHarness.PaintPage(container, page);

            // The SVG is an isolation group, so what is behind it starts empty and the filter has nothing to show at x 15.
            Assert.NotEqual([255, 255, 255, 255], Pixel(page, 15, 20));
        }

        [Fact]
        public async Task TheSvgsOwnBackground_IsPartOfTheBackdrop()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(Page(svgStyle: "background:rgb(0,255,0);"), margin: 0);
            var page = NewPage(100, 80);

            FragmentPaintHarness.PaintPage(container, page);

            // The SVG paints its green background under its content; the filter output sees it and the page.
            Assert.Equal([0, 255, 0, 255], Pixel(page, 15, 20));
        }

        [Fact]
        public async Task ATransformedAncestor_MapsThePageOntoTheFilterRegion()
        {
            // The whole block is moved 20pt right, so in device space the blue div spans 20..60 and the SVG 20..80.
            var html = $"""<div style="transform:translate(20pt,0);margin:0;width:60pt">{Page()}</div>""";
            var (_, container) = await LayoutHarness.LayoutAsync(html, margin: 0);
            var page = NewPage(120, 80);

            FragmentPaintHarness.PaintPage(container, page);

            // x 25 sees x 55 (blue); x 45 sees x 75 (paper).
            Assert.Equal([0, 0, 255, 255], Pixel(page, 25, 20));
            Assert.Equal([255, 255, 255, 255], Pixel(page, 45, 20));
        }

        [Fact]
        public async Task AnIsolatingAncestor_StartsTheBackdropAtItself()
        {
            var html = $"""<div style="opacity:0.99;margin:0;width:80pt;background:rgb(0,255,0)">{Page()}</div>""";
            var (_, container) = await LayoutHarness.LayoutAsync(html, margin: 0);
            var page = NewPage(120, 80);

            FragmentPaintHarness.PaintPage(container, page);

            // No paper: the ancestor is the isolation group, and its own green background is the backdrop beyond the blue div (x 15 sees x 45).
            var p = Pixel(page, 15, 20);
            Assert.True(p[1] > 240 && p[0] < 10 && p[2] < 10, $"pixel was {string.Join(",", p)}");
        }

        [Fact]
        public async Task AnSvgUsedAsAnImage_IsItsOwnDocumentWithNoPageBehindIt()
        {
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40' viewBox='0 0 80 40'><defs><filter id='f' color-interpolation-filters='sRGB'><feOffset in='BackgroundImage' dx='-40' dy='0'/></filter></defs><rect width='80' height='40' fill='red' filter='url(#f)'/></svg>";
            var uri = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
            var html = $"""<div style="margin:0;width:40pt;height:60pt;background:rgb(0,0,255)"><img src="{uri}" width="80" height="40" style="display:block"></div>""";
            var (_, container) = await LayoutHarness.LayoutAsync(html, margin: 0);
            var page = NewPage(100, 80);

            FragmentPaintHarness.PaintPage(container, page);

            // Nothing was painted inside the image before the rectangle, and the page is not its backdrop: the blue div shows through.
            Assert.Equal([0, 0, 255, 255], Pixel(page, 15, 20));
        }
    }
}
