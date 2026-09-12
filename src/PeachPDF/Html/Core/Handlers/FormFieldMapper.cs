using System;
using System.Collections.Generic;
using System.Globalization;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Which AcroForm field kind (if any) a box resolves to. Textarea and push-button fields are not
    /// classified this pass - see docs/html-css-support.md's Interactive PDF Forms section.
    /// </summary>
    internal enum FormFieldKind
    {
        None,
        Text,
        Checkbox,
        Radio,
        Select
    }

    /// <summary>
    /// One &lt;option&gt; of a classified &lt;select&gt; field.
    /// </summary>
    internal readonly struct FormFieldOption(string value, string label, bool selected)
    {
        public string Value { get; } = value;
        public string Label { get; } = label;
        public bool Selected { get; } = selected;
    }

    /// <summary>
    /// The HTML form-control attributes that map to PDF field entries rather than to a field's kind
    /// or value: <c>readonly</c>/<c>disabled</c>/<c>required</c> (ISO 32000-1 Table 221's bits 1-3,
    /// shared by every field type), plus <c>maxlength</c>, <c>type=password</c> and
    /// <c>placeholder</c>, which only a text field can carry.
    /// </summary>
    /// <param name="ReadOnly">The <c>readonly</c> attribute, or <c>disabled</c> - a disabled control is not editable either.</param>
    /// <param name="NoExport">The <c>disabled</c> attribute: HTML excludes a disabled control from form submission, which is what "/Ff" NoExport says in PDF.</param>
    /// <param name="Required">The <c>required</c> attribute.</param>
    /// <param name="Password"><c>type=password</c> - the reader must echo the value unreadably rather than show it.</param>
    /// <param name="MaxLength">The <c>maxlength</c> attribute as a positive character count, or null when absent or not a positive integer.</param>
    /// <param name="Placeholder">The <c>placeholder</c> attribute, or null when absent or empty.</param>
    internal readonly record struct FormFieldAttributes(
        bool ReadOnly, bool NoExport, bool Required, bool Password, int? MaxLength, string? Placeholder)
    {
        /// <summary>An element carrying none of them - every field's starting point, and what a non-field classifies to.</summary>
        public static FormFieldAttributes None => default;
    }

    /// <summary>
    /// The result of classifying a box for interactive-PDF-forms output.
    /// </summary>
    internal readonly struct FormFieldClassification
    {
        public FormFieldKind Kind { get; }

        /// <summary>The field's <c>name</c> attribute (its <c>/T</c> partial field name), or null if absent.</summary>
        public string? Name { get; }

        /// <summary>The field's current text value (text fields) or export value (checkbox/radio). Null for Select.</summary>
        public string? Value { get; }

        /// <summary>Whether a checkbox/radio's <c>checked</c> attribute is present. Always false for other kinds.</summary>
        public bool Checked { get; }

        /// <summary>A select field's &lt;option&gt; children, in document order. Empty for other kinds.</summary>
        public IReadOnlyList<FormFieldOption> Options { get; }

        /// <summary>
        /// <c>-peachpdf-pdf-form-field-auto-font-size</c>'s resolved value - only meaningful for
        /// <see cref="FormFieldKind.Text"/>.
        /// </summary>
        public bool AutoFontSize { get; }

        /// <summary>
        /// <c>-peachpdf-pdf-form-field-comb</c>'s resolved cell count, or null when unset - only
        /// meaningful for <see cref="FormFieldKind.Text"/>.
        /// </summary>
        public int? Comb { get; }

        /// <summary>
        /// <c>-peachpdf-pdf-form-field-do-not-scroll</c>'s resolved value - only meaningful for
        /// <see cref="FormFieldKind.Text"/>.
        /// </summary>
        public bool DoNotScroll { get; }

        /// <summary>
        /// Whether the standard HTML <c>placeholder</c> is shown for this empty control at PDF-
        /// generation time. It also becomes the field's <c>/TU</c> tooltip. Only meaningful for
        /// <see cref="FormFieldKind.Text"/>.
        /// </summary>
        public bool PlaceholderShown { get; }

        /// <summary>
        /// The HTML attributes that become PDF field entries rather than a kind or a value. The
        /// readonly/required/no-export three apply to every kind; the rest are text-field only.
        /// </summary>
        public FormFieldAttributes Attributes { get; }

        FormFieldClassification(FormFieldKind kind, string? name, string? value, bool @checked,
            IReadOnlyList<FormFieldOption> options, bool autoFontSize, int? comb, bool doNotScroll,
            bool placeholderShown, FormFieldAttributes attributes)
        {
            Kind = kind;
            Name = name;
            Value = value;
            Checked = @checked;
            Options = options;
            AutoFontSize = autoFontSize;
            Comb = comb;
            DoNotScroll = doNotScroll;
            PlaceholderShown = placeholderShown;
            Attributes = attributes;
        }

        public static readonly FormFieldClassification None =
            new(FormFieldKind.None, null, null, false, Array.Empty<FormFieldOption>(), false, null, false, false, FormFieldAttributes.None);

        public static FormFieldClassification Text(string? name, string? value, bool autoFontSize, int? comb, bool doNotScroll, bool placeholderShown, FormFieldAttributes attributes) =>
            new(FormFieldKind.Text, name, value, false, Array.Empty<FormFieldOption>(), autoFontSize, comb, doNotScroll, placeholderShown, attributes);

        public static FormFieldClassification Checkbox(string? name, string? value, bool @checked, FormFieldAttributes attributes) =>
            new(FormFieldKind.Checkbox, name, value, @checked, Array.Empty<FormFieldOption>(), false, null, false, false, attributes);

        public static FormFieldClassification Radio(string? name, string? value, bool @checked, FormFieldAttributes attributes) =>
            new(FormFieldKind.Radio, name, value, @checked, Array.Empty<FormFieldOption>(), false, null, false, false, attributes);

        public static FormFieldClassification Select(string? name, IReadOnlyList<FormFieldOption> options, FormFieldAttributes attributes) =>
            new(FormFieldKind.Select, name, null, false, options, false, null, false, false, attributes);
    }

    /// <summary>
    /// Classifies a <see cref="CssBox"/> for interactive-PDF-forms output. Mirrors
    /// <see cref="StructureTagMapper"/>'s shape: a pure function of the box's own resolved style and
    /// HTML attributes, no PDF objects, no side effects. Only a real <c>&lt;input&gt;</c>/
    /// <c>&lt;select&gt;</c> element (a <see cref="CssBoxFormField"/>) is ever eligible - a
    /// <c>-peachpdf-pdf-form-field</c> declaration on any other element (including a CSS-generated
    /// anonymous box) is simply inert, which is this feature's own deliberate scope boundary, not an
    /// accepted gap.
    /// </summary>
    internal static class FormFieldMapper
    {
        /// <summary>
        /// Whether an input is showing its standard HTML placeholder at generation time: the
        /// attribute is present, the value is empty, and the input state supports placeholders.
        /// This is shared by form appearance generation and <c>:placeholder-shown</c> matching so
        /// the two cannot disagree. The state is necessarily static in a PDF; AcroForm has no live
        /// CSS-selector equivalent after the user edits a field.
        /// </summary>
        internal static bool IsPlaceholderShown(CssBox box)
        {
            if (box is not CssBoxFormField || box.HtmlTag is not { } tag
                || !tag.Name.Equals(HtmlConstants.Input, StringComparison.OrdinalIgnoreCase)
                || !tag.HasAttribute("placeholder")
                || !string.IsNullOrEmpty(box.GetAttribute("value", null)))
            {
                return false;
            }

            var type = box.GetAttribute("type", Keywords.Text).ToLowerInvariant();
            return type switch
            {
                Keywords.Text or "search" or "tel" or "url" or "email" or "password" or "number" => true,
                "hidden" or "date" or "month" or "week" or "time" or "datetime-local"
                    or "range" or "color" or "checkbox" or "radio" or "file"
                    or "submit" or "image" or "reset" or "button" => false,
                // HTML's missing and invalid value defaults are both the Text state.
                _ => true
            };
        }

        public static FormFieldClassification Classify(CssBox box)
        {
            if (box is not CssBoxFormField || box.HtmlTag is null)
                return FormFieldClassification.None;

            var declared = box.PdfFormField;

            if (string.Equals(declared, Keywords.None, StringComparison.OrdinalIgnoreCase))
                return FormFieldClassification.None;

            if (string.IsNullOrEmpty(declared) || string.Equals(declared, Keywords.Auto, StringComparison.OrdinalIgnoreCase))
                return ClassifyAuto(box);

            return declared switch
            {
                _ when string.Equals(declared, Keywords.Text, StringComparison.OrdinalIgnoreCase) => ClassifyText(box),
                _ when string.Equals(declared, Keywords.Checkbox, StringComparison.OrdinalIgnoreCase) => ClassifyCheckbox(box),
                _ when string.Equals(declared, Keywords.Radio, StringComparison.OrdinalIgnoreCase) => ClassifyRadio(box),
                _ when string.Equals(declared, Keywords.Select, StringComparison.OrdinalIgnoreCase) => ClassifySelect(box),
                // Defensive only: the property's Converter already rejects anything outside the fixed
                // keyword set at CSS parse time, so a genuinely unrecognized value can't reach here.
                _ => ClassifyAuto(box)
            };
        }

        static FormFieldClassification ClassifyAuto(CssBox box)
        {
            var tag = box.HtmlTag!.Name;

            if (string.Equals(tag, HtmlConstants.Select, StringComparison.OrdinalIgnoreCase))
                return ClassifySelect(box);

            if (!string.Equals(tag, HtmlConstants.Input, StringComparison.OrdinalIgnoreCase))
                return FormFieldClassification.None;

            var type = box.GetAttribute("type", Keywords.Text).ToLowerInvariant();
            return type switch
            {
                Keywords.Checkbox => ClassifyCheckbox(box),
                Keywords.Radio => ClassifyRadio(box),
                // Not text-like - hidden/submit/reset/button/image/file inputs are out of scope this pass.
                "hidden" or "submit" or "reset" or "button" or "image" or "file" => FormFieldClassification.None,
                // text/email/password/number/tel/url/search/date/... all collapse to the closest
                // equivalent PeachPDF supports, same as the Prince alias's own field-type keywords do.
                _ => ClassifyText(box)
            };
        }

        /// <summary>
        /// Reads the HTML attributes that become PDF field entries rather than a kind or a value.
        /// <c>disabled</c> implies read-only as well as no-export: HTML defines a disabled control as
        /// neither editable nor submitted, and PDF spells those as two separate flag bits.
        /// <c>type=password</c> is read here off the element rather than from the classification,
        /// since a <c>-peachpdf-pdf-form-field: text</c> declaration can force a text field onto an
        /// element whose own type would not have produced one.
        /// </summary>
        static FormFieldAttributes ReadAttributes(CssBox box)
        {
            var tag = box.HtmlTag!;
            var disabled = tag.HasAttribute("disabled");
            var password = string.Equals(box.GetAttribute("type", null), "password", StringComparison.OrdinalIgnoreCase);

            // Per the HTML Standard, maxlength is a valid non-negative integer - ASCII digits only,
            // so NumberStyles.None and the invariant culture rather than the defaults, which would
            // accept a sign, surrounding whitespace, and a culture's own sign symbol. Anything else
            // (and a zero, which would forbid every character) is treated as absent, not clamped.
            int? maxLength = int.TryParse(box.GetAttribute("maxlength", null), NumberStyles.None,
                CultureInfo.InvariantCulture, out var max) && max > 0 ? max : null;

            var placeholder = box.GetAttribute("placeholder", null);

            return new FormFieldAttributes(
                ReadOnly: tag.HasAttribute("readonly") || disabled,
                NoExport: disabled,
                Required: tag.HasAttribute("required"),
                Password: password,
                MaxLength: maxLength,
                Placeholder: string.IsNullOrEmpty(placeholder) ? null : placeholder);
        }

        static FormFieldClassification ClassifyText(CssBox box)
        {
            var name = box.GetAttribute("name", null);
            var value = box.GetAttribute("value", null);
            var autoFontSize = string.Equals(box.PdfFormFieldAutoFontSize, Keywords.Auto, StringComparison.OrdinalIgnoreCase);
            var doNotScroll = string.Equals(box.PdfFormFieldDoNotScroll, Keywords.Auto, StringComparison.OrdinalIgnoreCase);
            int? comb = int.TryParse(box.PdfFormFieldComb, out var cells) && cells > 0 ? cells : null;
            return FormFieldClassification.Text(name, value, autoFontSize, comb, doNotScroll,
                IsPlaceholderShown(box), ReadAttributes(box));
        }

        static FormFieldClassification ClassifyCheckbox(CssBox box)
        {
            var name = box.GetAttribute("name", null);
            var value = box.GetAttribute("value", "Yes");
            var isChecked = box.HtmlTag!.HasAttribute("checked");
            return FormFieldClassification.Checkbox(name, value, isChecked, ReadAttributes(box));
        }

        static FormFieldClassification ClassifyRadio(CssBox box)
        {
            var name = box.GetAttribute("name", null);
            var value = box.GetAttribute("value", "Yes");
            var isChecked = box.HtmlTag!.HasAttribute("checked");
            return FormFieldClassification.Radio(name, value, isChecked, ReadAttributes(box));
        }

        static FormFieldClassification ClassifySelect(CssBox box)
        {
            var name = box.GetAttribute("name", null);
            var options = new List<FormFieldOption>();

            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag is null || !string.Equals(child.HtmlTag.Name, HtmlConstants.Option, StringComparison.OrdinalIgnoreCase))
                    continue;

                var label = CssContentEngine.ExtractText(child) ?? string.Empty;
                var value = child.GetAttribute("value", null) ?? label;
                var selected = child.HtmlTag.HasAttribute("selected");
                options.Add(new FormFieldOption(value, label, selected));
            }

            return FormFieldClassification.Select(name, options, ReadAttributes(box));
        }
    }
}
