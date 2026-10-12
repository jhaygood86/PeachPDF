// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.Drawing;

namespace PeachPDF.PDF.Rendering;

/// <summary>
/// Indicates whether we are within a BT/ET block.
/// </summary>
enum StreamMode
{
    /// <summary>
    /// Graphic mode. This is default.
    /// </summary>
    Graphic,

    /// <summary>
    /// Text mode.
    /// </summary>
    Text,
}
