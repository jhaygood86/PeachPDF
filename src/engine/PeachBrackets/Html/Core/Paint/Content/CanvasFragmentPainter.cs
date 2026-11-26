using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using System.Numerics;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>
    /// Paints a <c>&lt;canvas&gt;</c>: replays what a script drew onto the page canvas as vector content, scaled from the bitmap's size to the box's.
    /// </summary>
    internal sealed class CanvasFragmentPainter : ReplacedFragmentPainter
    {
        protected override CssRect ContentWord(CssBox box) => ((CssBoxCanvas)box).ContentWord;

        protected override void DrawContent(Canvas g, CssBox box, Rect rect)
        {
            if (((CssBoxCanvas)box).Painting is not { Width: > 0, Height: > 0 } painting || rect is not { Width: > 0, Height: > 0 })
                return;

            g.PushClip(rect);
            g.PushTransform(Matrix3x2.CreateScale((float)(rect.Width / painting.Width), (float)(rect.Height / painting.Height)) * Matrix3x2.CreateTranslation((float)rect.X, (float)rect.Y));
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
    }
}
