using System.Collections.Generic;

namespace PeachDrawing.Text
{
    /// <summary>
    /// What a caller says about a font it adds to a <see cref="FontSet"/>, in place of what the file says about
    /// itself: the counterpart of the descriptors of a CSS <c>@font-face</c> rule.
    /// </summary>
    /// <remarks>
    /// Every property left <see langword="null"/> means "use what the font file declares". A stylesheet's
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
        /// The code points the font is used for (CSS <c>unicode-range</c>), or <see langword="null"/> for whatever
        /// its <c>cmap</c> covers.
        /// </summary>
        public IReadOnlyList<RuneInterval>? UnicodeRanges { get; init; }
    }
}
