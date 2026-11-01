// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.Drawing;

namespace PeachDrawing.PDF.Engine.Rendering;

/// <summary>
/// Indicates whether we are within a BT/ET block.
/// </summary>
internal enum StreamMode
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
