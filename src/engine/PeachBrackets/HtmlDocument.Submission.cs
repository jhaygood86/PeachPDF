using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Paint.Content;
using PeachBrackets.Html.Updates;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    // The files chosen for each file input, by the input's place in the source: what a submission sends. Names alone are also kept in an attribute so the
    // control can show them (see SetFileNames).
    private readonly Dictionary<int, IReadOnlyList<ChosenFile>> _chosenFiles = [];

    /// <summary>
    /// Records the files a host's picker returned for a file input - their names, kinds and bytes - so the control shows their names and a submission of
    /// its form (<see cref="CreateFormSubmission(DocumentElement)"/>) sends them. Not applied to a control that is not a file input or is disabled, or when
    /// several files are given to one that does not take <c>multiple</c>.
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
    /// check box or radio button only when it is checked, a drop-down's chosen options, a file input's files, and the button that was used. An image
    /// button sends the point it was pressed at as <c>name.x</c> and <c>name.y</c> (0 and 0 here; see the overload that takes the point).
    /// </summary>
    /// <param name="submitter">the submit button: an <c>&lt;input type=submit&gt;</c> or <c>image</c>, or a <c>&lt;button&gt;</c> that is not <c>type=button</c> or <c>reset</c></param>
    /// <returns>the submission, or null when the element is not a submit button or is in no form</returns>
    /// <exception cref="ArgumentNullException"><paramref name="submitter"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public FormSubmission? CreateFormSubmission(DocumentElement submitter) => CreateFormSubmission(submitter, 0, 0, 0);

    /// <summary>
    /// As <see cref="CreateFormSubmission(DocumentElement)"/>, for a button pressed at a point: an image button sends the point, relative to the top
    /// left of the button, in CSS pixels.
    /// </summary>
    /// <param name="submitter">the submit button</param>
    /// <param name="pageIndex">the zero-based index of the page the point is on</param>
    /// <param name="x">the distance from the left edge of the sheet, in points</param>
    /// <param name="y">the distance from the top edge of the sheet, in points</param>
    /// <returns>the submission, or null when the element is not a submit button or is in no form</returns>
    /// <exception cref="ArgumentNullException"><paramref name="submitter"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public FormSubmission? CreateFormSubmission(DocumentElement submitter, int pageIndex, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(submitter);
        _ = Pages;
        if (LiveBoxOf(submitter) is not { } box || !IsSubmitButton(box) || box.HtmlTag!.HasAttribute("disabled") || FormOwnerOf(box) is not { } form)
            return null;

        var point = (X: 0, Y: 0);
        if (StyleUpdater.BoxRegionOf(_container, box, _container.FragmentTree?.Fragmentainers ?? []) is { } region && region.PageIndex == pageIndex)
            point = (Math.Max((int)Math.Floor((x - region.Bounds.X) / 0.75), 0), Math.Max((int)Math.Floor((y - region.Bounds.Y) / 0.75), 0));

        return BuildSubmission(form, box, point);
    }

    /// <summary>
    /// What pressing Enter in a text field submits, as a browser does: the form's default button - its first submit button - is used as if it was clicked;
    /// a form with none is submitted only when it has no more than one field that Enter would submit it from (a text, search, URL, telephone, email, password,
    /// number or date or time field).
    /// </summary>
    /// <param name="field">the field Enter was pressed in</param>
    /// <returns>the submission, or null when the element is not such a field, is in no form, or the form is not submitted by Enter</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public FormSubmission? CreateImplicitSubmission(DocumentElement field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _ = Pages;
        if (LiveBoxOf(field) is not { HtmlTag: { } tag } box || !tag.Name.Equals("input", StringComparison.OrdinalIgnoreCase) || !BlocksImplicitSubmission(tag)
            || FormOwnerOf(box) is not { } form)
        {
            return null;
        }

        var controls = OwnedControls(form).ToList();
        if (controls.FirstOrDefault(c => IsSubmitButton(c) && !c.HtmlTag!.HasAttribute("disabled")) is { } defaultButton)
            return BuildSubmission(form, defaultButton, (0, 0));

        return controls.Count(c => c.HtmlTag!.Name.Equals("input", StringComparison.OrdinalIgnoreCase) && BlocksImplicitSubmission(c.HtmlTag)) <= 1
            ? BuildSubmission(form, null, (0, 0))
            : null;
    }

    private static bool BlocksImplicitSubmission(HtmlTag tag) =>
        (tag.TryGetAttribute("type") ?? "text").Trim().ToLowerInvariant() is "text" or "search" or "url" or "tel" or "email" or "password" or "date" or "month" or "week" or "time"
            or "datetime-local" or "number" or "";

    // The live box of an element handle: the handle may be from before a restyle (a change of focus loads the source again), so it is found again by its
    // place in the source.
    private CssBox? LiveBoxOf(DocumentElement element)
    {
        var ordinal = element.Box.HtmlTag?.SourceOrdinal ?? -1;
        return ordinal < 0 ? null : FindByOrdinal(_container.Root, ordinal);
    }

    private FormSubmission BuildSubmission(CssBox form, CssBox? submitter, (int X, int Y) point)
    {
        var formTag = form.HtmlTag!;
        var entries = new List<FormEntry>();
        foreach (var control in OwnedControls(form))
            AddEntries(control, control.HtmlTag!, submitter, point, entries);

        static string Pick(string? preferred, string? own, string fallback) => !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() : !string.IsNullOrWhiteSpace(own) ? own.Trim() : fallback;
        var button = submitter?.HtmlTag;
        var method = Pick(button?.TryGetAttribute("formmethod"), formTag.TryGetAttribute("method"), "get").ToLowerInvariant();
        method = method is "post" or "dialog" ? method : "get";
        var encType = Pick(button?.TryGetAttribute("formenctype"), formTag.TryGetAttribute("enctype"), "application/x-www-form-urlencoded").ToLowerInvariant();
        encType = encType is "multipart/form-data" or "text/plain" ? encType : "application/x-www-form-urlencoded";
        var action = Pick(button?.TryGetAttribute("formaction"), formTag.TryGetAttribute("action"), string.Empty);
        return new FormSubmission(method, action.Length == 0 ? string.Empty : Html.Annotations.DocumentAnnotationCollector.ResolveHref(_container, action), encType, entries);
    }

    // The controls that belong to a form, in document order: the ones inside it and the ones elsewhere that name it with form="id".
    private IEnumerable<CssBox> OwnedControls(CssBox form)
    {
        var controls = new List<CssBox>();
        void Visit(CssBox current)
        {
            foreach (var child in current.Boxes)
            {
                if (child.HtmlTag is not null && ReferenceEquals(FormOwnerOf(child), form) && !IsDisabledByAncestor(child))
                    controls.Add(child);

                Visit(child);
            }
        }

        if (_container.Root is { } root)
            Visit(root);

        return controls;
    }

    private static bool IsSubmitButton(CssBox box)
    {
        var tag = box.HtmlTag!;
        var type = tag.TryGetAttribute("type")?.Trim().ToLowerInvariant();
        if (tag.Name.Equals("button", StringComparison.OrdinalIgnoreCase))
            return type is null or "" or "submit";

        return tag.Name.Equals("input", StringComparison.OrdinalIgnoreCase) && type is "submit" or "image";
    }

    private static bool IsResetButton(CssBox box)
    {
        var tag = box.HtmlTag!;
        var type = tag.TryGetAttribute("type")?.Trim().ToLowerInvariant();
        return (tag.Name.Equals("button", StringComparison.OrdinalIgnoreCase) || tag.Name.Equals("input", StringComparison.OrdinalIgnoreCase)) && type == "reset";
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

    private void AddEntries(CssBox box, HtmlTag tag, CssBox? submitter, (int X, int Y) point, List<FormEntry> entries)
    {
        var name = tag.TryGetAttribute("name");
        var type = ControlTypeOf(tag);
        if (tag.HasAttribute("disabled") || !IsFormControl(tag.Name))
            return;

        // An image button sends where it was pressed, under its name (or just x and y when it has none); it has no value.
        if (type == "image" && ReferenceEquals(box, submitter))
        {
            var prefix = string.IsNullOrEmpty(name) ? string.Empty : name + ".";
            entries.Add(new FormEntry(prefix + "x", point.X.ToString(CultureInfo.InvariantCulture), null));
            entries.Add(new FormEntry(prefix + "y", point.Y.ToString(CultureInfo.InvariantCulture), null));
            return;
        }

        if (string.IsNullOrEmpty(name))
            return;

        switch (type)
        {
            case "submit" or "image" or "button" or "reset":
                // Only the button that was used is sent.
                if (ReferenceEquals(box, submitter) && type == "submit")
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

            default:
                entries.Add(new FormEntry(name, Crlf(box.GetAttribute("value", string.Empty)), null));
                break;
        }
    }

    // What kind of control a tag is, in the words the form code uses: the input's type, "select", "textarea", or for a button element "submit" or "button".
    private static string ControlTypeOf(HtmlTag tag) => tag.Name.ToLowerInvariant() switch
    {
        "input" => (tag.TryGetAttribute("type") ?? "text").Trim().ToLowerInvariant(),
        "button" => (tag.TryGetAttribute("type") ?? "submit").Trim().ToLowerInvariant() is "button" or "reset" ? "button" : "submit",
        var other => other,
    };

    // A line break in a submitted value is a carriage return and a line feed, whichever it was written with.
    private static string Crlf(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\r\n", StringComparison.Ordinal);

    /// <summary>
    /// Puts the controls of a form back as the source has them - what a reset button does: text and the like to their <c>value</c>, check boxes and radio
    /// buttons to <c>checked</c>, drop-downs to their <c>selected</c> options, file inputs to none, text areas to their text and scrolled to the top - and
    /// styles the document again, so a rule that reads their state follows.
    /// </summary>
    /// <param name="control">a reset button, or any control or the form itself</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied (not for an element in no form) and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public async Task<UpdateResult> ResetFormAsync(DocumentElement control, Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        _ = Pages;
        if (LiveBoxOf(control) is not { HtmlTag: { } tag } box)
            return UpdateResult.NotApplied("The element is not in the document.");

        var form = tag.Name.Equals("form", StringComparison.OrdinalIgnoreCase) ? box : FormOwnerOf(box);
        if (form is null)
            return UpdateResult.NotApplied("The element is in no form.");

        var ordinals = new HashSet<int>();
        foreach (var owned in OwnedControls(form))
        {
            var ordinal = owned.HtmlTag!.SourceOrdinal;
            ordinals.Add(ordinal);
            _formChanges.Remove(ordinal);

            // What a drop-down holds is kept on its options.
            foreach (var option in OptionsOf(owned))
            {
                _formChanges.Remove(option.HtmlTag!.SourceOrdinal);
                ordinals.Add(option.HtmlTag.SourceOrdinal);
            }

            _chosenFiles.Remove(ordinal);
            _container.TextAreaScroll.Remove(ordinal);
            if (_container.FormEdit.Ordinal == ordinal)
                _container.FormEdit.Ordinal = -1;
        }

        return ordinals.Count == 0 ? UpdateResult.NotApplied("The form has no controls.") : await RestyleAsync(ordinals, measuringCanvas);
    }

    /// <summary>Whether an element is a reset button, for a host that decides what a click on it does.</summary>
    /// <param name="element">the element</param>
    /// <returns>whether it is an <c>&lt;input type=reset&gt;</c> or a <c>&lt;button type=reset&gt;</c></returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null</exception>
    public bool IsResetButton(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return LiveBoxOf(element) is { HtmlTag: not null } box && IsResetButton(box);
    }
}
