using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Svg;

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

        protected override void DrawContent(FragmentPainter painter, RGraphics g, BoxFragment fragment, CssBox box, RRect rect)
        {
            // Only an SVG whose filters read BackgroundImage needs the page behind it (see SvgRenderer.PageBackdrop).
            if (((CssBoxSvg)box).Document is not { ReadsBackdrop: true })
            {
                DrawContent(g, box, rect);
                return;
            }

            var previous = SvgRenderer.PageBackdrop;
            SvgRenderer.PageBackdrop = painter.CreateSvgBackdrop(fragment);
            try
            {
                DrawContent(g, box, rect);
            }
            finally
            {
                SvgRenderer.PageBackdrop = previous;
            }
        }
    }
}
