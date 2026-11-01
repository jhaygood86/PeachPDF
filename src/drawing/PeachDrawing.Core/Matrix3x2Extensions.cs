// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using System;
using System.Numerics;

namespace PeachDrawing.Core
{
    /// <summary>
    /// The two composition helpers the former <c>RMatrix</c> type had that <see cref="Matrix3x2"/> does
    /// not: a named "apply this first" alias for <c>value1 * value2</c> (the same row-vector convention
    /// <see cref="Matrix3x2"/>'s own <c>operator *</c> already uses - <c>a.Then(b)</c> and <c>a * b</c>
    /// compute identical results), and re-basing a transform's fixed point without touching its linear
    /// part. <see cref="TryInvert"/> is a from-scratch reimplementation, not a thin wrapper over
    /// <see cref="Matrix3x2.Invert"/>: the BCL method's own near-singular guard
    /// (<c>MathF.Abs(det) &lt; float.Epsilon</c>) does not reject a NaN determinant (a NaN comparison is
    /// always false, so the guard is skipped and the division silently produces a NaN matrix instead of
    /// failing), which the CSS <c>matrix()</c>/<c>transform</c> pipeline relies on failing cleanly for.
    /// </summary>
    public static class Matrix3x2Extensions
    {
        /// <summary>The transform that applies this one first and <paramref name="next"/> after it.</summary>
        public static Matrix3x2 Then(this Matrix3x2 matrix, Matrix3x2 next) => matrix * next;

        /// <summary>The transform that undoes this one; false when it collapses the plane (or is not finite) and has none.</summary>
        public static bool TryInvert(this Matrix3x2 matrix, out Matrix3x2 inverse)
        {
            var determinant = (double)matrix.M11 * matrix.M22 - (double)matrix.M12 * matrix.M21;
            if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
            {
                inverse = Matrix3x2.Identity;
                return false;
            }

            var a = (float)(matrix.M22 / determinant);
            var b = (float)(-matrix.M12 / determinant);
            var c = (float)(-matrix.M21 / determinant);
            var d = (float)(matrix.M11 / determinant);
            inverse = new Matrix3x2(a, b, c, d,
                (float)-(matrix.M31 * (double)a + matrix.M32 * (double)c),
                (float)-(matrix.M31 * (double)b + matrix.M32 * (double)d));
            return true;
        }

        /// <summary>
        /// Reinterprets this matrix - built treating the box's own top-left corner as local (0, 0) -
        /// as pivoting around the given absolute (page-space) point instead. Equivalent to
        /// translate(-px,-py) * this * translate(px,py): the linear part (M11/M12/M21/M22) is
        /// unchanged, only the offset shifts so that (px, py) becomes a fixed point wherever it sits
        /// on the page. Needed because painting draws in absolute page coordinates, and a box's page
        /// position (its Bounds plus the current page's scroll offset) can vary across repeated paint
        /// passes (e.g. pagination), while the underlying matrix itself is cached and computed once.
        /// </summary>
        public static Matrix3x2 RebaseOrigin(this Matrix3x2 matrix, double px, double py)
        {
            var offsetX = matrix.M31 + px * (1 - matrix.M11) - py * matrix.M21;
            var offsetY = matrix.M32 + py * (1 - matrix.M22) - px * matrix.M12;
            return new Matrix3x2(matrix.M11, matrix.M12, matrix.M21, matrix.M22, (float)offsetX, (float)offsetY);
        }
    }
}
