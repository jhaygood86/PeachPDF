using System.Collections.Generic;
using System.Text;

namespace PeachDrawing.Text
{
    /// <summary>
    /// What a caller wants from a family: the inputs to CSS Fonts 4 face matching.
    /// </summary>
    /// <remarks>
    /// Matching looks for the slant first, then the width, then the weight, and takes the nearest face when there
    /// is no exact one. The size of the text is not part of a query: a <see cref="Typeface"/> has no size.
    /// </remarks>
    /// <param name="Weight">The weight wanted, from 1 to 1000; 400 is normal and 700 is bold.</param>
    /// <param name="Width">The width wanted, as an OpenType width class from 1 (ultra-condensed) to 9 (ultra-expanded) with 5 being normal.</param>
    /// <param name="IsItalic">Whether an italic face is wanted.</param>
    /// <param name="MustCover">
    /// A character the face has to be able to draw, or <see langword="null"/> for none. A face is taken to cover the
    /// characters of its <c>unicode-range</c> if it has one, and otherwise the ones its <c>cmap</c> maps.
    /// </param>
    /// <param name="Axes">
    /// Values for the axes of a variable face (CSS <c>font-variation-settings</c>), or <see langword="null"/> for none. A face that is
    /// variable is matched at the location the weight, width and slant of the query give (its <c>wght</c>, <c>wdth</c> and <c>ital</c>
    /// axes), and these settings are applied after that, so they win. A face that is not variable ignores them.
    /// </param>
    public readonly record struct TypefaceQuery(
        int Weight = TypefaceQuery.NormalWeight,
        int Width = TypefaceQuery.NormalWidth,
        bool IsItalic = false,
        Rune? MustCover = null,
        IReadOnlyList<AxisSetting>? Axes = null)
    {
        /// <summary>
        /// Creates the query for regular, upright, normal-width text with no character to cover.
        /// </summary>
        /// <remarks>
        /// <c>default(TypefaceQuery)</c> is not the same thing: like any struct it is all zeros, which is a weight of
        /// zero and a width class of zero, so write <c>new TypefaceQuery()</c> for the normal query.
        /// </remarks>
        public TypefaceQuery()
            : this(NormalWeight, NormalWidth, false, null, null)
        {
        }

        /// <summary>The weight of regular text, 400.</summary>
        public const int NormalWeight = 400;

        /// <summary>The weight of bold text, 700.</summary>
        public const int BoldWeight = 700;

        /// <summary>The normal width class, 5.</summary>
        public const int NormalWidth = 5;
    }
}
