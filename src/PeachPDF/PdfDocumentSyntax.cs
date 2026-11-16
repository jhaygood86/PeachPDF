namespace PeachPDF
{
    /// <summary>
    /// How the HTML is parsed (see <see cref="PdfGenerateConfig.DocumentSyntax"/>). A browser decides from the MIME type the document is served as;
    /// PeachPDF takes it from the loader that supplies the document, and never guesses it from the text.
    /// </summary>
    public enum PdfDocumentSyntax
    {
        /// <summary>
        /// XHTML when the loader that supplies the document says it is <c>application/xhtml+xml</c> (a <c>.xhtml</c> or <c>.xht</c> file, or an
        /// HTTP response of that type), and HTML otherwise, including for a string handed over directly (the default).
        /// </summary>
        Auto,

        /// <summary>HTML, whatever the loader says.</summary>
        Html,

        /// <summary>
        /// XHTML, parsed with an XML parser whatever the loader says: the document must be well-formed, the named entities of XHTML are known, and it
        /// is never in quirks mode. A document that is not well-formed XML throws <see cref="HtmlRenderException"/>.
        /// </summary>
        Xml
    }
}
