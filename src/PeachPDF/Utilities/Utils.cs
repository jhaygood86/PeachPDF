// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachDrawing.Abstractions;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Utilities
{
    /// <summary>
    /// Utilities for converting WinForms entities to HtmlRenderer core entities.
    /// </summary>
    internal static class Utils
    {
        /// <summary>
        /// Convert from WinForms point to core point.
        /// </summary>
        public static PaintPoint Convert(XPoint p, double pixelsPerPoint)
        {
            return new PaintPoint(p.X * pixelsPerPoint, p.Y * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms point to core point.
        /// </summary>
        public static XPoint[] Convert(PaintPoint[] points, double pixelsPerPoint)
        {
            XPoint[] myPoints = new XPoint[points.Length];
            for (int i = 0; i < points.Length; i++)
                myPoints[i] = Convert(points[i], pixelsPerPoint);
            return myPoints;
        }

        /// <summary>
        /// Convert from core point to WinForms point.
        /// </summary>
        public static XPoint Convert(PaintPoint p, double pixelsPerPoint)
        {
            return new XPoint(p.X / pixelsPerPoint, p.Y / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms size to core size.
        /// </summary>
        public static PeachDrawing.Abstractions.Size Convert(XSize s, double pixelsPerPoint)
        {
            return new PeachDrawing.Abstractions.Size(s.Width * pixelsPerPoint, s.Height * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core size to WinForms size.
        /// </summary>
        public static XSize Convert(PeachDrawing.Abstractions.Size s, double pixelsPerPoint)
        {
            return new XSize(s.Width / pixelsPerPoint, s.Height / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms rectangle to core rectangle.
        /// </summary>
        public static Rect Convert(XRect r, double pixelsPerPoint)
        {
            return new Rect(r.X * pixelsPerPoint, r.Y * pixelsPerPoint, r.Width * pixelsPerPoint, r.Height * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core rectangle to WinForms rectangle.
        /// </summary>
        public static XRect Convert(Rect r, double pixelsPerPoint)
        {
            return new XRect(r.X / pixelsPerPoint, r.Y / pixelsPerPoint, r.Width / pixelsPerPoint, r.Height / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core color to WinForms color. A CSS <c>device-cmyk()</c>-authored
        /// <see cref="PaintColor"/> (<see cref="PaintColor.IsCmyk"/>) is carried through as a real CMYK
        /// <see cref="XColor"/> - no RGB approximation is computed for one; every solid brush/pen in the
        /// paint pipeline funnels through this one method, so this is the only place that needs to branch.
        /// </summary>
        public static XColor Convert(PaintColor c)
        {
            return c.IsCmyk
                ? XColor.FromCmyk(c.A / 255.0, c.C, c.M, c.Y, c.K)
                : XColor.FromArgb(c.A, c.R, c.G, c.B);
        }

        /// <summary>
        /// Convert from WinForms color to core color.
        /// </summary>
        public static PaintColor Convert(System.Drawing.Color c)
        {
            return PaintColor.FromArgb(c.A, c.R, c.G, c.B);
        }

    }
}