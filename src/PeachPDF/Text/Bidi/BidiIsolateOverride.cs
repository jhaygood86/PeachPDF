namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// The seven explicit directional pushes UAX #9's X2-X5c recognize, one per raw Unicode control
    /// character (LRE/RLE/LRO/RLO/LRI/RLI/FSI) - see <see cref="BidiIsolateOverride"/> for how a CSS
    /// <c>unicode-bidi</c> value maps onto a synthetic instance of one of these over a box's own text range.
    /// </summary>
    internal enum BidiExplicitPush : byte
    {
        Lre,
        Rle,
        Lro,
        Rlo,
        Lri,
        Rli,
        Fsi
    }

    /// <summary>
    /// A synthetic explicit-formatting push/pop pair contributed by one inline box's <c>unicode-bidi</c>
    /// value (CSS Writing Modes 3 §5.2's integration of CSS into UAX #9 - see the mapping table in
    /// <c>PeachPDF</c>'s bidi implementation plan), spanning <c>[Start, Start + Length)</c> of the flattened
    /// paragraph text <see cref="BidiResolver.Resolve"/> is given. <see cref="BidiResolver"/> treats this
    /// exactly as if a real <see cref="Push"/> control character opened at <see cref="Start"/> and a
    /// matching PDF/PDI closed at <c>Start + Length</c>, interleaved with the text's own real explicit
    /// formatting characters (a document can contain both).
    /// </summary>
    internal readonly record struct BidiIsolateOverride(int Start, int Length, BidiExplicitPush Push)
    {
        public int End => Start + Length;
    }
}
