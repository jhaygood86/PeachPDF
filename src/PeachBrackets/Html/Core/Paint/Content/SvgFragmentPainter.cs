using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Svg;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>
    /// Paints an inline <c>&lt;svg&gt;</c> — its scene graph, built once from the element's own
    /// children, rendered as real vector PDF content.
    /// </summary>
    internal sealed class SvgFragmentPainter : ReplacedFragmentPainter
    {
        protected override CssRect ContentWord(CssBox box) => ((CssBoxSvg)box).SvgWord;

        protected override void DrawContent(Canvas g, CssBox box, Rect rect)
        {
            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, rect, null, ((CssBoxSvg)box).Document, box);
        }

        protected override void DrawContent(FragmentPainter painter, Canvas g, BoxFragment fragment, CssBox box, Rect rect)
        {
            // Only an SVG whose filters read BackgroundImage needs the page behind it (see SvgRenderer.BindPageBackdrop).
            if (((CssBoxSvg)box).Document is not { ReadsBackdrop: true } document)
            {
                DrawContent(g, box, rect);
                return;
            }

            var previous = SvgRenderer.BindPageBackdrop(document, painter.CreateSvgBackdrop(fragment));
            try
            {
                DrawContent(g, box, rect);
            }
            finally
            {
                SvgRenderer.BindPageBackdrop(previous.Document, previous.Page);
            }
        }
    }
}
