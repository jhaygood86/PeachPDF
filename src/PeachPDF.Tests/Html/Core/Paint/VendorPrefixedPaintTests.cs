using PeachDrawing.Core;
using PeachDrawing;
using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// A prefixed-only declaration paints exactly what its standard spelling paints. Pages are laid out for real
    /// and rasterized, so a rewrite that parsed but rendered nothing (or the wrong way round) cannot pass.
    /// </summary>
    public class VendorPrefixedPaintTests
    {
        private static async Task<RasterCanvas> Paint(string body, int width = 120, int height = 100)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), margin: 0);
            var page = new RasterCanvas(new PdfSharpAdapter(), new RasterSurface(width, height, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        private static byte[] Pixel(RasterCanvas g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static byte[] AllPixels(RasterCanvas g, int width, int height)
        {
            var all = new byte[width * height * 4];
            for (var y = 0; y < height; y++) g.Surface.Row(y).Slice(0, width * 4).CopyTo(all.AsSpan(y * width * 4));
            return all;
        }

        private static string Box(string style) => $"<div style=\"margin:0;width:100pt;height:80pt;{style}\"></div>";

        private static async Task AssertSamePixels(string legacyStyle, string standardStyle)
        {
            var legacy = AllPixels(await Paint(Box(legacyStyle)), 120, 100);
            var standard = AllPixels(await Paint(Box(standardStyle)), 120, 100);

            Assert.Equal(standard, legacy);
            Assert.Contains(legacy, b => b != 0); // guards a mutual blank render
        }

        [Theory]
        [InlineData("background:-webkit-linear-gradient(left,#f00,#00f)", "background:linear-gradient(to right,#f00,#00f)")]
        [InlineData("background:-moz-linear-gradient(top,#f00,#00f)", "background:linear-gradient(to bottom,#f00,#00f)")]
        [InlineData("background:-webkit-linear-gradient(bottom,#f00,#00f)", "background:linear-gradient(to top,#f00,#00f)")]
        [InlineData("background:-webkit-linear-gradient(top left,#f00,#00f)", "background:linear-gradient(to bottom right,#f00,#00f)")]
        [InlineData("background:-webkit-linear-gradient(0deg,#f00,#00f)", "background:linear-gradient(90deg,#f00,#00f)")]
        [InlineData("background:-webkit-linear-gradient(45deg,#f00,#00f)", "background:linear-gradient(45deg,#f00,#00f)")]
        [InlineData("background:-webkit-repeating-linear-gradient(left,#f00 0,#00f 20pt)", "background:repeating-linear-gradient(to right,#f00 0,#00f 20pt)")]
        [InlineData("background:-webkit-radial-gradient(center,circle cover,#f00,#00f)", "background:radial-gradient(circle farthest-corner at center,#f00,#00f)")]
        [InlineData("background:-webkit-radial-gradient(30% 40%,#f00,#00f)", "background:radial-gradient(at 30% 40%,#f00,#00f)")]
        [InlineData("-webkit-box-shadow:6pt 6pt 0 #f00;margin:10pt", "box-shadow:6pt 6pt 0 #f00;margin:10pt")]
        [InlineData("-webkit-border-radius:20pt;background:#f00", "border-radius:20pt;background:#f00")]
        [InlineData("-webkit-transform:rotate(15deg);background:#f00", "transform:rotate(15deg);background:#f00")]
        [InlineData("-moz-transform:translate(10pt,5pt);background:#f00", "transform:translate(10pt,5pt);background:#f00")]
        public Task Prefixed_PaintsTheSameAsStandard(string legacyStyle, string standardStyle) =>
            AssertSamePixels(legacyStyle, standardStyle);

        [Fact]
        public async Task LegacyLeftKeyword_StartsAtTheLeftEdge()
        {
            // The legacy keyword names the START point: `left` runs left -> right, i.e. red on the left. Read with the
            // standard reading of the same word (`to left`) it would be the other way round.
            var page = await Paint(Box("background:-webkit-linear-gradient(left,#f00,#00f)"));

            Assert.True(Pixel(page, 2, 40)[0] > 200 && Pixel(page, 2, 40)[2] < 60);
            Assert.True(Pixel(page, 97, 40)[2] > 200 && Pixel(page, 97, 40)[0] < 60);
        }

        [Fact]
        public async Task LegacyAngle_IsCounterClockwiseFromEast()
        {
            // 90deg legacy points north: the gradient starts at the bottom (red) and ends at the top (blue).
            var page = await Paint(Box("background:-webkit-linear-gradient(90deg,#f00,#00f)"));

            Assert.True(Pixel(page, 50, 76)[0] > 200);
            Assert.True(Pixel(page, 50, 3)[2] > 200);
        }
    }
}
