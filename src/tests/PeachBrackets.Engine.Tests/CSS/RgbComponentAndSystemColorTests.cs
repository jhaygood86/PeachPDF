using PeachBrackets.Html.Parse;
using PeachDrawing.Core;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// The components of <c>rgb()</c> are numbers (not only integers) or percentages of 255, clamped and rounded to the nearest value (css-color-4
    /// section 5.1), in the legacy comma syntax and the modern space one alike; and the deprecated system colors are aliases of the current ones.
    /// </summary>
    public class RgbComponentAndSystemColorTests
    {
        private static PaintColor Parse(string text)
        {
            Assert.True(new CssValueParser(new HtmlRenderContext()).TryGetColor(text, 0, text.Length, out var color), text);
            return color;
        }

        [Theory]
        [InlineData("rgb(0% 50% 0%)", 0, 128, 0)]
        [InlineData("rgb(0%, 50%, 0%)", 0, 128, 0)]
        [InlineData("rgb(0 128.0 0)", 0, 128, 0)]
        [InlineData("rgb(0, 127.5, 0)", 0, 128, 0)]
        [InlineData("rgba(0%, 50%, 0%, 100%)", 0, 128, 0)]
        [InlineData("rgb(300 -20 10)", 255, 0, 10)]
        [InlineData("rgb(100% 0% 25.1%)", 255, 0, 64)]
        [InlineData("rgb(10, 20, 30)", 10, 20, 30)]
        [InlineData("rgba(10, 20, 30, 0.5)", 10, 20, 30)]
        public void AnRgbComponentIsANumberOrAPercentage_RoundedToTheNearestByte(string text, int r, int g, int b)
        {
            var color = Parse(text);

            Assert.Equal((r, g, b), (color.R, color.G, color.B));
        }

        [Theory]
        [InlineData("ActiveBorder", "ButtonBorder")]
        [InlineData("Window", "Canvas")]
        [InlineData("WindowText", "CanvasText")]
        [InlineData("ThreeDFace", "ButtonFace")]
        [InlineData("InactiveCaptionText", "GrayText")]
        public void ADeprecatedSystemColorComputesTheSameAsItsCurrentName(string deprecated, string current)
        {
            Assert.Equal(Parse(current), Parse(deprecated));
        }
    }
}
