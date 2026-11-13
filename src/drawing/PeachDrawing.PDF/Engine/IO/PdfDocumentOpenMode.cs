// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachDrawing.PDF.Engine.IO;

/// <summary>
/// Determines how a PDF document is opened. 
/// </summary>
internal enum PdfDocumentOpenMode
{
    /// <summary>
    /// The PDF stream is completely read into memory and can be modified. Pages can be deleted or
    /// inserted, but it is not possible to extract pages. This mode is useful for modifying an
    /// existing PDF document.
    /// </summary>
    Modify,

    /// <summary>
    /// The PDF stream is opened for importing pages from it. A document opened in this mode cannot
    /// be modified.
    /// </summary>
    Import,

    /// <summary>
    /// The PDF stream is completely read into memory, but cannot be modified. This mode preserves the
    /// original internal structure of the document and is useful for analyzing existing PDF files.
    /// </summary>
    ReadOnly,
}
