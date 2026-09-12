#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Paints a laid-out MathBox tree into the PDF content stream via ordinary RGraphics calls - the
// MathML equivalent of SvgRenderer. Never rasterizes: token text goes through the same DrawString
// path any other HTML/SVG text uses, fraction bars/radical rules are plain filled rectangles, and
// stretchy-operator glyphs (MATH-table variants/assemblies) go through the new RGraphics.DrawGlyphs
// primitive - all real vector PDF content.
//
#endregion

using System.Collections.Generic;
using System.Linq;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.MathML
{
    internal static class MathRenderer
    {
        /// <summary>Paints <paramref name="root"/> into <paramref name="destination"/> - the box's own
        /// baseline lands at <c>destination.Y + root.Ascent</c>, matching how
        /// <see cref="Html.Core.Dom.CssBoxMath"/> sized the phantom word this rect comes from (its
        /// height is exactly <c>root.Ascent + root.Descent</c>).</summary>
        public static void RenderInto(RGraphics g, MathBox root, RRect destination)
        {
            RenderBox(g, root, destination.X, destination.Y + root.Ascent);
        }

        static void RenderBox(RGraphics g, MathBox box, double x, double y)
        {
            switch (box.PaintKind)
            {
                case MathPaintKind.Text when !string.IsNullOrEmpty(box.Text):
                    g.DrawString(box.Text!, box.Font!, box.Color, new RPoint(x, y - box.Font!.Ascent),
                        new RSize(box.InlineSize, box.Ascent + box.Descent));
                    break;

                case MathPaintKind.Rule when box.RuleWidth > 0 && box.RuleHeight > 0:
                    g.DrawRectangle(g.GetSolidBrush(box.Color), x + box.RuleX, y + box.RuleY, box.RuleWidth, box.RuleHeight);
                    break;

                case MathPaintKind.Glyphs when box.Glyphs is { Count: > 0 }:
                    var placements = box.Glyphs
                        .Select(gl => new GlyphPlacement(gl.GlyphIndex, x + gl.X, y + gl.Y))
                        .ToList();
                    g.DrawGlyphs(placements, box.Font!, box.Color);
                    break;
            }

            foreach (var child in box.Children)
                RenderBox(g, child.Box, x + child.X, y + child.Y);
        }
    }
}
