using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Html.Paint
{
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Strokes the outline of one run of text with <c>-webkit-text-stroke</c> (Compatibility Standard): a stroke of the given width centred on the glyph
        /// outlines, drawn over the text itself. <paramref name="point"/> is where <c>DrawString</c> would anchor the run (the top-left of its line box). A font
        /// with no decodable outlines (a CID-keyed CFF) has nothing to stroke, so the text is drawn unstroked there, as it was before the property existed.
        /// </summary>
        private static void PaintTextStroke(Canvas g, CssBox styleSource, Font font, string text, PaintPoint point, ShapeSettings features)
        {
            // A stroke is a shape, not text: nothing for an invisible-text pass to supply.
            if (g.InvisibleText)
                return;

            var width = styleSource.ActualTextStrokeWidth;
            if (width <= 0)
                return;

            var color = styleSource.ActualTextStrokeColor;
            if (color.A == 0)
                return;

            // GetTextOutline places the baseline directly, unlike DrawString's top-left anchor: shift down by the ascent.
            using var outline = g.GetTextOutline(text, font, new PaintPoint(point.X, point.Y + font.Ascent), styleSource.ActualLetterSpacing, features);
            if (outline is null)
                return;

            // The outline is in layout units, a path reaches the canvas in points.
            if (g.PixelsPerPoint != 1.0)
                outline.Transform(System.Numerics.Matrix3x2.CreateScale((float)(1.0 / g.PixelsPerPoint)));

            var pen = g.GetPen(color);
            pen.Width = width / g.PixelsPerPoint;
            g.DrawPath(pen, outline);
        }
    }
}
