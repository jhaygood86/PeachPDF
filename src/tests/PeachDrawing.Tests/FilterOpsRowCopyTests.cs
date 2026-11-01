using PeachDrawing.Core;
using PeachDrawing.Filters;

namespace PeachDrawing.Tests
{
    /// <summary>The row-wise copies and kernel-backed composites of <see cref="FilterOps"/> against their per-pixel definitions.</summary>
    public class FilterOpsRowCopyTests
    {
        private static RasterSurface Noise(int width, int height, int seed)
        {
            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            new Random(seed).NextBytes(surface.Pixels);
            return surface;
        }

        private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

        [Theory]
        [InlineData(7, 5, 0, 0, 7, 5)]
        [InlineData(7, 5, 2, 1, 5, 4)]
        [InlineData(9, 3, 3, 0, 4, 3)]
        [InlineData(10, 10, 1, 1, 3, 2)]
        [InlineData(33, 2, 4, 0, 11, 2)]
        public void Tile_RepeatsTheTileRectangleAcrossTheDestination(int width, int height, int left, int top, int tileWidth, int tileHeight)
        {
            using var source = Noise(width, height, width * 31 + height);
            using var destination = new RasterSurface(width + 13, height + 5, 0, 0, 1, 1);

            FilterOps.Tile(source, destination, new IntRect(left, top, left + tileWidth, top + tileHeight));

            for (var y = 0; y < destination.Height; y++)
            {
                for (var x = 0; x < destination.Width; x++)
                {
                    var sx = left + PositiveModulo(x - left, tileWidth);
                    var sy = top + PositiveModulo(y - top, tileHeight);
                    for (var c = 0; c < 4; c++)
                        Assert.Equal(source.Pixels[(sy * width + sx) * 4 + c], destination.Pixels[(y * destination.Width + x) * 4 + c]);
                }
            }
        }

        [Theory]
        [InlineData(1, 1, 3, 2)]
        [InlineData(4, 3, 0, 0)]
        [InlineData(4, 3, 2, 1)]
        [InlineData(5, 5, 7, 9)]
        [InlineData(30, 2, 12, 3)]
        public void MirrorPad_ReflectsTheSourceAcrossItsEdges(int width, int height, int padX, int padY)
        {
            using var source = Noise(width, height, width * 17 + height);

            using var padded = FilterOps.MirrorPad(source, padX, padY);

            static int Reflect(int index, int length)
            {
                if (length == 1)
                    return 0;

                var i = PositiveModulo(index, 2 * length);
                return i < length ? i : 2 * length - 1 - i;
            }

            Assert.Equal(width + 2 * padX, padded.Width);
            Assert.Equal(height + 2 * padY, padded.Height);
            for (var y = 0; y < padded.Height; y++)
            {
                for (var x = 0; x < padded.Width; x++)
                {
                    var sx = Reflect(x - padX, width);
                    var sy = Reflect(y - padY, height);
                    for (var c = 0; c < 4; c++)
                        Assert.Equal(source.Pixels[(sy * width + sx) * 4 + c], padded.Pixels[(y * padded.Width + x) * 4 + c]);
                }
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(16)]
        [InlineData(37)]
        public void IsOpaque_FindsASingleTranslucentPixelAnywhere(int pixels)
        {
            using var surface = new RasterSurface(pixels, 1, 0, 0, 1, 1);
            surface.Pixels.Fill((byte)255);
            Assert.True(RasterSurfaceEncoding.IsOpaque(surface));

            for (var p = 0; p < pixels; p++)
            {
                surface.Pixels[p * 4 + 3] = 254;
                Assert.False(RasterSurfaceEncoding.IsOpaque(surface));
                surface.Pixels[p * 4 + 3] = 255;
            }

            // Colour bytes below 255 must not read as translucency.
            surface.Pixels[0] = 0;
            Assert.True(RasterSurfaceEncoding.IsOpaque(surface));
        }

        [Fact]
        public void OverInPlace_MatchesSourceOver_AndLeavesPixelsUnderATransparentSourceAlone()
        {
            using var destination = Noise(21, 3, 1);
            using var top = Noise(21, 3, 2);
            for (var i = 3; i < top.Pixels.Length; i += 20)
                top.Pixels[i] = 0;

            var expected = destination.Pixels.ToArray();
            for (var i = 0; i + 3 < expected.Length; i += 4)
            {
                var inverse = 255 - top.Pixels[i + 3];
                if (inverse == 255)
                    continue;

                for (var c = 0; c < 4; c++)
                    expected[i + c] = (byte)Math.Min(top.Pixels[i + c] + PixelKernels.Div255(expected[i + c] * inverse), 255);
            }

            FilterOps.OverInPlace(destination, top);

            Assert.Equal(expected, destination.Pixels.ToArray());
        }

        [Fact]
        public void Arithmetic_ComputesTheDocumentedFormulaPerPixel()
        {
            using var a = Noise(9, 2, 3);
            using var b = Noise(9, 2, 4);
            using var result = new RasterSurface(9, 2, 0, 0, 1, 1);
            const double k1 = 0.25, k2 = 0.5, k3 = 0.4, k4 = 0.01;

            FilterOps.Arithmetic(a, b, result, k1, k2, k3, k4);

            for (var i = 0; i < a.Pixels.Length; i += 4)
            {
                var values = new double[4];
                for (var c = 0; c < 4; c++)
                {
                    var ia = a.Pixels[i + c] / 255.0;
                    var ib = b.Pixels[i + c] / 255.0;
                    values[c] = Math.Clamp(k1 * ia * ib + k2 * ia + k3 * ib + k4, 0.0, 1.0);
                }

                for (var c = 0; c < 3; c++)
                    values[c] = Math.Min(values[c], values[3]);

                for (var c = 0; c < 4; c++)
                    Assert.Equal((byte)Math.Round(values[c] * 255.0), result.Pixels[i + c]);
            }
        }
    }
}
