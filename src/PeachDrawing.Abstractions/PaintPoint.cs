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
    ///     Represents an ordered pair of floating-point x- and y-coordinates that defines a point in a two-dimensional plane.
    /// </summary>
    public record struct PaintPoint(double X, double Y)
    {
        /// <summary>
        ///     Represents a new instance of the <see cref="PaintPoint" /> class with member data left uninitialized.
        /// </summary>
        /// <filterpriority>1</filterpriority>
        public static readonly PaintPoint Empty = new();
    }
}