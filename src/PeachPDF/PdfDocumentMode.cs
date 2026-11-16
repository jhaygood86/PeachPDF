namespace PeachPDF
{
    /// <summary>
    /// The mode an HTML document is laid out in, which decides whether the quirks of a browser's quirks mode apply (see
    /// <see cref="PdfGenerateConfig.DocumentMode"/>). A browser takes the mode from the DOCTYPE: a document with no DOCTYPE, or a legacy one,
    /// is in quirks mode.
    /// </summary>
    public enum PdfDocumentMode
    {
        /// <summary>The mode the document's DOCTYPE gives it, as a browser would decide (the default).</summary>
        Auto,

        /// <summary>Standards mode, whatever the DOCTYPE says: none of the quirks apply.</summary>
        NoQuirks,

        /// <summary>
        /// "Almost standards" mode: the standards, with the quirks mode's way of sizing line boxes (an inline box with no text does not
        /// make a line taller, and a block's own <c>line-height</c> does not give its line boxes a minimum height).
        /// </summary>
        LimitedQuirks,

        /// <summary>Quirks mode, whatever the DOCTYPE says.</summary>
        Quirks
    }
}
