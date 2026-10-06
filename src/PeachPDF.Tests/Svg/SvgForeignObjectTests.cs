using PeachDrawing;
using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>&lt;foreignObject&gt;</c> in an inline <c>&lt;svg&gt;</c>: the HTML inside is laid out as its own block and
    /// painted at the element's place in SVG user space. Driven through a real page layout into a raster page.
    /// </summary>
    public class SvgForeignObjectTests
    {
        private static RasterCanvas NewPage(int width, int height) =>
            new(new PdfSharpAdapter(), new RasterSurface(width, height, 0, 0, 1, 1), 1);

        private static byte[] Pixel(RasterCanvas g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static readonly byte[] Red = [255, 0, 0, 255];
        private static readonly byte[] White = [0, 0, 0, 0];

        private static string Page(string foreignObjectAttributes, string content, string svgAttributes = "") => "<style>body{margin:0}</style>" + $"""
            <svg width="100" height="100" viewBox="0 0 100 100" style="display:block" {svgAttributes}>
              <foreignObject {foreignObjectAttributes}>{content}</foreignObject>
            </svg>
            """;

        private static async Task<RasterCanvas> PaintAsync(string html)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(html, margin: 0);
            var page = NewPage(100, 100);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        [Fact]
        public async Task HtmlContent_IsPaintedAtTheForeignObjectsBox()
        {
            // 100 user units = 75pt (1 user unit = 0.75pt), so the 50x40 box at (20, 10) spans 15..52.5pt x 7.5..37.5pt.
            var page = await PaintAsync(Page("""x="20" y="10" width="50" height="40" """, """<div style="width:100%;height:100%;background:rgb(255,0,0)"></div>"""));

            Assert.Equal(Red, Pixel(page, 30, 20));
            Assert.Equal(White, Pixel(page, 10, 20));
            Assert.Equal(White, Pixel(page, 60, 20));
            Assert.Equal(White, Pixel(page, 30, 45));
            Assert.Equal(White, Pixel(page, 30, 4));
        }

        [Fact]
        public async Task HtmlContent_IsClippedToTheForeignObjectsViewport()
        {
            var page = await PaintAsync(Page("""x="20" y="10" width="50" height="40" """, """<div style="width:200px;height:200px;background:rgb(255,0,0)"></div>"""));

            Assert.Equal(Red, Pixel(page, 30, 20));
            Assert.Equal(White, Pixel(page, 60, 20));
            Assert.Equal(White, Pixel(page, 30, 45));
        }

        [Fact]
        public async Task ATransformOnTheForeignObject_MovesTheHtml()
        {
            var page = await PaintAsync(Page("""x="0" y="0" width="30" height="30" transform="translate(40 40)" """, """<div style="height:100%;background:rgb(255,0,0)"></div>"""));

            // 40..70 user units = 30..52.5pt.
            Assert.Equal(Red, Pixel(page, 40, 40));
            Assert.Equal(White, Pixel(page, 10, 10));
        }

        [Fact]
        public async Task ABlockInsideAnInlineAndAnonymousBoxes_AreNormalized()
        {
            // Text beside a block child needs an anonymous block box, which the structural passes skipped inside an svg.
            var page = await PaintAsync(Page("""x="0" y="0" width="100" height="100" """, """loose text <div style="height:30px;background:rgb(255,0,0)"></div>"""));

            // The block sits below the text line, not at the top.
            Assert.Equal(White, Pixel(page, 70, 2));
            Assert.Equal(Red, Pixel(page, 40, 25));
        }

        [Fact]
        public async Task NoWidthOrHeight_PaintsNothing()
        {
            var page = await PaintAsync(Page("""x="0" y="0" """, """<div style="height:100px;background:rgb(255,0,0)"></div>"""));

            Assert.Equal(White, Pixel(page, 30, 20));
        }

        [Fact]
        public async Task HtmlSelectors_MatchCaseInsensitively_InsideAForeignObject()
        {
            var page = await PaintAsync("<style>DIV{background:rgb(255,0,0);height:20px}</style>" + Page("""x="0" y="0" width="100" height="100" """, "<div></div>"));

            Assert.Equal(Red, Pixel(page, 40, 7));
        }

        [Fact]
        public async Task AForeignObject_InsideAGroupAndASwitch_IsPainted()
        {
            var html = "<style>body{margin:0}</style>" + """
                <svg width="100" height="100" viewBox="0 0 100 100" style="display:block">
                  <g transform="translate(10 10)"><switch>
                    <foreignObject width="40" height="40"><div style="height:100%;background:rgb(255,0,0)"></div></foreignObject>
                    <rect width="40" height="40" fill="blue"/>
                  </switch></g>
                </svg>
                """;
            var page = await PaintAsync(html);

            Assert.Equal(Red, Pixel(page, 20, 20));
        }

        [Fact]
        public async Task ASvgInsideAForeignObject_WithItsOwnForeignObject_IsPainted()
        {
            var page = await PaintAsync(Page("""x="0" y="0" width="100" height="100" """, """
                <svg width="60" height="60" viewBox="0 0 60 60" style="display:block"><foreignObject width="60" height="60"><div style="height:100%;background:rgb(255,0,0)"></div></foreignObject></svg>
                """));

            Assert.Equal(Red, Pixel(page, 20, 20));
        }
    }
}
