// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Represents the internal state of an PdfGraphics object.
/// This class is used as a handle for restoring the context.
/// </summary>
internal sealed class PdfGraphicsStateToken
{
    // This class is simply a wrapper of InternalGraphicsState.
    internal InternalGraphicsState InternalState = null!;
}
