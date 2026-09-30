using System.Collections.Generic;

namespace PeachDrawing.Text
{
    /// <summary>
    /// What a caller says about a font it adds to a <see cref="FontSet"/>, in place of what the file says about
    /// itself: the counterpart of the descriptors of a CSS <c>@font-face</c> rule.
    /// </summary>
    /// <remarks>
    /// Every property left <see langword="null"/> means "use what the font file declares": a variable font that is given no range
    /// covers the range of its own weight, width and slant axes. A stylesheet's
    /// descriptors are the authority on how one resource takes part in matching, whatever the file's own tables say.
    /// </remarks>
    public sealed class AddOptions
    {
        /// <summary>The family name to register the font under, when it is not the family name in the file.</summary>
        public string? FamilyName { get; init; }

        /// <summary>The weight the font is to match, from 1 to 1000, when it is not the weight the file declares.</summary>
        public int? Weight { get; init; }

        /// <summary>Whether the font is italic, when that is not what the file declares.</summary>
        public bool? IsItalic { get; init; }

        /// <summary>
        /// The width the font is to match, as an OpenType width class from 1 (ultra-condensed) to 9
        /// (ultra-expanded) with 5 being normal, when it is not the width the file declares.
        /// </summary>
        public int? Width { get; init; }

        /// <summary>
        /// The range of weights the font covers (CSS <c>font-weight: 100 900</c> in an <c>@font-face</c> rule), from 1 to 1000, when it is
        /// not the weight the file declares. It takes the place of <see cref="Weight"/>. A variable font declared with a range is matched for
        /// every weight in it, and the weight asked for sets the font's weight axis, kept inside the range.
        /// </summary>
        public AxisRange? WeightRange { get; init; }

        /// <summary>
        /// The range of widths the font covers, as percentages of the normal width (CSS <c>font-stretch: 75% 125%</c> in an
        /// <c>@font-face</c> rule); 100 is normal. It takes the place of <see cref="Width"/>.
        /// </summary>
        public AxisRange? WidthRange { get; init; }

        /// <summary>
        /// The range of oblique angles the font covers, in degrees leaning to the right (CSS <c>font-style: oblique 0deg 14deg</c> in an
        /// <c>@font-face</c> rule). A font with a range is oblique, and it also matches upright text when the range includes 0. It
        /// sets the font's slant axis, kept inside the range.
        /// </summary>
        public AxisRange? ObliqueRange { get; init; }

        /// <summary>
        /// The code points the font is used for (CSS <c>unicode-range</c>), or <see langword="null"/> for whatever
        /// its <c>cmap</c> covers.
        /// </summary>
        public IReadOnlyList<RuneInterval>? UnicodeRanges { get; init; }
    }
}
