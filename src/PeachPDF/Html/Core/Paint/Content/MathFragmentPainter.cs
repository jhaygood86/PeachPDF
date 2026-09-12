using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.MathML;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints an inline <c>&lt;math&gt;</c> - its presentation tree, laid out once during measurement,
    /// rendered as real vector PDF content (never rasterized).
    /// </summary>
    internal sealed class MathFragmentPainter : ReplacedFragmentPainter
    {
        protected override CssRect ContentWord(CssBox box) => ((CssBoxMath)box).MathWord;

        protected override void DrawContent(RGraphics g, CssBox box, RRect rect)
        {
            var mathBox = ((CssBoxMath)box).Layout;
            if (mathBox is not null)
                MathRenderer.RenderInto(g, mathBox, rect);
        }
    }
}
