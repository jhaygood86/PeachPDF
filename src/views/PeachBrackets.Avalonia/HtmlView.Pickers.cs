using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// The pickers of the form controls the page draws but cannot open itself - a drop-down's list of options, the flyouts of a date, time or color field, the
// platform's file picker for a file field - each writing its choice back through HtmlDocument.SetValue, and the drag of a range control's thumb.
public sealed partial class HtmlView
{
    private DocumentElement? _rangeElement;
    private int _rangePage;
    private FlyoutBase? _openFlyout;
    private readonly HashSet<FlyoutBase> _ownFlyouts = [];

    // A press that begins on a range control sets it at once, and moving on drags the thumb.
    private bool BeginRangeDrag(int page, double x, double y)
    {
        _rangeElement = null;
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

    // The pointer moved while a range control's thumb is held: true when there is one.
    private bool DragRange(global::Avalonia.Point position)
    {
        if (_rangeElement is null || !TryLocate(position, out _, out var x, out _))
            return _rangeElement is not null;

        SetRange(x);
        return true;
    }

    private void SetRange(double x)
    {
        if (_document is { } document && _rangeElement is { } element)
            AfterControlChange(document, document.SetRangeAtPoint(element, _rangePage, x));
    }

    // A click on a control that has a picker opens it. Returns whether it did.
    private bool OpenPickerAt(int page, double x, double y)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element
            || document.GetFormState(element) is not { } state || PickerValues.KindOf(state.Type) is not { } kind)
        {
            return false;
        }

        CloseFlyout();
        switch (kind)
        {
            case "select":
                return Show(BuildSelectMenu(document, element));

            case "date" or "datetime":
                return Show(BuildDateFlyout(document, element, state.Value, kind == "datetime"));

            case "time":
                return Show(BuildTimeFlyout(document, element, state.Value, null));

            case "color":
                return Show(BuildColorFlyout(document, element, state.Value));

            case "file":
                Forget(PickFilesAsync(document, element));
                return true;

            default:
                return false;
        }
    }

    // Shows one of the view's own flyouts. While it is open the keyboard focus is inside it, and the view losing it must not end the page's focus (the menu acts
    // on the field that has it, and a picker writes into the control that opened it); when it closes the focus comes back to the view.
    private bool Show(PopupFlyoutBase flyout, bool atPointer = false)
    {
        CloseFlyout();
        _openFlyout = flyout;
        _ownFlyouts.Add(flyout);
        flyout.Closed += OnOwnFlyoutClosed;
        flyout.ShowAt(this, atPointer);
        if (!flyout.IsOpen)
        {
            flyout.Closed -= OnOwnFlyoutClosed;
            _ownFlyouts.Remove(flyout);
            if (ReferenceEquals(_openFlyout, flyout))
                _openFlyout = null;

            return false;
        }

        return true;
    }

    private void CloseFlyout()
    {
        _openFlyout?.Hide();
        _openFlyout = null;
    }

    /// <summary>Whether a flyout the view opened (a field's menu or a picker) is open.</summary>
    internal bool HasOpenFlyout => _ownFlyouts.Count > 0;

    /// <summary>The flyout the view opened last, while it is open.</summary>
    internal FlyoutBase? OpenFlyout => _openFlyout;

    private void OnOwnFlyoutClosed(object? sender, EventArgs e)
    {
        if (sender is not FlyoutBase flyout)
            return;

        flyout.Closed -= OnOwnFlyoutClosed;
        _ownFlyouts.Remove(flyout);
        if (ReferenceEquals(_openFlyout, flyout))
            _openFlyout = null;

        // The focus goes back to the page, unless it has gone to some other control since.
        if (_ownFlyouts.Count == 0 && _document is not null && this.IsAttachedToVisualTree() && !IsFocused
            && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is var focused && (focused is null || IsInsideFlyoutChrome(focused)))
        {
            Focus();
        }
    }

    // The focus is in a flyout's own popup (its menu or its content), not in a control of the application.
    private static bool IsInsideFlyoutChrome(IInputElement element)
    {
        for (var visual = element as global::Avalonia.Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is FlyoutPresenter or MenuFlyoutPresenter or PopupRoot or OverlayPopupHost)
                return true;
        }

        return false;
    }

    // The list of options of a drop-down, the chosen one checked.
    internal MenuFlyout BuildSelectMenu(HtmlDocument document, DocumentElement element)
    {
        var options = document.GetSelectOptions(element);
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var item = new MenuItem
            {
                Header = option.Label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = option.Selected,
                IsEnabled = !option.Disabled,
                Tag = option.Value,
            };
            item.Click += (_, _) =>
            {
                if (IsCurrent(document))
                    AfterControlChange(document, document.SetValue(element, option.Value));
            };
            flyout.Items.Add(item);
        }

        return flyout;
    }

    // A calendar; for a date and time field the clock follows once a day is chosen.
    internal Flyout BuildDateFlyout(HtmlDocument document, DocumentElement element, string value, bool withTime)
    {
        var current = PickerValues.TryParseDate(value.Length >= 10 ? value[..10] : value, out var parsed) ? parsed : DateTime.Today;
        var calendar = new Calendar { SelectedDate = current, DisplayDate = current };
        var flyout = new Flyout { Content = calendar, Placement = PlacementMode.Pointer };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is not { } picked)
                return;

            flyout.Hide();
            if (!IsCurrent(document))
                return;

            if (withTime)
                Show(BuildTimeFlyout(document, element, value, picked));
            else
                AfterControlChange(document, document.SetValue(element, PickerValues.FormatDate(picked)));
        };
        return flyout;
    }

    // The clock of a time field, or the second half of a date-and-time field once its day has been chosen.
    internal Flyout BuildTimeFlyout(HtmlDocument document, DocumentElement element, string value, DateTime? date)
    {
        var timePart = value.Contains('T') ? value[(value.IndexOf('T') + 1)..] : value;
        var time = PickerValues.TryParseTime(timePart, out var parsed) ? parsed : TimeSpan.Zero;
        var hours = new NumericUpDown { Minimum = 0, Maximum = 23, Increment = 1, Value = time.Hours, FormatString = "00", Width = 110 };
        var minutes = new NumericUpDown { Minimum = 0, Maximum = 59, Increment = 1, Value = time.Minutes, FormatString = "00", Width = 110 };
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        var flyout = new Flyout
        {
            Placement = PlacementMode.Pointer,
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { hours, minutes } }, ok },
            },
        };
        ok.Click += (_, _) =>
        {
            flyout.Hide();
            if (!IsCurrent(document))
                return;

            var chosen = new TimeSpan((int)(hours.Value ?? 0), (int)(minutes.Value ?? 0), 0);
            var text = date is { } day ? PickerValues.FormatDateTime(value, day, chosen) : PickerValues.FormatTime(chosen);
            AfterControlChange(document, document.SetValue(element, text));
        };
        return flyout;
    }

    // Three sliders over a swatch, for a color field.
    internal Flyout BuildColorFlyout(HtmlDocument document, DocumentElement element, string value)
    {
        var (r, g, b) = PickerValues.ParseColor(value);
        var sliders = new[] { NewChannel(r), NewChannel(g), NewChannel(b) };
        var swatch = new Border { Height = 32, CornerRadius = new global::Avalonia.CornerRadius(3) };
        byte Channel(int i) => (byte)Math.Clamp(Math.Round(sliders[i].Value), 0, 255);
        void Update() => swatch.Background = new SolidColorBrush(Color.FromRgb(Channel(0), Channel(1), Channel(2)));
        foreach (var slider in sliders)
            slider.ValueChanged += (_, _) => Update();

        Update();
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 6, Width = 220 };
        panel.Children.Add(swatch);
        foreach (var slider in sliders)
            panel.Children.Add(slider);

        panel.Children.Add(ok);
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Pointer };
        ok.Click += (_, _) =>
        {
            flyout.Hide();
            if (IsCurrent(document))
                AfterControlChange(document, document.SetValue(element, PickerValues.FormatColor(Channel(0), Channel(1), Channel(2))));
        };
        return flyout;

        static Slider NewChannel(byte initial) => new() { Minimum = 0, Maximum = 255, Value = initial };
    }

    /// <summary>A file the picker handed back: its name and a way to read it.</summary>
    internal readonly record struct PickedFile(string Name, Func<Task<Stream>> OpenRead);

    /// <summary>Stands in for the platform's file picker when set. For tests, which have no picker to answer.</summary>
    internal Func<FilePickerOpenOptions, Task<IReadOnlyList<PickedFile>>>? FilePicker { get; set; }

    // The platform's file picker, limited to the kinds the accept attribute names.
    private async Task PickFilesAsync(HtmlDocument document, DocumentElement element)
    {
        var picker = FilePicker;
        if (picker is null)
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
                return;

            picker = async options => [.. (await storage.OpenFilePickerAsync(options)).Select(file => new PickedFile(file.Name, () => file.OpenReadAsync()))];
        }

        try
        {
            var info = document.GetFileInputInfo(element) ?? new FileInputInfo(false, string.Empty);
            var options = new FilePickerOpenOptions { AllowMultiple = info.Multiple, FileTypeFilter = FileTypesFor(info.Accept) };
            var files = await ReadChosenFilesAsync(await picker(options), info.Accept);
            if (files.Count > 0 && ReferenceEquals(document, _document))
                AfterControlChange(document, document.SetFiles(element, files));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (IsCurrent(document))
                RaiseDocumentFailed(exception);
        }
    }

    // What the picked files hold, with the media type each is submitted as.
    internal static async Task<List<ChosenFile>> ReadChosenFilesAsync(IEnumerable<PickedFile> picked, string accept)
    {
        var files = new List<ChosenFile>();
        foreach (var file in picked)
        {
            await using var stream = await file.OpenRead();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            files.Add(new ChosenFile(file.Name, ContentTypeOf(file.Name, accept), memory.ToArray()));
        }

        return files;
    }

    // The picker's filter for an accept attribute: one entry holding every media type and extension it names, or none for any file.
    internal static IReadOnlyList<FilePickerFileType>? FileTypesFor(string accept)
    {
        var (mimeTypes, extensions) = PickerValues.AcceptTokens(accept);
        if (mimeTypes.Count == 0 && extensions.Count == 0)
            return null;

        return
        [
            new FilePickerFileType("Accepted files")
            {
                MimeTypes = mimeTypes.Count > 0 ? mimeTypes.ToList() : null,
                Patterns = extensions.Count > 0 ? extensions.Select(e => "*." + e.TrimStart('.')).ToList() : null,
            },
        ];
    }

    // The media type a chosen file is submitted as: a single one the accept attribute names, or one the extension gives.
    internal static string ContentTypeOf(string name, string accept)
    {
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            "pdf" => "application/pdf",
            "txt" => "text/plain",
            "html" or "htm" => "text/html",
            "css" => "text/css",
            "json" => "application/json",
            "csv" => "text/csv",
            "xml" => "application/xml",
            "zip" => "application/zip",
            _ => PickerValues.AcceptTokens(accept).MimeTypes is { Count: 1 } single && !single[0].EndsWith("/*", StringComparison.Ordinal) ? single[0] : "application/octet-stream",
        };
    }

    // The files a picker returned, written into the file field.
    internal void SetChosenFiles(DocumentElement element, IReadOnlyList<ChosenFile> files)
    {
        if (_document is { } document)
            AfterControlChange(document, document.SetFiles(element, files));
    }
}
