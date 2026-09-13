using PeachPDF.Html.Adapters.Entities;
using System.Numerics;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Advanced
{
    public class ColorMatrixTests
    {
        [Fact]
        public void Identity_LeavesColorUnchanged()
        {
            var color = new Vector4(0.2f, 0.4f, 0.6f, 0.8f);

            Assert.Equal(color, ColorMatrix.Identity.Apply(color));
        }

        [Fact]
        public void Identity_IsChannelIndependent()
        {
            Assert.True(ColorMatrix.Identity.IsChannelIndependent);
        }

        // A uniform 150% brightness scale (CSS `brightness(1.5)`): output = input * 1.5, clamped
        // elsewhere - purely diagonal, so channel-independent.
        private static ColorMatrix Brightness(float factor) =>
            new(new Matrix4x4(
                factor, 0, 0, 0,
                0, factor, 0, 0,
                0, 0, factor, 0,
                0, 0, 0, 1), Vector4.Zero);

        // CSS `grayscale(1)`'s luminance weights - every output channel mixes all three inputs, the
        // textbook cross-channel case. Written as SVG feColorMatrix would author it (row = output,
        // column = input) and then TRANSPOSED into ColorMatrix.Linear's row=input/col=output
        // convention (see ColorMatrix.Linear's remarks) - each row below is one INPUT component's
        // contribution to the three outputs (which are identical for grayscale, since every output
        // channel applies the same luminance formula).
        private static readonly ColorMatrix Grayscale = new(new Matrix4x4(
            0.2126f, 0.2126f, 0.2126f, 0,
            0.7152f, 0.7152f, 0.7152f, 0,
            0.0722f, 0.0722f, 0.0722f, 0,
            0, 0, 0, 1), Vector4.Zero);

        [Fact]
        public void DiagonalMatrix_IsChannelIndependent()
        {
            Assert.True(Brightness(1.5f).IsChannelIndependent);
        }

        [Fact]
        public void CrossChannelMatrix_IsNotChannelIndependent()
        {
            Assert.False(Grayscale.IsChannelIndependent);
        }

        [Fact]
        public void Apply_DiagonalMatrix_ScalesEachChannel()
        {
            var color = new Vector4(0.2f, 0.4f, 0.6f, 1f);
            var result = Brightness(1.5f).Apply(color);

            Assert.Equal(0.3f, result.X, 4);
            Assert.Equal(0.6f, result.Y, 4);
            Assert.Equal(0.9f, result.Z, 4);
            Assert.Equal(1f, result.W, 4); // alpha untouched by the matrix's identity alpha row/col
        }

        [Fact]
        public void Apply_CrossChannelMatrix_MixesInputsIntoEachOutput()
        {
            var color = new Vector4(1f, 0f, 0f, 1f); // pure red
            var result = Grayscale.Apply(color);

            // Pure red through the luminance weights: R contributes 0.2126 to every output channel,
            // G and B contribute nothing (both zero here).
            Assert.Equal(0.2126f, result.X, 4);
            Assert.Equal(0.2126f, result.Y, 4);
            Assert.Equal(0.2126f, result.Z, 4);
        }

        [Fact]
        public void Compose_WithIdentity_IsUnchanged()
        {
            var composed = Brightness(1.5f).Compose(ColorMatrix.Identity);
            var color = new Vector4(0.2f, 0.4f, 0.6f, 1f);

            Assert.Equal(Brightness(1.5f).Apply(color), composed.Apply(color));
        }

        [Fact]
        public void Compose_MatchesApplyingBothInOrder()
        {
            // brightness(1.5) then grayscale(1) - a genuinely non-trivial pair (one diagonal, one
            // fully cross-channel), proving Compose's matrix/offset combination is correct in general,
            // not just for two diagonal (commuting) matrices.
            var first = Brightness(1.5f);
            var second = Grayscale;
            var composed = first.Compose(second);

            foreach (var color in new[]
                     {
                         new Vector4(0.2f, 0.4f, 0.6f, 1f),
                         new Vector4(1f, 1f, 1f, 1f),
                         new Vector4(0f, 0f, 0f, 1f),
                         new Vector4(0.9f, 0.1f, 0.5f, 0.7f),
                     })
            {
                var expected = second.Apply(first.Apply(color));
                var actual = composed.Apply(color);

                Assert.Equal(expected.X, actual.X, 4);
                Assert.Equal(expected.Y, actual.Y, 4);
                Assert.Equal(expected.Z, actual.Z, 4);
                Assert.Equal(expected.W, actual.W, 4);
            }
        }

        [Fact]
        public void Compose_WithOffsets_CarriesFirstOffsetThroughSecondLinearPart()
        {
            // invert(): output = 1 - input, i.e. linear = -Identity, offset = (1,1,1,0) (alpha
            // untouched). Composing invert-then-invert must be the identity transform again - this
            // specifically exercises Compose's offset-carrying term (Vector4.Transform(Offset,
            // appliedAfterThis.Linear)), which a same-matrix-composed-with-itself pass through the
            // matrices above (both offset-free) would never touch.
            var invert = new ColorMatrix(
                new Matrix4x4(
                    -1, 0, 0, 0,
                    0, -1, 0, 0,
                    0, 0, -1, 0,
                    0, 0, 0, 1),
                new Vector4(1, 1, 1, 0));

            var composed = invert.Compose(invert);
            var color = new Vector4(0.2f, 0.4f, 0.6f, 1f);

            var result = composed.Apply(color);
            Assert.Equal(color.X, result.X, 4);
            Assert.Equal(color.Y, result.Y, 4);
            Assert.Equal(color.Z, result.Z, 4);
            Assert.Equal(color.W, result.W, 4);
        }
    }
}
