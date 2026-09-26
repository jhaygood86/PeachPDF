using PeachDrawing.Text.Internal.Text;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Shaping
{
    /// <summary>
    /// Turns text into glyphs: the mapping of characters to glyphs through the font's <c>cmap</c>, the substitutions of its
    /// <c>GSUB</c> table, and the positioning of its <c>GPOS</c> table.
    /// </summary>
    /// <remarks>
    /// Substitution covers single, multiple, alternate, ligature, contextual and chaining contextual, and reverse chaining
    /// lookups, with the mark filtering ligature matching needs and the language-specific feature selection of the font's
    /// language systems. Positioning covers pair and single adjustment, cursive attachment, and mark attachment to a base, a
    /// ligature component and another mark. Arabic-family joining and Indic syllable reordering run as stages of their own, before
    /// the ordered pass that applies the rest.
    /// </remarks>
    public static class Shaper
    {
        /// <summary>
        /// Shapes a run of text in one face.
        /// </summary>
        /// <remarks>
        /// The text is shaped as the one run it is: everything the settings ask for applies to all of it. Characters that are
        /// invisible by definition are removed from the result after they have taken part in substitution and positioning.
        /// </remarks>
        /// <param name="typeface">The face to shape in.</param>
        /// <param name="text">The text, in logical order unless the settings ask for the result in visual order.</param>
        /// <param name="settings">What to apply; <see cref="ShapeSettings.Default"/> for the defaults.</param>
        /// <returns>The glyphs, in the order they are drawn.</returns>
        public static GlyphRun Shape(Typeface typeface, string text, in ShapeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            ArgumentNullException.ThrowIfNull(text);

            return new GlyphRun(typeface, typeface.Face.Descriptor.Shape(text, settings));
        }

        /// <summary>The <c>GSUB</c> feature tags that implement a caps mode, none for <see cref="CapsMode.None"/>.</summary>
        /// <remarks>Together with <see cref="Typeface.SupportsFeatures"/> this says whether a face has the feature for real, or a caller has to fall back.</remarks>
        /// <param name="caps">The caps mode.</param>
        public static IReadOnlySet<string> GetFeatureTags(CapsMode caps) => GsubShaper.GetFeatureTags(caps);

        /// <summary>The <c>GSUB</c> feature tags that implement a subscript or superscript mode, none for <see cref="SubSuperMode.None"/>.</summary>
        /// <remarks>Together with <see cref="Typeface.SupportsFeatures"/> this says whether a face substitutes real sub- or superscript glyphs, or a caller has to synthesize them.</remarks>
        /// <param name="position">The mode.</param>
        public static IReadOnlySet<string> GetFeatureTags(SubSuperMode position) => GsubShaper.GetFeatureTags(position);
    }
}
