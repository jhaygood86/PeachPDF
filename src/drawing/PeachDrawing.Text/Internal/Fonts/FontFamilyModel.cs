using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// One registered face of a <see cref="FontFamilyModel"/>: its CSS Fonts Level 4 matching axes
    /// (numeric weight, italic, stretch), the codepoint <see cref="RuneInterval"/>s it is restricted to (from
    /// an <c>@font-face</c> <c>unicode-range</c> descriptor or an explicit registration list) or null when
    /// it has none - in which case its effective coverage is whatever its font's <c>cmap</c> actually
    /// supports - and the description the resolver hands back once this face is chosen.
    /// </summary>
    internal sealed record FontFaceEntry(
        int Weight,
        bool Italic,
        int Stretch,
        IReadOnlyList<RuneInterval>? ExplicitRanges,
        TtfFontDescription Description)
    {
        /// <summary>
        /// The weights, widths and oblique angles this face covers when it declares more than a single weight and width: a range
        /// from an <c>@font-face</c> descriptor, or the range of the axes of a variable font that was given none. <see langword="null"/>
        /// for an ordinary face, which covers exactly <see cref="Weight"/> and <see cref="Stretch"/>.
        /// </summary>
        public FaceRanges? Declared { get; init; }

        /// <summary>What this face covers: <see cref="Declared"/>, or the single weight and width it is registered at.</summary>
        public FaceRanges Ranges => Declared ?? FaceRanges.Point(Weight, Stretch);
    }

    internal class FontFamilyModel
    {
        public string Name { get; set; } = null!;

        /// <summary>
        /// Every registered face for this family. Unlike the previous
        /// <c>Dictionary&lt;(weight,italic,stretch), TtfFontDescription&gt;</c>, this is a list because two
        /// faces can legitimately share the same matching axes yet differ by <c>unicode-range</c> (e.g. a
        /// Latin subset and a Cyrillic subset of one webfont family, both regular weight) - they are
        /// distinguished per-codepoint during resolution. See <see cref="FontResolver"/>'s codepoint-aware
        /// nearest-face matching (CSS Fonts Level 4 §5).
        /// </summary>
        public List<FontFaceEntry> Faces { get; } = [];
    }
}
