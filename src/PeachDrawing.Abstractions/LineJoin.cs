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

namespace PeachDrawing.Abstractions
{
    /// <summary>
    /// Specifies how consecutive stroked segments are joined, matching SVG's <c>stroke-linejoin</c> values.
    /// </summary>
    public enum LineJoin
    {
        /// <summary>The outer edges of the two segments are extended until they meet at a point, up to <see cref="Pen.MiterLimit"/>.</summary>
        Miter,
        /// <summary>A circular arc, centered on the join point with radius half the stroke width.</summary>
        Round,
        /// <summary>The two segments' outer corners are connected directly, cutting off the miter point.</summary>
        Bevel,
    }
}
