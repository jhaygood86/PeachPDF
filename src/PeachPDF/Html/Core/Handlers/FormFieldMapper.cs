using System;
using System.Collections.Generic;
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

        FormFieldClassification(FormFieldKind kind, string? name, string? value, bool @checked,
            IReadOnlyList<FormFieldOption> options, bool autoFontSize, int? comb, bool doNotScroll)
        {
            Kind = kind;
            Name = name;
            Value = value;
            Checked = @checked;
            Options = options;
            AutoFontSize = autoFontSize;
            Comb = comb;
            DoNotScroll = doNotScroll;
        }

        public static readonly FormFieldClassification None =
            new(FormFieldKind.None, null, null, false, Array.Empty<FormFieldOption>(), false, null, false);

        public static FormFieldClassification Text(string? name, string? value, bool autoFontSize, int? comb, bool doNotScroll) =>
            new(FormFieldKind.Text, name, value, false, Array.Empty<FormFieldOption>(), autoFontSize, comb, doNotScroll);

        public static FormFieldClassification Checkbox(string? name, string? value, bool @checked) =>
            new(FormFieldKind.Checkbox, name, value, @checked, Array.Empty<FormFieldOption>(), false, null, false);

        public static FormFieldClassification Radio(string? name, string? value, bool @checked) =>
            new(FormFieldKind.Radio, name, value, @checked, Array.Empty<FormFieldOption>(), false, null, false);

        public static FormFieldClassification Select(string? name, IReadOnlyList<FormFieldOption> options) =>
            new(FormFieldKind.Select, name, null, false, options, false, null, false);
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

        static FormFieldClassification ClassifyText(CssBox box)
        {
            var name = box.GetAttribute("name", null);
            var value = box.GetAttribute("value", null);
            var autoFontSize = string.Equals(box.PdfFormFieldAutoFontSize, Keywords.Auto, StringComparison.OrdinalIgnoreCase);
            var doNotScroll = string.Equals(box.PdfFormFieldDoNotScroll, Keywords.Auto, StringComparison.OrdinalIgnoreCase);
            int? comb = int.TryParse(box.PdfFormFieldComb, out var cells) && cells > 0 ? cells : null;
            return FormFieldClassification.Text(name, value, autoFontSize, comb, doNotScroll);
        }

        static FormFieldClassification ClassifyCheckbox(CssBox box)
        {
            var name = box.GetAttribute("name", null);
            var value = box.GetAttribute("value", "Yes");
            var isChecked = box.HtmlTag!.HasAttribute("checked");
            return FormFieldClassification.Checkbox(name, value, isChecked);
        }

        static FormFieldClassification ClassifyRadio(CssBox box)
        {
            var name = box.GetAttribute("name", null);
            var value = box.GetAttribute("value", "Yes");
            var isChecked = box.HtmlTag!.HasAttribute("checked");
            return FormFieldClassification.Radio(name, value, isChecked);
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

            return FormFieldClassification.Select(name, options);
        }
    }
}
