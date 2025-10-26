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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Utilities;
using System;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Adapter for WinForms Graphics for core.
    /// </summary>
    internal sealed class GraphicsAdapter : RGraphics
    {
        /// <summary>
        /// The wrapped WinForms graphics object
        /// </summary>
        private readonly XGraphics _g;

        /// <summary>
        /// if to release the graphics object on dispose
        /// </summary>
        private readonly bool _releaseGraphics;

        private double PixelsPerPoint { get; }

        /// <summary>
        /// Used to measure and draw strings
        /// </summary>
        private static readonly XStringFormat _stringFormat;

        static GraphicsAdapter()
        {
            _stringFormat = new XStringFormat
            {
                Alignment = XStringAlignment.Near,
                LineAlignment = XLineAlignment.Near
            };
        }

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="adapter">the adapter</param>
        /// <param name="g">the win forms graphics object to use</param>
        /// <param name="pixelsPerPoint">The number of pixels in each point</param>
        /// <param name="releaseGraphics">optional: if to release the graphics object on dispose (default - false)</param>
        public GraphicsAdapter(RAdapter adapter, XGraphics g, double pixelsPerPoint, bool releaseGraphics = false)
            : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue))
        {
            ArgumentNullException.ThrowIfNull(g);

            _g = g;
            _releaseGraphics = releaseGraphics;

            PixelsPerPoint = pixelsPerPoint;
        }

        public override void PopClip()
        {
            _clipStack.Pop();
            _g.Restore();
        }

        public override void PushClip(RRect rect)
        {
            _clipStack.Push(rect);
            _g.Save();
            _g.IntersectClip(Utils.Convert(rect, PixelsPerPoint));
        }

        public override void PushClipExclude(RRect rect)
        { }

        public override object SetAntiAliasSmoothingMode()
        {
            var prevMode = _g.SmoothingMode;
            _g.SmoothingMode = XSmoothingMode.AntiAlias;
            return prevMode;
        }

        public override void ReturnPreviousSmoothingMode(object? prevMode)
        {
            if (prevMode != null)
            {
                _g.SmoothingMode = (XSmoothingMode)prevMode;
            }
        }

        public override RSize MeasureString(string str, RFont font)
        {
            var fontAdapter = (FontAdapter)font;
            var realFont = fontAdapter.Font;
            var size = _g.MeasureString(str, realFont, _stringFormat);

            if (!(font.Height < 0)) return Utils.Convert(size, PixelsPerPoint);

            var height = realFont.Height;
            var fontResolver = ((PdfSharpAdapter)_adapter).FontResolver;
            var descent = realFont.Size * realFont.FontFamily.GetCellDescent(realFont.Style, fontResolver) / realFont.FontFamily.GetEmHeight(realFont.Style, fontResolver);
            fontAdapter.SetMetrics(height, (int)Math.Round((height - descent + 1f)));

            return Utils.Convert(size, PixelsPerPoint);
        }

        public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
        {
            // there is no need for it - used for text selection
            throw new NotSupportedException();
        }

        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl)
        {
            var xBrush = ((BrushAdapter)_adapter.GetSolidBrush(color)).Brush;

            var xPoint = Utils.Convert(point, PixelsPerPoint);

            _g.DrawString(str, ((FontAdapter)font).Font, xBrush, xPoint.X, xPoint.Y, _stringFormat);
        }

        public override RBrush GetTextureBrush(RImage image, RRect dstRect, RPoint translateTransformLocation)
        {
            return new BrushAdapter(new XTextureBrush(((ImageAdapter)image).Image, Utils.Convert(dstRect, PixelsPerPoint), Utils.Convert(translateTransformLocation, PixelsPerPoint)));
        }

        public override RGraphicsPath GetGraphicsPath()
        {
            return new GraphicsPathAdapter();
        }

        public override void Dispose()
        {
            if (_releaseGraphics)
                _g.Dispose();
        }


        #region Delegate graphics methods

        public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
        {
            _g.DrawLine(((PenAdapter)pen).Pen, x1 / PixelsPerPoint, y1 / PixelsPerPoint, x2 / PixelsPerPoint, y2 / PixelsPerPoint);
        }

        public override void DrawRectangle(RPen pen, double x, double y, double width, double height)
        {
            _g.DrawRectangle(((PenAdapter)pen).Pen, x / PixelsPerPoint, y / PixelsPerPoint, width / PixelsPerPoint, height / PixelsPerPoint);
        }

        public override void DrawRectangle(RBrush brush, double x, double y, double width, double height)
        {
            var xBrush = ((BrushAdapter)brush).Brush;
            if (xBrush is XTextureBrush xTextureBrush)
            {
                xTextureBrush.DrawRectangle(_g, x / PixelsPerPoint, y / PixelsPerPoint, width / PixelsPerPoint, height / PixelsPerPoint);
            }
            else
            {
                _g.DrawRectangle(xBrush, x / PixelsPerPoint, y / PixelsPerPoint, width / PixelsPerPoint, height / PixelsPerPoint);

                // handle bug in PdfSharp that keeps the brush color for next string draw
                if (xBrush is XLinearGradientBrush)
                    _g.DrawRectangle(XBrushes.White, 0, 0, 0.1 / PixelsPerPoint, 0.1 / PixelsPerPoint);
            }
        }

        public override void DrawImage(RImage image, RRect destRect, RRect srcRect)
        {
            _g.DrawImage(((ImageAdapter)image).Image, Utils.Convert(destRect, PixelsPerPoint), Utils.Convert(srcRect, PixelsPerPoint), XGraphicsUnit.Point);
        }

        public override void DrawImage(RImage image, RRect destRect)
        {
            _g.DrawImage(((ImageAdapter)image).Image, Utils.Convert(destRect, PixelsPerPoint));
        }

        public override void DrawPath(RPen pen, RGraphicsPath path)
        {
            _g.DrawPath(((PenAdapter)pen).Pen, ((GraphicsPathAdapter)path).GraphicsPath);
        }

        public override void DrawPath(RBrush brush, RGraphicsPath path)
        {
            _g.DrawPath(((BrushAdapter)brush).Brush, ((GraphicsPathAdapter)path).GraphicsPath);
        }

        public override void DrawPolygon(RBrush brush, RPoint[] points)
        {
            if (points is { Length: > 0 })
            {
                _g.DrawPolygon((XBrush)((BrushAdapter)brush).Brush, Utils.Convert(points, PixelsPerPoint), XFillMode.Winding);
            }
        }

        #endregion
    }
}