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

namespace PeachDrawing.Core
{
    /// <summary>
    /// Specifies how the ends of an unclosed subpath are drawn, matching SVG's <c>stroke-linecap</c> values.
    /// </summary>
    public enum LineCap
    {
        /// <summary>The stroke ends exactly at the subpath's endpoint, with no cap.</summary>
        Butt,
        /// <summary>A semicircular cap, centered on the endpoint, with radius half the stroke width.</summary>
        Round,
        /// <summary>A square cap that extends past the endpoint by half the stroke width.</summary>
        Square,
    }
}
