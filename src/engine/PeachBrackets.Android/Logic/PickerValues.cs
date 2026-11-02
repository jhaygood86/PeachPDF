using System;
using System.Globalization;

namespace PeachBrackets.Android.Logic;

/// <summary>
/// The values a date or time input holds and the dialog that sets them: which kinds of control get a picker, and how the text of a value
/// (<c>2026-11-02</c>, <c>14:30</c>, <c>2026-11-02T14:30</c>, <c>#ff8000</c>) turns into what a picker shows and back. Kept apart from the view so it
/// can be tested without a window.
/// </summary>
internal static class PickerValues
{
    /// <summary>Which picker a control type opens: <c>date</c>, <c>time</c>, <c>datetime</c> (both), <c>color</c>, <c>select</c>, or null for none.</summary>
    public static string? KindOf(string type) => type switch
    {
        "date" => "date",
        "time" => "time",
        "datetime-local" => "datetime",
        "color" => "color",
        "select" => "select",
        _ => null,
    };

    public static bool TryParseDate(string value, out DateTime date)
    {
        var text = value.Length >= 10 ? value[..10] : value;
        return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static bool TryParseTime(string value, out TimeSpan time)
    {
        var text = value.Contains('T', StringComparison.Ordinal) ? value[(value.IndexOf('T', StringComparison.Ordinal) + 1)..] : value;
        if (DateTime.TryParseExact(text, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            time = parsed.TimeOfDay;
            return true;
        }

        time = TimeSpan.Zero;
        return false;
    }

    public static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatTime(TimeSpan time) => new DateTime(1, 1, 1).Add(time).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The value of a <c>datetime-local</c> control with its date or its time replaced; the part not replaced is kept from the old value, or today and midnight.</summary>
    public static string FormatDateTime(string oldValue, DateTime? date, TimeSpan? time)
    {
        var day = date ?? (TryParseDate(oldValue, out var oldDay) ? oldDay : DateTime.Today);
        var clock = time ?? (TryParseTime(oldValue, out var oldTime) ? oldTime : TimeSpan.Zero);
        return FormatDate(day) + "T" + FormatTime(clock);
    }

    public static string FormatColor(byte r, byte g, byte b) => string.Create(CultureInfo.InvariantCulture, $"#{r:x2}{g:x2}{b:x2}");

    /// <summary>The red, green and blue of <c>#rrggbb</c>; black for anything else, as the control draws it.</summary>
    public static (byte R, byte G, byte B) ParseColor(string value)
    {
        var text = value.Trim();
        if (text.Length == 7 && text[0] == '#'
            && byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return (r, g, b);
        }

        return (0, 0, 0);
    }
}
