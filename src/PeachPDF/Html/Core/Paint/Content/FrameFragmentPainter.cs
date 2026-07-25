using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints an <c>&lt;iframe&gt;</c>: a static PDF cannot embed a browsing context, so the box shows
    /// its error border and, for a recognized embedded video, that video's still image.
    /// </summary>
    internal sealed class FrameFragmentPainter : ReplacedFragmentPainter
    {
        protected override CssRect ContentWord(CssBox box) => ((CssBoxFrame)box).ImageWord;

        protected override void DrawContent(RGraphics g, CssBox box, RRect rect)
        {
            var image = ((CssBoxFrame)box).ImageWord.Image;
            if (image == null) return;

            if (rect is { Width: > 0, Height: > 0 })
            {
                g.DrawImage(image, rect);
            }
        }
    }
}
