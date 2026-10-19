#nullable enable

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.AcroForms;
using PeachDrawing.PDF.Engine.Annotations;
using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace PeachDrawing.PDF
{
    /// <summary>The kinds of interactive form field a <see cref="PdfFormBuilder"/> makes.</summary>
    public enum PdfFormFieldKind
    {
        /// <summary>A single-line text field.</summary>
        Text,

        /// <summary>A check box, on or off.</summary>
        Checkbox,

        /// <summary>One button of a group of radio buttons; the buttons sharing a name are one field.</summary>
        Radio,

        /// <summary>A drop-down list (combo box) of options.</summary>
        Select,
    }

    /// <summary>One choice of a <see cref="PdfFormFieldKind.Select"/> field.</summary>
    /// <param name="value">the value the field takes when the option is chosen</param>
    /// <param name="label">the text shown for the option</param>
    /// <param name="selected">whether the option is the one chosen initially</param>
    public readonly struct PdfFormOption(string value, string label, bool selected)
    {
        /// <summary>The value the field takes when the option is chosen.</summary>
        public string Value { get; } = value;

        /// <summary>The text shown for the option.</summary>
        public string Label { get; } = label;

        /// <summary>Whether the option is the one chosen initially.</summary>
        public bool Selected { get; } = selected;
    }

    /// <summary>
    /// How a form field looks, drawn by whoever knows what it should look like: the writer asks for each state it needs once, and draws it
    /// into an appearance stream of its own at <see cref="PixelsPerPoint"/>.
    /// </summary>
    public interface IPdfFormAppearance
    {
        /// <summary>
        /// The layout units per point the appearance is drawn in. The canvases passed to the paint methods use it, so a field's border,
        /// padding and font stay sized consistently with the page it sits on.
        /// </summary>
        double PixelsPerPoint { get; }

        /// <summary>The size in points of the font a text appearance of this size draws with; written as the field's default appearance.</summary>
        /// <param name="sizePoints">the field's size, in points</param>
        /// <param name="autoFontSize">whether the font is fitted to the field's height</param>
        /// <param name="isPlaceholder">whether the text is a placeholder hint rather than the value</param>
        double TextFontSizePoints(Size sizePoints, bool autoFontSize, bool isPlaceholder);

        /// <summary>
        /// Draws a text field (or a drop-down's selected label): its border and background, then <paramref name="text"/>. The text must be
        /// drawn between <see cref="Canvas.BeginVariableText"/> and <see cref="Canvas.EndVariableText"/>, so a reader that regenerates the
        /// field once it is edited replaces exactly that.
        /// </summary>
        /// <param name="canvas">the canvas to draw on</param>
        /// <param name="sizePoints">the field's size, in points</param>
        /// <param name="text">the text to draw</param>
        /// <param name="autoFontSize">whether the font is fitted to the field's height</param>
        /// <param name="combCells">the number of comb cells, or <see langword="null"/> for none</param>
        /// <param name="isPlaceholder">whether <paramref name="text"/> is a placeholder hint</param>
        void PaintText(Canvas canvas, Size sizePoints, string text, bool autoFontSize, int? combCells, bool isPlaceholder);

        /// <summary>Draws a check box, with its check mark when <paramref name="isChecked"/>.</summary>
        /// <param name="canvas">the canvas to draw on</param>
        /// <param name="sizePoints">the field's size, in points</param>
        /// <param name="isChecked">whether to draw the check mark</param>
        void PaintCheckbox(Canvas canvas, Size sizePoints, bool isChecked);

        /// <summary>Draws a radio button, with its inner dot when <paramref name="isSelected"/>.</summary>
        /// <param name="canvas">the canvas to draw on</param>
        /// <param name="sizePoints">the field's size, in points</param>
        /// <param name="isSelected">whether to draw the inner dot</param>
        void PaintRadio(Canvas canvas, Size sizePoints, bool isSelected);
    }

    /// <summary>
    /// An interactive form field to put on a page: what it is, where it is, and how it looks.
    /// </summary>
    public sealed class PdfFormFieldDefinition
    {
        /// <summary>What kind of field it is.</summary>
        public required PdfFormFieldKind Kind { get; init; }

        /// <summary>The field's name, or <see langword="null"/> to have the builder generate one.</summary>
        public string? Name { get; init; }

        /// <summary>The text of a text field, or the value a check box or radio button takes when it is on. Unused by a select.</summary>
        public string? Value { get; init; }

        /// <summary>Whether a check box or radio button is on initially.</summary>
        public bool IsChecked { get; init; }

        /// <summary>The choices of a select, in order.</summary>
        public IReadOnlyList<PdfFormOption> Options { get; init; } = [];

        /// <summary>Whether the field cannot be edited.</summary>
        public bool ReadOnly { get; init; }

        /// <summary>Whether the field is left out when the form is submitted.</summary>
        public bool NoExport { get; init; }

        /// <summary>Whether the field must be filled in.</summary>
        public bool Required { get; init; }

        /// <summary>Whether a text field echoes its value unreadably (a password).</summary>
        public bool Password { get; init; }

        /// <summary>The most characters a text field accepts, or <see langword="null"/> for no limit. A comb field's cell count wins over it.</summary>
        public int? MaxLength { get; init; }

        /// <summary>
        /// A hint for the field: shown as its tooltip and read by assistive technology in place of the machine-oriented name. When the field has
        /// no value and <see cref="PlaceholderShown"/> is set it is also drawn in the field.
        /// </summary>
        public string? Placeholder { get; init; }

        /// <summary>Whether <see cref="Placeholder"/> is drawn in an empty text field.</summary>
        public bool PlaceholderShown { get; init; }

        /// <summary>Whether a text field fits its font to its height.</summary>
        public bool AutoFontSize { get; init; }

        /// <summary>The number of comb cells of a text field (one character each), or <see langword="null"/> for none.</summary>
        public int? CombCells { get; init; }

        /// <summary>Whether a text field does not scroll when its text overflows.</summary>
        public bool DoNotScroll { get; init; }

        /// <summary>
        /// Where the field sits, in points from the page's top-left corner with y down: its box, or the bounding box of the drawn shape when it
        /// was transformed. An empty rectangle at its bottom-left corner keeps the field (it still holds a value and belongs to its form) with
        /// nothing to see or click.
        /// </summary>
        public required Rect Bounds { get; init; }

        /// <summary>The size, in points, the appearance is drawn at: the field's own size, even when a transform moved it.</summary>
        public required Size ContentSize { get; init; }

        /// <summary>
        /// The linear part of the transform that carries the upright appearance to where the field was drawn (translation left out; the
        /// reader fits the result to <see cref="Bounds"/>), in the same y-down convention as <see cref="Bounds"/>, or <see langword="null"/>
        /// when the field was not transformed or the transform is not affine.
        /// </summary>
        public Matrix3x2? AppearanceTransform { get; init; }

        /// <summary>How the field looks.</summary>
        public required IPdfFormAppearance Appearance { get; init; }
    }

    /// <summary>
    /// Adds interactive form fields (AcroForm text fields, check boxes, radio buttons and drop-downs) to the pages of a document.
    /// </summary>
    /// <remarks>
    /// Get one from <see cref="PdfComposer.CreateFormBuilder"/> and call <see cref="AddField"/> for each field once its page exists. Radio
    /// buttons that share a name become the buttons of one field. Each field's look is drawn once per state by its
    /// <see cref="PdfFormFieldDefinition.Appearance"/> into an appearance stream, so a reader shows it as drawn until the user edits it.
    /// </remarks>
    public sealed class PdfFormBuilder
    {
        private readonly string _generatedNamePrefix;

        internal PdfFormBuilder(PdfDocument document, PdfRenderContext renderContext, string generatedNamePrefix)
        {
            Document = document;
            RenderContext = renderContext;
            _generatedNamePrefix = generatedNamePrefix;
        }

        internal PdfDocument Document { get; }
        internal PdfRenderContext RenderContext { get; }

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
                var helvetica = GetOrCreateHelveticaFontResource(Document, ref _helveticaFontResource);
                var fontsDict = new PdfDictionary(Document);
                fontsDict.Elements.SetReference("/Helv", helvetica);
                acroForm.DefaultResources.Elements.SetObject("/Font", fontsDict);
                acroForm.DefaultAppearance = "/Helv 10 Tf 0 g";
            }
            return acroForm;
        }

        string ResolveFieldName(string? declaredName) =>
            string.IsNullOrEmpty(declaredName) ? $"{_generatedNamePrefix}{++_generatedFieldNameCounter}" : declaredName;

        /// <summary>
        /// The "on" appearance-state name for a checkbox/radio widget - the field's <see cref="PdfFormFieldDefinition.Value"/>
        /// verbatim, except "Off" itself (PDF's own reserved "unchecked" state name,
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
        static int SharedFieldFlags(PdfFormFieldDefinition a)
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
        static void ApplyAlternateName(PdfAcroField field, PdfFormFieldDefinition a)
        {
            if (a.Placeholder is { Length: > 0 } placeholder)
                field.AlternateFieldName = placeholder;
        }

        /// <summary>
        /// Creates the field (or, for a radio button, the button and its group) and places it on <paramref name="page"/>.
        /// </summary>
        /// <param name="page">the page the field goes on</param>
        /// <param name="field">what the field is, where it is and how it looks</param>
        public void AddField(PdfComposerPage page, PdfFormFieldDefinition field)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(field);

            var acroForm = EnsureAcroForm();
            var content = field.ContentSize;
            var enginePage = page.Page;

            // Positions arrive measured from the page's top-left corner; a PDF rect counts from the bottom edge, and the appearance stream's own
            // space is y up too, so the y-down transform is mirrored across the x axis.
            var bounds = field.Bounds;
            var rect = new PdfRectangle(new Rect(bounds.X, page.HeightPoints - (bounds.Y + bounds.Height), bounds.Width, bounds.Height));
            Matrix3x2? matrix = field.AppearanceTransform is { } m ? new Matrix3x2(m.M11, -m.M12, -m.M21, m.M22, 0, 0) : null;

            switch (field.Kind)
            {
                case PdfFormFieldKind.Text:
                    AddTextField(acroForm, enginePage, rect, content, matrix, field.Appearance, field);
                    break;
                case PdfFormFieldKind.Checkbox:
                    AddCheckboxField(acroForm, enginePage, rect, content, matrix, field.Appearance, field);
                    break;
                case PdfFormFieldKind.Radio:
                    AddRadioButton(acroForm, enginePage, rect, content, matrix, field.Appearance, field);
                    break;
                case PdfFormFieldKind.Select:
                    AddSelectField(acroForm, enginePage, rect, content, matrix, field.Appearance, field);
                    break;
            }
        }

        void AddTextField(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, Size content, Matrix3x2? matrix, IPdfFormAppearance look, PdfFormFieldDefinition c)
        {
            var field = new PdfTextField(Document)
            {
                Rectangle = rect,
                PartialFieldName = ResolveFieldName(c.Name),
                Value = c.Value ?? string.Empty,
            };

            var flags = SharedFieldFlags(c);
            if (c.DoNotScroll) flags |= PdfTextField.DoNotScrollFlag;
            if (c.Password) flags |= PdfTextField.PasswordFlag;
            if (c.CombCells is > 0)
            {
                // A comb field's cell count IS its "/MaxLen" (ISO 32000-1 Table 228), so the comb
                // declaration wins over a maxlength attribute that disagrees with it - the field is
                // drawn with exactly this many cells, and a longer maximum would let the user type
                // past the last one.
                flags |= PdfTextField.CombFlag;
                field.MaxLen = c.CombCells.Value;
            }
            else if (c.MaxLength is { } maxLength)
            {
                field.MaxLen = maxLength;
            }
            field.FieldFlags = flags;
            ApplyAlternateName(field, c);

            var (drawnText, isPlaceholder) = DrawnText(c);
            var fontSizePt = look.TextFontSizePoints(content, c.AutoFontSize, isPlaceholder);
            var appearance = CreateForm(look, content, g => look.PaintText(g, content, drawnText, c.AutoFontSize, c.CombCells, isPlaceholder));

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
        static (string Text, bool IsPlaceholder) DrawnText(PdfFormFieldDefinition c)
        {
            if (c.Value is not { Length: > 0 } value)
            {
                return c.PlaceholderShown && c.Placeholder is { Length: > 0 } placeholder
                    ? (placeholder, true)
                    : (string.Empty, false);
            }

            return (c.Password ? new string('*', value.Length) : value, false);
        }

        void AddCheckboxField(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, Size content, Matrix3x2? matrix, IPdfFormAppearance look, PdfFormFieldDefinition c)
        {
            var onValue = ResolveOnValue(c.Value);

            var field = new PdfCheckBoxField(Document)
            {
                Rectangle = rect,
                PartialFieldName = ResolveFieldName(c.Name),
                Value = c.IsChecked ? "/" + onValue : "/Off",
                AppearanceState = c.IsChecked ? "/" + onValue : "/Off",
                FieldFlags = SharedFieldFlags(c),
            };

            ApplyAlternateName(field, c);

            var onAppearance = CreateForm(look, content, g => look.PaintCheckbox(g, content, isChecked: true));
            var offAppearance = CreateForm(look, content, g => look.PaintCheckbox(g, content, isChecked: false));
            SetTwoStateAppearance(field, onValue, onAppearance, offAppearance, matrix);

            page.Annotations.Add(field);
            acroForm.AddField(field);
        }

        void AddRadioButton(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, Size content, Matrix3x2? matrix, IPdfFormAppearance look, PdfFormFieldDefinition c)
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

            group.FieldFlags |= SharedFieldFlags(c);
            ApplyAlternateName(group, c);

            var widget = new PdfWidgetAnnotation(Document)
            {
                Rectangle = rect,
                AppearanceState = c.IsChecked ? "/" + onValue : "/Off",
            };

            var onAppearance = CreateForm(look, content, g => look.PaintRadio(g, content, isSelected: true));
            var offAppearance = CreateForm(look, content, g => look.PaintRadio(g, content, isSelected: false));
            SetTwoStateAppearance(widget, onValue, onAppearance, offAppearance, matrix);

            group.AddKid(widget);
            if (c.IsChecked)
                group.Value = "/" + onValue;

            page.Annotations.Add(widget);
        }

        void AddSelectField(PdfAcroForm acroForm, PdfPage page, PdfRectangle rect, Size content, Matrix3x2? matrix, IPdfFormAppearance look, PdfFormFieldDefinition c)
        {
            var selected = c.Options.Count > 0 ? System.Linq.Enumerable.FirstOrDefault(c.Options, o => o.Selected) : default;
            var selectedLabel = c.Options.Count == 0 ? string.Empty : (selected.Label ?? c.Options[0].Label);
            var selectedValue = c.Options.Count == 0 ? string.Empty : (selected.Value ?? c.Options[0].Value);

            var field = new PdfComboBoxField(Document)
            {
                Rectangle = rect,
                PartialFieldName = ResolveFieldName(c.Name),
                Value = selectedValue,
                FieldFlags = PdfComboBoxField.ComboFlag | SharedFieldFlags(c),
            };

            ApplyAlternateName(field, c);

            var options = new List<(string Value, string Label)>();
            foreach (var option in c.Options)
                options.Add((option.Value, option.Label));
            field.SetOptions(options);

            var appearance = CreateForm(look, content, g => look.PaintText(g, content, selectedLabel, autoFontSize: false, combCells: null, isPlaceholder: false));
            SetNormalAppearance(field, appearance, matrix);

            page.Annotations.Add(field);
            acroForm.AddField(field);
        }

        /// <summary>
        /// Makes an appearance stream (a <see cref="PeachDrawing.PDF.Engine.Advanced.PdfFormXObject"/>) by painting onto a fresh <see cref="PdfForm"/>
        /// through a <see cref="PdfCanvas"/> - the recipe <c>PdfCanvas.CreateTile</c> already uses for SVG mask and pattern tiles - at the
        /// pixels-per-point the control was laid out with, so its real CSS border, padding and font stay consistently sized.
        /// </summary>
        PeachDrawing.PDF.Engine.Advanced.PdfFormXObject CreateForm(IPdfFormAppearance look, Size sizePt, Action<Canvas> draw)
        {
            var form = new PdfForm(Document, sizePt);
            var formGraphics = PdfGraphics.FromForm(form);
            using (var g = new PdfCanvas(RenderContext, formGraphics, look.PixelsPerPoint, releaseGraphics: true))
            {
                draw(g);
            }
            return form.FormXObject;
        }

        /// <summary>
        /// Creates the shared "/Helv" Type1 Helvetica font resource dictionary. Not used by any appearance stream (those use the field's
        /// own real, possibly embedded font) - only by each field's "/DA" default appearance string, which governs how a reader
        /// regenerates the field's look once a user actually edits it. "/DA" deliberately keeps using a PDF standard font rather than
        /// the field's own real one: PeachPDF (like most PDF generators) subsets an embedded font down to only the glyphs the document
        /// actually drew, so a user typing a character that never appeared in the baked appearance would have no glyph to render with the
        /// real font, whereas Helvetica's WinAnsi coverage is complete and needs no embedding at all.
        /// </summary>
        static PdfDictionary GetOrCreateHelveticaFontResource(PdfDocument document, ref PdfDictionary? cache)
        {
            if (cache != null)
                return cache;

            var font = new PdfDictionary(document);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", "/Helvetica");
            font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
            document.Internals.AddObject(font);

            cache = font;
            return font;
        }

        /// <summary>
        /// Gives an appearance stream the <c>/Matrix</c> that carries its upright <c>/BBox</c> to where the control was drawn; a reader fits the
        /// matrix-transformed box to the widget's <c>/Rect</c> (ISO 32000-1 §12.5.5). That fit already scales and moves, so the matrix is
        /// written only when it does something the fit cannot: turn or skew the appearance (an off-diagonal term), or mirror it (a
        /// diagonal that is not positive).
        /// </summary>
        static void ApplyMatrix(PeachDrawing.PDF.Engine.Advanced.PdfFormXObject appearance, Matrix3x2? matrix)
        {
            // The tolerance absorbs the rounding the divided and converted corner coordinates leave in a plain move or scale.
            if (matrix is { } m && (System.Math.Abs(m.M12) > 1e-6 || System.Math.Abs(m.M21) > 1e-6 || m.M11 <= 1e-6 || m.M22 <= 1e-6))
                appearance.Elements.SetMatrix(PeachDrawing.PDF.Engine.Advanced.PdfFormXObject.Keys.Matrix, m);
        }

        static void SetNormalAppearance(PdfAnnotation annotation, PeachDrawing.PDF.Engine.Advanced.PdfFormXObject appearance, Matrix3x2? matrix = null)
        {
            ApplyMatrix(appearance, matrix);
            var ap = new PdfDictionary(annotation.Owner);
            ap.Elements.SetReference("/N", appearance);
            annotation.Elements.SetObject(PdfAnnotation.Keys.AP, ap);
        }

        static void SetTwoStateAppearance(PdfAnnotation annotation, string onValue,
            PeachDrawing.PDF.Engine.Advanced.PdfFormXObject onAppearance, PeachDrawing.PDF.Engine.Advanced.PdfFormXObject offAppearance,
            Matrix3x2? matrix = null)
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
