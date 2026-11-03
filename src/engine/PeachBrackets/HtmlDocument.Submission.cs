using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Paint.Content;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    // The files chosen for each file input, by the input's place in the source: what a submission sends. Names alone are also kept in an attribute so the
    // control can show them (see SetFileNames).
    private readonly Dictionary<int, IReadOnlyList<ChosenFile>> _chosenFiles = [];

    /// <summary>
    /// Records the files a host's picker returned for a file input - their names, kinds and bytes - so the control shows their names and a submission of
    /// its form (<see cref="CreateFormSubmission"/>) sends them. Not applied to a control that is not a file input or is disabled, or when several files
    /// are given to one that does not take <c>multiple</c>.
    /// </summary>
    /// <param name="element">the file input</param>
    /// <param name="files">the chosen files; empty to clear</param>
    /// <returns>whether it was applied and the regions that changed</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetFiles(DocumentElement element, IReadOnlyList<ChosenFile> files)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(files);
        var result = SetFileNames(element, [.. files.Select(f => f.Name)]);
        if (result.Applied && element.Box.HtmlTag is { } tag)
        {
            var chosen = files.Where(f => f.Name.Trim().Length > 0).ToList();
            if (chosen.Count == 0)
                _chosenFiles.Remove(tag.SourceOrdinal);
            else
                _chosenFiles[tag.SourceOrdinal] = chosen;
        }

        return result;
    }

    /// <summary>
    /// What submitting the form a button belongs to sends: the method, address and encoding (the button's <c>formmethod</c>, <c>formaction</c> and
    /// <c>formenctype</c> replace the form's own), and the name and value of every control that takes part - those with a name that are not disabled, a
    /// check box or radio button only when it is checked, a drop-down's chosen options, a file input's files, and the button that was used.
    /// </summary>
    /// <param name="submitter">the submit button: an <c>&lt;input type=submit&gt;</c> or <c>image</c>, or a <c>&lt;button&gt;</c> that is not <c>type=button</c> or <c>reset</c></param>
    /// <returns>the submission, or null when the element is not a submit button or is in no form</returns>
    /// <exception cref="ArgumentNullException"><paramref name="submitter"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public FormSubmission? CreateFormSubmission(DocumentElement submitter)
    {
        ArgumentNullException.ThrowIfNull(submitter);
        _ = Pages;
        var ordinal = submitter.Box.HtmlTag?.SourceOrdinal ?? -1;
        if (ordinal < 0 || FindByOrdinal(_container.Root, ordinal) is not { HtmlTag: { } button } box || !IsSubmitButton(box) || button.HasAttribute("disabled"))
            return null;

        var form = FormOwnerOf(box);
        if (form?.HtmlTag is not { } formTag)
            return null;

        var entries = new List<FormEntry>();
        void Visit(CssBox current)
        {
            foreach (var child in current.Boxes)
            {
                if (child.HtmlTag is { } tag && ReferenceEquals(FormOwnerOf(child), form) && !IsDisabledByAncestor(child))
                    AddEntries(child, tag, box, entries);

                Visit(child);
            }
        }

        if (_container.Root is { } root)
            Visit(root);

        static string Pick(string? preferred, string? own, string fallback) => !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() : !string.IsNullOrWhiteSpace(own) ? own.Trim() : fallback;
        var method = Pick(button.TryGetAttribute("formmethod"), formTag.TryGetAttribute("method"), "get").ToLowerInvariant();
        method = method is "post" or "dialog" ? method : "get";
        var encType = Pick(button.TryGetAttribute("formenctype"), formTag.TryGetAttribute("enctype"), "application/x-www-form-urlencoded").ToLowerInvariant();
        encType = encType is "multipart/form-data" or "text/plain" ? encType : "application/x-www-form-urlencoded";
        var action = Pick(button.TryGetAttribute("formaction"), formTag.TryGetAttribute("action"), string.Empty);
        return new FormSubmission(method, action.Length == 0 ? string.Empty : Html.Annotations.DocumentAnnotationCollector.ResolveHref(_container, action), encType, entries);
    }

    private static bool IsSubmitButton(CssBox box)
    {
        var tag = box.HtmlTag!;
        var type = tag.TryGetAttribute("type")?.Trim().ToLowerInvariant();
        if (tag.Name.Equals("button", StringComparison.OrdinalIgnoreCase))
            return type is null or "" or "submit";

        return tag.Name.Equals("input", StringComparison.OrdinalIgnoreCase) && type is "submit" or "image";
    }

    // The form a control belongs to: the one its form attribute names, else the form around it.
    private CssBox? FormOwnerOf(CssBox box)
    {
        if (box.HtmlTag is not { } tag || !IsFormControl(tag.Name))
            return null;

        if (tag.TryGetAttribute("form") is { Length: > 0 } id)
            return FindFormById(_container.Root, id);

        for (var parent = box.ParentBox; parent is not null; parent = parent.ParentBox)
        {
            if (parent.HtmlTag is { } p && p.Name.Equals("form", StringComparison.OrdinalIgnoreCase))
                return parent;
        }

        return null;
    }

    private static bool IsFormControl(string name) =>
        name.Equals("input", StringComparison.OrdinalIgnoreCase) || name.Equals("select", StringComparison.OrdinalIgnoreCase)
        || name.Equals("textarea", StringComparison.OrdinalIgnoreCase) || name.Equals("button", StringComparison.OrdinalIgnoreCase);

    private static CssBox? FindFormById(CssBox? box, string id)
    {
        if (box is null)
            return null;

        if (box.HtmlTag is { } tag && tag.Name.Equals("form", StringComparison.OrdinalIgnoreCase) && tag.TryGetAttribute("id") == id)
            return box;

        foreach (var child in box.Boxes)
        {
            if (FindFormById(child, id) is { } found)
                return found;
        }

        return null;
    }

    private static bool IsDisabledByAncestor(CssBox box)
    {
        for (var parent = box.ParentBox; parent is not null; parent = parent.ParentBox)
        {
            if (parent.HtmlTag is { } tag && tag.Name.Equals("fieldset", StringComparison.OrdinalIgnoreCase) && tag.HasAttribute("disabled"))
                return true;
        }

        return false;
    }

    private void AddEntries(CssBox box, HtmlTag tag, CssBox submitter, List<FormEntry> entries)
    {
        var name = tag.TryGetAttribute("name");
        if (string.IsNullOrEmpty(name) || tag.HasAttribute("disabled") || !IsFormControl(tag.Name))
            return;

        var type = tag.Name.ToLowerInvariant() switch
        {
            "input" => (tag.TryGetAttribute("type") ?? "text").Trim().ToLowerInvariant(),
            "button" => (tag.TryGetAttribute("type") ?? "submit").Trim().ToLowerInvariant() is "button" or "reset" ? "button" : "submit",
            var other => other,
        };
        switch (type)
        {
            case "submit" or "image" or "button" or "reset":
                // Only the button that was used is sent.
                if (ReferenceEquals(box, submitter) && type is "submit" or "image")
                    entries.Add(new FormEntry(name, Crlf(tag.TryGetAttribute("value") ?? string.Empty), null));
                break;

            case "checkbox" or "radio":
                if (tag.HasAttribute("checked"))
                    entries.Add(new FormEntry(name, Crlf(tag.TryGetAttribute("value") ?? "on"), null));
                break;

            case "file":
                if (_chosenFiles.TryGetValue(tag.SourceOrdinal, out var files) && files.Count > 0)
                    entries.AddRange(files.Select(f => new FormEntry(name, null, f)));
                else
                    entries.Add(new FormEntry(name, null, new ChosenFile(string.Empty, "application/octet-stream", [])));
                break;

            case "select":
                var options = OptionsOf(box).Where(o => !o.HtmlTag!.HasAttribute("disabled")).ToList();
                var chosen = options.Where(o => o.HtmlTag!.HasAttribute("selected")).ToList();
                if (chosen.Count == 0 && !tag.HasAttribute("multiple") && options.Count > 0)
                    chosen = [options[0]];

                foreach (var option in chosen)
                    entries.Add(new FormEntry(name, Crlf(option.HtmlTag!.TryGetAttribute("value") ?? OptionText(option)), null));
                break;

            case "textarea":
                entries.Add(new FormEntry(name, Crlf(box.GetAttribute("value", string.Empty)), null));
                break;

            default:
                entries.Add(new FormEntry(name, Crlf(box.GetAttribute("value", string.Empty)), null));
                break;
        }
    }

    // A line break in a submitted value is a carriage return and a line feed, whichever it was written with.
    private static string Crlf(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\r\n", StringComparison.Ordinal);
}
