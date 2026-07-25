using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints a repeated table header/footer's proxy. The fragment builder already positioned the
    /// repeated subtree from the proxy's own geometry snapshot, so the content itself is the ordinary
    /// generic box paint.
    /// </summary>
    /// <remarks>
    /// The snapshot is still written back onto the live source boxes first. One source subtree is
    /// shared by every page's proxy, so those boxes carry only whichever page positioned them last —
    /// and while paint takes its <i>rectangles</i> from fragments, the <c>overflow: hidden</c> clip
    /// walk still resolves ancestor client rectangles off the live boxes. Without this, that clip lands
    /// at another page's position and culls the whole repeated row. Removing this last piece of
    /// live-geometry coupling is follow-on work.
    /// </remarks>
    internal sealed class ProxyFragmentPainter : IFragmentContentPainter
    {
        public void Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment)
        {
            ((CssProxyBox)fragment.Box).ApplySourceGeometry();

            painter.PaintBoxContent(g, fragment);
        }
    }
}
