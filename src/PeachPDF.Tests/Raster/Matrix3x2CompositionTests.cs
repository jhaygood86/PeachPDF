using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachDrawing;
using System.Numerics;

namespace PeachPDF.Tests.Raster
{
    /// <summary>Composition and inversion of <see cref="Matrix3x2"/> (via <see cref="Matrix3x2Extensions"/>), and the accumulated transform a graphics reports (the mapping an SVG backdrop repaint relies on).</summary>
    public class Matrix3x2CompositionTests
    {
        private static (double X, double Y) Apply(Matrix3x2 m, double x, double y) =>
            (x * m.M11 + y * m.M21 + m.M31, x * m.M12 + y * m.M22 + m.M32);

        [Fact]
        public void Then_AppliesTheReceiverFirst()
        {
            var scale = new Matrix3x2(2, 0, 0, 3, 0, 0);
            var move = new Matrix3x2(1, 0, 0, 1, 10, 20);

            Assert.Equal((12.0, 26.0), Apply(scale.Then(move), 1, 2));
            Assert.Equal((22.0, 66.0), Apply(move.Then(scale), 1, 2));
        }

        [Fact]
        public void TryInvert_UndoesARotationScaleAndTranslation()
        {
            var m = new Matrix3x2(0, 2, -2, 0, 5, 7);

            Assert.True(m.TryInvert(out var inverse));
            var (x, y) = Apply(m, 3, 4);
            var (bx, by) = Apply(inverse, x, y);

            Assert.Equal(3, bx, 9);
            Assert.Equal(4, by, 9);
        }

        [Fact]
        public void TryInvert_FailsForACollapsedMatrix()
        {
            Assert.False(new Matrix3x2(1, 2, 2, 4, 0, 0).TryInvert(out _));
            Assert.False(new Matrix3x2(float.NaN, 0, 0, 1, 0, 0).TryInvert(out _));
        }

        [Fact]
        public void RasterGraphics_AccumulatesPushedTransformsAndRestoresThemOnPop()
        {
            var g = new RasterCanvas(new PdfSharpAdapter(), new RasterSurface(10, 10, 0, 0, 1, 1), 1);
            var first = new Matrix3x2(2, 0, 0, 2, 0, 0);
            var second = new Matrix3x2(1, 0, 0, 1, 5, 0);

            g.PushTransform(first);
            g.PushTransform(second);
            Assert.Equal(second.Then(first), g.CurrentTransform);

            g.PopTransform();
            Assert.Equal(first, g.CurrentTransform);

            g.PopTransform();
            Assert.True(g.CurrentTransform.IsIdentity);
        }

        [Fact]
        public void ARasterRegion_StartsFromItsRequestersTransform()
        {
            var g = new RasterCanvas(new PdfSharpAdapter(), new RasterSurface(20, 20, 0, 0, 1, 1), 1);
            var move = new Matrix3x2(1, 0, 0, 1, 3, 4);
            g.PushTransform(move);

            using var scope = g.BeginRasterSurface(new Rect(0, 0, 5, 5));

            Assert.NotNull(scope);
            Assert.Equal(move, scope!.Graphics.CurrentTransform);
        }
    }
}
