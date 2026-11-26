using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachDrawing.Core;
using System.Numerics;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>
    /// Paints a canvas that makes its children drawable, in the probe document. In a picture of one of its own children it is an ordinary box; in a picture of anything else it is a canvas:
    /// what a script drew on it, in the place it has in the paint order, and not its children (a script draws those with <c>drawElementImage</c>).
    /// </summary>
    internal sealed class DrawableCanvasFragmentPainter : IFragmentContentPainter
    {
        public void Paint(FragmentPainter painter, Canvas g, BoxFragment fragment)
        {
            var box = (CssBoxDrawableCanvas)fragment.Box;
            if (box.HtmlContainer?.ProbeRenderRoot is not { } root || IsInside(box, root))
            {
                painter.PaintBoxContent(g, fragment);
                return;
            }

            var rect = fragment.PrimaryRect;
            FragmentPainter.PaintBackground(g, box, BoxDecorationGeometry.Unbroken(rect));
            if (box.Painting is not { Width: > 0, Height: > 0 } painting || rect is not { Width: > 0, Height: > 0 })
                return;

            var content = new Rect(
                rect.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
                rect.Y + box.ActualBorderTopWidth + box.ActualPaddingTop,
                rect.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth - box.ActualPaddingLeft - box.ActualPaddingRight,
                rect.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth - box.ActualPaddingTop - box.ActualPaddingBottom);
            if (content is not { Width: > 0, Height: > 0 })
                return;

            g.PushClip(content);
            g.PushTransform(Matrix3x2.CreateScale((float)(content.Width / painting.Width), (float)(content.Height / painting.Height)) * Matrix3x2.CreateTranslation((float)content.X, (float)content.Y));
            try
            {
                painting.Paint(g);
            }
            finally
            {
                g.PopTransform();
                g.PopClip();
            }
        }

        private static bool IsInside(CssBox ancestor, CssBox box)
        {
            for (var current = box; current is not null; current = current.ParentBox)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }

            return false;
        }
    }
}
