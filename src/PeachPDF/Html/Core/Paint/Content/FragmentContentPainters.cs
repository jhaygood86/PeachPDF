using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Resolves a box to the <see cref="IFragmentContentPainter"/> that paints its content, or null for
    /// an ordinary box (which the generic <see cref="FragmentPainter.PaintBoxContent"/> handles).
    /// </summary>
    internal static class FragmentContentPainters
    {
        private static readonly ImageFragmentPainter ImagePainter = new();
        private static readonly ObjectFragmentPainter ObjectPainter = new();
        private static readonly SvgFragmentPainter SvgPainter = new();
        private static readonly FrameFragmentPainter FramePainter = new();
        private static readonly HrFragmentPainter HrPainter = new();
        private static readonly MarkerFragmentPainter MarkerPainter = new();
        private static readonly FormFieldFragmentPainter FormFieldPainter = new();

        /// <summary>
        /// The painter for <paramref name="box"/>'s content, or null when the generic box paint applies.
        /// The painters are stateless, so one instance each serves every page and every document.
        /// </summary>
        internal static IFragmentContentPainter? For(CssBox box) => box switch
        {
            CssBoxImage => ImagePainter,
            // Also matches CssBoxVideo, which resolves its poster through the same <object> machinery.
            CssBoxObject => ObjectPainter,
            CssBoxSvg => SvgPainter,
            CssBoxFrame => FramePainter,
            CssBoxHr => HrPainter,
            CssBoxMarker => MarkerPainter,
            // Only when interactive forms are enabled AND the box actually resolves to a field kind -
            // otherwise this falls through to the generic path, preserving the flag-off static box
            // rendering byte-for-byte (see FormFieldFragmentPainter's own doc comment).
            CssBoxFormField formField when formField.HtmlContainer?.FormFieldBuilder != null
                && FormFieldMapper.Classify(formField).Kind != FormFieldKind.None => FormFieldPainter,
            _ => null,
        };
    }
}
