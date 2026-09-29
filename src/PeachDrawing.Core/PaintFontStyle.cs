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

namespace PeachDrawing.Core
{
    /// <summary>
    /// Specifies style information applied to text.
    /// </summary>
    [Flags]
    public enum PaintFontStyle
    {
        /// <summary>No style bits set.</summary>
        Regular = 0,
        /// <summary>Bold weight (synthesized if the matched face isn't already bold enough).</summary>
        Bold = 1,
        /// <summary>Italic slant (synthesized if the matched face isn't already italic/oblique).</summary>
        Italic = 2,
        /// <summary>An underline drawn beneath the text.</summary>
        Underline = 4,
        /// <summary>A line drawn through the middle of the text.</summary>
        Strikeout = 8,
    }
}