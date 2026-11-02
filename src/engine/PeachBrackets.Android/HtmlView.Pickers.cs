using Android.App;
using Android.Views;
using Android.Widget;
using PeachBrackets.Android.Logic;
using PeachBrackets.Updates;
using System;
using System.Linq;

namespace PeachBrackets.Android;

/// <summary>
/// The pickers of the form controls the page draws but cannot open itself - a drop-down's list of options, the dialogs of a date or time field - each
/// writing its choice back through <see cref="HtmlDocument.SetValue"/>, and the drag of a range control's thumb. A colour or file field has no picker yet.
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

            default:
                return false;
        }
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
