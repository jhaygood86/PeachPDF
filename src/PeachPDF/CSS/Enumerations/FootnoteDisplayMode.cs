namespace PeachPDF.CSS
{
    /// <summary>
    /// css-gcpm-3 §2.3's <c>footnote-display</c>: how an already-detached footnote body stacks inside
    /// the note area (<c>HtmlContainerInt.ResolveFootnotesForThisAttempt</c>), not whether it detaches
    /// in the first place — see <c>Floating.Footnote</c>.
    /// </summary>
    internal enum FootnoteDisplayMode : byte
    {
        /// <summary>Each footnote body starts its own block in the note area (the initial value).</summary>
        Block,

        /// <summary>A footnote body runs inline after the previous one in the note area.</summary>
        Inline,

        /// <summary>The user agent chooses block or inline per body, based on available width.</summary>
        Compact
    }
}
