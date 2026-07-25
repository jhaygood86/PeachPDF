using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints an <c>&lt;object&gt;</c> (or a <c>&lt;video&gt;</c>'s poster) that resolved to a supported
    /// image. One that did not is not a replaced element at all and paints its fallback DOM children
    /// instead — see <see cref="ReplacedFragmentPainter.IsReplaced"/>.
    /// </summary>
    internal sealed class ObjectFragmentPainter : ReplacedFragmentPainter
    {
        protected override bool IsReplaced(CssBox box) => ((CssBoxObject)box).IsReplaced;

        protected override CssRect? ContentWord(CssBox box) => ((CssBoxObject)box).ReplacedWord;

        protected override void DrawContent(RGraphics g, CssBox box, RRect rect)
        {
            var obj = (CssBoxObject)box;

            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, rect, obj.ReplacedWord?.Image, obj.SvgDocument, box);
        }
    }
}
