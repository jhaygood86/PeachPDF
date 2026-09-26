namespace PeachDrawing.Text.Shaping
{
    /// <summary>
    /// One glyph a shaper produced, with where it came from in the text and how far it is nudged from where its advance would
    /// put it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deltas and offsets are the positioning (<c>GPOS</c>) adjustments in design units, the same space as
    /// <see cref="Typeface.GetAdvance"/>, and are all zero for a glyph that positioning does not touch. A glyph's pen advance is
    /// <c>GetAdvance(GlyphIndex) + XAdvanceDelta</c>.
    /// </para>
    /// <para>
    /// A ligature is one glyph whose cluster covers the whole matched span of the text. A glyph that a multiple substitution
    /// adds after the first output of its source has a cluster of zero length, anchored at the end of that source.
    /// </para>
    /// <para>
    /// Two glyphs are equal when all their values are, except that the component list of a ligature is compared by reference, so
    /// two ligature glyphs from separate shaping calls are never equal.
    /// </para>
    /// </remarks>
    /// <param name="GlyphIndex">The glyph's number in the font.</param>
    /// <param name="ClusterStart">The UTF-16 offset, into the shaped text, where the characters this glyph stands for begin.</param>
    /// <param name="ClusterLength">How many UTF-16 code units of the text this glyph stands for.</param>
    /// <param name="XAdvanceDelta">The change to the horizontal advance.</param>
    /// <param name="YAdvanceDelta">The change to the vertical advance.</param>
    /// <param name="XOffset">How far the glyph is drawn to the right of the pen position, without moving the pen.</param>
    /// <param name="YOffset">How far the glyph is drawn above the pen position, without moving the pen.</param>
    /// <param name="LigatureComponentClusterStarts">For a ligature, the cluster start of each of its components in order, which is what tells a later mark which component it belongs to; otherwise <see langword="null"/>.</param>
    /// <param name="AttachedToIndex">For a mark that positioning attached to another glyph, the index of that glyph in the run; otherwise <see langword="null"/>. It is not kept on a run shaped for display in reverse order, where every glyph's offset already accounts for the reversal.</param>
    /// <param name="IsHiddenIgnorable">Whether the glyph stands for a character that is invisible by definition and that the font draws nothing for: any variation selector, and another default-ignorable character (a joiner, a bidi control) the font has no glyph of its own for. Such a glyph takes part in substitution and positioning, so a lookup that matches on it still sees it, and is removed from the run at the end. A font that does map such a character keeps its glyph, which is drawn.</param>
    public readonly record struct PlacedGlyph(
        int GlyphIndex,
        int ClusterStart,
        int ClusterLength,
        double XAdvanceDelta = 0,
        double YAdvanceDelta = 0,
        double XOffset = 0,
        double YOffset = 0,
        int[]? LigatureComponentClusterStarts = null,
        int? AttachedToIndex = null,
        bool IsHiddenIgnorable = false);
}
