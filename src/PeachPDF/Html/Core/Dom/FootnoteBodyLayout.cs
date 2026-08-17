using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Lays a detached <c>float: footnote</c> body out for real against a rect inside its landing page's
    /// footnote area - a thin, footnote-specific entry point over <see cref="RunningElementLayout.LayoutRunningElementFor"/>,
    /// which already implements exactly this shape of problem ("lay this box out again, off to the side,
    /// at a provisional position, within one document-layout generation") for css-gcpm-3's other
    /// out-of-flow mechanism, <c>position: running()</c>. Kept as its own type rather than a direct call
    /// at each footnote call site so a footnote-specific behavior - the accepted-gap "a body taller than
    /// one page's content band overflows rather than splitting" (this method never fragments the body,
    /// same as a running element) - has one documented seam if it ever needs to diverge from the running-
    /// element path.
    /// </summary>
    internal static class FootnoteBodyLayout
    {
        internal static ValueTask LayoutFootnoteBodyFor(RGraphics g, CssBox body, RRect contentRect, HtmlContainerInt container) =>
            RunningElementLayout.LayoutRunningElementFor(g, body, contentRect, container);
    }
}
