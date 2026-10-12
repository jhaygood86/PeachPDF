// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;

namespace PeachPDF.PDF;

/// <summary>
/// Specifies what color model is used in a PDF document.
/// </summary>
internal enum PdfColorMode
{
    /// <summary>
    /// All color values are written as specified in the PaintColor objects they come from.
    /// </summary>
    Undefined,

    /// <summary>
    /// All colors are converted to RGB.
    /// </summary>
    Rgb,

    /// <summary>
    /// All colors are converted to CMYK.
    /// </summary>
    Cmyk,
}
