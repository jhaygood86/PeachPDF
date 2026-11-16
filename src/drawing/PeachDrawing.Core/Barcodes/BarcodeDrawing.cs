using System;

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// Draws a <see cref="Barcode"/> on any <see cref="Canvas"/>, or adds it to a <see cref="GraphicsPath"/>. The dark
    /// modules are rectangles of one path with the nonzero fill rule, so every backend writes them as vector shapes
    /// and no seams show between neighboring bars.
    /// </summary>
    public static class BarcodeDrawing
    {
        /// <summary>
        /// Adds the dark bars of the barcode to the path, one closed rectangle each. The light margin and the text are
        /// not part of the path. The rectangles never overlap, so the path draws the same under either fill rule.
        /// </summary>
        /// <param name="path">The path to add to.</param>
        /// <param name="barcode">The barcode.</param>
        /// <param name="origin">The top-left corner of the barcode including its light margin.</param>
        /// <param name="moduleSize">The size of one module, in the path's units.</param>
        public static void AddBarcode(this GraphicsPath path, Barcode barcode, PaintPoint origin, double moduleSize)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(barcode);
            AddBars(path, barcode.CreateLayout(), origin, moduleSize);
        }

        static void AddBars(GraphicsPath path, BarcodeLayout layout, PaintPoint origin, double moduleSize)
        {
            foreach (var bar in layout.Bars)
            {
                double x = origin.X + bar.X * moduleSize;
                double y = origin.Y + bar.Y * moduleSize;
                double right = x + bar.Width * moduleSize;
                double bottom = y + bar.Height * moduleSize;
                path.Start(x, y);
                path.LineTo(right, y);
                path.LineTo(right, bottom);
                path.LineTo(x, bottom);
                path.CloseFigure();
            }
        }

        /// <summary>
        /// Draws the barcode: its light margin if a background is given, its bars as one filled path and, for a
        /// linear code, its human-readable text.
        /// </summary>
        /// <param name="canvas">The canvas to draw on.</param>
        /// <param name="barcode">The barcode.</param>
        /// <param name="origin">The top-left corner of the barcode including its light margin.</param>
        /// <param name="moduleSize">The size of one module, in the canvas's units.</param>
        /// <param name="color">The color of the bars and the text.</param>
        /// <param name="background">The color of the light margin and the spaces, or <c>null</c> to leave them unpainted.</param>
        /// <param name="textFont">Makes the font for text of the given size in the canvas's units; <c>null</c> draws no text.</param>
        /// <returns>The bounds the barcode took, including its light margin.</returns>
        public static Rect DrawBarcode(this Canvas canvas, Barcode barcode, PaintPoint origin, double moduleSize, PaintColor color,
            PaintColor? background = null, Func<double, Font>? textFont = null)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(barcode);
            var layout = barcode.CreateLayout();
            var bounds = new Rect(origin.X, origin.Y, layout.Width * moduleSize, layout.Height * moduleSize);

            if (background is PaintColor back)
                canvas.DrawRectangle(canvas.GetSolidBrush(back), bounds.X, bounds.Y, bounds.Width, bounds.Height);

            var path = canvas.GetGraphicsPath();
            path.FillMode = FillMode.Nonzero;
            AddBars(path, layout, origin, moduleSize);
            canvas.DrawPath(canvas.GetSolidBrush(color), path);

            if (textFont != null)
            {
                foreach (var text in layout.Texts)
                {
                    var box = new Rect(origin.X + text.Bounds.X * moduleSize, origin.Y + text.Bounds.Y * moduleSize,
                        text.Bounds.Width * moduleSize, text.Bounds.Height * moduleSize);
                    canvas.DrawStringInRect(text.Text, textFont(text.FontSize * moduleSize), color, box, BoxAlignment.Center, BoxAlignment.Start);
                }
            }

            canvas.AddSemanticRegion(bounds, new SemanticInfo("img") { Name = barcode.DisplayText });
            return bounds;
        }
    }
}
