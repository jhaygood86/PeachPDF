using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Raster;

namespace PeachPDF.Tests.Raster
{
    /// <summary>The colour matrices Filter Effects 1 §3 defines, checked against colours whose result follows straight from the spec's tables.</summary>
    public class FilterMatrixTests
    {
        private static FilterGrammar.FilterFunction Function(string name, params string[] arguments) =>
            new() { Name = name, Arguments = arguments };

        private static byte[] Run(FilterGrammar.FilterFunction function, byte r, byte g, byte b, byte a = 255)
        {
            var matrix = FilterEffectResolver.TryGetMatrix(function);
            Assert.NotNull(matrix);
            var source = new byte[] { r, g, b, a };
            var result = new byte[4];
            ColorMatrixFilter.Apply(source, result, matrix.Value);
            return result;
        }

        [Fact]
        public void Grayscale_Full_MapsRedToItsLuminance()
        {
            var p = Run(Function("grayscale", "1"), 255, 0, 0);

            // 0.2126 * 255 = 54.2
            Assert.Equal(p[0], p[1]);
            Assert.Equal(p[1], p[2]);
            Assert.InRange(p[0], 53, 55);
            Assert.Equal(255, p[3]);
        }

        [Fact]
        public void Grayscale_Zero_IsTheIdentity_AndHalfIsInBetween()
        {
            Assert.Equal(new byte[] { 200, 100, 50, 255 }, Run(Function("grayscale", "0"), 200, 100, 50));

            var half = Run(Function("grayscale", "0.5"), 255, 0, 0);
            Assert.InRange(half[0], 140, 160);
            Assert.InRange(half[1], 25, 30);
        }

        [Fact]
        public void Sepia_Full_OfWhite_ClampsTheOverflowingChannels()
        {
            // rows sum to 1.351 / 1.203 / 0.937 -> 255, 255, 239
            var p = Run(Function("sepia", "1"), 255, 255, 255);

            Assert.Equal(255, p[0]);
            Assert.Equal(255, p[1]);
            Assert.InRange(p[2], 237, 241);
        }

        [Fact]
        public void Saturate_Zero_IsGrayscaleByTheSaturationWeights_AndOneIsIdentity()
        {
            var gray = Run(Function("saturate", "0"), 255, 0, 0);
            Assert.InRange(gray[0], 53, 56); // 0.213 * 255
            Assert.Equal(gray[0], gray[1]);

            Assert.Equal(new byte[] { 10, 200, 90, 255 }, Run(Function("saturate", "1"), 10, 200, 90));
        }

        [Fact]
        public void Saturate_AboveOne_PushesChannelsApart()
        {
            var p = Run(Function("saturate", "2"), 200, 100, 100);

            Assert.True(p[0] > 200);
            Assert.True(p[1] < 100);
        }

        [Fact]
        public void HueRotate_Zero_IsTheIdentity()
        {
            Assert.Equal(new byte[] { 30, 90, 240, 255 }, Run(Function("hue-rotate", "0deg"), 30, 90, 240));
        }

        [Fact]
        public void HueRotate_HalfTurn_PreservesWhiteAndLuminanceOrderingChanges()
        {
            // Every row of the hue-rotate matrix sums to 1, so white is a fixed point at any angle.
            var white = Run(Function("hue-rotate", "180deg"), 255, 255, 255);
            Assert.InRange(white[0], 254, 255);
            Assert.InRange(white[1], 254, 255);
            Assert.InRange(white[2], 254, 255);

            // Pure red rotated 180 degrees becomes a cyan-ish colour: red falls, green and blue rise.
            var red = Run(Function("hue-rotate", "180deg"), 255, 0, 0);
            Assert.True(red[0] < 60);
            Assert.True(red[2] > red[0]);
        }

        [Fact]
        public void HueRotate_ThreeThirds_ReturnsToTheStart()
        {
            // A muted colour, so none of the intermediate rotations clamps (a clamp would make the round trip lossy).
            var once = Run(Function("hue-rotate", "120deg"), 150, 120, 130);
            var twice = Run(Function("hue-rotate", "120deg"), once[0], once[1], once[2]);
            var thrice = Run(Function("hue-rotate", "120deg"), twice[0], twice[1], twice[2]);

            Assert.InRange(thrice[0], 147, 153);
            Assert.InRange(thrice[1], 117, 123);
            Assert.InRange(thrice[2], 127, 133);
        }

        [Fact]
        public void ChannelIndependentFunctions_MatchTheirDefinitions()
        {
            Assert.Equal(new byte[] { 100, 100, 100, 255 }, Run(Function("brightness", "0.5"), 200, 200, 200));
            Assert.Equal(new byte[] { 55, 155, 205, 255 }, Run(Function("invert", "1"), 200, 100, 50));
            Assert.Equal(new byte[] { 128, 128, 128, 255 }, Run(Function("contrast", "0"), 20, 240, 130));
        }

        [Fact]
        public void TransparentPixels_StayTransparent_AndPartialAlphaIsPreserved()
        {
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Run(Function("grayscale", "1"), 0, 0, 0, 0));

            // Premultiplied red at 50% alpha: (128, 0, 0, 128) -> grayscale keeps alpha and stays premultiplied.
            var p = Run(Function("grayscale", "1"), 128, 0, 0, 128);
            Assert.Equal(128, p[3]);
            Assert.InRange(p[0], 26, 29);
        }

        [Fact]
        public void OpacityMatrix_ScalesAlpha()
        {
            var matrix = FilterEffectResolver.OpacityMatrix(0.5);
            var result = new byte[4];

            ColorMatrixFilter.Apply(new byte[] { 200, 100, 50, 255 }, result, matrix);

            Assert.Equal(128, result[3]);
            Assert.InRange(result[0], 99, 101);
        }

        [Theory]
        [InlineData("blur", true)]
        [InlineData("brightness", false)]
        public void UnknownMatrixFunctions_HaveNoMatrix(string name, bool expectNull)
        {
            var matrix = FilterEffectResolver.TryGetMatrix(Function(name, name == "blur" ? "2px" : "1"));

            Assert.Equal(expectNull, matrix is null);
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("0", true)]
        [InlineData("0px", true)]
        [InlineData("0.0em", true)]
        [InlineData("2px", false)]
        [InlineData("0.5px", false)]
        public void IsZeroLength_RecognisesANoOpBlur(string argument, bool expected)
        {
            var function = argument.Length == 0 ? Function("blur") : Function("blur", argument);

            Assert.Equal(expected, FilterEffectResolver.IsZeroLength(function));
        }
    }
}
