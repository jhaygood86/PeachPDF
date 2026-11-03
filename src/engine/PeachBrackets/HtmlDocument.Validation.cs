using PeachBrackets.Html.Dom;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    /// <summary>
    /// The controls of the form a button belongs to whose values do not satisfy their constraints, as a browser checks before it submits: <c>required</c>,
    /// <c>minlength</c>, <c>maxlength</c>, <c>pattern</c>, the syntax of an email address, URL or number, and <c>min</c>, <c>max</c> and <c>step</c> of a number,
    /// range, date or time. Disabled and read-only controls, hidden inputs and buttons are not checked, and nothing is when the form has <c>novalidate</c> or the
    /// button <c>formnovalidate</c>.
    /// </summary>
    /// <param name="submitter">the submit button used, or the form itself or any control of it</param>
    /// <returns>the problems, in document order, at most one for each control; empty when the form is valid or is not checked</returns>
    /// <exception cref="ArgumentNullException"><paramref name="submitter"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public IReadOnlyList<ValidationProblem> GetValidationProblems(DocumentElement submitter)
    {
        ArgumentNullException.ThrowIfNull(submitter);
        _ = Pages;
        if (LiveBoxOf(submitter) is not { HtmlTag: { } tag } box)
            return [];

        var form = tag.Name.Equals("form", StringComparison.OrdinalIgnoreCase) ? box : FormOwnerOf(box);
        if (form?.HtmlTag is not { } formTag || formTag.HasAttribute("novalidate") || (IsFormControl(tag.Name) && tag.HasAttribute("formnovalidate")))
            return [];

        var problems = new List<ValidationProblem>();
        var radiosSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in OwnedControls(form))
        {
            if (ProblemOf(control, radiosSeen) is { } problem)
                problems.Add(new ValidationProblem(new DocumentElement(control), problem.Kind, problem.Message));
        }

        return problems;
    }

    // The first thing wrong with one control, or null.
    private (ValidationKind Kind, string Message)? ProblemOf(CssBox box, HashSet<string> radiosSeen)
    {
        var tag = box.HtmlTag!;
        var type = ControlTypeOf(tag);
        if (tag.HasAttribute("disabled") || type is "submit" or "image" or "button" or "reset" or "hidden")
            return null;

        var required = tag.HasAttribute("required");
        var readOnly = tag.HasAttribute("readonly");
        var value = box.GetAttribute("value", string.Empty);
        switch (type)
        {
            case "checkbox":
                return required && !tag.HasAttribute("checked") ? (ValidationKind.ValueMissing, "Please check this box if you want to proceed.") : null;

            case "radio":
                // A group is checked once, at its first member: one required button makes the group required.
                var group = tag.TryGetAttribute("name") ?? string.Empty;
                if (group.Length == 0)
                    return required && !tag.HasAttribute("checked") ? (ValidationKind.ValueMissing, "Please select one of these options.") : null;
                if (!radiosSeen.Add(group))
                    return null;

                var members = RadiosOfGroup(box).ToList();
                return members.Any(r => r.HtmlTag!.HasAttribute("required")) && !members.Any(r => r.HtmlTag!.HasAttribute("checked"))
                    ? (ValidationKind.ValueMissing, "Please select one of these options.")
                    : null;

            case "select":
                if (!required)
                    return null;

                var options = OptionsOf(box);
                var chosen = options.Where(o => o.HtmlTag!.HasAttribute("selected")).ToList();
                if (chosen.Count == 0 && !tag.HasAttribute("multiple") && options.Count > 0)
                    chosen = [options[0]];

                // The first option of a one-line drop-down with no value is its placeholder.
                var placeholder = !tag.HasAttribute("multiple") && chosen.Count == 1 && ReferenceEquals(chosen[0], options[0])
                    && string.IsNullOrEmpty(chosen[0].HtmlTag!.TryGetAttribute("value") ?? OptionText(chosen[0]));
                return chosen.Count == 0 || placeholder ? (ValidationKind.ValueMissing, "Please select an item in the list.") : null;

            case "file":
                return required && !(_chosenFiles.TryGetValue(tag.SourceOrdinal, out var files) && files.Count > 0)
                    ? (ValidationKind.ValueMissing, "Please select a file.")
                    : null;
        }

        if (readOnly)
            return null;

        if (value.Length == 0)
            return required ? (ValidationKind.ValueMissing, "Please fill out this field.") : null;

        if (tag.TryGetAttribute("minlength") is { } min && int.TryParse(min, NumberStyles.None, CultureInfo.InvariantCulture, out var minLength) && value.Length < minLength)
            return (ValidationKind.TooShort, $"Please lengthen this text to {minLength} characters or more (you are currently using {value.Length} characters).");
        if (tag.TryGetAttribute("maxlength") is { } max && int.TryParse(max, NumberStyles.None, CultureInfo.InvariantCulture, out var maxLength) && value.Length > maxLength)
            return (ValidationKind.TooLong, $"Please shorten this text to {maxLength} characters or fewer (you are currently using {value.Length} characters).");

        switch (type)
        {
            case "email" when !(tag.HasAttribute("multiple") ? value.Split(',').Select(v => v.Trim()).All(IsEmailAddress) : IsEmailAddress(value.Trim())):
                return (ValidationKind.TypeMismatch, "Please enter an email address.");
            case "url" when !Uri.TryCreate(value.Trim(), UriKind.Absolute, out _):
                return (ValidationKind.TypeMismatch, "Please enter a URL.");
        }

        if (type is "text" or "search" or "tel" or "url" or "email" or "password" && tag.TryGetAttribute("pattern") is { Length: > 0 } pattern && !MatchesPattern(pattern, value))
            return (ValidationKind.PatternMismatch, "Please match the requested format.");

        return type is "number" or "range" or "date" or "time" or "month" or "datetime-local" ? RangeProblem(tag, type, value) : null;
    }

    private static bool IsEmailAddress(string value) =>
        Regex.IsMatch(value, @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$", RegexOptions.CultureInvariant);

    // The pattern must match the whole value; one that is not a valid expression is ignored, as a browser does.
    private static bool MatchesPattern(string pattern, string value)
    {
        try
        {
            return Regex.IsMatch(value, "^(?:" + pattern + ")$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    // A number, range, date or time against its min, max and step.
    private static (ValidationKind Kind, string Message)? RangeProblem(HtmlTag tag, string type, string value)
    {
        if (!TryParseValue(type, value, out var number))
            return (ValidationKind.TypeMismatch, type == "number" || type == "range" ? "Please enter a number." : "Please enter a valid value.");

        if (tag.TryGetAttribute("min") is { } min && TryParseValue(type, min, out var low) && number < low)
            return (ValidationKind.RangeUnderflow, $"Value must be greater than or equal to {min}.");
        if (tag.TryGetAttribute("max") is { } max && TryParseValue(type, max, out var high) && number > high)
            return (ValidationKind.RangeOverflow, $"Value must be less than or equal to {max}.");

        // Step applies to numbers (a range's step defaults to 1 too); dates and times are in days, months or seconds and are left to min and max.
        if (type is "number" or "range" && tag.TryGetAttribute("step") is not "any" and var step)
        {
            var size = step is not null && double.TryParse(step, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 1;
            var origin = tag.TryGetAttribute("min") is { } baseText && double.TryParse(baseText, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : 0;
            var steps = (number - origin) / size;
            if (Math.Abs(steps - Math.Round(steps)) > 1e-9)
                return (ValidationKind.StepMismatch, "Please enter a valid value.");
        }

        return null;
    }

    // A value as one number that orders like the value: the number itself, or the ticks of a date or time.
    private static bool TryParseValue(string type, string text, out double number)
    {
        text = text.Trim();
        number = 0;
        switch (type)
        {
            case "number" or "range":
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
            case "date" when DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                number = date.Ticks;
                return true;
            case "month" when DateTime.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month):
                number = month.Ticks;
                return true;
            case "time" when DateTime.TryParseExact(text, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time):
                number = time.TimeOfDay.Ticks;
                return true;
            case "datetime-local" when DateTime.TryParseExact(text, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local):
                number = local.Ticks;
                return true;
            default:
                return false;
        }
    }
}
