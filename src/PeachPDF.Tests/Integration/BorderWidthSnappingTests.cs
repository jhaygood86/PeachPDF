using PeachPDF.Html.Core.Parse;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class BorderWidthSnappingTests
    {
        private const double Px = 0.75;

        [Theory]
        [InlineData(0.5, 1.0)]
        [InlineData(0.01, 1.0)]
        [InlineData(1.0, 1.0)]
        [InlineData(1.5, 1.0)]
        [InlineData(2.0, 2.0)]
        [InlineData(2.9, 2.0)]
        [InlineData(3.0, 3.0)]
        public void SnapBorderWidth_RoundsOnTheCssPixel(double inputPx, double expectedPx)
        {
            Assert.Equal(expectedPx * Px, CssValueParser.SnapBorderWidth(inputPx * Px, enabled: true), 6);
        }

        [Fact]
        public void SnapBorderWidth_ZeroStaysZero_AndDisabledIsANoOp()
        {
            Assert.Equal(0d, CssValueParser.SnapBorderWidth(0, enabled: true));
            Assert.Equal(0.375, CssValueParser.SnapBorderWidth(0.375, enabled: false));
        }

        [Fact]
        public void Config_DefaultsToOff() => Assert.False(new PdfGenerateConfig().SnapBorderWidthsToCssPixels);

        private static async Task<(double Border, double Width, double Outline, double Keyword)> LayoutAsync(bool snap)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='b' style='border:1.5px solid black; outline:0.5px solid red; width:100px'>x</div>" +
                    "<div id='k' style='border:medium solid black'>x</div>"),
                prepare: r => r.HtmlContainer!.SnapBorderWidthsToCssPixels = snap);
            var b = LayoutHarness.FindById(root, "b")!;
            var k = LayoutHarness.FindById(root, "k")!;
            return (b.ActualBorderTopWidth, b.ActualBottom - b.Location.Y, b.ActualOutlineWidth, k.ActualBorderTopWidth);
        }

        [Fact]
        public async Task Enabled_SnapsBorderOutlineAndChangesBoxSize_ButNotKeywords()
        {
            var off = await LayoutAsync(false);
            var on = await LayoutAsync(true);

            Assert.Equal(1.5 * Px, off.Border, 6);
            Assert.Equal(0.5 * Px, off.Outline, 6);

            Assert.Equal(1.0 * Px, on.Border, 6);
            Assert.Equal(1.0 * Px, on.Outline, 6);
            Assert.Equal(off.Width - 2 * (0.5 * Px), on.Width, 6);
            Assert.Equal(off.Keyword, on.Keyword, 6);
        }
    }
}
