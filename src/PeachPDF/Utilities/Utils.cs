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

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using System.Drawing;

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
        public static RPoint Convert(XPoint p, double pixelsPerPoint)
        {
            return new RPoint(p.X * pixelsPerPoint, p.Y * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms point to core point.
        /// </summary>
        public static XPoint[] Convert(RPoint[] points, double pixelsPerPoint)
        {
            XPoint[] myPoints = new XPoint[points.Length];
            for (int i = 0; i < points.Length; i++)
                myPoints[i] = Convert(points[i], pixelsPerPoint);
            return myPoints;
        }

        /// <summary>
        /// Convert from core point to WinForms point.
        /// </summary>
        public static XPoint Convert(RPoint p, double pixelsPerPoint)
        {
            return new XPoint(p.X / pixelsPerPoint, p.Y / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms size to core size.
        /// </summary>
        public static RSize Convert(XSize s, double pixelsPerPoint)
        {
            return new RSize(s.Width * pixelsPerPoint, s.Height * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core size to WinForms size.
        /// </summary>
        public static XSize Convert(RSize s, double pixelsPerPoint)
        {
            return new XSize(s.Width / pixelsPerPoint, s.Height / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from WinForms rectangle to core rectangle.
        /// </summary>
        public static RRect Convert(XRect r, double pixelsPerPoint)
        {
            return new RRect(r.X * pixelsPerPoint, r.Y * pixelsPerPoint, r.Width * pixelsPerPoint, r.Height * pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core rectangle to WinForms rectangle.
        /// </summary>
        public static XRect Convert(RRect r, double pixelsPerPoint)
        {
            return new XRect(r.X / pixelsPerPoint, r.Y / pixelsPerPoint, r.Width / pixelsPerPoint, r.Height / pixelsPerPoint);
        }

        /// <summary>
        /// Convert from core color to WinForms color.
        /// </summary>
        public static XColor Convert(RColor c)
        {
            return XColor.FromArgb(c.A, c.R, c.G, c.B);
        }

        /// <summary>
        /// Convert from  color to WinForms color.
        /// </summary>
        public static RColor Convert(Color c)
        {
            return RColor.FromArgb(c.A, c.R, c.G, c.B);
        }

    }
}