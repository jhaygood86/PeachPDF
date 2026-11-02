using PeachBrackets.Accessibility;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Html.Semantics
{
    internal sealed partial class AccessibilityTreeBuilder
    {
        // The words of a piece of text as they are read: the white space between them is kept as one space, and none is made where the source
        // had none (so "foo<b>bar</b>" is "foobar").
        private sealed class TextBuilder
        {
            private readonly StringBuilder _text = new();
            private CssRect? _last;
            private bool _breakPending;

            public void AddWord(CssRect word)
            {
                if (string.IsNullOrEmpty(word.Text))
                    return;

                if (_text.Length > 0 && (_breakPending || (_last?.HasSpaceAfter ?? false) || word.HasSpaceBefore))
                    _text.Append(' ');

                _text.Append(word.Text);
                _last = word;
                _breakPending = false;
            }

            public void AddText(string text)
            {
                if (text.Length == 0)
                    return;

                if (_text.Length > 0)
                    _text.Append(' ');

                _text.Append(text);
                _last = null;
                _breakPending = false;
            }

            // Between blocks, and at a line break, the words on either side are apart whatever the source had.
            public void Break() => _breakPending = true;

            public override string ToString() => Collapse(_text.ToString());
        }

        private static string OwnText(CssBox box)
        {
            var text = new TextBuilder();
            foreach (var word in box.Words)
            {
                if (word.IsLineBreak)
                    text.Break();
                else if (!word.IsImage)
                    text.AddWord(word);
            }

            return text.ToString();
        }

        /// <summary>The text under an element that is shown, as it is read.</summary>
        private string TextOf(CssBox box)
        {
            var text = new TextBuilder();
            AppendContent(box, text, null, new HashSet<CssBox>(ReferenceEqualityComparer.Instance), asName: false);
            return text.ToString();
        }

        // The content of an element, for a name or a text: the words in it and the alternatives of what it holds (an image's alt, a nested
        // element's aria-label), leaving out what is hidden and what is not shown.
        private void AppendContent(CssBox box, TextBuilder text, CssBox? skip, HashSet<CssBox> visiting, bool asName)
        {
            if (!visiting.Add(box))
                return;

            foreach (var word in box.Words)
            {
                if (word.IsLineBreak)
                    text.Break();
                else if (!word.IsImage && box.Visibility.Value == Visibility.Visible)
                    text.AddWord(word);
            }

            foreach (var child in box.Boxes)
            {
                if (child is CssProxyBox || ReferenceEquals(child, skip))
                    continue;

                if (child.HtmlTag is { } tag)
                {
                    if (IsTrue(tag, "aria-hidden") || tag.HasAttribute("inert") || child.DerivedStyle.ActualDisplay == Keywords.None)
                        continue;

                    if (asName && NonEmpty(tag.TryGetAttribute("aria-label")) is { } label)
                    {
                        text.AddText(label);
                        continue;
                    }

                    if (child is CssBoxImage || IsTag(tag, "img"))
                    {
                        if (NonEmpty(tag.TryGetAttribute("alt")) is { } alt)
                            text.AddText(alt);

                        continue;
                    }

                    if (IsTag(tag, "br"))
                    {
                        text.Break();
                        continue;
                    }
                }

                var block = !child.IsInline;
                if (block)
                    text.Break();

                AppendContent(child, text, skip, visiting, asName);
                if (block)
                    text.Break();
            }

            visiting.Remove(box);
        }

        // ---- names -------------------------------------------------------------------------------------------------------------------

        // The accessible name, by the order the Accessible Name and Description Computation gives its sources: aria-labelledby, aria-label,
        // what the element is natively labelled by, its content (for the roles that are named by it), and last its title.
        private string ComputeName(CssBox box, HtmlTag tag, AccessibilityRole role, out bool fromTitle)
        {
            fromTitle = false;

            if (NonEmpty(tag.TryGetAttribute("aria-labelledby")) is { } ids)
            {
                var referenced = new List<string>();
                foreach (var id in ids.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (_byElementId.TryGetValue(id, out var target) && Collapse(TextAlternative(target)) is { Length: > 0 } text)
                        referenced.Add(text);
                }

                if (referenced.Count > 0)
                    return string.Join(' ', referenced);
            }

            if (NonEmpty(tag.TryGetAttribute("aria-label")) is { } ariaLabel)
                return Collapse(ariaLabel);

            if (NativeName(box, tag) is { Length: > 0 } native)
                return native;

            if (RoleResolver.NamesFromContent(role))
            {
                var content = new TextBuilder();
                AppendContent(box, content, null, new HashSet<CssBox>(ReferenceEqualityComparer.Instance), asName: true);
                if (content.ToString() is { Length: > 0 } fromContent)
                    return fromContent;
            }

            if (NonEmpty(tag.TryGetAttribute("title")) is { } title)
            {
                fromTitle = true;
                return Collapse(title);
            }

            return string.Empty;
        }

        // The text alternative of an element somebody else points at (an aria-labelledby target): its own label, or its content.
        private string TextAlternative(CssBox target)
        {
            if (target.HtmlTag is { } tag)
            {
                if (NonEmpty(tag.TryGetAttribute("aria-label")) is { } label)
                    return label;

                if (NativeName(target, tag) is { Length: > 0 } native)
                    return native;
            }

            var text = new TextBuilder();
            AppendContent(target, text, null, new HashSet<CssBox>(ReferenceEqualityComparer.Instance), asName: true);
            return text.ToString();
        }

        // What HTML gives an element as its label: the alt of an image, the text of its label element, a fieldset's legend, a table's caption.
        private string? NativeName(CssBox box, HtmlTag tag)
        {
            switch (tag.Name.ToLowerInvariant())
            {
                case "img" or "area":
                    return NonEmpty(tag.TryGetAttribute("alt")) is { } alt ? Collapse(alt) : null;
                case "input":
                    var type = tag.TryGetAttribute("type")?.Trim().ToLowerInvariant() ?? "text";
                    if (type == "image" && NonEmpty(tag.TryGetAttribute("alt")) is { } imageAlt)
                        return Collapse(imageAlt);

                    if (type is "button" or "submit" or "reset")
                    {
                        if (LabelOf(box, tag) is { Length: > 0 } buttonLabel)
                            return buttonLabel;

                        if (NonEmpty(tag.TryGetAttribute("value")) is { } value)
                            return Collapse(value);

                        return type == "submit" ? "Submit" : type == "reset" ? "Reset" : null;
                    }

                    return LabelOf(box, tag) is { Length: > 0 } inputLabel ? inputLabel : NonEmpty(tag.TryGetAttribute("placeholder")) is { } p ? Collapse(p) : null;
                case "select" or "textarea" or "meter" or "progress" or "output" or "button":
                    return LabelOf(box, tag);
                case "fieldset":
                    return ChildNamed(box, "legend");
                case "table":
                    return ChildNamed(box, "caption");
                case "figure":
                    return ChildNamed(box, "figcaption");
                case "optgroup":
                    return NonEmpty(tag.TryGetAttribute("label")) is { } groupLabel ? Collapse(groupLabel) : null;
                case "option":
                    return NonEmpty(tag.TryGetAttribute("label")) is { } optionLabel ? Collapse(optionLabel) : null;
                case "math":
                    return NonEmpty(tag.TryGetAttribute("alttext")) is { } alttext ? Collapse(alttext) : null;
                default:
                    return null;
            }
        }

        // The text of the label elements of a control: those that name it with for=, and the one it is inside.
        private string? LabelOf(CssBox control, HtmlTag tag)
        {
            var parts = new List<string>();
            if (tag.TryGetAttribute("id") is { Length: > 0 } id && _labelsFor.TryGetValue(id, out var labels))
            {
                foreach (var label in labels)
                {
                    if (LabelText(label, control) is { Length: > 0 } text)
                        parts.Add(text);
                }
            }

            for (var ancestor = control.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (ancestor.HtmlTag is { } t && IsTag(t, "label") && !t.HasAttribute("for"))
                {
                    if (LabelText(ancestor, control) is { Length: > 0 } text)
                        parts.Add(text);

                    break;
                }
            }

            return parts.Count == 0 ? null : string.Join(' ', parts);
        }

        // A label's text without the control it labels (a control inside its label would otherwise name itself).
        private string LabelText(CssBox label, CssBox control)
        {
            var text = new TextBuilder();
            AppendContent(label, text, control, new HashSet<CssBox>(ReferenceEqualityComparer.Instance), asName: true);
            return text.ToString();
        }

        private string? ChildNamed(CssBox box, string name)
        {
            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag is { } tag && IsTag(tag, name))
                {
                    var text = new TextBuilder();
                    AppendContent(child, text, null, new HashSet<CssBox>(ReferenceEqualityComparer.Instance), asName: true);
                    return text.ToString();
                }
            }

            return null;
        }

        private string ComputeDescription(HtmlTag tag, bool nameFromTitle)
        {
            if (NonEmpty(tag.TryGetAttribute("aria-describedby")) is { } ids)
            {
                var referenced = new List<string>();
                foreach (var id in ids.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (_byElementId.TryGetValue(id, out var target) && Collapse(TextAlternative(target)) is { Length: > 0 } text)
                        referenced.Add(text);
                }

                if (referenced.Count > 0)
                    return string.Join(' ', referenced);
            }

            if (NonEmpty(tag.TryGetAttribute("aria-description")) is { } description)
                return Collapse(description);

            return !nameFromTitle && NonEmpty(tag.TryGetAttribute("title")) is { } title ? Collapse(title) : string.Empty;
        }

        // ---- states ------------------------------------------------------------------------------------------------------------------

        private static void ApplyStates(AccessibilityNode node, CssBox box, HtmlTag tag, AccessibilityRole role, in SemanticContext context)
        {
            var name = tag.Name.ToLowerInvariant();
            var nativelyDisableable = name is "button" or "input" or "select" or "textarea" or "option" or "optgroup" or "fieldset";
            node.Disabled = IsTrue(tag, "aria-disabled") || (nativelyDisableable && (tag.HasAttribute("disabled") || (context.FieldsetDisabled && name != "fieldset")));
            node.Required = IsTrue(tag, "aria-required") || tag.HasAttribute("required");
            node.ReadOnly = IsTrue(tag, "aria-readonly") || tag.HasAttribute("readonly");
            node.Invalid = NonEmpty(tag.TryGetAttribute("aria-invalid")) is { } invalid && !invalid.Equals("false", StringComparison.OrdinalIgnoreCase);
            node.Busy = IsTrue(tag, "aria-busy");
            node.Modal = IsTrue(tag, "aria-modal");
            node.Current = NonEmpty(tag.TryGetAttribute("aria-current")) is { } current && !current.Equals("false", StringComparison.OrdinalIgnoreCase) ? current.ToLowerInvariant() : null;

            if (role is AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio)
                node.Checked = Toggle(tag.TryGetAttribute("aria-checked"), tag.HasAttribute("checked"));

            if (NonEmpty(tag.TryGetAttribute("aria-pressed")) is { } pressed)
                node.Pressed = Toggle(pressed, false);

            if (NonEmpty(tag.TryGetAttribute("aria-expanded")) is { } expanded)
                node.Expanded = expanded.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (name == "details")
                node.Expanded = tag.HasAttribute("open");
            else if (name == "summary" && box.ParentBox?.HtmlTag is { } details && IsTag(details, "details"))
                node.Expanded = details.HasAttribute("open");

            if (NonEmpty(tag.TryGetAttribute("aria-selected")) is { } selected)
                node.Selected = selected.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (role == AccessibilityRole.Option)
                node.Selected = tag.HasAttribute("selected");

            var live = NonEmpty(tag.TryGetAttribute("aria-live"))?.ToLowerInvariant();
            node.Live = live switch
            {
                "polite" => LiveSetting.Polite,
                "assertive" => LiveSetting.Assertive,
                "off" => LiveSetting.Off,
                _ => role switch
                {
                    AccessibilityRole.Alert or AccessibilityRole.AlertDialog => LiveSetting.Assertive,
                    AccessibilityRole.Status or AccessibilityRole.Log => LiveSetting.Polite,
                    _ => LiveSetting.Off,
                },
            };
            node.Atomic = IsTrue(tag, "aria-atomic") || role is AccessibilityRole.Alert or AccessibilityRole.Status;
        }

        private static ToggleState Toggle(string? aria, bool native)
        {
            if (aria is not null)
            {
                return aria.Trim().ToLowerInvariant() switch
                {
                    "true" => ToggleState.True,
                    "mixed" => ToggleState.Mixed,
                    _ => ToggleState.False,
                };
            }

            return native ? ToggleState.True : ToggleState.False;
        }
    }
}
