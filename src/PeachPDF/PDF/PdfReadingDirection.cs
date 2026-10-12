// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF;

/// <summary>
/// Specifies how the document should be displayed by a viewer when opened.
/// </summary>
internal enum PdfReadingDirection
{
    /// <summary>
    /// Left to right.
    /// </summary>
    LeftToRight,

    /// <summary>
    /// Right to left (including vertical writing systems, such as Chinese, Japanese, and Korean)
    /// </summary>
    RightToLeft,
}
