using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace PeachBrackets.WinUI;

/// <summary>
/// The pickers of the form controls the page draws but cannot open itself: a drop-down's list of options, the calendar and clock of a date or time
/// field, and the colour of a colour field - each a flyout over the control that writes its choice back through <see cref="PeachBrackets.HtmlDocument.SetValue"/> -
/// and the drag of a range control's thumb. A file field has no picker yet.
/// </summary>
public sealed partial class HtmlView
{
    private DocumentElement? _rangeElement;
    private int _rangePage;

    // A press on a range control sets it at once, and moving on drags the thumb.
    internal bool BeginRangeDrag(int page, double x, double y)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element
            || document.GetFormState(element) is not { Type: "range" })
        {
            return false;
        }

        _rangeElement = element;
        _rangePage = page;
        SetRange(x);
        return true;
    }

    internal void SetRange(double x)
    {
        if (_document is { } document && _rangeElement is { } element)
            AfterControlChange(document, document.SetRangeAtPoint(element, _rangePage, x));
    }

    private void EndRangeDrag() => _rangeElement = null;

    // A click on a control that has a picker opens it over the control. Returns whether it did.
    internal bool OpenPickerAt(int page, double x, double y, Point at)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element
            || document.GetFormState(element) is not { } state || HtmlViewPickers.KindOf(state.Type) is not { } kind)
        {
            return false;
        }

        if (kind == "file")
        {
            _ = PickFilesAsync(document, element);
            return true;
        }

        if (BuildPicker(kind, document, element, state.Value) is not { } flyout)
            return false;

        ShowFlyout(flyout, at);
        return true;
    }

    /// <summary>Replaces the system's file picker, which a file input opens; given what the input asks for, it returns the names of the chosen files. For tests.</summary>
    internal Func<FileInputInfo, Task<string[]>>? FilePicker { get; set; }

    private async Task PickFilesAsync(PeachBrackets.HtmlDocument document, DocumentElement element)
    {
        var info = document.GetFileInputInfo(element) ?? new FileInputInfo(false, string.Empty);
        string[] names;
        try
        {
            names = FilePicker is { } picker ? await picker(info) : await ShowFilePickerAsync(info);
        }
        catch (Exception)
        {
            // A picker that cannot open (no window to belong to) is the same as nothing chosen.
            return;
        }

        if (ReferenceEquals(document, _document) && names.Length > 0)
            AfterControlChange(document, document.SetFileNames(element, names));
    }

    // The system's file picker, which needs the window the view is in; it opens a modal dialog, so a test cannot drive it (the window tests replace it with
    // FilePicker).
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Opens the system's modal file picker.")]
    private async Task<string[]> ShowFilePickerAsync(FileInputInfo info)
    {
        if (XamlRoot?.ContentIslandEnvironment?.AppWindowId is not { } windowId)
            return [];

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Microsoft.UI.Win32Interop.GetWindowFromWindowId(windowId));
        foreach (var extension in HtmlViewPickers.ExtensionsFor(info.Accept))
            picker.FileTypeFilter.Add(extension);

        if (info.Multiple)
            return [.. (await picker.PickMultipleFilesAsync()).Select(f => f.Name)];

        return await picker.PickSingleFileAsync() is { } file ? [file.Name] : [];
    }

    private FlyoutBase? _openFlyout;

    // Shows a flyout over the page; the view keeps track of it so that taking the view out of the window closes it.
    private void ShowFlyout(FlyoutBase flyout, Point at)
    {
        _openFlyout?.Hide();
        _openFlyout = flyout;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openFlyout, flyout))
                _openFlyout = null;
        };
        flyout.ShowAt(_canvas, new FlyoutShowOptions { Position = at });
    }

    /// <summary>Closes the picker or menu that is open over the page, if one is.</summary>
    internal void CloseFlyouts()
    {
        _openFlyout?.Hide();
        _openFlyout = null;
    }

    // Builds the flyout of a kind of picker.
    private FlyoutBase? BuildPicker(string kind, PeachBrackets.HtmlDocument document, DocumentElement element, string value)
    {
        FlyoutBase? flyout = null;
        switch (kind)
        {
            case "select":
                var menu = new MenuFlyout();
                foreach (var option in document.GetSelectOptions(element))
                {
                    var chosen = option;
                    var item = new ToggleMenuFlyoutItem { Text = option.Label, IsChecked = option.Selected, IsEnabled = !option.Disabled };
                    item.Click += (_, _) => AfterControlChange(document, document.SetValue(element, chosen.Value));
                    menu.Items.Add(item);
                }

                flyout = menu;
                break;

            case "date" or "datetime":
                var calendar = new CalendarView { SelectionMode = CalendarViewSelectionMode.Single };
                if (HtmlViewPickers.TryParseDate(value, out var day))
                {
                    calendar.SelectedDates.Add(new DateTimeOffset(day));
                    calendar.SetDisplayDate(new DateTimeOffset(day));
                }

                calendar.SelectedDatesChanged += (_, args) =>
                {
                    if (args.AddedDates.Count == 0)
                        return;

                    var picked = args.AddedDates[0].Date;
                    var text = kind == "date" ? HtmlViewPickers.FormatDate(picked) : HtmlViewPickers.FormatDateTime(CurrentValue(document, element), picked, null);
                    AfterControlChange(document, document.SetValue(element, text));
                };
                flyout = new Flyout { Content = calendar };
                break;

            case "time":
                var clock = new TimePicker { Time = HtmlViewPickers.TryParseTime(value, out var time) ? time : TimeSpan.Zero };
                clock.TimeChanged += (_, args) => AfterControlChange(document, document.SetValue(element, HtmlViewPickers.FormatTime(args.NewTime)));
                flyout = new Flyout { Content = clock };
                break;

            case "color":
                var (r, g, b) = HtmlViewPickers.ParseColor(value);
                var picker = new ColorPicker { Color = Windows.UI.Color.FromArgb(255, r, g, b), IsAlphaEnabled = false };
                picker.ColorChanged += (_, args) =>
                    AfterControlChange(document, document.SetValue(element, HtmlViewPickers.FormatColor(args.NewColor.R, args.NewColor.G, args.NewColor.B)));
                flyout = new Flyout { Content = picker };
                break;
        }

        return flyout;
    }

    private static string CurrentValue(PeachBrackets.HtmlDocument document, DocumentElement element) => document.GetFormState(element)?.Value ?? string.Empty;
}
