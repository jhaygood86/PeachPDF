using PeachBrackets.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Handlers;

namespace PeachBrackets.Html.Paint.Content
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
        private static readonly MathFragmentPainter MathPainter = new();
        private static readonly FrameFragmentPainter FramePainter = new();
        private static readonly MarkerFragmentPainter MarkerPainter = new();
        private static readonly FormFieldFragmentPainter FormFieldPainter = new();
        private static readonly ScreenFormControlPainter ScreenFormPainter = new();
        private static readonly MeterFragmentPainter MeterPainter = new();
        private static readonly StaticTextAreaPainter StaticTextAreaPainter = new();

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
            CssBoxMath => MathPainter,
            CssBoxFrame => FramePainter,
            CssBoxMarker => MarkerPainter,
            // Only when interactive forms are enabled AND the box actually resolves to a field kind -
            // otherwise this falls through to the generic path, preserving the flag-off static box
            // rendering byte-for-byte (see FormFieldFragmentPainter's own doc comment).
            // A host that shows the page draws the controls itself, whatever they are, and whether or not it also collects them as fields.
            CssBoxFormField formField when formField.HtmlContainer?.PaintFormControls == true => ScreenFormPainter,
            CssBoxFormField formField when formField.HtmlContainer?.InteractiveForms == true
                && FormFieldMapper.Classify(formField).Kind != FormFieldKind.None => FormFieldPainter,
            // A text area is not flowed content: its value is drawn by the control, so a static page shows it the way a screen does (an interactive
            // field has its own widget draw the text, and took the arm above).
            CssBoxFormField { IsTextArea: true } => StaticTextAreaPainter,
            // A progress bar or meter is a plain box with a drawn look, in a PDF as on a screen.
            _ when MeterFragmentPainter.IsMeterLike(box) => MeterPainter,
            _ => null,
        };
    }
}
