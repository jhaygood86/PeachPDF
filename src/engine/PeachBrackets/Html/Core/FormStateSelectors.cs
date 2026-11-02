using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using System;

namespace PeachBrackets.Html
{
    /// <summary>
    /// The form-state pseudo-classes the document's own attributes decide - <c>:checked</c>, <c>:disabled</c>, <c>:enabled</c>, <c>:required</c>,
    /// <c>:optional</c>, <c>:read-only</c> and <c>:read-write</c> - answered from the element and its ancestors as the HTML standard defines them
    /// (<see href="https://html.spec.whatwg.org/multipage/semantics-other.html#pseudo-classes">HTML §4.16.3</see>). A control's state is an attribute
    /// (<c>checked</c>, <c>disabled</c>...), which a host changes through <see cref="HtmlDocument.SetChecked"/> and the like, so these answer the
    /// state the document is in when it is styled.
    /// </summary>
    /// <remarks>
    /// Not here: the ones that need a constraint-validation model (<c>:valid</c>, <c>:invalid</c>, <c>:in-range</c>, <c>:out-of-range</c>), the ones
    /// that need the form's own structure or live user action (<c>:default</c>, <c>:indeterminate</c>, <c>:user-valid</c>, <c>:autofill</c>) - they
    /// still parse and select nothing, as before.
    /// </remarks>
    internal static class FormStateSelectors
    {
        /// <summary>Whether <paramref name="name"/> is a pseudo-class this answers.</summary>
        public static bool Handles(string name) =>
            name == PseudoClassNames.Checked || name == PseudoClassNames.Disabled || name == PseudoClassNames.Enabled || name == PseudoClassNames.Required
            || name == PseudoClassNames.Optional || name == PseudoClassNames.ReadOnly || name == PseudoClassNames.ReadWrite;

        /// <summary>Whether the pseudo-class <paramref name="name"/> matches <paramref name="box"/>.</summary>
        public static bool Matches(string name, CssBox box)
        {
            if (box.HtmlTag is not { } tag)
                return false;

            var element = tag.Name.ToLowerInvariant();
            if (name == PseudoClassNames.Checked)
                return IsChecked(tag, element);
            if (name == PseudoClassNames.Disabled)
                return CanBeDisabled(element) && IsDisabled(box, tag, element);
            if (name == PseudoClassNames.Enabled)
                return CanBeDisabled(element) && !IsDisabled(box, tag, element);
            if (name == PseudoClassNames.Required)
                return CanBeRequired(tag, element) && tag.HasAttribute("required");
            if (name == PseudoClassNames.Optional)
                return CanBeRequired(tag, element) && !tag.HasAttribute("required");
            if (name == PseudoClassNames.ReadWrite)
                return IsReadWrite(box, tag, element);
            if (name == PseudoClassNames.ReadOnly)
                return !IsReadWrite(box, tag, element);

            return false;
        }

        private static string TypeOf(HtmlTag tag) => (tag.TryGetAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();

        // A checkbox or radio button that is checked, and an option that is selected.
        private static bool IsChecked(HtmlTag tag, string element) => element switch
        {
            "input" => TypeOf(tag) is "checkbox" or "radio" && tag.HasAttribute("checked"),
            "option" => tag.HasAttribute("selected"),
            _ => false,
        };

        private static bool CanBeDisabled(string element) =>
            element is "button" or "input" or "select" or "textarea" or "fieldset" or "optgroup" or "option";

        // Disabled by its own attribute, by a disabled fieldset around it (except inside that fieldset's first legend), or, for an option, by its optgroup.
        private static bool IsDisabled(CssBox box, HtmlTag tag, string element)
        {
            if (tag.HasAttribute("disabled"))
                return true;

            if (element == "option" && box.ParentBox?.HtmlTag is { } group && group.Name.Equals("optgroup", StringComparison.OrdinalIgnoreCase) && group.HasAttribute("disabled"))
                return true;

            CssBox? legendOnPath = null;
            for (var parent = box.ParentBox; parent is not null; parent = parent.ParentBox)
            {
                if (parent.HtmlTag is { } parentTag && parentTag.Name.Equals("legend", StringComparison.OrdinalIgnoreCase))
                    legendOnPath = parent;

                if (parent.HtmlTag is not { } fieldsetTag || !fieldsetTag.Name.Equals("fieldset", StringComparison.OrdinalIgnoreCase) || !fieldsetTag.HasAttribute("disabled"))
                    continue;

                if (legendOnPath is null || !ReferenceEquals(FirstLegendOf(parent), legendOnPath))
                    return true;
            }

            return false;
        }

        // The first legend child of a fieldset exempts what is inside it from the fieldset being disabled. The box tree can wrap a legend in an
        // anonymous box, so this looks through boxes that are not elements.
        private static CssBox? FirstLegendOf(CssBox fieldset)
        {
            foreach (var child in fieldset.Boxes)
            {
                if (child.HtmlTag is { } t)
                {
                    if (t.Name.Equals("legend", StringComparison.OrdinalIgnoreCase))
                        return child;
                }
                else if (FirstLegendOf(child) is { } inner)
                {
                    return inner;
                }
            }

            return null;
        }

        // The controls the required attribute applies to: not those whose type has no value to require.
        private static bool CanBeRequired(HtmlTag tag, string element) => element switch
        {
            "select" or "textarea" => true,
            "input" => TypeOf(tag) is not ("hidden" or "range" or "color" or "button" or "submit" or "reset" or "image"),
            _ => false,
        };

        // Text-like inputs and textareas the user can change, and anything made editable; everything else is read-only.
        private static bool IsReadWrite(CssBox box, HtmlTag tag, string element)
        {
            if (element == "textarea")
                return !tag.HasAttribute("readonly") && !IsDisabled(box, tag, element);

            if (element == "input")
            {
                var type = TypeOf(tag);
                var editable = type is "" or "text" or "search" or "url" or "tel" or "email" or "password" or "number" or "date" or "datetime-local" or "month" or "week" or "time";
                return editable && !tag.HasAttribute("readonly") && !IsDisabled(box, tag, element);
            }

            return tag.TryGetAttribute("contenteditable") is { } editableValue && !editableValue.Equals("false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
