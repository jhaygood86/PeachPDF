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

        /// <summary>
        /// Reinterprets this matrix - built treating the box's own top-left corner as local (0, 0) -
        /// as pivoting around the given absolute (page-space) point instead. Equivalent to
        /// translate(-px,-py) * this * translate(px,py): the linear part (M11/M12/M21/M22) is
        /// unchanged, only the offset shifts so that (px, py) becomes a fixed point wherever it sits
        /// on the page. Needed because painting draws in absolute page coordinates, and a box's page
        /// position (its Bounds plus the current page's scroll offset) can vary across repeated paint
        /// passes (e.g. pagination), while the underlying matrix itself is cached and computed once.
        /// </summary>
        public RMatrix RebaseOrigin(double px, double py)
        {
            var offsetX = OffsetX + px * (1 - M11) - py * M21;
            var offsetY = OffsetY + py * (1 - M22) - px * M12;
            return new RMatrix(M11, M12, M21, M22, offsetX, offsetY);
        }
    }
}
