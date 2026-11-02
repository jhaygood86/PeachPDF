#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// The PDF/UA (ISO 14289, "universal accessibility") conformance level to target, set via
    /// <see cref="PdfGenerateConfig.PdfUAConformance"/>. Defaults to <see cref="None"/>: no PDF/UA claim is made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requesting a level turns on <see cref="PdfGenerateConfig.EnableTaggedPdf"/> (a PDF/UA file is a tagged file), gives every figure alternative text
    /// (the element's <c>alt</c>, or its accessible name, or an empty one that marks a picture decorative), identifies the file in its XMP metadata
    /// (<c>pdfuaid:part</c>, and <c>pdfuaid:rev</c> for <see cref="UA2"/>), and makes the viewer show the document's title instead of the file name.
    /// It needs a document <c>&lt;title&gt;</c> (or <see cref="PdfGenerateConfig.Metadata"/>), a language (<c>&lt;html lang&gt;</c> or
    /// <see cref="PdfGenerateConfig.DefaultLanguage"/>) and a creation date, and generation throws if one is missing.
    /// </para>
    /// <para>
    /// It can be combined with PDF/A: <see cref="UA1"/> with PDF/A-1a, 2a or 3a (the levels defined against PDF 1.x), <see cref="UA2"/> with PDF/A-4.
    /// It cannot be combined with PDF/X. <see cref="UA1"/> writes a PDF 1.7 file and <see cref="UA2"/> a PDF 2.0 one.
    /// </para>
    /// <para>
    /// The claim says the file is structured for accessibility; whether its content is depends on the HTML it was made from - headings that do not
    /// skip levels, a sensible reading order, real alternative text. Check a file with a PDF/UA checker such as veraPDF.
    /// </para>
    /// </remarks>
    public enum PdfUAConformance
    {
        /// <summary>No PDF/UA claim is made. Default.</summary>
        None = 0,

        /// <summary>PDF/UA-1 (ISO 14289-1), defined against PDF 1.7.</summary>
        UA1 = 1,

        /// <summary>PDF/UA-2 (ISO 14289-2), defined against PDF 2.0.</summary>
        UA2 = 2,
    }
}
