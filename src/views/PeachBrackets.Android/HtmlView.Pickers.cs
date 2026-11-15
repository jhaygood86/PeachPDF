using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Provider;
using Android.Views;
using Android.Widget;
using PeachBrackets.Android.Logic;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Android;

/// <summary>
/// The pickers of the form controls the page draws but cannot open itself - a drop-down's list of options, the dialogs of a date or time field - each
/// writing its choice back through <see cref="HtmlDocument.SetValue"/> - a colour field's is three sliders over a swatch - the system's document picker for a
/// file field, and the drag of a range control's thumb. The file picker returns through the activity, so the application forwards the result to
/// <see cref="HandleActivityResult"/>.
/// </summary>
public sealed partial class HtmlView
{
    private DocumentElement? _rangeElement;
    private int _rangePage;

    // A touch that begins on a range control sets it at once, and moving on drags the thumb.
    private bool BeginRangeDrag(int page, double x, double y)
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

    private void SetRange(double x)
    {
        if (_document is { } document && _rangeElement is { } element)
            AfterControlChange(document, document.SetRangeAtPoint(element, _rangePage, x));
    }

    // A tap on a control that has a picker opens it. Returns whether it did.
    private bool OpenPickerAt(int page, double x, double y)
    {
        if (_document is not { } document || Context is not { } context || document.GetElementAt(page, x, y) is not { } element
            || document.GetFormState(element) is not { } state || PickerValues.KindOf(state.Type) is not { } kind)
        {
            return false;
        }

        switch (kind)
        {
            case "select":
                var options = document.GetSelectOptions(element);
                var menu = new PopupMenu(context, this);
                for (var i = 0; i < options.Count; i++)
                {
                    var item = menu.Menu?.Add(0, i, i, options[i].Label);
                    item?.SetCheckable(true);
                    item?.SetChecked(options[i].Selected);
                    item?.SetEnabled(!options[i].Disabled);
                }

                menu.MenuItemClick += (_, args) =>
                {
                    if (args.Item is { } chosen && chosen.ItemId is var index && index >= 0 && index < options.Count)
                        AfterControlChange(document, document.SetValue(element, options[index].Value));
                };
                menu.Show();
                return true;

            case "date" or "datetime":
                var day = PickerValues.TryParseDate(state.Value, out var parsed) ? parsed : DateTime.Today;
                new DatePickerDialog(context, (_, args) =>
                {
                    var picked = args.Date;
                    if (kind == "date")
                        AfterControlChange(document, document.SetValue(element, PickerValues.FormatDate(picked)));
                    else
                        OpenTimeDialog(context, document, element, picked);
                }, day.Year, day.Month - 1, day.Day).Show();
                return true;

            case "time":
                OpenTimeDialog(context, document, element, null);
                return true;

            case "color":
                OpenColorDialog(context, document, element, state.Value);
                return true;

            case "file":
                return StartFilePicker(context, document, element);

            default:
                return false;
        }
    }

    /// <summary>The request code the file picker is started with; an application that forwards activity results can tell which one is the view's.</summary>
    public const int FilePickerRequestCode = 0x7E11;

    private DocumentElement? _pendingFileElement;

    // Opens the system's document picker for a file input, limited to the kinds its accept attribute names.
    private bool StartFilePicker(Context context, HtmlDocument document, DocumentElement element)
    {
        var activity = ActivityOf(context);
        if (activity is null)
            return false;

        var info = document.GetFileInputInfo(element) ?? new FileInputInfo(false, string.Empty);
        var (mimeTypes, extensions) = PickerValues.AcceptTokens(info.Accept);
        var types = mimeTypes.ToList();
        foreach (var extension in extensions)
        {
            if (global::Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) is { Length: > 0 } mime && !types.Contains(mime))
                types.Add(mime);
        }

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(types.Count == 1 ? types[0] : "*/*");
        if (types.Count > 1)
            intent.PutExtra(Intent.ExtraMimeTypes, types.ToArray());

        intent.PutExtra(Intent.ExtraAllowMultiple, info.Multiple);
        _pendingFileElement = element;
        activity.StartActivityForResult(intent, FilePickerRequestCode);
        return true;
    }

    /// <summary>
    /// Takes the result of the file picker a file input opened. An application calls it from its activity's <c>OnActivityResult</c> with every result; it
    /// uses the ones for <see cref="FilePickerRequestCode"/> and returns whether it did.
    /// </summary>
    /// <param name="requestCode">the request code of the result</param>
    /// <param name="resultCode">whether the user chose something</param>
    /// <param name="data">the chosen documents</param>
    /// <returns>whether the result was the view's</returns>
    public bool HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != FilePickerRequestCode)
            return false;

        var element = _pendingFileElement;
        _pendingFileElement = null;
        if (resultCode != Result.Ok || data is null || element is null || _document is not { } document || Context?.ContentResolver is not { } resolver)
            return true;

        var uris = new List<global::Android.Net.Uri>();
        if (data.ClipData is { } clip)
        {
            for (var i = 0; i < clip.ItemCount; i++)
            {
                if (clip.GetItemAt(i)?.Uri is { } item)
                    uris.Add(item);
            }
        }
        else if (data.Data is { } single)
        {
            uris.Add(single);
        }

        var files = new List<ChosenFile>();
        foreach (var uri in uris)
        {
            using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            var name = cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : uri.LastPathSegment;
            if (string.IsNullOrEmpty(name))
                continue;

            using var memory = new System.IO.MemoryStream();
            using (var input = resolver.OpenInputStream(uri))
                input?.CopyTo(memory);

            files.Add(new ChosenFile(name, resolver.GetType(uri) ?? string.Empty, memory.ToArray()));
        }

        AfterControlChange(document, document.SetFiles(element, files));
        return true;
    }

    private static Activity? ActivityOf(Context? context)
    {
        while (context is ContextWrapper wrapper)
        {
            if (context is Activity activity)
                return activity;

            context = wrapper.BaseContext;
        }

        return context as Activity;
    }

    // Three sliders over a swatch, for a colour field.
    private void OpenColorDialog(Context context, HtmlDocument document, DocumentElement element, string value)
    {
        var (r, g, b) = PickerValues.ParseColor(value);
        var channels = new[] { (int)r, g, b };
        var layout = new LinearLayout(context) { Orientation = Orientation.Vertical };
        layout.SetPadding(48, 24, 48, 0);
        var swatch = new global::Android.Views.View(context);
        swatch.SetBackgroundColor(Color.Rgb(channels[0], channels[1], channels[2]));
        layout.AddView(swatch, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 96));
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var bar = new SeekBar(context) { Max = 255, Progress = channels[i] };
            bar.ProgressChanged += (_, args) =>
            {
                channels[index] = args.Progress;
                swatch.SetBackgroundColor(Color.Rgb(channels[0], channels[1], channels[2]));
            };
            layout.AddView(bar);
        }

        new AlertDialog.Builder(context)!
            .SetTitle("Colour")!
            .SetView(layout)!
            .SetPositiveButton("OK", (_, _) =>
                AfterControlChange(document, document.SetValue(element, PickerValues.FormatColor((byte)channels[0], (byte)channels[1], (byte)channels[2]))))!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // The clock of a time field, or the second half of a date-and-time field once its date has been chosen.
    private void OpenTimeDialog(global::Android.Content.Context context, HtmlDocument document, DocumentElement element, DateTime? date)
    {
        var current = document.GetFormState(element)?.Value ?? string.Empty;
        var time = PickerValues.TryParseTime(current, out var parsed) ? parsed : TimeSpan.Zero;
        new TimePickerDialog(context, (_, args) =>
        {
            var chosen = new TimeSpan(args.HourOfDay, args.Minute, 0);
            var text = date is { } day ? PickerValues.FormatDateTime(current, day, chosen) : PickerValues.FormatTime(chosen);
            AfterControlChange(document, document.SetValue(element, text));
        }, time.Hours, time.Minutes, true).Show();
    }
}
