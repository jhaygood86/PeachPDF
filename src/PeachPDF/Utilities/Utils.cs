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

using PeachDrawing.Core;
using PeachPDF.PDF.Drawing;

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
        public static PaintPoint ToPixels(PaintPoint p, double pixelsPerPoint)
        {
            return new PaintPoint(p.X * pixelsPerPoint, p.Y * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms point to core point.
        /// </summary>
        public static PaintPoint[] ToPoints(PaintPoint[] points, double pixelsPerPoint)
        {
            PaintPoint[] myPoints = new PaintPoint[points.Length];
            for (int i = 0; i < points.Length; i++)
                myPoints[i] = ToPoints(points[i], pixelsPerPoint);
            return myPoints;
        }

        /// <summary>
        /// Convert from core point to WinForms point.
        /// </summary>
        public static PaintPoint ToPoints(PaintPoint p, double pixelsPerPoint)
        {
            return new PaintPoint(p.X / pixelsPerPoint, p.Y / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms size to core size.
        /// </summary>
        public static PeachDrawing.Core.Size ToPixels(Size s, double pixelsPerPoint)
        {
            return new PeachDrawing.Core.Size(s.Width * pixelsPerPoint, s.Height * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core size to WinForms size.
        /// </summary>
        public static Size ToPoints(PeachDrawing.Core.Size s, double pixelsPerPoint)
        {
            return new Size(s.Width / pixelsPerPoint, s.Height / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms rectangle to core rectangle.
        /// </summary>
        public static Rect ToPixels(Rect r, double pixelsPerPoint)
        {
            return new Rect(r.X * pixelsPerPoint, r.Y * pixelsPerPoint, r.Width * pixelsPerPoint, r.Height * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core rectangle to WinForms rectangle.
        /// </summary>
        public static Rect ToPoints(Rect r, double pixelsPerPoint)
        {
            return new Rect(r.X / pixelsPerPoint, r.Y / pixelsPerPoint, r.Width / pixelsPerPoint, r.Height / pixelsPerPoint);
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