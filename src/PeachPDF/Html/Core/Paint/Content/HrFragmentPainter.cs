using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints an <c>&lt;hr&gt;</c>. The rule is drawn as the box's own borders over its whole (never
    /// fragmented) border box rather than through the generic decoration path, which is what gives a
    /// thin rule its classic engraved look: the top border always paints, and the remaining three only
    /// once the box is tall enough to have distinguishable sides.
    /// </summary>
    internal sealed class HrFragmentPainter : IFragmentContentPainter
    {
        public void Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment)
        {
            var box = fragment.Box;
            var rect = fragment.WholeBoxRect;

            if (rect.Height > 2 && RenderUtils.IsColorVisible(box.ActualBackgroundColor))
            {
                g.DrawRectangle(g.GetSolidBrush(box.ActualBackgroundColor), rect.X, rect.Y, rect.Width, rect.Height);
            }

            var b1 = g.GetSolidBrush(box.ActualBorderTopColor);
            BordersDrawHandler.DrawBorder(Border.Top, g, box, b1, rect);

            if (rect.Height > 1)
            {
                var b2 = g.GetSolidBrush(box.ActualBorderLeftColor);
                BordersDrawHandler.DrawBorder(Border.Left, g, box, b2, rect);

                var b3 = g.GetSolidBrush(box.ActualBorderRightColor);
                BordersDrawHandler.DrawBorder(Border.Right, g, box, b3, rect);

                var b4 = g.GetSolidBrush(box.ActualBorderBottomColor);
                BordersDrawHandler.DrawBorder(Border.Bottom, g, box, b4, rect);
            }
        }
    }
}
