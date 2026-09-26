namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// One of the seven explicit directional formatting pushes that UAX #9 rules X2 to X5c recognise, each
    /// named after the control character that opens it.
    /// </summary>
    public enum ExplicitPush : byte
    {
        /// <summary>Left-to-right embedding (U+202A).</summary>
        Lre,

        /// <summary>Right-to-left embedding (U+202B).</summary>
        Rle,

        /// <summary>Left-to-right override (U+202D).</summary>
        Lro,

        /// <summary>Right-to-left override (U+202E).</summary>
        Rlo,

        /// <summary>Left-to-right isolate (U+2066).</summary>
        Lri,

        /// <summary>Right-to-left isolate (U+2067).</summary>
        Rli,

        /// <summary>First-strong isolate (U+2068).</summary>
        Fsi
    }

    /// <summary>
    /// A directional push that applies to a stretch of the analysed text without any control character being
    /// present in it: how a host with its own markup (CSS <c>unicode-bidi</c>, an SVG <c>direction</c>
    /// attribute) tells <see cref="Bidi.Analyze"/> about embeddings the string itself does not spell out.
    /// </summary>
    /// <remarks>
    /// The analysis treats a span exactly as though a real <see cref="Push"/> control character opened at
    /// <see cref="Start"/> and its matching terminator closed at <see cref="End"/>. Spans and real control
    /// characters may be mixed in one text. Of two spans that share a start, the one that comes earlier in the
    /// list is the outer one.
    /// </remarks>
    /// <param name="Start">Index, in UTF-16 code units, of the first character the push covers.</param>
    /// <param name="Length">How many code units the push covers.</param>
    /// <param name="Push">Which push opens the span.</param>
    public readonly record struct EmbeddingSpan(int Start, int Length, ExplicitPush Push)
    {
        /// <summary>The index just past the last code unit the span covers.</summary>
        public int End => Start + Length;
    }
}
