using PeachPDF.Svg;
using System;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>The coverage-mask kernels (vectorized) against plain scalar references, on sizes that exercise every vector tail.</summary>
    public class SvgRasterKernelsTests
    {
        private static byte[] RandomBytes(int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);

            // Plenty of 0 and 255 runs, the cases the kernels shortcut.
            for (var i = 0; i < length; i += 7)
                bytes[i] = i % 14 == 0 ? (byte)0 : (byte)255;
            return bytes;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(37)]
        [InlineData(1000)]
        public void ExtractAlpha_MatchesTheFourthByteOfEachPixel(int pixels)
        {
            var rgba = RandomBytes(pixels * 4, 1);
            var alpha = new byte[pixels];

            SvgRasterKernels.ExtractAlpha(rgba, alpha);

            for (var i = 0; i < pixels; i++)
                Assert.Equal(rgba[i * 4 + 3], alpha[i]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(61)]
        [InlineData(1000)]
        public void ScaleByCoverage_MatchesRoundedChannelTimesCoverage(int pixels)
        {
            var rgba = RandomBytes(pixels * 4, 2);
            var coverage = RandomBytes(pixels, 3);
            var expected = (byte[])rgba.Clone();
            for (var i = 0; i < pixels; i++)
            {
                for (var c = 0; c < 4; c++)
                    expected[i * 4 + c] = (byte)Math.Round(rgba[i * 4 + c] * coverage[i] / 255.0, MidpointRounding.AwayFromZero);
            }

            SvgRasterKernels.ScaleByCoverage(rgba, coverage);

            Assert.Equal(expected, rgba);
        }

        [Fact]
        public void Difference_IsASaturatingSubtract()
        {
            var grown = RandomBytes(517, 4);
            var shrunk = RandomBytes(517, 5);
            var expected = new byte[517];
            for (var i = 0; i < expected.Length; i++)
                expected[i] = (byte)Math.Max(0, grown[i] - shrunk[i]);

            Assert.Equal(expected, SvgRasterKernels.Difference(grown, shrunk));
        }

        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(7, 5, 1)]
        [InlineData(33, 20, 3)]
        [InlineData(40, 9, 12)]
        [InlineData(5, 40, 48)]
        [InlineData(65, 66, 7)]
        public void Morphology_MatchesASquareWindowMaxAndMin(int width, int height, int radius)
        {
            var source = RandomBytes(width * height, 6);

            foreach (var grow in new[] { true, false })
            {
                var expected = new byte[source.Length];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        // Outside the map counts as 0 for a grow and as 255 for a shrink, so an edge does not eat the shape.
                        var value = grow ? 0 : 255;
                        for (var yy = Math.Max(0, y - radius); yy <= Math.Min(height - 1, y + radius); yy++)
                        {
                            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(width - 1, x + radius); xx++)
                                value = grow ? Math.Max(value, source[yy * width + xx]) : Math.Min(value, source[yy * width + xx]);
                        }

                        expected[y * width + x] = (byte)value;
                    }
                }

                Assert.Equal(expected, SvgRasterKernels.Morphology(source, width, height, radius, grow));
            }
        }

        [Fact]
        public void Morphology_WithNoRadius_ReturnsACopy()
        {
            var source = RandomBytes(30, 7);
            var result = SvgRasterKernels.Morphology(source, 6, 5, 0, grow: true);

            Assert.Equal(source, result);
            Assert.NotSame(source, result);
        }
    }
}
