using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// The border/background/checkbox-radio-glyph chrome shared between the flag-on static page
    /// painter (<see cref="FormFieldFragmentPainter"/>) and a field's own generated widget
    /// appearance stream (<c>Html/Core/Handlers/FormFieldAppearanceBuilder</c>) - both need to draw
    /// the identical CSS-styled look, just onto different <see cref="RGraphics"/> targets (the real
    /// page vs. a field's own local <c>XForm</c>), so the drawing logic itself lives here once
    /// rather than drifting between two independently maintained copies.
    /// </summary>
    internal static class FormFieldChrome
    {
        /// <summary>The border/background shared by every field kind - reuses the same CSS box-model painting every other box gets.</summary>
        internal static void PaintBorderAndBackground(RGraphics g, CssBox box, RRect rect)
        {
            FragmentPainter.PaintBackground(g, box, BoxDecorationGeometry.Unbroken(rect));
            BordersDrawHandler.DrawBoxBorders(g, box, rect, hasLeftEdge: true, hasRightEdge: true);
        }

        /// <summary>The check mark glyph, in the box's resolved text color - only drawn when checked.</summary>
        internal static void PaintCheckboxGlyph(RGraphics g, CssBox box, RRect rect, bool isChecked)
        {
            if (!isChecked) return;

            var mark = g.GetPen(box.ActualColor);
            mark.Width = System.Math.Max(rect.Width, rect.Height) * 0.12;
            var inset = System.Math.Min(rect.Width, rect.Height) * 0.2;
            g.DrawLine(mark, rect.X + inset, rect.Y + rect.Height * 0.5, rect.X + rect.Width * 0.42, rect.Y + rect.Height - inset);
            g.DrawLine(mark, rect.X + rect.Width * 0.42, rect.Y + rect.Height - inset, rect.X + rect.Width - inset, rect.Y + inset);
        }

        /// <summary>
        /// The circular ring a radio button uses instead of <see cref="PaintBorderAndBackground"/>'s
        /// rectangle - background from the box's resolved <c>background-color</c>, the ring itself
        /// from its <c>border-top</c> (a circle has one ring, not four independent edges, so
        /// border-top stands in as the representative side - matching how <c>border-radius</c>
        /// elsewhere in CSS already treats a fully-rounded box as a single curve, not four).
        /// </summary>
        internal static void PaintRadioBackground(RGraphics g, CssBox box, RRect rect)
        {
            var rx = rect.Width / 2;
            var ry = rect.Height / 2;
            using var path = RenderUtils.GetRoundRect(g, rect, rx, ry, rx, ry, rx, ry, rx, ry);

            var backgroundColor = box.ActualBackgroundColor;
            if (RenderUtils.IsColorVisible(backgroundColor))
            {
                using var background = g.GetSolidBrush(backgroundColor);
                g.DrawPath(background, path);
            }

            if (box.BorderTopStyle.Value is not (LineStyle.None or LineStyle.Hidden) && box.ActualBorderTopWidth > 0)
            {
                var pen = g.GetPen(box.ActualBorderTopColor);
                pen.Width = box.ActualBorderTopWidth;
                g.DrawPath(pen, path);
            }
        }

        /// <summary>The filled inner dot, in the box's resolved text color - only drawn when checked.</summary>
        internal static void PaintRadioGlyph(RGraphics g, CssBox box, RRect rect, bool isChecked)
        {
            if (!isChecked) return;

            var inset = System.Math.Min(rect.Width, rect.Height) * 0.28;
            var dotRect = new RRect(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2);
            var dx = dotRect.Width / 2;
            var dy = dotRect.Height / 2;
            using var dotPath = RenderUtils.GetRoundRect(g, dotRect, dx, dy, dx, dy, dx, dy, dx, dy);
            using var brush = g.GetSolidBrush(box.ActualColor);
            g.DrawPath(brush, dotPath);
        }
    }
}
