using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html.Annotations;
using PeachBrackets.Html.Handlers;
using System.Linq;

namespace PeachPDF.PdfFeatures
{
    /// <summary>
    /// Turns the form controls a laid-out document collected (<see cref="PageFormField"/>) into the PDF writer's interactive form fields.
    /// Constructed only when <see cref="PdfGenerateConfig.EnableInteractivePdfForms"/> is set (see <c>PdfGenerator.GeneratePdf</c>'s single
    /// gate point). It only translates: the writer's <see cref="PdfFormBuilder"/> makes the AcroForm objects and appearance streams, and the
    /// control's own look is drawn by <see cref="FormFieldAppearance"/>.
    /// </summary>
    internal sealed class FormFieldBuilder(PdfFormBuilder builder)
    {
        /// <summary>Places one collected control on its page as an interactive field.</summary>
        public void AddField(PdfComposerPage page, PageFormField field)
        {
            var c = field.Classification;
            var kind = c.Kind switch
            {
                FormFieldKind.Text => PdfFormFieldKind.Text,
                FormFieldKind.Checkbox => PdfFormFieldKind.Checkbox,
                FormFieldKind.Radio => PdfFormFieldKind.Radio,
                FormFieldKind.Select => PdfFormFieldKind.Select,
                _ => (PdfFormFieldKind?)null,
            };

            if (kind is not { } fieldKind)
                return;

            builder.AddField(page, new PdfFormFieldDefinition
            {
                Kind = fieldKind,
                Name = c.Name,
                Value = c.Value,
                IsChecked = c.Checked,
                Options = c.Options.Select(o => new PdfFormOption(o.Value, o.Label, o.Selected)).ToList(),
                ReadOnly = c.Attributes.ReadOnly,
                NoExport = c.Attributes.NoExport,
                Required = c.Attributes.Required,
                Password = c.Attributes.Password,
                MaxLength = c.Attributes.MaxLength,
                Placeholder = c.Attributes.Placeholder,
                PlaceholderShown = c.PlaceholderShown,
                AutoFontSize = c.AutoFontSize,
                CombCells = c.Comb,
                DoNotScroll = c.DoNotScroll,
                Bounds = field.Bounds,
                ContentSize = field.ContentSize,
                AppearanceTransform = field.AppearanceTransform,
                Appearance = new Appearance(field.Appearance),
            });
        }

        /// <summary>Draws a control's states for the writer.</summary>
        private sealed class Appearance(FormFieldAppearance look) : IPdfFormAppearance
        {
            public double PixelsPerPoint => look.PixelsPerPoint;

            public double TextFontSizePoints(Size sizePoints, bool autoFontSize, bool isPlaceholder) =>
                look.TextFontSizePt(sizePoints, autoFontSize, isPlaceholder);

            public void PaintText(Canvas canvas, Size sizePoints, string text, bool autoFontSize, int? combCells, bool isPlaceholder) =>
                look.PaintText(canvas, sizePoints, text, autoFontSize, combCells, isPlaceholder);

            public void PaintCheckbox(Canvas canvas, Size sizePoints, bool isChecked) => look.PaintCheckbox(canvas, sizePoints, isChecked);

            public void PaintRadio(Canvas canvas, Size sizePoints, bool isSelected) => look.PaintRadio(canvas, sizePoints, isSelected);
        }
    }
}
