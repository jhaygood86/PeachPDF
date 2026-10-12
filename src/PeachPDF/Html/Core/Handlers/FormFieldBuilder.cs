using System.Collections.Generic;
using System.Globalization;
using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.AcroForms;
using PeachPDF.PdfSharpCore.Pdf.Annotations;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Orchestrates AcroForm field/widget bookkeeping for one <c>PdfGenerator.GeneratePdf</c> call.
    /// Constructed only when <see cref="PdfGenerateConfig.EnableInteractivePdfForms"/> is set (see
    /// <c>PdfGenerator.GeneratePdf</c>'s single gate point) - unlike <see cref="StructureTagBuilder"/>,
    /// this has no paint-time hook of its own; it is consumed entirely by
    /// <c>PdfGenerator.HandleFormFields</c>, a pass over the finished layout that runs after painting,
    /// the same way link annotations are placed by <c>PdfGenerator.HandleLinks</c>.
    /// </summary>
    internal sealed class FormFieldBuilder(PdfDocument document, RenderContext adapter)
    {
        internal PdfDocument Document { get; } = document;
        internal RenderContext Adapter { get; } = adapter;

        /// <summary>
        /// The container's real device-pixel-to-point scale, read fresh on every use (not captured at
        /// construction) - <c>PdfGenerator.GeneratePdf</c> constructs this builder before the
        /// <c>ShrinkToFit</c>/<c>ScaleToPageSize</c> rescale pass may still adjust
        /// <see cref="PdfSharpAdapter.PixelsPerPoint"/>, and <c>PdfGenerator.HandleFormFields</c> only
        /// runs once that has settled - mirrors <c>HtmlContainer.PixelsPerPoint</c>'s own idiom.
        /// </summary>
        double PixelsPerPoint => ((PdfSharpAdapter)Adapter).PixelsPerPoint;

        PdfDictionary? _helveticaFontResource;
        bool _acroFormInitialized;

        // Radio buttons sharing an HTML name attribute become widget-kids of one shared field rather
        // than independent fields (see PdfRadioButtonField's own doc comment) - keyed by name, scoped
        // to this one GeneratePdf call/document.
        readonly Dictionary<string, PdfRadioButtonField> _radioGroupsByName = new();
        int _unnamedRadioGroupCounter;
        int _generatedFieldNameCounter;

        PdfAcroForm EnsureAcroForm()
        {
            var acroForm = Document.Catalog.AcroForm;
            if (!_acroFormInitialized)
            {
                _acroFormInitialized = true;
                var helvetica = FormFieldAppearanceBuilder.GetOrCreateHelveticaFontResource(Document, ref _helveticaFontResource);
                var fontsDict = new PdfDictionary(Document);
                fontsDict.Elements.SetReference("/Helv", helvetica);
                acroForm.DefaultResources.Elements.SetObject("/Font", fontsDict);
                acroForm.DefaultAppearance = "/Helv 10 Tf 0 g";
            }
            return acroForm;
        }

        string ResolveFieldName(string? declaredName) =>
            string.IsNullOrEmpty(declaredName) ? $"PeachPDFField{++_generatedFieldNameCounter}" : declaredName;

        /// <summary>
        /// The "on" appearance-state name for a checkbox/radio widget - the HTML <c>value</c>
        /// attribute verbatim, except "Off" itself (PDF's own reserved "unchecked" state name,
        /// case-sensitive per ISO 32000-1 §12.7.4.2.3): a literal <c>value="Off"</c> would otherwise
        /// make the checked and unchecked states indistinguishable, so it falls back to the same
        /// "Yes" default an absent/empty <c>value</c> already uses.
        /// </summary>
        static string ResolveOnValue(string? declaredValue) =>
            string.IsNullOrEmpty(declaredValue) || declaredValue == "Off" ? "Yes" : declaredValue;

        /// <summary>
        /// The ISO 32000-1 Table 221 flag bits every field type shares, from the HTML attributes that
        /// mean the same thing: <c>readonly</c> (or <c>disabled</c>) to ReadOnly, <c>required</c> to
        /// Required, and <c>disabled</c> additionally to NoExport, since HTML defines a disabled
        /// control as one that is not submitted. Returned as a starting value for each field's own
        /// type-specific flags to build on, so the two can never be assigned over one another.
        /// </summary>
        static int SharedFieldFlags(FormFieldAttributes a)
        {
            var flags = 0;
            if (a.ReadOnly) flags |= PdfAcroField.ReadOnlyFlag;
            if (a.Required) flags |= PdfAcroField.RequiredFlag;
            if (a.NoExport) flags |= PdfAcroField.NoExportFlag;
            return flags;
        }

        /// <summary>
        /// Gives a field the tooltip/accessible name its element's <c>placeholder</c> supplies, if
        /// any. "/TU" is the closest durable PDF home for it: a reader shows it on hover and
        /// assistive technology reads it in place of the machine-oriented field name, and unlike a
        /// drawn hint it can never be mistaken for the field's value.
        /// </summary>
        static void ApplyAlternateName(PdfAcroField field, FormFieldAttributes a)
        {
            if (a.Placeholder is { Length: > 0 } placeholder)
                field.AlternateFieldName = placeholder;
        }

        /// <summary>
        /// Creates the AcroForm field/widget for one classified form-control box and places it on
        /// <paramref name="page"/> at <paramref name="rect"/> (already resolved to page-space PDF
        /// points by <c>PdfGenerator.HandleFormFields</c>).
        /// </summary>
        /// <param name="page">the page the widget goes on</param>
        /// <param name="rect">the widget's <c>/Rect</c></param>
        /// <param name="box">the form-control box</param>
        /// <param name="classification">what kind of field it is</param>
        /// <param name="contentSize">
        /// the size, in points, the appearance is drawn at, when that is not <paramref name="rect"/>'s: a control a <c>transform</c> moved is
        /// still drawn upright at its own size, and <paramref name="appearanceMatrix"/> carries it to where it landed. Defaults to
        /// <paramref name="rect"/>'s size.
        /// </param>
        /// <param name="appearanceMatrix">the <c>/Matrix</c> to give each appearance stream, or null for none</param>
        internal void AddField(PdfPage page, PdfRectangle rect, CssBox box, FormFieldClassification classification,
            XSize? contentSize = null, XMatrix? appearanceMatrix = null)
        {
            var acroForm = EnsureAcroForm();
            var content = contentSize ?? new XSize(rect.Width, rect.Height);
            var matrix = appearanceMatrix;

            switch (classification.Kind)
            {
                case FormFieldKind.Text:
                    AddTextField(acroForm, page, rect, content, matrix, box, classification);
                    break;
                case FormFieldKind.Checkbox:
                    AddCheckboxField(acroForm, page, rect, content, matrix, box, classification);
                    break;
                case FormFieldKind.Radio:
                    AddRadioButton(acroForm, page, rect, content, matrix, box, classification);
                    break;
                case FormFieldKind.Select:
                    AddSelectField(acroForm, page, rect, content, matrix, box, classification);
                    break;
            }
        }

        void AddTextField(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, XSize content, XMatrix? matrix, CssBox box, FormFieldClassification c)
        {
            var field = new PdfTextField(Document)
            {
                Rectangle = rect,
                PartialFieldName = ResolveFieldName(c.Name),
                Value = c.Value ?? string.Empty,
            };

            var flags = SharedFieldFlags(c.Attributes);
            if (c.DoNotScroll) flags |= PdfTextField.DoNotScrollFlag;
            if (c.Attributes.Password) flags |= PdfTextField.PasswordFlag;
            if (c.Comb is > 0)
            {
                // A comb field's cell count IS its "/MaxLen" (ISO 32000-1 Table 228), so the comb
                // declaration wins over a maxlength attribute that disagrees with it - the field is
                // drawn with exactly this many cells, and a longer maximum would let the user type
                // past the last one.
                flags |= PdfTextField.CombFlag;
                field.MaxLen = c.Comb.Value;
            }
            else if (c.Attributes.MaxLength is { } maxLength)
            {
                field.MaxLen = maxLength;
            }
            field.FieldFlags = flags;
            ApplyAlternateName(field, c.Attributes);

            var (drawnText, isPlaceholder) = DrawnText(c);
            var appearance = FormFieldAppearanceBuilder.BuildTextAppearance(
                Document, Adapter, PixelsPerPoint, box, content.Width, content.Height,
                drawnText, c.AutoFontSize, c.Comb, isPlaceholder, out var fontSizePt);

            // "/DA" (as opposed to the baked "/AP /N" appearance above) is what a reader uses to
            // regenerate this field's appearance once the user actually edits it - "0 Tf" is PDF's
            // own literal auto-size convention (ISO 32000-1 §12.7.3.3), the real analogue of
            // -peachpdf-pdf-form-field-auto-font-size for interactive (not just initial) rendering.
            // The point size otherwise agrees with the baked appearance's own resolved font size,
            // even though the face itself differs - see FormFieldAppearanceBuilder.GetOrCreateHelveticaFontResource.
            field.DefaultAppearance = c.AutoFontSize
                ? "/Helv 0 Tf 0 g"
                : $"/Helv {fontSizePt.ToString("0.###", CultureInfo.InvariantCulture)} Tf 0 g";

            SetNormalAppearance(field, appearance, matrix);

            page.Annotations.Add(field);
            acroForm.AddField(field);
        }

        /// <summary>
        /// What a text field's appearance stream actually draws. Normally the value itself; for a
        /// <c>type=password</c> field, one asterisk per character instead - the same unreadable echo
        /// ISO 32000-1 Table 228's Password flag requires of a reader, applied to the initial
        /// appearance the flag says nothing about, so a prefilled password is not simply legible on
        /// the page. When the field has no value and its element's standard HTML
        /// <c>placeholder</c> applies, that hint is drawn instead. See
        /// <c>FormFieldAppearanceBuilder.BuildTextAppearance</c>'s <c>isPlaceholder</c>, which applies
        /// the independently-cascaded <c>::placeholder</c> style.
        /// </summary>
        /// <remarks>
        /// The masking is presentation only: "/V" still carries the real value, because the author
        /// wrote it into the element's own <c>value</c> attribute and the field is genuinely
        /// prefilled with it. A password an author does not want in the file should not be in the
        /// HTML either - see docs/html-css-support.md.
        /// </remarks>
        static (string Text, bool IsPlaceholder) DrawnText(FormFieldClassification c)
        {
            if (c.Value is not { Length: > 0 } value)
            {
                return c.PlaceholderShown && c.Attributes.Placeholder is { Length: > 0 } placeholder
                    ? (placeholder, true)
                    : (string.Empty, false);
            }

            return (c.Attributes.Password ? new string('*', value.Length) : value, false);
        }

        void AddCheckboxField(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, XSize content, XMatrix? matrix, CssBox box, FormFieldClassification c)
        {
            var onValue = ResolveOnValue(c.Value);

            var field = new PdfCheckBoxField(Document)
            {
                Rectangle = rect,
                PartialFieldName = ResolveFieldName(c.Name),
                Value = c.Checked ? "/" + onValue : "/Off",
                AppearanceState = c.Checked ? "/" + onValue : "/Off",
                FieldFlags = SharedFieldFlags(c.Attributes),
            };

            ApplyAlternateName(field, c.Attributes);

            var onAppearance = FormFieldAppearanceBuilder.BuildCheckboxOnAppearance(Document, Adapter, PixelsPerPoint, box, content.Width, content.Height);
            var offAppearance = FormFieldAppearanceBuilder.BuildCheckboxOffAppearance(Document, Adapter, PixelsPerPoint, box, content.Width, content.Height);
            SetTwoStateAppearance(field, onValue, onAppearance, offAppearance, matrix);

            page.Annotations.Add(field);
            acroForm.AddField(field);
        }

        void AddRadioButton(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, XSize content, XMatrix? matrix, CssBox box, FormFieldClassification c)
        {
            var groupKey = string.IsNullOrEmpty(c.Name) ? $"__unnamed_radio_{++_unnamedRadioGroupCounter}__" : c.Name;
            var onValue = ResolveOnValue(c.Value);

            if (!_radioGroupsByName.TryGetValue(groupKey, out var group))
            {
                // The group, not the widget, is the field - so the shared flags and the tooltip
                // belong on it. Unlike the group's own name (which can only come from one button),
                // they accumulate across the group below: the HTML Standard makes a radio group
                // required when ANY member carries `required`, so taking only the first button's
                // attributes would silently drop it from every other spelling of the same form.
                group = new PdfRadioButtonField(Document)
                {
                    PartialFieldName = ResolveFieldName(c.Name),
                    Value = "/Off",
                    FieldFlags = PdfRadioButtonField.RadioFlag,
                };
                _radioGroupsByName[groupKey] = group;
                acroForm.AddField(group);
            }

            group.FieldFlags |= SharedFieldFlags(c.Attributes);
            ApplyAlternateName(group, c.Attributes);

            var widget = new PdfWidgetAnnotation(Document)
            {
                Rectangle = rect,
                AppearanceState = c.Checked ? "/" + onValue : "/Off",
            };

            var onAppearance = FormFieldAppearanceBuilder.BuildRadioOnAppearance(Document, Adapter, PixelsPerPoint, box, content.Width, content.Height);
            var offAppearance = FormFieldAppearanceBuilder.BuildRadioOffAppearance(Document, Adapter, PixelsPerPoint, box, content.Width, content.Height);
            SetTwoStateAppearance(widget, onValue, onAppearance, offAppearance, matrix);

            group.AddKid(widget);
            if (c.Checked)
                group.Value = "/" + onValue;

            page.Annotations.Add(widget);
        }

        void AddSelectField(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, XSize content, XMatrix? matrix, CssBox box, FormFieldClassification c)
        {
            var selected = c.Options.Count > 0 ? System.Linq.Enumerable.FirstOrDefault(c.Options, o => o.Selected) : default;
            var selectedLabel = c.Options.Count == 0 ? string.Empty : (selected.Label ?? c.Options[0].Label);
            var selectedValue = c.Options.Count == 0 ? string.Empty : (selected.Value ?? c.Options[0].Value);

            var field = new PdfComboBoxField(Document)
            {
                Rectangle = rect,
                PartialFieldName = ResolveFieldName(c.Name),
                Value = selectedValue,
                FieldFlags = PdfComboBoxField.ComboFlag | SharedFieldFlags(c.Attributes),
            };

            ApplyAlternateName(field, c.Attributes);

            var options = new List<(string Value, string Label)>();
            foreach (var option in c.Options)
                options.Add((option.Value, option.Label));
            field.SetOptions(options);

            var appearance = FormFieldAppearanceBuilder.BuildTextAppearance(
                Document, Adapter, PixelsPerPoint, box, content.Width, content.Height, selectedLabel,
                autoFontSize: false, combCells: null, isPlaceholder: false, out _);
            SetNormalAppearance(field, appearance, matrix);

            page.Annotations.Add(field);
            acroForm.AddField(field);
        }

        /// <summary>
        /// Gives an appearance stream the <c>/Matrix</c> that carries its upright <c>/BBox</c> to where the control was drawn; a reader fits the
        /// matrix-transformed box to the widget's <c>/Rect</c> (ISO 32000-1 §12.5.5). That fit already scales and moves, so the matrix is
        /// written only when it does something the fit cannot: turn or skew the appearance (an off-diagonal term), or mirror it (a
        /// diagonal that is not positive).
        /// </summary>
        static void ApplyMatrix(PeachPDF.PdfSharpCore.Pdf.Advanced.PdfFormXObject appearance, XMatrix? matrix)
        {
            // The tolerance absorbs the rounding the divided and converted corner coordinates leave in a plain move or scale.
            if (matrix is { } m && (System.Math.Abs(m.M12) > 1e-6 || System.Math.Abs(m.M21) > 1e-6 || m.M11 <= 1e-6 || m.M22 <= 1e-6))
                appearance.Elements.SetMatrix(PeachPDF.PdfSharpCore.Pdf.Advanced.PdfFormXObject.Keys.Matrix, m);
        }

        static void SetNormalAppearance(PdfAnnotation annotation, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfFormXObject appearance, XMatrix? matrix = null)
        {
            ApplyMatrix(appearance, matrix);
            var ap = new PdfDictionary(annotation.Owner);
            ap.Elements.SetReference("/N", appearance);
            annotation.Elements.SetObject(PdfAnnotation.Keys.AP, ap);
        }

        static void SetTwoStateAppearance(PdfAnnotation annotation, string onValue,
            PeachPDF.PdfSharpCore.Pdf.Advanced.PdfFormXObject onAppearance, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfFormXObject offAppearance,
            XMatrix? matrix = null)
        {
            ApplyMatrix(onAppearance, matrix);
            ApplyMatrix(offAppearance, matrix);

            var states = new PdfDictionary(annotation.Owner);
            states.Elements.SetReference("/" + onValue, onAppearance);
            states.Elements.SetReference("/Off", offAppearance);

            var ap = new PdfDictionary(annotation.Owner);
            ap.Elements.SetObject("/N", states);
            annotation.Elements.SetObject(PdfAnnotation.Keys.AP, ap);
        }
    }
}
