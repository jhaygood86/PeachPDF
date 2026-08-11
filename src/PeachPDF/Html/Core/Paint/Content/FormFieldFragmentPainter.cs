using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints the flag-on "static look" of a classified form field - a checkbox square/check mark, a
    /// radio circle/dot, or a bordered text/select box. Only reached when
    /// <c>PdfGenerateConfig.EnableInteractivePdfForms</c> is on and the box actually classifies to a
    /// field (see <c>FragmentContentPainters.For</c>'s guard) - the flag-off static box rendering is
    /// unchanged, handled entirely by the generic <see cref="FragmentPainter.PaintBoxContent"/> path.
    /// </summary>
    /// <remarks>
    /// A text/select field's current value/selected-label text is deliberately NOT drawn here, even
    /// though <c>FormFieldAppearanceBuilder</c> bakes the identical text into the widget's own "/AP /N"
    /// appearance stream - the two are not simply redundant. A reader that renders annotations (the
    /// normal case) composites the widget's appearance on top of this page content, and when a text
    /// field gains focus for editing, readers commonly render their own live/interactive text (from
    /// "/V" via "/DA") without first re-covering whatever this method already painted underneath -
    /// drawing the same text twice at two independently-computed positions (this method uses the
    /// page's own resolved font; the appearance stream uses a fixed-size Helvetica) then shows as
    /// visibly doubled, offset text the moment the field is selected. The border/background chrome
    /// carries no such risk (a reader's own edit-mode rendering is opaque over it) and stays.
    /// Checkbox/radio glyphs stay too - toggling one swaps between two pre-baked appearance states
    /// rather than opening a live-text edit mode, so the same doubling risk does not apply to them.
    /// </remarks>
    internal sealed class FormFieldFragmentPainter : IFragmentContentPainter
    {
        public void Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment)
        {
            var box = (CssBoxFormField)fragment.Box;
            var classification = FormFieldMapper.Classify(box);
            if (classification.Kind == FormFieldKind.None)
            {
                painter.PaintBoxContent(g, fragment);
                return;
            }

            // Mirrors ReplacedFragmentPainter's own choice of fragment.PrimaryRect (the phantom word's
            // line-resolved rect) over WholeBoxRect - an inline-level box's whole-box geometry is only
            // resolved at fragment materialization (see CLAUDE.md's fragment-tree architecture note),
            // but the box's own line rect - what CssRectFormField's intrinsic sizing actually
            // populated - is already correct here, the same way it already is for CssBoxImage.
            var rect = fragment.PrimaryRect;
            if (rect is not { Width: > 0, Height: > 0 }) return;

            switch (classification.Kind)
            {
                case FormFieldKind.Checkbox:
                    FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                    FormFieldChrome.PaintCheckboxGlyph(g, box, rect, classification.Checked);
                    break;
                case FormFieldKind.Radio:
                    FormFieldChrome.PaintRadioBackground(g, box, rect);
                    FormFieldChrome.PaintRadioGlyph(g, box, rect, classification.Checked);
                    break;
                case FormFieldKind.Text:
                case FormFieldKind.Select:
                    FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                    break;
            }
        }
    }
}
