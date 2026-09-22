namespace PeachPDF.CSS
{
    /// <summary>
    /// css-gcpm-3 §2.8's <c>footnote-policy</c>: what happens when a footnote's own note area cannot be
    /// placed on the page its call naturally landed on. Read off the footnote source element
    /// (<c>HtmlContainerInt.ResolveFootnotesForThisAttempt</c>), the same box <c>FootnoteDisplayMode</c> is.
    /// </summary>
    internal enum FootnotePolicyMode : byte
    {
        /// <summary>No forced break; a note area that doesn't fit simply overflows (the initial value).</summary>
        Auto,

        /// <summary>Force a page break at the start of the line carrying the footnote's call.</summary>
        Line,

        /// <summary>Force a page break before the paragraph (containing block) carrying the footnote's call.</summary>
        Block
    }
}
