using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints an <c>&lt;img&gt;</c>: its decoded raster, or the parsed SVG scene graph when the source
    /// was detected to be an SVG image.
    /// </summary>
    internal sealed class ImageFragmentPainter : ReplacedFragmentPainter
    {
        protected override ValueTask EnsureContentResolved(CssBox box) =>
            ((CssBoxImage)box).EnsureImageLoaded();

        protected override CssRect ContentWord(CssBox box) => ((CssBoxImage)box).ReplacedWord;

        protected override void DrawContent(RGraphics g, CssBox box, RRect rect)
        {
            var image = (CssBoxImage)box;

            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, rect, image.Image, image.SvgDocument, box);
        }
    }
}
