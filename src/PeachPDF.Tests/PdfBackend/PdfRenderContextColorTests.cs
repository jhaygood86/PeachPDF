using PeachPDF.Rendering;
using PeachDrawing.PDF;
using Xunit;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>
    /// Direct unit tests for <see cref="PeachPDF.Rendering.HtmlRenderContext"/>'s named-color resolution
    /// (<c>GetColorInt</c>), which maps a CSS/system color name to an <c>PaintColor</c> via
    /// <see cref="System.Drawing.KnownColor"/>. Guards the trim/AOT-safe generic <c>Enum.TryParse&lt;KnownColor&gt;</c>
    /// path against regression.
    /// </summary>
    public class PdfRenderContextColorTests
    {
        [Theory]
        [InlineData("Red", 255, 0, 0)]
        [InlineData("red", 255, 0, 0)] // case-insensitive
        [InlineData("Lime", 0, 255, 0)]
        [InlineData("Blue", 0, 0, 255)]
        public void GetColor_KnownColorName_ResolvesToRgb(string name, byte r, byte g, byte b)
        {
            var renderContext = new HtmlRenderContext();

            var color = renderContext.GetColor(name);

            Assert.False(color.IsEmpty);
            Assert.Equal(r, color.R);
            Assert.Equal(g, color.G);
            Assert.Equal(b, color.B);
        }

        [Fact]
        public void GetColor_UnknownColorName_ReturnsEmpty()
        {
            var renderContext = new HtmlRenderContext();

            var color = renderContext.GetColor("not-a-real-color-name");

            Assert.True(color.IsEmpty);
        }
    }
}
