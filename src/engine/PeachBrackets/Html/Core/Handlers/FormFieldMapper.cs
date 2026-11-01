using PeachBrackets.Annotations;
using System;
using System.Collections.Generic;
using System.Globalization;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Handlers
{
    /// <summary>
    /// Which AcroForm field kind (if any) a box resolves to. Textarea and push-button fields are not
    /// classified this pass - see docs/html-css-support.md's Interactive PDF Forms section.
    /// </summary>

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
