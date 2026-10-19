#nullable enable

namespace PeachDrawing.PDF
{
    /// <summary>
    /// The PDF version to target for the generated document's file header (the "%PDF-x.y" marker)
    /// and any version-gated features written into it.
    /// </summary>
    public enum PdfVersion
    {
        /// <summary>
        /// PDF 1.7 (ISO 32000-1): the file every PDF reader opens. The header the writer emits is 1.4 unless a feature
        /// (an attachment, encryption) needs a later one.
        /// </summary>
        Pdf17,

        /// <summary>
        /// PDF 2.0 (ISO 32000-2), the default. Needed for spec-conformant use of PDF 2.0-only features such as a structure
        /// element's <c>/AF</c> (Associated Files) array, and the version the PDF/A-4 and PDF/X-6 levels require.
        /// </summary>
        Pdf20
    }
}
