using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints an inline <c>&lt;svg&gt;</c> — its scene graph, built once from the element's own
    /// children, rendered as real vector PDF content.
    /// </summary>
    internal sealed class SvgFragmentPainter : ReplacedFragmentPainter
    {
        protected override ValueTask EnsureContentResolved(CssBox box)
        {
            // Building the scene graph is synchronous and idempotent; any network <image> hrefs it
            // needs were prefetched during measurement.
            ((CssBoxSvg)box).EnsureDocument();
            return ValueTask.CompletedTask;
        }

        protected override CssRect ContentWord(CssBox box) => ((CssBoxSvg)box).SvgWord;

        protected override void DrawContent(RGraphics g, CssBox box, RRect rect)
        {
            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, rect, null, ((CssBoxSvg)box).Document, box);
        }
    }
}
