using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachBrackets.Html.Updates;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PeachBrackets.Html.Paint.Content;
using System.Threading.Tasks;
using PeachDrawing.Core;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    // What was changed on each control since the source was loaded, by the control's place in the source.
    private readonly Dictionary<int, Dictionary<string, string?>> _formChanges = [];

    /// <summary>
    /// What a form control holds now: the value of a text field, range or button, whether a check box or radio button is checked, and the label of
    /// the chosen option of a drop-down.
    /// </summary>
    /// <param name="element">the control, from <see cref="GetElement"/> or <see cref="GetElementAt(int, double, double)"/></param>
    /// <returns>its state, or null when the element is not an <c>&lt;input&gt;</c> or a <c>&lt;select&gt;</c></returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null</exception>
    public FormControlState? GetFormState(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return ControlOf(element) is { } control ? StateOf(control) : null;
    }

    /// <summary>
    /// Checks or unchecks a check box, or checks a radio button (which unchecks the others of its group: those with the same <c>name</c> in the same
    /// <c>form</c>), in the document itself, and reports what to repaint. No layout pass runs. Not applied to a control that is disabled, or to
    /// anything else.
    /// </summary>
    /// <param name="element">the control</param>
    /// <param name="isChecked">whether it is checked; a radio button cannot be unchecked this way</param>
    /// <returns>whether it was applied and the regions that changed</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetChecked(DocumentElement element, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = Pages;
        if (ControlOf(element) is not { } control)
            return UpdateResult.NotApplied("The element is not a form control.");
        if (control.HtmlTag!.HasAttribute("disabled"))
            return UpdateResult.NotApplied("The control is disabled.");

        var type = TypeOf(control);
        var changed = new List<CssBox>();
        if (type == "checkbox")
        {
            if (control.HtmlTag.HasAttribute("checked") == isChecked)
                return new UpdateResult(true, null, []);

            SetFlag(control, "checked", isChecked);
            changed.Add(control);
        }
        else if (type == "radio")
        {
            if (!isChecked)
                return UpdateResult.NotApplied("A radio button is unchecked by checking another of its group.");

            foreach (var other in RadiosOfGroup(control))
            {
                var shouldBeChecked = ReferenceEquals(other, control);
                if (other.HtmlTag!.HasAttribute("checked") != shouldBeChecked)
                {
                    SetFlag(other, "checked", shouldBeChecked);
                    changed.Add(other);
                }
            }
        }
        else
        {
            return UpdateResult.NotApplied("Only a check box or a radio button is checked.");
        }

        return Changed(changed);
    }

    /// <summary>
    /// Sets what a text field, a range or a drop-down holds - the text of the field, the number of the range (kept between its <c>min</c> and
    /// <c>max</c>), the <c>value</c> of the option to choose - in the document itself, and reports what to repaint. No layout pass runs, so the
    /// control keeps its size. Not applied to a control that is disabled or read-only, or to anything else.
    /// </summary>
    /// <param name="element">the control</param>
    /// <param name="value">the new value</param>
    /// <returns>whether it was applied and the regions that changed</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetValue(DocumentElement element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(value);
        _ = Pages;
        if (ControlOf(element) is not { } control)
            return UpdateResult.NotApplied("The element is not a form control.");
        if (control.HtmlTag!.HasAttribute("disabled") || control.HtmlTag.HasAttribute("readonly"))
            return UpdateResult.NotApplied("The control is disabled or read-only.");

        if (control.HtmlTag.Name.Equals("select", StringComparison.OrdinalIgnoreCase))
        {
            var options = OptionsOf(control);
            CssBox? chosen = null;
            foreach (var option in options)
            {
                if ((option.HtmlTag!.TryGetAttribute("value") ?? OptionText(option)) == value)
                {
                    chosen = option;
                    break;
                }
            }

            // Nothing changes unless there is such an option.
            if (chosen is null)
                return UpdateResult.NotApplied("The drop-down has no such option.");

            foreach (var option in options)
                SetFlag(option, "selected", ReferenceEquals(option, chosen));

            return Changed([control]);
        }

        switch (TypeOf(control))
        {
            case "checkbox" or "radio" or "button" or "submit" or "reset" or "image" or "hidden" or "file":
                return UpdateResult.NotApplied("This kind of control does not hold a text value.");
            case "range":
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return UpdateResult.NotApplied("A range holds a number.");

                var min = Number(control, "min", 0);
                var max = Number(control, "max", 100);
                if (max <= min)
                    max = min + 100;
                value = Math.Clamp(number, min, max).ToString(CultureInfo.InvariantCulture);
                break;
        }

        SetControlAttribute(control, "value", value);
        ClearComposition(control);
        return Changed([control]);
    }

    /// <summary>
    /// Sets a range control from a point on its page, as a press or a drag on its track does: the point is mapped onto the track the way the control
    /// draws it, and the number is kept between <c>min</c> and <c>max</c> and rounded to <c>step</c> (a step of <c>any</c> or none that is a number
    /// is one). Not applied to a control that is not a range, or is disabled.
    /// </summary>
    /// <param name="element">the range control</param>
    /// <param name="pageIndex">the zero-based index of the page the point is on</param>
    /// <param name="x">the distance from the left edge of the sheet, in points</param>
    /// <returns>whether it was applied and the regions that changed</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetRangeAtPoint(DocumentElement element, int pageIndex, double x)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = Pages;
        if (ControlOf(element) is not { } control || TypeOf(control) != "range")
            return UpdateResult.NotApplied("The element is not a range control.");
        if (StyleUpdater.BoxRegionOf(_container, control, _container.FragmentTree?.Fragmentainers ?? []) is not { } region || region.PageIndex != pageIndex)
            return UpdateResult.NotApplied("The control is not on that page.");

        var units = _unitsPerPoint;
        var left = region.Bounds.X + ((control.ActualBorderLeftWidth + control.ActualPaddingLeft) / units);
        var width = region.Bounds.Width - ((control.ActualBorderLeftWidth + control.ActualPaddingLeft + control.ActualBorderRightWidth + control.ActualPaddingRight) / units);
        var height = region.Bounds.Height - ((control.ActualBorderTopWidth + control.ActualPaddingTop + control.ActualBorderBottomWidth + control.ActualPaddingBottom) / units);
        var thumb = Math.Min(height, width / 2);
        var span = Math.Max(width - thumb, 0);
        var fraction = span <= 0 ? 0 : Math.Clamp((x - left - (thumb / 2)) / span, 0, 1);

        var min = Number(control, "min", 0);
        var max = Number(control, "max", 100);
        if (max <= min)
            max = min + 100;

        var value = min + (fraction * (max - min));
        if (control.GetAttribute("step", string.Empty) is var step && !step.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            var size = Number(control, "step", 1);
            if (size > 0)
                value = min + (Math.Round((value - min) / size) * size);
        }

        return SetValue(element, Math.Clamp(value, min, max).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// What a file input asks of a file picker: whether it takes several files and which kinds, for a host that opens one.
    /// </summary>
    /// <param name="element">the file input</param>
    /// <returns>the request, or null for anything that is not a file input</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public FileInputInfo? GetFileInputInfo(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = Pages;
        if (ControlOf(element) is not { } control || TypeOf(control) != "file")
            return null;

        return new FileInputInfo(control.HtmlTag!.HasAttribute("multiple"), control.GetAttribute("accept", string.Empty));
    }

    /// <summary>
    /// Records the files a host's picker returned for a file input, so the control shows their names (or how many there are) and
    /// <see cref="GetFormState"/> reports them. Only the names are kept: the document does not read or hold the files. Not applied to a control that is not a
    /// file input or is disabled, or when several names are given to one that does not take <c>multiple</c>.
    /// </summary>
    /// <param name="element">the file input</param>
    /// <param name="names">the names of the chosen files; empty to clear</param>
    /// <returns>whether it was applied and the regions that changed</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetFileNames(DocumentElement element, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(names);
        _ = Pages;
        if (ControlOf(element) is not { } control || TypeOf(control) != "file")
            return UpdateResult.NotApplied("The element is not a file input.");
        if (control.HtmlTag!.HasAttribute("disabled"))
            return UpdateResult.NotApplied("The control is disabled.");
        if (names.Count > 1 && !control.HtmlTag.HasAttribute("multiple"))
            return UpdateResult.NotApplied("The file input takes one file.");

        var chosen = names.Select(n => n.Replace('\n', ' ').Trim()).Where(n => n.Length > 0).ToList();
        SetControlAttribute(control, ScreenFormControlPainter.ChosenFilesAttribute, chosen.Count == 0 ? null : string.Join('\n', chosen));
        return Changed([control]);
    }

    /// <summary>The options of a drop-down, in document order, for a host that lets the user choose one (see <see cref="SetValue"/>).</summary>
    /// <param name="element">the drop-down</param>
    /// <returns>its options; empty for anything that is not a drop-down</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public IReadOnlyList<SelectOption> GetSelectOptions(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = Pages;
        if (ControlOf(element) is not { } control || TypeOf(control) != "select")
            return [];

        var options = new List<SelectOption>();
        foreach (var option in OptionsOf(control))
        {
            var tag = option.HtmlTag!;
            options.Add(new SelectOption(tag.TryGetAttribute("value") ?? OptionText(option), OptionText(option), tag.HasAttribute("selected"), tag.HasAttribute("disabled")));
        }

        return options;
    }

    /// <summary>
    /// Does what a click on a control does: toggles a check box and checks a radio button. Anything else is not applied. A host calls it when a
    /// pointer is released over a control.
    /// </summary>
    /// <param name="element">the control</param>
    /// <returns>whether it was applied and the regions that changed</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult Activate(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = Pages;
        if (ControlOf(element) is not { } control)
            return UpdateResult.NotApplied("The element is not a form control.");

        return TypeOf(control) switch
        {
            "checkbox" => SetChecked(element, !control.HtmlTag!.HasAttribute("checked")),
            "radio" => SetChecked(element, true),
            _ => UpdateResult.NotApplied("Nothing to do for this kind of control."),
        };
    }

    /// <summary>
    /// Opens or closes a <c>&lt;details&gt;</c> element, as a click on its summary does: a closed one shows only its summary, so the document is styled
    /// and laid out again with the new state, and the pages from the first one whose layout moved are to be repainted (the element's own state is
    /// kept if the document is loaded again for a focus or pointer rule).
    /// </summary>
    /// <param name="element">the <c>details</c> element</param>
    /// <param name="open">whether it is open</param>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public async Task<UpdateResult> SetOpenAsync(DocumentElement element, bool open, Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        ThrowIfDisposed();
        _ = Pages;
        if (element.Box.HtmlTag is not { } tag || !tag.Name.Equals("details", StringComparison.OrdinalIgnoreCase))
            return UpdateResult.NotApplied("The element is not a details element.");
        if (tag.HasAttribute("open") == open)
            return UpdateResult.NotApplied("The element is already so.");
        if (_source is null || _options is null)
            return UpdateResult.NotApplied("Only a document loaded from HTML can be restyled for a change of state.");
        if (_textEdited)
            return UpdateResult.NotApplied("The text was edited in place, which loading the document again would undo.");

        SetControlAttribute(element.Box, "open", open ? string.Empty : null);
        return await RestyleAsync([tag.SourceOrdinal], measuringCanvas);
    }

    private static CssBox? ControlOf(DocumentElement element) =>
        element.Box is CssBoxFormField { HtmlTag: not null } field ? field : null;

    private static string TypeOf(CssBox control) =>
        control.HtmlTag!.Name.Equals("select", StringComparison.OrdinalIgnoreCase) ? "select"
        : control.HtmlTag.Name.Equals("textarea", StringComparison.OrdinalIgnoreCase) ? "textarea"
        : control.GetAttribute("type", "text").ToLowerInvariant();

    private static double Number(CssBox control, string name, double fallback) =>
        double.TryParse(control.GetAttribute(name, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    // Every change to what a control holds goes through here, which remembers it: loading the source again (a change of :focus can do that)
    // would otherwise put the control back as the source has it.
    private void SetFlag(CssBox box, string name, bool on) => SetControlAttribute(box, name, on ? string.Empty : null);

    private void SetControlAttribute(CssBox box, string name, string? value)
    {
        var tag = box.HtmlTag!;
        if (value is null)
            tag.RemoveAttribute(name);
        else
            tag.SetAttribute(name, value);

        if (tag.SourceOrdinal < 0)
            return;

        if (!_formChanges.TryGetValue(tag.SourceOrdinal, out var changes))
            _formChanges[tag.SourceOrdinal] = changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        changes[name] = value;

        // A rule that reads the state this attribute is (checked, disabled...) has to be matched again; the text of a field is not such state.
        if (_container.FormStatePseudoClassesInUse && name.ToLowerInvariant() is "checked" or "selected" or "disabled" or "readonly" or "required" or "contenteditable")
            _restyleOrdinals.Add(tag.SourceOrdinal);
    }

    // The controls whose state changed since the document was last styled, when a rule reads that state.
    private readonly HashSet<int> _restyleOrdinals = [];

    /// <summary>
    /// Whether the document has to be styled again after changes to controls (<see cref="SetChecked"/>, <see cref="SetValue"/>, <see cref="Activate"/>): true
    /// when a style sheet rule reads <c>:checked</c>, <c>:disabled</c>, <c>:enabled</c>, <c>:required</c>, <c>:optional</c>, <c>:read-only</c> or
    /// <c>:read-write</c> and a control's state it reads has changed. A host that sees it calls <see cref="RestyleFormAsync"/>.
    /// </summary>
    public bool NeedsRestyle => _restyleOrdinals.Count > 0;

    /// <summary>
    /// Styles the document again with the controls as they are now, so rules such as <c>input:checked + label</c> match what a change made, and reports the
    /// pages to repaint from the first one whose layout moved. Does nothing when <see cref="NeedsRestyle"/> is false.
    /// </summary>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measuringCanvas"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public async Task<UpdateResult> RestyleFormAsync(Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        ThrowIfDisposed();
        _ = Pages;
        if (_restyleOrdinals.Count == 0)
            return UpdateResult.NotApplied("No control the style sheet reads has changed.");
        if (_source is null || _options is null)
            return UpdateResult.NotApplied("Only a document loaded from HTML can be restyled for a change of state.");
        if (_textEdited)
            return UpdateResult.NotApplied("The text was edited in place, which loading the document again would undo.");

        return await RestyleAsync([.. _restyleOrdinals], measuringCanvas);
    }


    // The options of a drop-down, in order, wherever the parser put them (an option left open can hold the next).
    private static List<CssBox> OptionsOf(CssBox select)
    {
        var options = new List<CssBox>();
        void Visit(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag is { } tag && tag.Name.Equals("option", StringComparison.OrdinalIgnoreCase))
                    options.Add(child);

                Visit(child);
            }
        }

        Visit(select);
        return options;
    }

    private static string OptionText(CssBox option) => (CssContentEngine.ExtractText(option) ?? string.Empty).Trim();

    private static FormControlState StateOf(CssBox control)
    {
        var tag = control.HtmlTag!;
        var type = TypeOf(control);
        var value = control.GetAttribute("value", string.Empty);
        if (type == "select")
        {
            value = string.Empty;
            foreach (var child in OptionsOf(control))
            {
                if (child.HtmlTag!.HasAttribute("selected"))
                    value = child.HtmlTag.TryGetAttribute("value") ?? OptionText(child);
            }
        }

        if (type == "file")
            value = control.GetAttribute(ScreenFormControlPainter.ChosenFilesAttribute, string.Empty).Replace("\n", ", ", StringComparison.Ordinal);

        return new FormControlState(type, tag.TryGetAttribute("name"), value, tag.HasAttribute("checked"), tag.HasAttribute("disabled"));
    }

    // The radio buttons that are one group: the same name, in the same form (or, with no form, in the same document).
    private IEnumerable<CssBox> RadiosOfGroup(CssBox radio)
    {
        var name = radio.HtmlTag!.TryGetAttribute("name");
        if (string.IsNullOrEmpty(name))
        {
            yield return radio;
            yield break;
        }

        static CssBox? FormOf(CssBox box)
        {
            for (var parent = box.ParentBox; parent is not null; parent = parent.ParentBox)
            {
                if (parent.HtmlTag is { } tag && tag.Name.Equals("form", StringComparison.OrdinalIgnoreCase))
                    return parent;
            }

            return null;
        }

        var form = FormOf(radio);
        var stack = new Stack<CssBox>();
        if (_container.Root is { } root)
            stack.Push(root);
        while (stack.Count > 0)
        {
            var box = stack.Pop();
            foreach (var child in box.Boxes)
                stack.Push(child);

            if (box is CssBoxFormField { HtmlTag: not null } field && TypeOf(field) == "radio" && field.HtmlTag!.TryGetAttribute("name") == name
                && ReferenceEquals(FormOf(field), form))
            {
                yield return field;
            }
        }
    }

    private UpdateResult Changed(IReadOnlyList<CssBox> controls)
    {
        _container.ResetSemanticTree();
        var fragmentainers = _container.FragmentTree?.Fragmentainers ?? [];
        var regions = new List<DirtyRegion>();
        foreach (var control in controls)
            regions.AddRange(StyleUpdater.DirtyRegionsOf(_container, control, fragmentainers));

        return new UpdateResult(true, null, regions);
    }
}
