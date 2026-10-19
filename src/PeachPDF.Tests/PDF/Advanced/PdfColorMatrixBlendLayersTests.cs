using System;
using System.Linq;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Advanced;

namespace PeachPDF.Tests.PdfEngineTests.Advanced
{
    /// <summary>
    /// <see cref="PdfColorMatrixBlendLayers.Build"/> turns a channel-independent <see cref="ColorMatrix"/> into
    /// blend-mode fills. These hold it to the matrix it came from: evaluating the layers with the PDF blend
    /// formulas must give <c>clamp(m * c + b)</c> for every input, which is what makes the layer stack a faithful
    /// stand-in for the <c>/TR</c> function it replaces.
    /// </summary>
    public class PdfColorMatrixBlendLayersTests
    {
        private static ColorMatrix Diagonal(double r, double g, double b, double offsetR = 0, double offsetG = 0, double offsetB = 0) =>
            new(new Matrix4x4(
                (float)r, 0, 0, 0,
                0, (float)g, 0, 0,
                0, 0, (float)b, 0,
                0, 0, 0, 1), new Vector4((float)offsetR, (float)offsetG, (float)offsetB, 0));

        private static ColorMatrix Uniform(double m, double b) => Diagonal(m, m, m, b, b, b);

        [Fact]
        public void Identity_NeedsNoLayers()
        {
            Assert.Empty(PdfColorMatrixBlendLayers.Build(ColorMatrix.Identity));
        }

        [Fact]
        public void BrightnessAboveOne_IsOneColorDodge()
        {
            var layers = PdfColorMatrixBlendLayers.Build(Uniform(1.5, 0));

            var layer = Assert.Single(layers);
            Assert.Equal("ColorDodge", layer.BlendMode);
            Assert.Equal(1 - 1 / 1.5, layer.R, 6);
        }

        [Fact]
        public void BrightnessBelowOne_IsOneMultiply()
        {
            var layer = Assert.Single(PdfColorMatrixBlendLayers.Build(Uniform(0.5, 0)));

            Assert.Equal("Multiply", layer.BlendMode);
            Assert.Equal(0.5, layer.G, 6);
        }

        [Fact]
        public void FullInvert_IsOneDifferenceAgainstWhite()
        {
            // invert(1) is slope -1, offset +1.
            var layer = Assert.Single(PdfColorMatrixBlendLayers.Build(Uniform(-1, 1)));

            Assert.Equal("Difference", layer.BlendMode);
            Assert.Equal(1, layer.B, 6);
        }

        [Fact]
        public void ContrastAboveOne_IsDodgeThenBurn()
        {
            // contrast(2): slope 2, offset -0.5.
            var layers = PdfColorMatrixBlendLayers.Build(Uniform(2, -0.5));

            Assert.Equal(["ColorDodge", "ColorBurn"], layers.Select(l => l.BlendMode));
            Assert.Equal(0.25, layers[0].R, 6);
            Assert.Equal(2.0 / 3, layers[1].R, 6);
        }

        [Fact]
        public void ChannelsThatNeedDifferentLayers_KeepTheirOwnFillAndLeaveTheOthersNeutral()
        {
            // Red is brightened, green is untouched, blue is darkened.
            var layers = PdfColorMatrixBlendLayers.Build(Diagonal(2, 1, 0.5));

            var dodge = Assert.Single(layers, l => l.BlendMode == "ColorDodge");
            Assert.Equal(0.5, dodge.R, 6);
            Assert.Equal(0, dodge.G, 6);
            Assert.Equal(0, dodge.B, 6);

            var multiply = Assert.Single(layers, l => l.BlendMode == "Multiply");
            Assert.Equal(1, multiply.R, 6);
            Assert.Equal(1, multiply.G, 6);
            Assert.Equal(0.5, multiply.B, 6);
        }

        [Fact]
        public void AConstantChannel_IsZeroedThenScreenedToTheConstant()
        {
            var layers = PdfColorMatrixBlendLayers.Build(Uniform(0, 0.25));

            Assert.Equal(["Multiply", "Screen"], layers.Select(l => l.BlendMode));
            Assert.Equal(0, layers[0].R, 6);
            Assert.Equal(0.25, layers[1].R, 6);
        }

        [Theory]
        [InlineData(-3.0)]
        [InlineData(-1.0)]
        [InlineData(-0.2)]
        [InlineData(0.0)]
        [InlineData(0.1)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        [InlineData(5.0)]
        public void TheLayers_ReproduceTheClampedAffineMapAcrossTheWholeRange(double m)
        {
            foreach (var b in new[] { -1.5, -0.5, -0.1, 0.0, 0.1, 0.5, 0.9, 1.0, 1.5 })
            {
                var layers = PdfColorMatrixBlendLayers.Build(Uniform(m, b));

                for (var step = 0; step <= 40; step++)
                {
                    var c = step / 40.0;
                    var expected = Math.Clamp(m * c + b, 0, 1);

                    for (var channel = 0; channel < 3; channel++)
                    {
                        var actual = PdfColorMatrixBlendLayers.Apply(layers, c, channel);
                        Assert.True(Math.Abs(expected - actual) < 1e-5,
                            $"m={m}, b={b}, c={c}, channel={channel}: expected {expected}, layers gave {actual}");
                    }
                }
            }
        }

        [Fact]
        public void Apply_RejectsABlendModeItDoesNotModel()
        {
            Assert.Throws<InvalidOperationException>(() =>
                PdfColorMatrixBlendLayers.Apply([new PdfBlendLayer("Hue", 0, 0, 0)], 0.5, 0));
        }
    }
}
