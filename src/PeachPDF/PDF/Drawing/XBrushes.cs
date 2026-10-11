using PeachDrawing.Core;

namespace PeachPDF.PDF.Drawing
{
    /// <summary>The few solid brushes the PDF layer and its tests use by name.</summary>
    internal static class XBrushes
    {
        public static XBrush Black => new XSolidBrush(PaintColor.Black, true);

        public static XBrush White => new XSolidBrush(PaintColor.White, true);

        // Transparent white, the value System.Drawing.Color.Transparent has, rather than transparent black.
        public static XBrush Transparent => new XSolidBrush(PaintColor.FromArgb(0, 255, 255, 255), true);

        public static XBrush Red => new XSolidBrush(PaintColor.FromArgb(255, 0, 0), true);

        public static XBrush Green => new XSolidBrush(PaintColor.FromArgb(0, 128, 0), true);

        public static XBrush Blue => new XSolidBrush(PaintColor.FromArgb(0, 0, 255), true);

        public static XBrush SeaShell => new XSolidBrush(PaintColor.FromArgb(255, 245, 238), true);
    }
}
