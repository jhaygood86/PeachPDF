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
    /// Specifies how the interior of a (possibly self-intersecting) path is determined for filling.
    /// </summary>
    public enum FillMode
    {
        /// <summary>PDF 32000-1 §8.5.3.3: a point is inside when the signed count of edge crossings around it is nonzero.</summary>
        Nonzero,
        /// <summary>PDF 32000-1 §8.5.3.3: a point is inside when a ray from it crosses the path an odd number of times.</summary>
        EvenOdd,
    }
}
