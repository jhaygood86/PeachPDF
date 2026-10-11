using PeachDrawing.Core;

namespace PeachPDF.PDF.Drawing
{
    /// <summary>The few one-point pens the PDF layer's tests use by name.</summary>
    internal static class XPens
    {
        public static XPen Black => new XPen(PaintColor.Black, 1, true);

        public static XPen Red => new XPen(PaintColor.FromArgb(255, 0, 0), 1, true);
    }
}
