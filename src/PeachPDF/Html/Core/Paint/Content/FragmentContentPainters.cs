using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Resolves a box to the <see cref="IFragmentContentPainter"/> that paints its content, or null for
    /// an ordinary box (which the generic <see cref="FragmentPainter.PaintBoxContent"/> handles).
    /// </summary>
    internal static class FragmentContentPainters
    {
        private static readonly ImageFragmentPainter Image = new();
        private static readonly ObjectFragmentPainter Object = new();
        private static readonly SvgFragmentPainter Svg = new();
        private static readonly FrameFragmentPainter Frame = new();
        private static readonly HrFragmentPainter Hr = new();
        private static readonly MarkerFragmentPainter Marker = new();
        private static readonly ProxyFragmentPainter Proxy = new();

        /// <summary>
        /// The painter for <paramref name="box"/>'s content, or null when the generic box paint applies.
        /// The painters are stateless, so one instance each serves every page and every document.
        /// </summary>
        internal static IFragmentContentPainter? For(CssBox box) => box switch
        {
            CssBoxImage => Image,
            // Also matches CssBoxVideo, which resolves its poster through the same <object> machinery.
            CssBoxObject => Object,
            CssBoxSvg => Svg,
            CssBoxFrame => Frame,
            CssBoxHr => Hr,
            CssBoxMarker => Marker,
            CssProxyBox => Proxy,
            _ => null,
        };
    }
}
