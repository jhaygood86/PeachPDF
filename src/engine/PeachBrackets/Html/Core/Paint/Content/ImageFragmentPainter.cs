using PeachDrawing.Core;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>
    /// Paints an <c>&lt;img&gt;</c>: its decoded raster, or the parsed SVG scene graph when the source
    /// was detected to be an SVG image.
    /// </summary>
    internal sealed class ImageFragmentPainter : ReplacedFragmentPainter
    {
        protected override CssRect ContentWord(CssBox box) => ((CssBoxImage)box).ReplacedWord;

        protected override void DrawContent(Canvas g, CssBox box, Rect rect)
        {
            var image = (CssBoxImage)box;

            // An image that could not be loaded shows its alt text, clipped to the box it was given.
            if (image.BrokenImageAlt is { } alt)
            {
                g.PushClip(rect);
                g.DrawString(alt, box.ActualFont, box.ActualColor, new PaintPoint(rect.X, rect.Y), new Size(rect.Width, rect.Height),
                    letterSpacing: 0, fontPalette: null, box.ActualTextShapingFeatures);
                g.PopClip();
                return;
            }

            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, rect, image.Image, image.SvgDocument?.Current, box);
        }
    }
}
