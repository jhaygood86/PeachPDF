using System.Numerics;

namespace PeachDrawing.Abstractions.Tests.Geometry
{
    public class Matrix3x2ExtensionsTests
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
        public void Then_MatchesPlainMatrixMultiplication()
        {
            var a = new Matrix3x2(1, 2, 3, 4, 5, 6);
            var b = new Matrix3x2(6, 5, 4, 3, 2, 1);

            Assert.Equal(a * b, a.Then(b));
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
        }

        [Fact]
        public void TryInvert_FailsForANonFiniteDeterminant()
        {
            // Matrix3x2.Invert's own near-singular guard (MathF.Abs(det) < float.Epsilon) does not
            // reject a NaN determinant, since a NaN comparison is always false - this is exactly why
            // TryInvert re-derives its own explicit IsFinite check rather than delegating to it.
            Assert.False(new Matrix3x2(float.NaN, 0, 0, 1, 0, 0).TryInvert(out _));
        }

        [Fact]
        public void RebaseOrigin_Identity_StaysIdentityAnywhere()
        {
            var rebased = Matrix3x2.Identity.RebaseOrigin(1234, -567);

            Assert.True(rebased.IsIdentity);
        }

        [Fact]
        public void RebaseOrigin_KeepsTheRebasePointFixed()
        {
            // A rotation built as if the origin were local (0,0), when rebased to an arbitrary
            // absolute point, must leave that exact point unmoved.
            var local = new Matrix3x2(0, 1, -1, 0, 0, 0); // rotate(90deg) around local (0,0)
            var rebased = local.RebaseOrigin(347.5, -12.25);

            var (mappedX, mappedY) = Apply(rebased, 347.5, -12.25);

            Assert.Equal(347.5, mappedX, 6);
            Assert.Equal(-12.25, mappedY, 6);
        }

        [Fact]
        public void RebaseOrigin_PureTranslation_IsUnaffectedByThePagePosition()
        {
            var local = new Matrix3x2(1, 0, 0, 1, 50, 20);
            var rebased = local.RebaseOrigin(999, -333);

            Assert.Equal(50, rebased.M31, 6);
            Assert.Equal(20, rebased.M32, 6);
        }
    }
}
