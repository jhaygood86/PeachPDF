// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachDrawing.PDF.Engine.Drawing;

/// <summary>
/// Specifies how the content of an existing PDF page and new content is combined.
/// </summary>
internal enum PdfPageAppendMode
{
    /// <summary>
    /// The new content is inserted behind the old content and any subsequent drawing in done above the existing graphic.
    /// </summary>
    Append,

    /// <summary>
    /// The new content is inserted before the old content and any subsequent drawing in done beneath the existing graphic.
    /// </summary>
    Prepend,

    /// <summary>
    /// The new content entirely replaces the old content and any subsequent drawing in done on a blank page.
    /// </summary>
    Replace,
}
