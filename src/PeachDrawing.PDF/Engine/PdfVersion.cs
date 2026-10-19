// An internal mirror of PeachPDF's public option type of the same name: PeachPDF maps its PdfGenerateConfig onto these.
#nullable enable

namespace PeachDrawing.PDF.Engine
{
    /// <summary>
    /// The PDF version to target for the generated document's file header (the "%PDF-x.y" marker)
    /// and any version-gated features written into it.
    /// </summary>
    internal enum PdfVersion
    {
        /// <summary>
        /// PDF 1.7 (ISO 32000-1): the file every PDF reader opens. PeachPDF's output before PDF 2.0 became the default; the PDF
        /// header it writes is 1.4 unless a feature or a PDF/A or PDF/X level needs a later one.
        /// </summary>
        Pdf17,

        /// <summary>
        /// PDF 2.0 (ISO 32000-2), the default. Compresses streams with the <c>/BrotliDecode</c> filter unless
        /// <c>PdfGenerateConfig.BrotliCompression</c> is off. Needed for spec-conformant use of PDF 2.0-only structure-tree
        /// features such as a structure element's <c>/AF</c> (Associated Files) array - see the
        /// <c>-peachpdf-pdf-tag-type: Formula</c> MathML embedding described in
        /// docs/html-css-support.md. The version the PDF/A-4 and PDF/X-6
        /// levels require; incompatible with every earlier PDF/A and PDF/X level, which are defined against PDF 1.4, 1.6 or 1.7.
        /// </summary>
        Pdf20
    }
}
