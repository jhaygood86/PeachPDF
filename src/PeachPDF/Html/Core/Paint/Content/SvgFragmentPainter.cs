using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints an inline <c>&lt;svg&gt;</c> — its scene graph, built once from the element's own
    /// children, rendered as real vector PDF content.
    /// </summary>
    internal sealed class SvgFragmentPainter : ReplacedFragmentPainter
    {
        protected override CssRect ContentWord(CssBox box) => ((CssBoxSvg)box).SvgWord;

        protected override void DrawContent(RGraphics g, CssBox box, RRect rect)
        {
            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, rect, null, ((CssBoxSvg)box).Document, box);
        }
    }
}
