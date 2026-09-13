using PeachPDF.Svg;
using System.Numerics;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Proves <see cref="SvgColorMatrixTable.Build"/>'s row/column transpose against a known
    /// <c>feColorMatrix type="matrix"</c> example, per <c>ColorMatrix</c>'s own remarks: SVG's table is
    /// row-major with ROW = output, but <c>ColorMatrix.Linear</c> needs ROW = input (the convention
    /// <see cref="System.Numerics.Vector4.Transform(Vector4, Matrix4x4)"/> actually multiplies by) - a
    /// transpose bug here would silently swap which input channel feeds which output channel while still
    /// "looking right" for the common diagonal/identity case, so this specifically exercises an
    /// off-diagonal (genuinely cross-channel) coefficient to catch that class of bug.
    /// </summary>
    public class SvgColorMatrixTableTests
    {
        [Fact]
        public void Identity_TransformsAnyColorUnchanged()
        {
            var matrix = SvgColorMatrixTable.Build(SvgColorMatrixTable.Identity);

            var result = matrix.Apply(new Vector4(0.2f, 0.4f, 0.6f, 0.8f));

            Assert.Equal(0.2f, result.X, 5);
            Assert.Equal(0.4f, result.Y, 5);
            Assert.Equal(0.6f, result.Z, 5);
            Assert.Equal(0.8f, result.W, 5);
        }

        [Fact]
        public void Identity_IsChannelIndependent()
        {
            var matrix = SvgColorMatrixTable.Build(SvgColorMatrixTable.Identity);

            Assert.True(matrix.IsChannelIndependent);
        }

        /// <summary>
        /// R' = 0.5*G (row 0 of the SVG table: [0, 0.5, 0, 0, 0]), every other output channel identity.
        /// A correct transpose puts this coefficient at <c>Linear.M21</c> (row = input G = 2, col =
        /// output R = 1); an un-transposed (bug) implementation would instead put it at <c>M12</c> (row =
        /// output R, col = input G swapped) - <see cref="Matrix4x4.M21"/> and <c>M12</c> occupy different
        /// matrix cells, so a transpose bug here would make R' respond to the wrong stimulus entirely
        /// (columns of <see cref="Matrix4x4"/> map to <em>outputs</em> under
        /// <see cref="Vector4.Transform(Vector4, Matrix4x4)"/>, so a swapped coefficient would show up as
        /// G' changing instead of R' below).
        /// </summary>
        [Fact]
        public void CrossChannelCoefficient_TransformsTheCorrectOutputChannel()
        {
            double[] values =
            [
                0, 0.5, 0, 0, 0, // R' = 0.5*G
                0, 1, 0, 0, 0,   // G' = G
                0, 0, 1, 0, 0,   // B' = B
                0, 0, 0, 1, 0,   // A' = A
            ];

            var matrix = SvgColorMatrixTable.Build(values);

            // R=0, G=1, B=0, A=1 -> expect R'=0.5 (0.5*G), G'=1, B'=0, A'=1.
            var result = matrix.Apply(new Vector4(0, 1, 0, 1));

            Assert.Equal(0.5f, result.X, 5);
            Assert.Equal(1f, result.Y, 5);
            Assert.Equal(0f, result.Z, 5);
            Assert.Equal(1f, result.W, 5);
        }

        [Fact]
        public void CrossChannelCoefficient_IsNotChannelIndependent()
        {
            double[] values =
            [
                0, 0.5, 0, 0, 0,
                0, 1, 0, 0, 0,
                0, 0, 1, 0, 0,
                0, 0, 0, 1, 0,
            ];

            var matrix = SvgColorMatrixTable.Build(values);

            Assert.False(matrix.IsChannelIndependent);
        }

        /// <summary>A diagonal matrix with a constant offset (the shape <c>brightness()</c>/<c>contrast()</c> use) - channel-independent, and its offset column maps straight through (no transpose needed for the constant term).</summary>
        [Fact]
        public void DiagonalWithOffset_TransformsEachChannelIndependently()
        {
            double[] values =
            [
                0.5, 0, 0, 0, 0.1, // R' = 0.5*R + 0.1
                0, 0.5, 0, 0, 0.1, // G' = 0.5*G + 0.1
                0, 0, 0.5, 0, 0.1, // B' = 0.5*B + 0.1
                0, 0, 0, 1, 0,     // A' = A
            ];

            var matrix = SvgColorMatrixTable.Build(values);

            Assert.True(matrix.IsChannelIndependent);

            var result = matrix.Apply(new Vector4(1, 1, 1, 1));
            Assert.Equal(0.6f, result.X, 5);
            Assert.Equal(0.6f, result.Y, 5);
            Assert.Equal(0.6f, result.Z, 5);
            Assert.Equal(1f, result.W, 5);
        }
    }
}
