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

namespace PeachPDF.Html.Adapters.Entities
{
    /// <summary>
    /// Represents a 2D affine transform matrix, in the same 6-value convention as the CSS
    /// <c>matrix(a, b, c, d, e, f)</c> function: a point (x, y) maps to
    /// (x*M11 + y*M21 + OffsetX, x*M12 + y*M22 + OffsetY).
    /// </summary>
    internal readonly struct RMatrix
    {
        /// <summary>
        /// Represents the identity transform matrix.
        /// </summary>
        public static readonly RMatrix Identity = new(1, 0, 0, 1, 0, 0);

        public RMatrix(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
        {
            M11 = m11;
            M12 = m12;
            M21 = m21;
            M22 = m22;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public double M11 { get; }
        public double M12 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }

        public bool IsIdentity =>
            M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;
    }
}
