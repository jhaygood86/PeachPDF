using System;
using System.Collections.Generic;

namespace PeachBrackets.Annotations
{
    /// <summary>The kinds of form control a document's elements classify as.</summary>
    public enum FormFieldKind
    {
        /// <summary>The element is not a form control, or opted out of being one.</summary>
        None,

        /// <summary>A single-line text control.</summary>
        Text,

        /// <summary>A check box.</summary>
        Checkbox,

        /// <summary>One button of a radio group; the buttons sharing a name are one field.</summary>
        Radio,

        /// <summary>A drop-down list of options.</summary>
        Select
    }

    /// <summary>
    /// One &lt;option&gt; of a classified &lt;select&gt; field.
    /// </summary>
    /// <param name="value">the value the field takes when the option is chosen</param>
    /// <param name="label">the text shown for the option</param>
    /// <param name="selected">whether the option is the one chosen initially</param>
    public readonly struct FormFieldOption(string value, string label, bool selected)
    {
        /// <summary>The value the field takes when the option is chosen.</summary>
        public string Value { get; } = value;

        /// <summary>The text shown for the option.</summary>
        public string Label { get; } = label;

        /// <summary>Whether the option is the one chosen initially.</summary>
        public bool Selected { get; } = selected;
    }

    /// <summary>
    /// The HTML form-control attributes that describe how a field behaves rather than what kind it is or what it holds:
    /// <c>readonly</c>/<c>disabled</c>/<c>required</c>, which any kind of control can carry, plus <c>maxlength</c>,
    /// <c>type=password</c> and <c>placeholder</c>, which only a text control can.
    /// </summary>
    /// <param name="ReadOnly">The <c>readonly</c> attribute, or <c>disabled</c> - a disabled control is not editable either.</param>
    /// <param name="NoExport">The <c>disabled</c> attribute: HTML excludes a disabled control from form submission.</param>
    /// <param name="Required">The <c>required</c> attribute.</param>
    /// <param name="Password"><c>type=password</c> - the value must be echoed unreadably rather than shown.</param>
    /// <param name="MaxLength">The <c>maxlength</c> attribute as a positive character count, or null when absent or not a positive integer.</param>
    /// <param name="Placeholder">The <c>placeholder</c> attribute, or null when absent or empty.</param>
    public readonly record struct FormFieldAttributes(
        bool ReadOnly, bool NoExport, bool Required, bool Password, int? MaxLength, string? Placeholder)
    {
        /// <summary>An element carrying none of them - every field's starting point, and what a non-field classifies to.</summary>
        public static FormFieldAttributes None => default;
    }

    /// <summary>
    /// What a form control is: its kind, name and value, and the attributes that shape how it behaves.
    /// </summary>
    public readonly struct FormFieldClassification
    {
        /// <summary>The kind of control.</summary>
        public FormFieldKind Kind { get; }

        /// <summary>The field's <c>name</c> attribute, or null if absent.</summary>
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
        /// Whether the standard HTML <c>placeholder</c> is shown for this empty control, so a consumer that makes the control
        /// interactive knows whether to draw the placeholder or the value. Only meaningful for <see cref="FormFieldKind.Text"/>.
        /// </summary>
        public bool PlaceholderShown { get; }

        /// <summary>
        /// The HTML attributes that shape the field's behavior. The readonly/required/no-export three apply to every kind; the rest
        /// are text-field only.
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

        /// <summary>The classification of an element that is not a form control.</summary>
        public static readonly FormFieldClassification None =
            new(FormFieldKind.None, null, null, false, Array.Empty<FormFieldOption>(), false, null, false, false, FormFieldAttributes.None);

        internal static FormFieldClassification Text(string? name, string? value, bool autoFontSize, int? comb, bool doNotScroll, bool placeholderShown, FormFieldAttributes attributes) =>
            new(FormFieldKind.Text, name, value, false, Array.Empty<FormFieldOption>(), autoFontSize, comb, doNotScroll, placeholderShown, attributes);

        internal static FormFieldClassification Checkbox(string? name, string? value, bool @checked, FormFieldAttributes attributes) =>
            new(FormFieldKind.Checkbox, name, value, @checked, Array.Empty<FormFieldOption>(), false, null, false, false, attributes);

        internal static FormFieldClassification Radio(string? name, string? value, bool @checked, FormFieldAttributes attributes) =>
            new(FormFieldKind.Radio, name, value, @checked, Array.Empty<FormFieldOption>(), false, null, false, false, attributes);

        internal static FormFieldClassification Select(string? name, IReadOnlyList<FormFieldOption> options, FormFieldAttributes attributes) =>
            new(FormFieldKind.Select, name, null, false, options, false, null, false, false, attributes);
    }
}
