using System;
using System.Numerics;
using System.Text;
using PeachBrackets.Css;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// Mixes two <c>transform</c> lists that cannot be mixed function by function (CSS Transforms 1 §13, the
    /// matrix-interpolation case): each list has already been composed to a single matrix, and this decomposes both into
    /// translation, scale, skew and rotation, mixes the parts, and recomposes the result.
    /// </summary>
    /// <remarks>
    /// Matrices use <see cref="Matrix4x4"/>'s row-vector convention (a point is multiplied on the left, translation in the
    /// fourth row), where the CSS decomposition <c>T · R · K · S</c> reads <c>S · K · R · T</c>. A matrix with a perspective
    /// part, or one that collapses a dimension (a zero scale), has no decomposition this class takes; <see cref="TryMix"/>
    /// then reports it and the caller keeps the discrete flip. The recomposed matrix is written as text, in CSS pixels, for
    /// the ordinary <c>transform</c> setter to parse: <c>matrix()</c> when both sides are flat (2D, CSS Transforms 1 §13's
    /// "Interpolation of decomposed 2D matrix values", where the rotation is an angle taking the short way round), otherwise
    /// <c>matrix3d()</c> with the rotation a quaternion mixed along the short arc.
    /// </remarks>
    internal static class TransformMatrixInterpolator
    {
        /// <summary>The parts of a matrix without perspective, which recompose as <c>S · K · R · T</c>.</summary>
        internal readonly record struct Decomposed(
            double Tx, double Ty, double Tz,
            double Sx, double Sy, double Sz,
            double SkewXy, double SkewXz, double SkewYz,
            Quaternion Rotation, double Angle, bool IsFlat);

        private const double Epsilon = 1e-9;

        /// <summary>
        /// The matrix between <paramref name="from"/> and <paramref name="to"/> at <paramref name="t"/>, as a
        /// <c>matrix()</c>/<c>matrix3d()</c> value in CSS pixels; false when either cannot be decomposed.
        /// Translations are in the layout unit (points) on the way in, as <c>ComposeTransformFunctions</c> returns them.
        /// </summary>
        public static bool TryMix(Matrix4x4 from, Matrix4x4 to, double t, out string result)
        {
            result = string.Empty;
            if (!TryDecompose(from, out var a) || !TryDecompose(to, out var b)) return false;

            // CSS Transforms 1 §13: a flat side meeting a spatial one is decomposed as a spatial matrix too, so that a mirror
            // is carried the same way on both.
            if (a.IsFlat != b.IsFlat)
            {
                // Cannot fail: the flat side decomposed a moment ago, and only the perspective test and the collapsed-scale
                // test depend on allowFlat, which the flat form already passed.
                _ = a.IsFlat ? TryDecompose(from, out a, allowFlat: false) : TryDecompose(to, out b, allowFlat: false);
            }

            var mixed = Mix(a, b, t);
            result = Format(Recompose(mixed), mixed);
            return true;
        }

        /// <summary>
        /// Splits <paramref name="matrix"/> into its parts; false for a perspective part, a collapsed dimension or a non-finite entry.
        /// A flat matrix decomposes as an angle unless <paramref name="allowFlat"/> is false.
        /// </summary>
        public static bool TryDecompose(Matrix4x4 matrix, out Decomposed result, bool allowFlat = true)
        {
            result = default;

            double[,] m =
            {
                { matrix.M11, matrix.M12, matrix.M13, matrix.M14 },
                { matrix.M21, matrix.M22, matrix.M23, matrix.M24 },
                { matrix.M31, matrix.M32, matrix.M33, matrix.M34 },
                { matrix.M41, matrix.M42, matrix.M43, matrix.M44 }
            };

            foreach (var entry in m)
            {
                if (!double.IsFinite(entry)) return false;
            }

            // Perspective (CSS Transforms 2 "Decomposing a 3D matrix" would carry it as a fifth part; not taken here).
            if (Math.Abs(m[3, 3]) < Epsilon) return false;
            if (m[3, 3] != 1)
            {
                for (var i = 0; i < 4; i++)
                    for (var j = 0; j < 4; j++)
                        m[i, j] /= m[3, 3];
            }

            if (Math.Abs(m[0, 3]) > Epsilon || Math.Abs(m[1, 3]) > Epsilon || Math.Abs(m[2, 3]) > Epsilon) return false;

            var flat = allowFlat && Math.Abs(m[0, 2]) < Epsilon && Math.Abs(m[1, 2]) < Epsilon && Math.Abs(m[2, 0]) < Epsilon
                && Math.Abs(m[2, 1]) < Epsilon && Math.Abs(m[3, 2]) < Epsilon && Math.Abs(m[2, 2] - 1) < Epsilon;

            var row0 = new Vector3d(m[0, 0], m[0, 1], flat ? 0 : m[0, 2]);
            var row1 = new Vector3d(m[1, 0], m[1, 1], flat ? 0 : m[1, 2]);
            var row2 = flat ? new Vector3d(0, 0, 1) : new Vector3d(m[2, 0], m[2, 1], m[2, 2]);

            // Gram-Schmidt: the lengths are the scales, the removed projections the skews, what is left a rotation.
            var sx = row0.Length;
            if (sx < Epsilon) return false;
            var u0 = row0 / sx;

            var skewXy = Vector3d.Dot(u0, row1);
            var rest1 = row1 - u0 * skewXy;
            var sy = rest1.Length;
            if (sy < Epsilon) return false;
            var u1 = rest1 / sy;
            skewXy /= sy;

            double skewXz = 0, skewYz = 0, sz = 1;
            Vector3d u2;
            if (flat)
            {
                u2 = new Vector3d(0, 0, 1);
            }
            else
            {
                skewXz = Vector3d.Dot(u0, row2);
                var rest2 = row2 - u0 * skewXz;
                skewYz = Vector3d.Dot(u1, rest2);
                rest2 -= u1 * skewYz;
                sz = rest2.Length;
                if (sz < Epsilon) return false;
                u2 = rest2 / sz;
                skewXz /= sz;
                skewYz /= sz;
            }

            var tx = m[3, 0];
            var ty = m[3, 1];
            var tz = m[3, 2];

            if (flat)
            {
                // A flip is carried by one scale alone, so the rotation stays a proper one (an angle); which one is the
                // spec's choice (the x scale when the matrix's own m11 is below its m22). The skew is a ratio to the flipped
                // scale, so it changes sign with it.
                if (u0.X * u1.Y - u0.Y * u1.X < 0)
                {
                    if (m[0, 0] < m[1, 1])
                    {
                        u0 = -u0;
                        sx = -sx;
                    }
                    else
                    {
                        u1 = -u1;
                        sy = -sy;
                    }

                    skewXy = -skewXy;
                }

                var angle = Math.Atan2(u0.Y, u0.X);
                result = new Decomposed(tx, ty, tz, sx, sy, sz, skewXy, 0, 0, Quaternion.Identity, angle, true);
                return true;
            }

            // A mirror is carried by negating every scale and axis; the skews are ratios and do not change.
            if (Vector3d.Dot(u0, Vector3d.Cross(u1, u2)) < 0)
            {
                u0 = -u0; u1 = -u1; u2 = -u2;
                sx = -sx; sy = -sy; sz = -sz;
            }

            var rotation = new Matrix4x4(
                (float)u0.X, (float)u0.Y, (float)u0.Z, 0,
                (float)u1.X, (float)u1.Y, (float)u1.Z, 0,
                (float)u2.X, (float)u2.Y, (float)u2.Z, 0,
                0, 0, 0, 1);
            var q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));

            result = new Decomposed(tx, ty, tz, sx, sy, sz, skewXy, skewXz, skewYz, q, 0, false);
            return true;
        }

        /// <summary>Mixes the parts; both sides flat mixes the angle (CSS Transforms 1 §13), otherwise the quaternion.</summary>
        public static Decomposed Mix(Decomposed a, Decomposed b, double t)
        {
            static double Lerp(double x, double y, double t) => x + (y - x) * t;

            if (a.IsFlat && b.IsFlat)
            {
                var angleA = a.Angle;
                var angleB = b.Angle;

                // A mirror in x on one side and in y on the other would mix through a zero scale: the same matrix
                // is a mirror in the other axis, a half turn on (negating both scales and turning 180 degrees).
                if ((a.Sx < 0 && b.Sy < 0) || (a.Sy < 0 && b.Sx < 0))
                {
                    a = a with { Sx = -a.Sx, Sy = -a.Sy };
                    angleA += Math.PI;
                }

                // The short way round: when the angles are more than half a turn apart, shift the smaller one by a turn.
                if (Math.Abs(angleA - angleB) > Math.PI)
                {
                    if (angleA < angleB) angleA += 2 * Math.PI;
                    else angleB += 2 * Math.PI;
                }

                return new Decomposed(
                    Lerp(a.Tx, b.Tx, t), Lerp(a.Ty, b.Ty, t), 0,
                    Lerp(a.Sx, b.Sx, t), Lerp(a.Sy, b.Sy, t), 1,
                    Lerp(a.SkewXy, b.SkewXy, t), 0, 0,
                    Quaternion.Identity, Lerp(angleA, angleB, t), true);
            }

            return new Decomposed(
                Lerp(a.Tx, b.Tx, t), Lerp(a.Ty, b.Ty, t), Lerp(a.Tz, b.Tz, t),
                Lerp(a.Sx, b.Sx, t), Lerp(a.Sy, b.Sy, t), Lerp(a.Sz, b.Sz, t),
                Lerp(a.SkewXy, b.SkewXy, t), Lerp(a.SkewXz, b.SkewXz, t), Lerp(a.SkewYz, b.SkewYz, t),
                Quaternion.Slerp(ToQuaternion(a), ToQuaternion(b), (float)t), 0, false);
        }

        /// <summary>The matrix (layout-unit translation) the parts compose to: <c>S · K · R · T</c>.</summary>
        public static Matrix4x4 Recompose(Decomposed d)
        {
            var scale = Matrix4x4.CreateScale((float)d.Sx, (float)d.Sy, (float)d.Sz);
            var skew = Matrix4x4.Identity;
            skew.M21 = (float)d.SkewXy;
            skew.M31 = (float)d.SkewXz;
            skew.M32 = (float)d.SkewYz;

            var rotation = d.IsFlat ? Matrix4x4.CreateRotationZ((float)d.Angle) : Matrix4x4.CreateFromQuaternion(d.Rotation);
            var linear = scale * skew * rotation;

            linear.M41 = (float)d.Tx;
            linear.M42 = (float)d.Ty;
            linear.M43 = (float)d.Tz;
            return linear;
        }

        private static Quaternion ToQuaternion(Decomposed d) => d.IsFlat ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)d.Angle) : d.Rotation;

        /// <summary>
        /// The text of the recomposed matrix <paramref name="m"/>. The translation is written from <paramref name="parts"/>, which
        /// holds it in doubles, rather than from <paramref name="m"/>, whose float entries run out of digits for a large one.
        /// </summary>
        private static string Format(Matrix4x4 m, Decomposed parts)
        {
            // The matrix lengths are points; a CSS matrix()'s are pixels (the same factor BuildFunctionMatrix undoes).
            const double pointsPerPx = Length.PointsPerPx;

            static string N(double value) => CssValueInterpolator.Format(value);

            var sb = new StringBuilder();

            if (parts.IsFlat)
            {
                return sb.Append("matrix(")
                    .Append(N(m.M11)).Append(", ").Append(N(m.M12)).Append(", ")
                    .Append(N(m.M21)).Append(", ").Append(N(m.M22)).Append(", ")
                    .Append(N(parts.Tx / pointsPerPx)).Append(", ").Append(N(parts.Ty / pointsPerPx)).Append(')')
                    .ToString();
            }

            // CSS lists matrix3d() column-major, which is the row-major Matrix4x4 read straight through.
            return sb.Append("matrix3d(")
                .Append(N(m.M11)).Append(", ").Append(N(m.M12)).Append(", ").Append(N(m.M13)).Append(", ").Append(N(m.M14)).Append(", ")
                .Append(N(m.M21)).Append(", ").Append(N(m.M22)).Append(", ").Append(N(m.M23)).Append(", ").Append(N(m.M24)).Append(", ")
                .Append(N(m.M31)).Append(", ").Append(N(m.M32)).Append(", ").Append(N(m.M33)).Append(", ").Append(N(m.M34)).Append(", ")
                .Append(N(parts.Tx / pointsPerPx)).Append(", ").Append(N(parts.Ty / pointsPerPx)).Append(", ").Append(N(parts.Tz / pointsPerPx)).Append(", ").Append(N(m.M44))
                .Append(')').ToString();
        }

        /// <summary>A double-precision 3-vector: the decomposition runs in doubles, <see cref="Vector3"/> would lose digits in a translation.</summary>
        private readonly record struct Vector3d(double X, double Y, double Z)
        {
            public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

            public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector3d operator -(Vector3d a) => new(-a.X, -a.Y, -a.Z);
            public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
            public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);

            public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            public static Vector3d Cross(Vector3d a, Vector3d b) =>
                new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
    }
}
