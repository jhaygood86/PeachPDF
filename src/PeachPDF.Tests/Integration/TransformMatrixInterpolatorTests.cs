using PeachPDF.Html.Core.Animation;
using System;
using System.Numerics;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The decomposition behind CSS Transforms 1 §13's matrix interpolation: two transform lists that do not match
    /// function for function are mixed as the matrices they compose to.
    /// </summary>
    public class TransformMatrixInterpolatorTests
    {
        private const float Tolerance = 1e-4f;

        private static void AssertMatrix(Matrix4x4 expected, Matrix4x4 actual, float tolerance = Tolerance)
        {
            Assert.Equal(expected.M11, actual.M11, tolerance); Assert.Equal(expected.M12, actual.M12, tolerance);
            Assert.Equal(expected.M13, actual.M13, tolerance); Assert.Equal(expected.M14, actual.M14, tolerance);
            Assert.Equal(expected.M21, actual.M21, tolerance); Assert.Equal(expected.M22, actual.M22, tolerance);
            Assert.Equal(expected.M23, actual.M23, tolerance); Assert.Equal(expected.M24, actual.M24, tolerance);
            Assert.Equal(expected.M31, actual.M31, tolerance); Assert.Equal(expected.M32, actual.M32, tolerance);
            Assert.Equal(expected.M33, actual.M33, tolerance); Assert.Equal(expected.M34, actual.M34, tolerance);
            Assert.Equal(expected.M41, actual.M41, tolerance); Assert.Equal(expected.M42, actual.M42, tolerance);
            Assert.Equal(expected.M43, actual.M43, tolerance); Assert.Equal(expected.M44, actual.M44, tolerance);
        }

        private static Matrix4x4 Skewed(float xy, float xz, float yz)
        {
            var skew = Matrix4x4.Identity;
            skew.M21 = xy;
            skew.M31 = xz;
            skew.M32 = yz;
            return skew;
        }

        public static TheoryData<string> Matrices => new()
        {
            "translate",
            "rotate-z",
            "scale",
            "flat-everything",
            "flat-mirrored",
            "rotate-3d",
            "everything-3d",
            "mirrored-3d"
        };

        private static Matrix4x4 Build(string name)
        {
            // Row-vector convention: the first factor is applied first.
            return name switch
            {
                "translate" => Matrix4x4.CreateTranslation(30, -12, 0),
                "rotate-z" => Matrix4x4.CreateRotationZ(0.7f),
                "scale" => Matrix4x4.CreateScale(2, 0.5f, 1),
                "flat-everything" => Matrix4x4.CreateScale(2, 3, 1) * Skewed(0.4f, 0, 0) * Matrix4x4.CreateRotationZ(1.1f) * Matrix4x4.CreateTranslation(5, 6, 0),
                "flat-mirrored" => Matrix4x4.CreateScale(-2, 1.5f, 1) * Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(1, 2, 0),
                "rotate-3d" => Matrix4x4.CreateRotationX(0.9f) * Matrix4x4.CreateRotationY(-0.4f),
                "everything-3d" => Matrix4x4.CreateScale(1.5f, 2, 0.7f) * Skewed(0.3f, -0.2f, 0.5f)
                    * Matrix4x4.CreateFromYawPitchRoll(0.5f, -0.8f, 1.2f) * Matrix4x4.CreateTranslation(7, -3, 11),
                "mirrored-3d" => Matrix4x4.CreateScale(1, -2, 1.5f) * Matrix4x4.CreateFromYawPitchRoll(0.2f, 0.6f, -0.9f) * Matrix4x4.CreateTranslation(4, 5, 6),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }

        [Theory]
        [MemberData(nameof(Matrices))]
        public void Decompose_ThenRecompose_ReproducesTheMatrix(string name)
        {
            var original = Build(name);

            Assert.True(TransformMatrixInterpolator.TryDecompose(original, out var parts));

            AssertMatrix(original, TransformMatrixInterpolator.Recompose(parts), 1e-3f);
        }

        [Fact]
        public void Decompose_FlatMatrix_IsFlat_And_ARotationAboutXIsNot()
        {
            Assert.True(TransformMatrixInterpolator.TryDecompose(Build("flat-everything"), out var flat));
            Assert.True(flat.IsFlat);

            Assert.True(TransformMatrixInterpolator.TryDecompose(Build("rotate-3d"), out var spatial));
            Assert.False(spatial.IsFlat);
        }

        [Fact]
        public void Decompose_Mirror_IsCarriedByTheScale()
        {
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(-1, 1, 1), out var flat));
            Assert.Equal(-1, flat.Sx * flat.Sy, 6);

            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(-1, 1, 1) * Matrix4x4.CreateRotationX(0.5f), out var spatial));
            Assert.Equal(-1, Math.Sign(spatial.Sx * spatial.Sy * spatial.Sz));
        }

        [Fact]
        public void Decompose_Perspective_HasNoDecomposition()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 300;

            Assert.False(TransformMatrixInterpolator.TryDecompose(perspective, out _));
        }

        [Theory]
        [InlineData(0f, 1f, 1f)]   // x collapsed
        [InlineData(1f, 0f, 1f)]
        [InlineData(1f, 1f, 0f)]   // z collapsed
        public void Decompose_ACollapsedDimension_HasNoDecomposition(float x, float y, float z) =>
            Assert.False(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(x, y, z) * Matrix4x4.CreateRotationZ(0.4f), out _));

        [Fact]
        public void Decompose_ANonFiniteEntry_HasNoDecomposition()
        {
            var broken = Matrix4x4.Identity;
            broken.M11 = float.NaN;

            Assert.False(TransformMatrixInterpolator.TryDecompose(broken, out _));
        }

        [Fact]
        public void Mix_Ends_AreTheTwoMatrices()
        {
            var a = Build("flat-everything");
            var b = Build("translate");

            Assert.True(TransformMatrixInterpolator.TryDecompose(a, out var from));
            Assert.True(TransformMatrixInterpolator.TryDecompose(b, out var to));

            AssertMatrix(a, TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(from, to, 0)), 1e-3f);
            AssertMatrix(b, TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(from, to, 1)), 1e-3f);
        }

        [Fact]
        public void Mix_Flat_TurnsTheShortWayRound()
        {
            // 10deg to 350deg is 20deg the other way: half way is 0deg, not 180deg.
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationZ(MathF.PI * 10 / 180), out var from));
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationZ(MathF.PI * 350 / 180), out var to));

            var half = TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(from, to, 0.5));

            AssertMatrix(Matrix4x4.Identity, half);
        }

        [Theory]
        [InlineData(170, 190)]    // 20 degrees apart across the half turn, in either order
        [InlineData(190, 170)]
        public void Mix_Flat_TurnsTheShortWayRound_AcrossTheHalfTurn(int fromDegrees, int toDegrees)
        {
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationZ(MathF.PI * fromDegrees / 180), out var from));
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationZ(MathF.PI * toDegrees / 180), out var to));

            var half = TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(from, to, 0.5));

            AssertMatrix(Matrix4x4.CreateRotationZ(MathF.PI), half);
        }

        [Fact]
        public void Mix_Flat_AMirrorInXAgainstAMirrorInY_DoesNotPassThroughAZeroScale()
        {
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(-1, 1, 1), out var x));
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(1, -1, 1), out var y));

            var half = TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(x, y, 0.5));

            // A mirror turned a quarter: still a plane of area 1, where mixing the scales would have given 0.
            Assert.Equal(-1.0, half.M11 * half.M22 - half.M12 * half.M21, 3);

            AssertMatrix(Matrix4x4.CreateScale(-1, 1, 1), TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(x, y, 0)));
            AssertMatrix(Matrix4x4.CreateScale(1, -1, 1), TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(x, y, 1)));
        }

        [Fact]
        public void Mix_Spatial_TakesTheShortArc_WhenTheQuaternionsPointAwayFromEachOther()
        {
            // +170 and -170 degrees about x: the quaternions have a negative dot product, and the short way between them is
            // through 180 degrees. Going the long way would pass through the identity.
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationX(MathF.PI * 170 / 180), out var from));
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationX(-MathF.PI * 170 / 180), out var to));

            var half = TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(from, to, 0.5));

            AssertMatrix(Matrix4x4.CreateRotationX(MathF.PI), half);
        }

        [Fact]
        public void TryMix_ASpatialMatrixAgainstAFlatMirror_EndsAtBothMatrices()
        {
            var spatial = Matrix4x4.CreateRotationX(MathF.PI / 4) * Matrix4x4.CreateTranslation(0, 0, 3);
            var flat = Matrix4x4.CreateScale(1, -1, 1) * Matrix4x4.CreateTranslation(6, 0, 0);

            Assert.True(TransformMatrixInterpolator.TryMix(spatial, flat, 1, out var atEnd));

            Assert.Equal("matrix3d(1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 8, 0, 0, 1)", atEnd);
        }

        [Fact]
        public void Mix_Flat_MixesTranslationScaleAndRotationTogether()
        {
            // The parts mix on their own; the matrices are not averaged.
            var a = Matrix4x4.Identity;
            var b = Matrix4x4.CreateScale(3, 3, 1) * Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(20, 40, 0);

            Assert.True(TransformMatrixInterpolator.TryDecompose(a, out var from));
            Assert.True(TransformMatrixInterpolator.TryDecompose(b, out var to));

            var half = TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(from, to, 0.5));

            AssertMatrix(Matrix4x4.CreateScale(2, 2, 1) * Matrix4x4.CreateRotationZ(MathF.PI / 4) * Matrix4x4.CreateTranslation(10, 20, 0), half);
        }

        [Fact]
        public void Mix_Spatial_RotatesAlongTheShortArc()
        {
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationX(0.1f), out var from));
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0, 10), out var to));

            var half = TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(from, to, 0.5));

            AssertMatrix(Matrix4x4.CreateRotationX((0.1f + MathF.PI / 2) / 2) * Matrix4x4.CreateTranslation(0, 0, 5), half);
        }

        [Fact]
        public void Mix_AFlatAndASpatialSide_MeetInTheQuaternion()
        {
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationZ(0), out var flat));
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateRotationX(0.5f), out var spatial));

            var end = TransformMatrixInterpolator.Mix(flat, spatial, 1);

            Assert.False(end.IsFlat);
            AssertMatrix(Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateRotationX(0.5f), TransformMatrixInterpolator.Recompose(end), 1e-3f);
        }

        [Fact]
        public void Decompose_FlatMirror_FollowsTheSpecsAxisChoice()
        {
            // m11 < m22: the x scale carries the mirror, and the angle stays 0.
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(-1, 1, 1), out var x));
            Assert.Equal(-1, x.Sx, 6);
            Assert.Equal(1, x.Sy, 6);
            Assert.Equal(0, x.Angle, 6);

            // Otherwise the y scale does.
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(1, -1, 1), out var y));
            Assert.Equal(1, y.Sx, 6);
            Assert.Equal(-1, y.Sy, 6);
            Assert.Equal(0, y.Angle, 6);
        }

        [Fact]
        public void Decompose_ASkewedFlatMirror_ReproducesTheMatrix()
        {
            foreach (var scale in new[] { Matrix4x4.CreateScale(-1, 1, 1), Matrix4x4.CreateScale(1, -1, 1) })
            {
                var original = scale * Skewed(0.6f, 0, 0) * Matrix4x4.CreateRotationZ(0.4f) * Matrix4x4.CreateTranslation(3, 4, 0);

                Assert.True(TransformMatrixInterpolator.TryDecompose(original, out var parts));

                AssertMatrix(original, TransformMatrixInterpolator.Recompose(parts), 1e-3f);
            }
        }

        [Fact]
        public void Decompose_AllowFlatFalse_TreatsAFlatMatrixAsSpatial()
        {
            Assert.True(TransformMatrixInterpolator.TryDecompose(Build("flat-mirrored"), out var spatial, allowFlat: false));

            Assert.False(spatial.IsFlat);
            AssertMatrix(Build("flat-mirrored"), TransformMatrixInterpolator.Recompose(spatial), 1e-3f);
        }

        [Fact]
        public void TryMix_AFlatMirrorAgainstASpatialMatrix_EndsAtBothMatrices()
        {
            var flat = Matrix4x4.CreateScale(1, -1, 1) * Matrix4x4.CreateTranslation(6, 0, 0);
            var spatial = Matrix4x4.CreateRotationX(MathF.PI / 4) * Matrix4x4.CreateTranslation(0, 0, 3);

            // Mixed once at each end, through the text the setter would parse, in pixels (6pt is 8px, 3pt is 4px).
            Assert.True(TransformMatrixInterpolator.TryMix(flat, spatial, 0, out var atStart));
            Assert.True(TransformMatrixInterpolator.TryMix(flat, spatial, 1, out var atEnd));

            Assert.Equal("matrix3d(1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 8, 0, 0, 1)", atStart);
            Assert.Equal("matrix3d(1, 0, 0, 0, 0, 0.707107, 0.707107, 0, 0, -0.707107, 0.707107, 0, 0, 0, 4, 1)", atEnd);
        }

        [Fact]
        public void Mix_Flat_AMirrorInYAgainstAMirrorInX_DoesNotPassThroughAZeroScale()
        {
            // The same pair in the other order takes the other arm of the fix-up.
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(1, -1, 1), out var y));
            Assert.True(TransformMatrixInterpolator.TryDecompose(Matrix4x4.CreateScale(-1, 1, 1), out var x));

            var half = TransformMatrixInterpolator.Recompose(TransformMatrixInterpolator.Mix(y, x, 0.5));

            Assert.Equal(-1.0, half.M11 * half.M22 - half.M12 * half.M21, 3);
        }

        [Fact]
        public void Decompose_ANonUnitHomogeneousScale_IsNormalisedAway()
        {
            // Every entry doubled, including the 1 in the corner: the same transform as the identity.
            var doubled = new Matrix4x4(2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2);

            Assert.True(TransformMatrixInterpolator.TryDecompose(doubled, out var parts));
            AssertMatrix(Matrix4x4.Identity, TransformMatrixInterpolator.Recompose(parts));

            Assert.False(TransformMatrixInterpolator.TryDecompose(new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0), out _));
        }

        [Fact]
        public void TryMix_Flat_WritesAMatrixInPixels()
        {
            // 75pt is 100px (1px = 0.75pt).
            Assert.True(TransformMatrixInterpolator.TryMix(Matrix4x4.Identity, Matrix4x4.CreateTranslation(75, 0, 0), 1, out var text));

            Assert.Equal("matrix(1, 0, 0, 1, 100, 0)", text);
        }

        [Fact]
        public void TryMix_Spatial_WritesAMatrix3d()
        {
            Assert.True(TransformMatrixInterpolator.TryMix(Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, 0, 75), 1, out var text));

            Assert.Equal("matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 100, 1)", text);
        }

        [Fact]
        public void TryMix_WithoutADecomposition_ReportsIt()
        {
            Assert.False(TransformMatrixInterpolator.TryMix(Matrix4x4.Identity, Matrix4x4.CreateScale(0, 1, 1), 0.5, out var text));
            Assert.Equal(string.Empty, text);
        }
    }
}
