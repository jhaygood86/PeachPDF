#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// The PDF version to target for the generated document's file header (the "%PDF-x.y" marker)
    /// and any version-gated features written into it.
    /// </summary>
    public enum PdfVersion
    {
        /// <summary>
        /// PDF 1.7 (ISO 32000-1) - PeachPDF's long-standing default output version.
        /// </summary>
        Pdf17,

        /// <summary>
        /// PDF 2.0 (ISO 32000-2). Needed for spec-conformant use of PDF 2.0-only structure-tree
        /// features such as a structure element's <c>/AF</c> (Associated Files) array - see the
        /// <c>-peachpdf-pdf-tag-type: Formula</c> MathML embedding described in
        /// docs/html-css-support.md. Incompatible with requesting any
        /// <see cref="PeachPDF.PdfAConformance"/> level other than <see cref="PeachPDF.PdfAConformance.None"/>:
        /// PeachPDF does not implement PDF/A-4 (ISO 19005-4), the PDF-2.0-based PDF/A level, and every
        /// PDF/A level it does implement is defined against PDF 1.4 or 1.7.
        /// </summary>
        Pdf20
    }
}
