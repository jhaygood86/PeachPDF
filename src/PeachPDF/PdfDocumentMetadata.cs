#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// Optional document-information overrides applied to the generated PDF's Info dictionary via
    /// <see cref="PdfGenerateConfig.Metadata"/>. Each property is nullable: when a property is
    /// non-null it overrides the value extracted from the HTML source (<c>&lt;title&gt;</c> and the
    /// <c>&lt;meta name="author|subject|keywords|generator"&gt;</c> tags); when it is null the
    /// HTML-extracted value is used unchanged.
    /// </summary>
    /// <remarks>
    /// Document language is not set here — use <see cref="PdfGenerateConfig.DefaultLanguage"/>, which
    /// populates the PDF catalog <c>/Lang</c> entry when the document itself declares no language
    /// (a document's own <c>&lt;html lang&gt;</c> takes priority).
    /// </remarks>
    public sealed class PdfDocumentMetadata
    {
        /// <summary>Overrides the PDF document title (from <c>&lt;title&gt;</c>).</summary>
        public string? Title { get; set; }

        /// <summary>Overrides the PDF document author (from <c>&lt;meta name="author"&gt;</c>).</summary>
        public string? Author { get; set; }

        /// <summary>Overrides the PDF document subject (from <c>&lt;meta name="subject"&gt;</c>).</summary>
        public string? Subject { get; set; }

        /// <summary>Overrides the PDF document keywords (from <c>&lt;meta name="keywords"&gt;</c>).</summary>
        public string? Keywords { get; set; }

        /// <summary>Overrides the PDF document creator (from <c>&lt;meta name="generator"&gt;</c>).</summary>
        public string? Creator { get; set; }
    }
}
