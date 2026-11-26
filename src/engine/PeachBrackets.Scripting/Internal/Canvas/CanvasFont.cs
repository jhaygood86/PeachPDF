using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeachBrackets.Scripting.Internal.Canvas;

/// <summary>What a relative font size or spacing is relative to: the canvas element's own computed font, the root element's, and the viewport.</summary>
/// <param name="FontSize">the canvas element's font size, in pixels</param>
/// <param name="RootFontSize">the root element's font size, in pixels</param>
/// <param name="LineHeight">the canvas element's line height, in pixels</param>
/// <param name="ViewportWidth">the width of the viewport, in pixels</param>
/// <param name="ViewportHeight">the height of the viewport, in pixels</param>
internal readonly record struct FontEnvironment(double FontSize, double RootFontSize, double LineHeight, double ViewportWidth, double ViewportHeight)
{
    /// <summary>What a canvas that is not in a document is relative to: 10 pixels.</summary>
    public static FontEnvironment Detached { get; } = new(10, 10, 12, 300, 150);
}

/// <summary>One family of a font list: a generic keyword, or a name.</summary>
/// <param name="Name">the family's name or keyword</param>
/// <param name="Generic">whether it is a generic family keyword</param>
internal readonly record struct FontFamilyName(string Name, bool Generic);

/// <summary>
/// The value of a canvas's <c>font</c>: the CSS <c>font</c> shorthand as the canvas reads it (style, variant, weight, stretch, a size resolved against the canvas element, and a
/// family list; a line height is accepted and ignored) and as it gives it back.
/// </summary>
internal sealed class CanvasFont
{
    private static readonly string[] Generics =
        ["serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "math", "emoji", "fangsong"];

    private static readonly string[] Stretches =
        ["ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "semi-expanded", "expanded", "extra-expanded", "ultra-expanded"];

    private static readonly string[] CssWide = ["initial", "inherit", "unset", "revert", "revert-layer", "default"];

    private static readonly string[] SystemFonts = ["caption", "icon", "menu", "message-box", "small-caption", "status-bar"];

    public string Style { get; private init; } = "normal";

    public bool SmallCaps { get; private init; }

    public double Weight { get; private init; } = 400;

    public string Stretch { get; private init; } = "normal";

    public double SizePixels { get; private init; }

    public IReadOnlyList<FontFamilyName> Families { get; private init; } = [];

    /// <summary>The value as the <c>font</c> attribute gives it back: style, variant, weight and stretch only when they are not <c>normal</c> (or 400), then the size in pixels, then the families.</summary>
    public string Serialize()
    {
        var text = new StringBuilder();
        if (Style != "normal")
            text.Append(Style).Append(' ');
        if (SmallCaps)
            text.Append("small-caps ");
        if (Weight != 400)
            text.Append(Weight == 700 ? "bold" : Number(Weight)).Append(' ');
        if (Stretch != "normal")
            text.Append(Stretch).Append(' ');

        text.Append(Number(SizePixels)).Append("px ");
        text.Append(string.Join(", ", Families.Select(f => f.Generic || IsIdentifierList(f.Name) && !f.Name.Contains(' ') ? f.Name : Quote(f.Name))));
        return text.ToString();
    }

    /// <summary>The value for a font that is not valid CSS in the shorthand: the one that came with the canvas, <c>10px sans-serif</c>.</summary>
    public static CanvasFont Default { get; } = new() { SizePixels = 10, Families = [new FontFamilyName("sans-serif", true)] };

    /// <summary>
    /// Reads a font. The text is the CSS <c>font</c> shorthand, with the line height allowed and dropped; a value that is not valid, uses a CSS-wide keyword or a function (<c>var()</c>),
    /// or has more than the font (a <c>;</c>) is not a font, and neither is a system font that is not named.
    /// </summary>
    /// <param name="text">the text</param>
    /// <param name="environment">what relative sizes are relative to</param>
    /// <returns>the font, or null when the text is not a valid font</returns>
    public static CanvasFont? Parse(string text, FontEnvironment environment)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.IndexOfAny([';', '{', '}']) >= 0 && !InsideQuotes(trimmed))
            return null;

        if (CssWide.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return null;

        // A system font: some font of the platform, which is a size and a family here.
        if (SystemFonts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return new CanvasFont { SizePixels = 13, Families = [new FontFamilyName("sans-serif", true)] };

        var tokens = Tokenize(trimmed);
        if (tokens is null)
            return null;

        string? style = null, stretch = null, weightText = null;
        var smallCaps = false;
        var variantSet = false;
        var normalCount = 0;
        var index = 0;

        for (; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var lower = token.ToLowerInvariant();
            if (lower == "normal")
            {
                normalCount++;
                continue;
            }

            if (lower is "italic" && style is null)
                style = "italic";
            else if (lower is "oblique" && style is null)
            {
                style = "oblique";

                // An angle may follow.
                if (index + 1 < tokens.Count && TryAngle(tokens[index + 1], out var degrees) && degrees is >= -90 and <= 90)
                {
                    style = degrees == 14 ? "oblique" : "oblique " + Number(degrees) + "deg";
                    index++;
                }
            }
            else if (lower is "small-caps" && !variantSet)
            {
                smallCaps = true;
                variantSet = true;
            }
            else if (lower is "bold" or "bolder" or "lighter" && weightText is null)
                weightText = lower;
            else if (weightText is null && double.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 1000 && !lower.Contains('e'))
                weightText = lower;
            else if (Stretches.Contains(lower) && stretch is null)
                stretch = lower;
            else
                break;
        }

        // normal may fill at most as many of the four slots as are left empty.
        var slots = (style is null ? 1 : 0) + (variantSet ? 0 : 1) + (weightText is null ? 1 : 0) + (stretch is null ? 1 : 0);
        if (normalCount > slots || index >= tokens.Count)
            return null;

        // The size, and a line height after a slash.
        var sizeToken = tokens[index];
        string? lineHeight = null;
        var slash = sizeToken.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            lineHeight = sizeToken[(slash + 1)..];
            sizeToken = sizeToken[..slash];
        }
        else if (index + 1 < tokens.Count && tokens[index + 1].StartsWith('/'))
        {
            index++;
            lineHeight = tokens[index].Length > 1 ? tokens[index][1..] : (index + 1 < tokens.Count ? tokens[++index] : null);
            if (lineHeight is null)
                return null;
        }

        if (lineHeight is not null && !IsLineHeight(lineHeight))
            return null;

        if (!TrySize(sizeToken, environment, out var size) || size < 0)
            return null;

        index++;
        if (index >= tokens.Count)
            return null;

        var families = ParseFamilies(string.Join(' ', tokens.Skip(index)));
        if (families is null)
            return null;

        var weight = weightText switch
        {
            "bold" => 700,
            "bolder" => 700,
            "lighter" => 100,
            null => 400,
            _ => double.Parse(weightText, CultureInfo.InvariantCulture),
        };

        return new CanvasFont { Style = style ?? "normal", SmallCaps = smallCaps, Weight = weight, Stretch = stretch ?? "normal", SizePixels = size, Families = families };
    }

    private static bool InsideQuotes(string text) => text.Count(c => c == '"') >= 2 && text.IndexOfAny([';', '{', '}']) > text.IndexOf('"', StringComparison.Ordinal);

    // The whitespace-separated pieces of a font, with a quoted string kept whole and a comma a piece of its own.
    private static List<string>? Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            if (c is '"' or '\'')
            {
                i++;
                while (i < text.Length && text[i] != c)
                    i += text[i] == '\\' ? 2 : 1;

                if (i >= text.Length)
                    return null;

                i++;
            }
            else if (c == ',')
            {
                i++;
            }
            else
            {
                var depth = 0;
                while (i < text.Length && (depth > 0 || !char.IsWhiteSpace(text[i]) && text[i] != ',' && text[i] != '"' && text[i] != '\''))
                {
                    if (text[i] == '(')
                        depth++;
                    else if (text[i] == ')')
                        depth--;

                    i++;
                }

                if (text[start..i].Contains('(', StringComparison.Ordinal))
                {
                    // A function: calc() is a size, anything else (var(), if(), env()) is not a font.
                    if (!text[start..i].StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
                        return null;
                }
            }

            tokens.Add(text[start..i]);
        }

        return tokens;
    }

    private static bool IsLineHeight(string text) => text.Equals("normal", StringComparison.OrdinalIgnoreCase)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
        || TryLength(text, FontEnvironment.Detached, out _, allowPercent: true);

    private static bool TryAngle(string text, out double degrees)
    {
        degrees = 0;
        var lower = text.ToLowerInvariant();
        foreach (var (unit, scale) in new[] { ("deg", 1.0), ("grad", 0.9), ("rad", 180 / Math.PI), ("turn", 360.0) })
        {
            if (lower.EndsWith(unit, StringComparison.Ordinal) && double.TryParse(lower[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                degrees = number * scale;
                return true;
            }
        }

        return false;
    }

    private static bool TrySize(string text, FontEnvironment environment, out double pixels)
    {
        pixels = 0;
        var lower = text.ToLowerInvariant();
        var keyword = lower switch
        {
            "xx-small" => 9.0,
            "x-small" => 10,
            "small" => 13,
            "medium" => 16,
            "large" => 18,
            "x-large" => 24,
            "xx-large" => 32,
            "xxx-large" => 48,
            "larger" => environment.FontSize * 1.2,
            "smaller" => environment.FontSize / 1.2,
            _ => -1,
        };

        if (keyword >= 0)
        {
            pixels = keyword;
            return true;
        }

        return TryLength(text, environment, out pixels, allowPercent: true);
    }

    /// <summary>A length in pixels: a number with a unit (relative ones against the environment), a percentage of the element's font size when allowed, or a bare 0.</summary>
    public static bool TryLength(string text, FontEnvironment environment, out double pixels, bool allowPercent = false)
    {
        pixels = 0;
        var lower = text.Trim().ToLowerInvariant();
        if (lower.StartsWith("calc(", StringComparison.Ordinal))
            return false;

        (string Unit, double Scale)[] units =
        [
            ("px", 1), ("pt", 4.0 / 3), ("pc", 16), ("in", 96), ("cm", 96 / 2.54), ("mm", 96 / 25.4), ("q", 96 / 101.6),
            ("rem", environment.RootFontSize), ("rlh", environment.RootFontSize * 1.2), ("em", environment.FontSize), ("ex", environment.FontSize / 2), ("ch", environment.FontSize / 2),
            ("ic", environment.FontSize), ("cap", environment.FontSize * 0.7), ("lh", environment.LineHeight),
            ("vw", environment.ViewportWidth / 100), ("vh", environment.ViewportHeight / 100),
            ("vmin", Math.Min(environment.ViewportWidth, environment.ViewportHeight) / 100), ("vmax", Math.Max(environment.ViewportWidth, environment.ViewportHeight) / 100),
        ];

        // The longest unit first, so that em is not read as the end of rem, nor vh of the end of ... another.
        foreach (var (unit, scale) in units.OrderByDescending(u => u.Unit.Length))
        {
            if (lower.EndsWith(unit, StringComparison.Ordinal) && NumberOf(lower[..^unit.Length]) is { } number)
            {
                pixels = number * scale;
                return true;
            }
        }

        if (allowPercent && lower.EndsWith('%') && NumberOf(lower[..^1]) is { } percent)
        {
            pixels = percent / 100 * environment.FontSize;
            return true;
        }

        return NumberOf(lower) is 0;
    }

    private static double? NumberOf(string text) =>
        text.Length > 0 && !char.IsWhiteSpace(text[0]) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    // A comma-separated family list: strings, generic keywords, and runs of identifiers (one name made of several words).
    private static List<FontFamilyName>? ParseFamilies(string text)
    {
        var families = new List<FontFamilyName>();
        var tokens = Tokenize(text);
        if (tokens is null || tokens.Count == 0)
            return null;

        var words = new List<string>();
        var quoted = (string?)null;

        bool Finish()
        {
            if (quoted is not null)
            {
                if (words.Count > 0)
                    return false;

                families.Add(new FontFamilyName(quoted, false));
            }
            else
            {
                if (words.Count == 0)
                    return false;

                var name = string.Join(' ', words);
                if (words.Count == 1 && Generics.Contains(name, StringComparer.OrdinalIgnoreCase))
                    families.Add(new FontFamilyName(name.ToLowerInvariant(), true));
                else if (words.Any(w => !IsIdentifier(w)) || CssWide.Contains(name, StringComparer.OrdinalIgnoreCase))
                    return false;
                else
                    families.Add(new FontFamilyName(name, false));
            }

            words.Clear();
            quoted = null;
            return true;
        }

        foreach (var token in tokens)
        {
            if (token == ",")
            {
                if (!Finish())
                    return null;
            }
            else if (token[0] is '"' or '\'')
            {
                if (quoted is not null || words.Count > 0)
                    return null;

                quoted = Unquote(token);
            }
            else
            {
                if (quoted is not null)
                    return null;

                words.Add(token);
            }
        }

        return Finish() ? families : null;
    }

    private static string Unquote(string token)
    {
        var inner = token[1..^1];
        var text = new StringBuilder();
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
                i++;

            text.Append(inner[i]);
        }

        return text.ToString();
    }

    private static bool IsIdentifier(string word) =>
        word.Length > 0 && (char.IsLetter(word[0]) || word[0] == '_' || word[0] == '-' && word.Length > 1 && !char.IsDigit(word[1]) || word[0] > 127)
        && word.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' || c > 127);

    private static bool IsIdentifierList(string name) => name.Split(' ').All(IsIdentifier);

    private static string Quote(string name) => "\"" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>A letter or word spacing: a length with its unit, as the canvas gives it back (<c>1PX</c> is <c>1px</c>, a bare <c>0</c> is <c>0px</c>); anything else is not one.</summary>
    public static bool TryParseSpacing(string text, out string canonical)
    {
        canonical = "0px";
        var lower = text.Trim().ToLowerInvariant();
        if (lower.Length == 0)
            return false;

        foreach (var unit in new[] { "px", "pt", "pc", "in", "cm", "mm", "q", "rem", "rlh", "em", "ex", "ch", "ic", "cap", "lh", "vw", "vh", "vmin", "vmax", "vi", "vb" }.OrderByDescending(u => u.Length))
        {
            if (lower.EndsWith(unit, StringComparison.Ordinal) && NumberOf(lower[..^unit.Length]) is { } number)
            {
                canonical = Number(number) + unit;
                return true;
            }
        }

        if (NumberOf(lower) is 0)
            return true;

        return false;
    }

    /// <summary>A spacing in pixels, its relative units against the font the text is drawn with (and the root and viewport of <paramref name="environment"/>).</summary>
    public static double SpacingPixels(string canonical, double fontSize, FontEnvironment environment)
    {
        var relative = environment with { FontSize = fontSize, LineHeight = fontSize * 1.2 };
        return TryLength(canonical, relative, out var pixels) ? pixels : 0;
    }

    internal static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
