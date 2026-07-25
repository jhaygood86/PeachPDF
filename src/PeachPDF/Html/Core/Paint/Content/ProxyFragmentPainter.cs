using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Fragments;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints a repeated table header/footer's proxy. The fragment builder already positioned the
    /// repeated subtree from the proxy's own geometry snapshot, so the content itself is the ordinary
    /// generic box paint.
    /// </summary>
    /// <remarks>
    /// Nothing else is needed here. This used to write the proxy's geometry snapshot back onto the live
    /// source boxes first, because the <c>overflow: hidden</c> clip walk resolved ancestor rectangles
    /// off them and would otherwise land a page away and cull the whole repeated row. The clip is now
    /// resolved in the builder, which knows which page's snapshot a fragment came from, so paint no
    /// longer mutates layout state at all.
    /// </remarks>
    internal sealed class ProxyFragmentPainter : IFragmentContentPainter
    {
        public void Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment)
        {
            painter.PaintBoxContent(g, fragment);
        }
    }
}
