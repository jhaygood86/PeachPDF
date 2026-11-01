namespace PeachDrawing.Core.Tests
{
    // The vector paths must produce exactly the bytes of the scalar formulas below, for every colour/alpha pair.
    public class PixelMathTests
    {
        private static byte[] EveryChannelAlphaPair()
        {
            // 256 x 256 pixels: red = c, green = c ^ 0x55, blue = 255 - c, alpha = a.
            var pixels = new byte[256 * 256 * 4];
            for (var a = 0; a < 256; a++)
            {
                for (var c = 0; c < 256; c++)
                {
                    var p = (a * 256 + c) * 4;
                    pixels[p] = (byte)c;
                    pixels[p + 1] = (byte)(c ^ 0x55);
                    pixels[p + 2] = (byte)(255 - c);
                    pixels[p + 3] = (byte)a;
                }
            }

            return pixels;
        }

        private static byte[] ReferencePremultiply(byte[] rgba)
        {
            var result = (byte[])rgba.Clone();
            for (var i = 0; i < result.Length; i += 4)
            {
                var a = result[i + 3];
                if (a == 255)
                    continue;

                for (var k = 0; k < 3; k++)
                    result[i + k] = (byte)PixelMath.Div255(result[i + k] * a);
            }

            return result;
        }

        private static byte[] ReferenceUnpremultiply(byte[] premultiplied)
        {
            var result = new byte[premultiplied.Length];
            for (var i = 0; i < result.Length; i += 4)
            {
                int a = premultiplied[i + 3];
                if (a == 0)
                    continue;

                for (var k = 0; k < 3; k++)
                    result[i + k] = (byte)Math.Min(255, (premultiplied[i + k] * 255 + a / 2) / a);

                result[i + 3] = (byte)a;
            }

            return result;
        }

        [Fact]
        public void Div255_IsTheRoundedQuotientForEveryProductOfTwoBytes()
        {
            for (var x = 0; x <= 255 * 255; x++)
                Assert.Equal((int)Math.Round(x / 255.0, MidpointRounding.AwayFromZero), PixelMath.Div255(x));
        }

        [Fact]
        public void Premultiply_MatchesTheScalarFormulaForEveryColourAndAlpha()
        {
            var pixels = EveryChannelAlphaPair();
            var expected = ReferencePremultiply(pixels);

            PixelMath.Premultiply(pixels);

            Assert.Equal(expected, pixels);
        }

        [Fact]
        public void Unpremultiply_MatchesTheScalarFormulaForEveryColourAndAlpha()
        {
            var pixels = EveryChannelAlphaPair();
            var expected = ReferenceUnpremultiply(pixels);
            var actual = new byte[pixels.Length];

            PixelMath.Unpremultiply(pixels, actual);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(9)]
        public void BothConversions_HandleABufferThatIsNotAWholeNumberOfVectors(int pixelCount)
        {
            var source = EveryChannelAlphaPair().AsSpan(1000 * 4, pixelCount * 4).ToArray();

            var premultiplied = (byte[])source.Clone();
            PixelMath.Premultiply(premultiplied);
            Assert.Equal(ReferencePremultiply(source), premultiplied);

            var straight = new byte[source.Length];
            PixelMath.Unpremultiply(source, straight);
            Assert.Equal(ReferenceUnpremultiply(source), straight);
        }
    }
}
