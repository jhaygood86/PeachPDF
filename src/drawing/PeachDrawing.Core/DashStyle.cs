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
    /// Specifies the style of dashed lines drawn with a <see cref="Pen"/> object.
    /// </summary>
    public enum DashStyle
    {
        /// <summary>A solid, unbroken line.</summary>
        Solid,
        /// <summary>A line of evenly spaced dashes.</summary>
        Dash,
        /// <summary>A line of evenly spaced dots.</summary>
        Dot,
        /// <summary>A repeating dash-dot pattern.</summary>
        DashDot,
        /// <summary>A repeating dash-dot-dot pattern.</summary>
        DashDotDot,
        /// <summary>A user-supplied pattern; see <see cref="Pen.DashPattern"/>.</summary>
        Custom,
    }
}