using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeachBrackets.Scripting.Internal;

/// <summary>What an image's media conditions and <c>sizes</c> lengths are worked out against: the viewport, the font size lengths in em are relative to, the device pixel ratio.</summary>
internal readonly record struct ImageViewport(double Width, double Height, double FontSize = 16, double DevicePixelRatio = 1);

/// <summary>One candidate of a <c>srcset</c>: an address and the descriptors it came with.</summary>
internal readonly record struct SrcsetCandidate(string Url, double? Width, double? Density, double? Height);

/// <summary>
/// The responsive image algorithms of the HTML standard (4.8.4.3): parsing <c>srcset</c> and <c>sizes</c> attributes and choosing the candidate a
/// viewport and a screen of a given pixel density call for. <c>sizes</c> is read as CSS component values (comments, escapes, nested blocks, <c>calc()</c>
/// and its siblings), and its media conditions are evaluated with the three-valued logic of Media Queries 4: a condition a user agent does not
/// understand is "unknown", which counts as not matching.
/// </summary>
internal static class ImageSources
{
    // ---------------------------------------------------------------- srcset

    /// <summary>HTML "parse a srcset attribute": the candidates, with every candidate whose descriptors are not valid left out.</summary>
    public static List<SrcsetCandidate> ParseSrcset(string input)
    {
        var candidates = new List<SrcsetCandidate>();
        var position = 0;

        static bool Space(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

        while (true)
        {
            while (position < input.Length && (Space(input[position]) || input[position] == ','))
                position++;

            if (position >= input.Length)
                return candidates;

            var start = position;
            while (position < input.Length && !Space(input[position]))
                position++;

            var url = input[start..position];
            var descriptors = new List<string>();
            if (url.EndsWith(','))
            {
                url = url.TrimEnd(',');
            }
            else
            {
                // Tokenize the descriptors: white space separates them, a comma ends them (unless inside parentheses), and a comment-free reading otherwise.
                var current = new StringBuilder();
                var inParentheses = false;
                var state = 0; // 0 in descriptor, 1 in parens, 2 after descriptor
                while (true)
                {
                    var c = position < input.Length ? input[position] : '\0';
                    var end = position >= input.Length;
                    switch (state)
                    {
                        case 0:
                            if (Space(c) && !end)
                            {
                                if (current.Length > 0) { descriptors.Add(current.ToString()); current.Clear(); state = 2; }
                            }
                            else if (c == ',' && !end)
                            {
                                position++;
                                if (current.Length > 0) descriptors.Add(current.ToString());
                                goto done;
                            }
                            else if (c == '(' && !end)
                            {
                                current.Append(c);
                                state = 1;
                            }
                            else if (end)
                            {
                                if (current.Length > 0) descriptors.Add(current.ToString());
                                goto done;
                            }
                            else
                            {
                                current.Append(c);
                            }

                            break;
                        case 1:
                            if (c == ')' && !end)
                            {
                                current.Append(c);
                                state = 0;
                            }
                            else if (end)
                            {
                                descriptors.Add(current.ToString());
                                goto done;
                            }
                            else
                            {
                                current.Append(c);
                            }

                            break;
                        default:
                            if (end)
                                goto done;
                            if (!Space(c))
                            {
                                state = 0;
                                continue; // reconsume in the descriptor state
                            }

                            break;
                    }

                    position++;
                    _ = inParentheses;
                }

            done:
                _ = inParentheses;
            }

            if (url.Length == 0)
                continue;

            double? width = null, density = null, height = null;
            var error = false;
            foreach (var descriptor in descriptors)
            {
                var last = descriptor[^1];
                var number = descriptor[..^1];
                if (last == 'w')
                {
                    if (width is not null || density is not null || !TryNonNegativeInteger(number, out var w) || w == 0) { error = true; break; }
                    width = w;
                }
                else if (last == 'x')
                {
                    if (width is not null || density is not null || height is not null || !TryFloat(number, out var d) || d < 0) { error = true; break; }
                    density = d;
                }
                else if (last == 'h')
                {
                    if (density is not null || height is not null || !TryNonNegativeInteger(number, out var h) || h == 0) { error = true; break; }
                    height = h;
                }
                else
                {
                    error = true;
                    break;
                }
            }

            if (!error && height is not null && width is null)
                error = true;

            if (!error)
                candidates.Add(new SrcsetCandidate(url, width, density, height));
        }
    }

    private static bool TryNonNegativeInteger(string text, out double value)
    {
        value = 0;
        if (text.Length == 0 || !text.All(char.IsAsciiDigit))
            return false;

        return double.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    // The rules for parsing floating-point number values: an optional minus, digits, an optional fraction and exponent; nothing else.
    private static bool TryFloat(string text, out double value)
    {
        value = 0;
        var i = 0;
        if (i < text.Length && text[i] == '-')
            i++;

        var digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
        if (i < text.Length && text[i] == '.')
        {
            i++;
            var fraction = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; fraction++; }
            if (fraction == 0)
                return false;

            digits += fraction;
        }

        if (digits == 0)
            return false;

        if (i < text.Length && text[i] is 'e' or 'E')
        {
            var j = i + 1;
            if (j < text.Length && text[j] is '+' or '-')
                j++;

            var exponent = 0;
            while (j < text.Length && char.IsAsciiDigit(text[j])) { j++; exponent++; }
            if (exponent == 0)
                return false;

            i = j;
        }

        return i == text.Length && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    // ---------------------------------------------------------------- selection

    /// <summary>
    /// The address a set of candidates (and the <c>src</c> beside them) selects for a viewport and screen, or null for none: widths become densities against
    /// the source size, a <c>src</c> counts as a 1x candidate unless the set has one or uses widths, and the choice is the smallest density that is enough.
    /// </summary>
    public static string? Select(IReadOnlyList<SrcsetCandidate> candidates, string? src, double sourceSize, double devicePixelRatio)
    {
        var set = new List<(string Url, double Density)>();
        var hasWidth = candidates.Any(c => c.Width is not null);
        foreach (var c in candidates)
        {
            var density = c.Width is { } w ? (sourceSize > 0 ? w / sourceSize : double.PositiveInfinity) : c.Density ?? 1;
            set.Add((c.Url, density));
        }

        if (!string.IsNullOrEmpty(src) && !hasWidth && !set.Any(c => c.Density == 1))
            set.Add((src, 1));

        if (set.Count == 0)
            return null;

        // A later candidate of a density an earlier one has is left out.
        var unique = new List<(string Url, double Density)>();
        foreach (var c in set)
        {
            if (!unique.Any(u => u.Density == c.Density))
                unique.Add(c);
        }

        var enough = unique.Where(c => c.Density >= devicePixelRatio).OrderBy(c => c.Density).ToList();
        return (enough.Count > 0 ? enough[0] : unique.OrderByDescending(c => c.Density).First()).Url;
    }

    // ---------------------------------------------------------------- component values

    private abstract record Token;
    private sealed record Space : Token;
    private sealed record Comma : Token;
    private sealed record Colon : Token;
    private sealed record Ident(string Name) : Token;
    private sealed record Number(double Value, bool Signed) : Token;
    private sealed record Dimension(double Value, string Unit) : Token;
    private sealed record Percentage(double Value) : Token;
    private sealed record Text(string Value) : Token;
    private sealed record Delim(char Character) : Token;
    private sealed record Function(string Name, List<Token> Arguments) : Token;
    private sealed record Block(char Open, List<Token> Contents) : Token;

    // The CSS tokenizer, enough for this: comments dropped, escapes decoded, blocks and functions nested. An unclosed one runs to the end of the input.
    private sealed class Tokenizer(string input)
    {
        private int _i;

        public List<Token> Run() => Until('\0');

        private char Peek(int offset = 0) => _i + offset < input.Length ? input[_i + offset] : '\0';

        private bool End => _i >= input.Length;

        private List<Token> Until(char close)
        {
            var tokens = new List<Token>();
            while (!End)
            {
                var c = input[_i];
                if (c == '/' && Peek(1) == '*')
                {
                    var end = input.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    _i = end < 0 ? input.Length : end + 2;
                    continue;
                }

                if (c is ' ' or '\t' or '\n' or '\r' or '\f')
                {
                    while (!End && input[_i] is ' ' or '\t' or '\n' or '\r' or '\f')
                        _i++;

                    tokens.Add(new Space());
                    continue;
                }

                if (close != '\0' && c == close)
                {
                    _i++;
                    return tokens;
                }

                if (c is ')' or ']' or '}')
                {
                    _i++;
                    tokens.Add(new Delim(c));
                    continue;
                }

                if (c == ',') { _i++; tokens.Add(new Comma()); continue; }
                if (c == ':') { _i++; tokens.Add(new Colon()); continue; }
                if (c is '(' or '[' or '{')
                {
                    _i++;
                    tokens.Add(new Block(c, Until(c == '(' ? ')' : c == '[' ? ']' : '}')));
                    continue;
                }

                if (c is '"' or '\'')
                {
                    tokens.Add(new Text(ReadString(c)));
                    continue;
                }

                if (StartsNumber())
                {
                    tokens.Add(ReadNumeric());
                    continue;
                }

                if (StartsIdent())
                {
                    var name = ReadName();
                    if (Peek() == '(')
                    {
                        _i++;
                        tokens.Add(new Function(name, Until(')')));
                    }
                    else
                    {
                        tokens.Add(new Ident(name));
                    }

                    continue;
                }

                _i++;
                tokens.Add(new Delim(c));
            }

            return tokens;
        }

        private bool StartsNumber()
        {
            var c = Peek();
            if (char.IsAsciiDigit(c)) return true;
            if (c == '.') return char.IsAsciiDigit(Peek(1));
            if (c is '+' or '-') return char.IsAsciiDigit(Peek(1)) || (Peek(1) == '.' && char.IsAsciiDigit(Peek(2)));
            return false;
        }

        private bool StartsIdent()
        {
            var c = Peek();
            if (c == '-') return Peek(1) == '-' || IsNameStart(Peek(1)) || Peek(1) == '\\';
            return IsNameStart(c) || c == '\\';
        }

        private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_' || c > 0x7F;

        private static bool IsName(char c) => IsNameStart(c) || char.IsAsciiDigit(c) || c == '-';

        // An identifier, with the escapes in it decoded.
        private string ReadName()
        {
            var name = new StringBuilder();
            while (!End)
            {
                var c = input[_i];
                if (IsName(c))
                {
                    name.Append(c);
                    _i++;
                }
                else if (c == '\\' && _i + 1 < input.Length && input[_i + 1] != '\n')
                {
                    _i++;
                    name.Append(ReadEscape());
                }
                else
                {
                    break;
                }
            }

            return name.ToString();
        }

        private string ReadEscape()
        {
            var c = input[_i];
            if (Uri.IsHexDigit(c))
            {
                var start = _i;
                while (_i < input.Length && _i - start < 6 && Uri.IsHexDigit(input[_i]))
                    _i++;

                var value = int.Parse(input[start.._i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (_i < input.Length && input[_i] is ' ' or '\t' or '\n')
                    _i++;

                return value is 0 or > 0x10FFFF || (value is >= 0xD800 and <= 0xDFFF) ? "�" : char.ConvertFromUtf32(value);
            }

            _i++;
            return c.ToString();
        }

        private string ReadString(char quote)
        {
            _i++;
            var text = new StringBuilder();
            while (!End)
            {
                var c = input[_i];
                if (c == quote) { _i++; break; }
                if (c == '\\' && _i + 1 < input.Length)
                {
                    _i++;
                    if (input[_i] == '\n') { _i++; continue; }
                    text.Append(ReadEscape());
                    continue;
                }

                text.Append(c);
                _i++;
            }

            return text.ToString();
        }

        private Token ReadNumeric()
        {
            var start = _i;
            var signed = Peek() is '+' or '-';
            if (signed) _i++;
            while (char.IsAsciiDigit(Peek())) _i++;
            if (Peek() == '.' && char.IsAsciiDigit(Peek(1)))
            {
                _i++;
                while (char.IsAsciiDigit(Peek())) _i++;
            }

            if (Peek() is 'e' or 'E' && (char.IsAsciiDigit(Peek(1)) || (Peek(1) is '+' or '-' && char.IsAsciiDigit(Peek(2)))))
            {
                _i += 2;
                while (char.IsAsciiDigit(Peek())) _i++;
            }

            var value = double.Parse(input[start.._i], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (Peek() == '%') { _i++; return new Percentage(value); }
            if (StartsIdent()) return new Dimension(value, ReadName());
            return new Number(value, signed);
        }
    }

    // ---------------------------------------------------------------- lengths and calc()

    private static readonly Dictionary<string, double> AbsoluteUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["px"] = 1, ["cm"] = 96 / 2.54, ["mm"] = 96 / 25.4, ["q"] = 96 / 101.6, ["in"] = 96, ["pt"] = 96.0 / 72, ["pc"] = 16,
    };

    private static double? UnitToPixels(string unit, ImageViewport viewport) => unit.ToLowerInvariant() switch
    {
        "em" or "rem" => viewport.FontSize,
        "ex" or "ch" => viewport.FontSize / 2,
        "vw" or "svw" or "lvw" or "dvw" => viewport.Width / 100,
        "vh" or "svh" or "lvh" or "dvh" => viewport.Height / 100,
        "vmin" or "svmin" or "lvmin" or "dvmin" => Math.Min(viewport.Width, viewport.Height) / 100,
        "vmax" or "svmax" or "lvmax" or "dvmax" => Math.Max(viewport.Width, viewport.Height) / 100,
        var absolute when AbsoluteUnits.TryGetValue(absolute, out var factor) => factor,
        _ => null,
    };

    // A length (in pixels) from component values that are all there is to it: a dimension, a unitless zero, or calc()/min()/max()/clamp().
    private static bool TryLength(IReadOnlyList<Token> tokens, ImageViewport viewport, out double pixels)
    {
        pixels = 0;
        var meaningful = tokens.Where(t => t is not Space).ToList();
        if (meaningful.Count != 1)
            return false;

        return TryLengthToken(meaningful[0], viewport, out pixels);
    }

    private static bool TryLengthToken(Token token, ImageViewport viewport, out double pixels)
    {
        pixels = 0;
        switch (token)
        {
            case Dimension d when UnitToPixels(d.Unit, viewport) is { } factor:
                pixels = d.Value * factor;
                return true;
            case Number { Value: 0 }:
                return true;
            case Function f when f.Name.ToLowerInvariant() is "calc" or "min" or "max" or "clamp" or "-webkit-calc":
                return TryMath(f, viewport, out var value) && value.IsLength && (pixels = value.Amount) == pixels;
            default:
                return false;
        }
    }

    private readonly record struct Quantity(double Amount, bool IsLength);

    // calc(), min(), max() and clamp() with + - * / over lengths and numbers; anything else is not a length.
    private static bool TryMath(Function function, ImageViewport viewport, out Quantity result)
    {
        result = default;
        var name = function.Name.ToLowerInvariant();
        if (name is "calc" or "-webkit-calc")
            return TrySum(function.Arguments.Where(t => t is not Space).ToList(), viewport, out result);

        var arguments = new List<List<Token>> { new() };
        foreach (var t in function.Arguments)
        {
            if (t is Comma) arguments.Add([]);
            else arguments[^1].Add(t);
        }

        var values = new List<Quantity>();
        foreach (var argument in arguments)
        {
            if (!TrySum(argument.Where(t => t is not Space).ToList(), viewport, out var value))
                return false;

            values.Add(value);
        }

        if (values.Select(v => v.IsLength).Distinct().Count() > 1)
            return false;

        switch (name)
        {
            case "min" when values.Count >= 1:
                result = new Quantity(values.Min(v => v.Amount), values[0].IsLength);
                return true;
            case "max" when values.Count >= 1:
                result = new Quantity(values.Max(v => v.Amount), values[0].IsLength);
                return true;
            case "clamp" when values.Count == 3:
                result = new Quantity(Math.Max(values[0].Amount, Math.Min(values[1].Amount, values[2].Amount)), values[0].IsLength);
                return true;
            default:
                return false;
        }
    }

    // A calculation's sum: terms joined by + and - (each with white space around it, as calc() requires), each term a product of factors.
    private static bool TrySum(List<Token> tokens, ImageViewport viewport, out Quantity result)
    {
        result = default;
        if (tokens.Count == 0)
            return false;

        // The tokens here have lost their white space; a + or - that is an operator arrives as a Delim, or as the sign of a Number that follows a value.
        var terms = new List<(char Sign, List<Token> Factors)> { ('+', []) };
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t is Delim { Character: '+' or '-' } d && terms[^1].Factors.Count > 0)
                terms.Add((d.Character, []));
            else
                terms[^1].Factors.Add(t);
        }

        double? total = null;
        bool? isLength = null;
        foreach (var (sign, factors) in terms)
        {
            if (!TryProduct(factors, viewport, out var term))
                return false;

            if (isLength is not null && isLength != term.IsLength)
                return false;

            isLength = term.IsLength;
            total = (total ?? 0) + (sign == '-' ? -term.Amount : term.Amount);
        }

        result = new Quantity(total!.Value, isLength!.Value);
        return true;
    }

    private static bool TryProduct(List<Token> factors, ImageViewport viewport, out Quantity result)
    {
        result = default;
        Quantity? product = null;
        var divide = false;
        foreach (var t in factors)
        {
            if (t is Delim { Character: '*' }) { divide = false; continue; }
            if (t is Delim { Character: '/' }) { divide = true; continue; }

            Quantity value;
            switch (t)
            {
                case Number n:
                    value = new Quantity(n.Value, false);
                    break;
                case Block { Open: '(' } b when TrySum(b.Contents.Where(x => x is not Space).ToList(), viewport, out var inner):
                    value = inner;
                    break;
                case Function f when TryMath(f, viewport, out var nested):
                    value = nested;
                    break;
                default:
                    if (!TryLengthToken(t, viewport, out var pixels) || t is Number)
                        return false;
                    value = new Quantity(pixels, true);
                    break;
            }

            if (product is null)
            {
                product = value;
            }
            else if (divide)
            {
                if (value.IsLength || value.Amount == 0)
                    return false;

                product = new Quantity(product.Value.Amount / value.Amount, product.Value.IsLength);
            }
            else
            {
                if (product.Value.IsLength && value.IsLength)
                    return false;

                product = new Quantity(product.Value.Amount * value.Amount, product.Value.IsLength || value.IsLength);
            }
        }

        if (product is null)
            return false;

        result = product.Value;
        return true;
    }

    // ---------------------------------------------------------------- sizes

    /// <summary>
    /// The source size HTML "parse a sizes attribute" gives (in CSS pixels): the first entry whose media condition is true. <c>100vw</c> when none is, or when
    /// the attribute is missing. A first match of <c>auto</c> is reported through <paramref name="auto"/> so the caller can use the laid-out width.
    /// </summary>
    public static double SourceSize(string? sizes, ImageViewport viewport, out bool auto)
    {
        auto = false;
        if (sizes is not null)
        {
            var tokens = new Tokenizer(sizes).Run();
            var entry = new List<Token>();
            for (var i = 0; i <= tokens.Count; i++)
            {
                if (i < tokens.Count && tokens[i] is not Comma)
                {
                    entry.Add(tokens[i]);
                    continue;
                }

                if (TryEntry(entry, viewport, out var size, out var isAuto))
                {
                    auto = isAuto;
                    return size;
                }

                entry = [];
            }
        }

        return viewport.Width;
    }

    private static bool TryEntry(List<Token> entry, ImageViewport viewport, out double size, out bool auto)
    {
        size = 0;
        auto = false;
        var tokens = entry.ToList();
        while (tokens.Count > 0 && tokens[^1] is Space)
            tokens.RemoveAt(tokens.Count - 1);

        if (tokens.Count == 0)
            return false;

        var last = tokens[^1];
        if (last is Ident { Name: var name } && name.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            auto = true;
        }
        else if (last is Dimension { Value: < 0 })
        {
            return false;
        }
        else if (!TryLengthToken(last, viewport, out size))
        {
            return false;
        }

        // A length a calculation made negative is no wider than nothing.
        size = Math.Max(0, size);
        tokens.RemoveAt(tokens.Count - 1);
        while (tokens.Count > 0 && tokens[^1] is Space) tokens.RemoveAt(tokens.Count - 1);
        while (tokens.Count > 0 && tokens[0] is Space) tokens.RemoveAt(0);
        if (tokens.Count == 0)
            return true;

        return EvaluateCondition(tokens, viewport) == true;
    }

    // ---------------------------------------------------------------- media conditions

    /// <summary>Whether a media query list (as an <c>source</c>'s <c>media</c> attribute has it) matches the viewport.</summary>
    public static bool MediaMatches(string? media, ImageViewport viewport)
    {
        if (string.IsNullOrWhiteSpace(media))
            return true;

        var tokens = new Tokenizer(media).Run();
        var query = new List<Token>();
        for (var i = 0; i <= tokens.Count; i++)
        {
            if (i < tokens.Count && tokens[i] is not Comma)
            {
                query.Add(tokens[i]);
                continue;
            }

            if (EvaluateQuery(query, viewport))
                return true;

            query = [];
        }

        return false;
    }

    // [not | only]? <media-type> [and <media-condition-without-or>]? | <media-condition>
    private static bool EvaluateQuery(List<Token> query, ImageViewport viewport)
    {
        var tokens = query.Where(t => t is not Space).ToList();
        if (tokens.Count == 0)
            return true;

        var i = 0;
        var negate = false;
        if (tokens[0] is Ident { Name: var first } && (first.Equals("not", StringComparison.OrdinalIgnoreCase) || first.Equals("only", StringComparison.OrdinalIgnoreCase)) && tokens.Count > 1 && tokens[1] is Ident)
        {
            negate = first.Equals("not", StringComparison.OrdinalIgnoreCase);
            i = 1;
        }

        if (tokens[i] is Ident type && !type.Name.Equals("not", StringComparison.OrdinalIgnoreCase))
        {
            var matchesType = type.Name.Equals("all", StringComparison.OrdinalIgnoreCase) || type.Name.Equals("screen", StringComparison.OrdinalIgnoreCase);
            var known = matchesType || type.Name.Equals("print", StringComparison.OrdinalIgnoreCase);
            if (!known)
                return false;

            var result = matchesType;
            if (tokens.Count > i + 1)
            {
                if (tokens[i + 1] is not Ident { Name: var and } || !and.Equals("and", StringComparison.OrdinalIgnoreCase))
                    return false;

                result &= EvaluateCondition([.. tokens.Skip(i + 2)], viewport) == true;
            }

            return negate ? !result : result;
        }

        return EvaluateCondition(tokens, viewport) == true;
    }

    // <media-condition>: not <in-parens> | <in-parens> [[and <in-parens>]* | [or <in-parens>]*]; true, false, or null for "unknown"; invalid is false.
    private static bool? EvaluateCondition(List<Token> input, ImageViewport viewport)
    {
        var tokens = input.Where(t => t is not Space).ToList();
        if (tokens.Count == 0)
            return false;

        if (tokens[0] is Ident { Name: var name } && name.Equals("not", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Count != 2)
                return false;

            var inner = EvaluateInParens(tokens[1], viewport, out var valid);
            return !valid ? false : inner is null ? null : !inner;
        }

        bool? result = null;
        var first = true;
        string? connective = null;
        for (var i = 0; i < tokens.Count; i += 2)
        {
            if (i > 0)
            {
                if (tokens[i - 1] is not Ident { Name: var word } || !(word.Equals("and", StringComparison.OrdinalIgnoreCase) || word.Equals("or", StringComparison.OrdinalIgnoreCase)))
                    return false;

                var op = word.ToLowerInvariant();
                if (connective is not null && connective != op)
                    return false;

                connective = op;
            }

            if (i >= tokens.Count)
                return false;

            var value = EvaluateInParens(tokens[i], viewport, out var valid);
            if (!valid)
                return false;

            if (first)
            {
                result = value;
                first = false;
            }
            else if (connective == "and")
            {
                result = result == false || value == false ? false : result is null || value is null ? null : true;
            }
            else
            {
                result = result == true || value == true ? true : result is null || value is null ? null : false;
            }
        }

        return tokens.Count % 2 == 1 ? result : false;
    }

    // <media-in-parens> = ( <media-condition> ) | <media-feature> | <general-enclosed>; valid is false for something that is not any of them.
    private static bool? EvaluateInParens(Token token, ImageViewport viewport, out bool valid)
    {
        valid = true;
        switch (token)
        {
            case Block { Open: '(' } block:
            {
                var contents = block.Contents.Where(t => t is not Space).ToList();
                if (contents.Count > 0 && (contents[0] is Block { Open: '(' } || contents[0] is Ident { Name: var notName } && notName.Equals("not", StringComparison.OrdinalIgnoreCase)))
                {
                    var inner = EvaluateCondition(block.Contents, viewport);
                    return inner;
                }

                return EvaluateFeature(contents, viewport);
            }

            case Function:
                return null;
            default:
                valid = false;
                return false;
        }
    }

    // A media feature: (name), (name: value), (min-name: value), (name <op> value), (value <op> name): true, false, or null for a feature or value not understood.
    private static bool? EvaluateFeature(List<Token> tokens, ImageViewport viewport)
    {
        if (tokens.Count == 0)
            return null;

        string name;
        string? comparison = null;
        List<Token>? value = null;

        if (tokens[0] is Ident { Name: var featureName } && tokens.Count >= 3 && tokens[1] is Colon)
        {
            name = featureName.ToLowerInvariant();
            value = tokens.Skip(2).ToList();
        }
        else if (tokens[0] is Ident { Name: var booleanName } && tokens.Count == 1)
        {
            name = booleanName.ToLowerInvariant();
        }
        else if (tokens[0] is Ident { Name: var rangeName } && tokens.Count >= 3 && tokens[1] is Delim { Character: '<' or '>' or '=' })
        {
            name = rangeName.ToLowerInvariant();
            var (op, rest) = ReadComparison(tokens.Skip(1).ToList());
            if (op is null) return null;
            comparison = op;
            value = rest;
        }
        else
        {
            return null;
        }

        // min- and max- prefixes turn a plain feature into a range test.
        var minMax = name.StartsWith("min-", StringComparison.Ordinal) ? ">=" : name.StartsWith("max-", StringComparison.Ordinal) ? "<=" : null;
        var baseName = minMax is null ? name : name[4..];
        if (minMax is not null && comparison is not null)
            return null;

        var op2 = comparison ?? minMax ?? "=";
        switch (baseName)
        {
            case "width" or "height":
            {
                var actual = baseName == "width" ? viewport.Width : viewport.Height;
                if (value is null)
                    return actual != 0;

                if (!TryLength(value, viewport, out var wanted) || (minMax is not null && wanted < 0))
                    return null;

                return Compare(actual, wanted, op2);
            }

            case "aspect-ratio":
            {
                var actual = viewport.Width / viewport.Height;
                if (value is null)
                    return true;

                var parts = value.Where(t => t is not Space).ToList();
                double? ratio = parts switch
                {
                    [Number n] => n.Value,
                    [Number a, Delim { Character: '/' }, Number b] when b.Value != 0 => a.Value / b.Value,
                    _ => null,
                };
                return ratio is null || ratio < 0 ? null : Compare(actual, ratio.Value, op2);
            }

            case "orientation":
                return value is [.., Ident { Name: var orientation }] && value.Count(t => t is not Space) == 1
                    ? orientation.ToLowerInvariant() switch
                    {
                        "portrait" => viewport.Height >= viewport.Width,
                        "landscape" => viewport.Width > viewport.Height,
                        _ => null,
                    }
                    : null;

            case "resolution":
            {
                if (value is null)
                    return true;

                var parts = value.Where(t => t is not Space).ToList();
                if (parts is not [Dimension d])
                    return null;

                double? dppx = d.Unit.ToLowerInvariant() switch { "dppx" or "x" => d.Value, "dpi" => d.Value / 96, "dpcm" => d.Value * 2.54 / 96, _ => null };
                return dppx is null || dppx < 0 ? null : Compare(viewport.DevicePixelRatio, dppx.Value, op2);
            }

            case "color":
                return value is null ? true : value.Count(t => t is not Space) == 1 && value.First(t => t is not Space) is Number colorBits ? Compare(8, colorBits.Value, op2) : null;

            case "monochrome":
                return value is null ? false : value.Count(t => t is not Space) == 1 && value.First(t => t is not Space) is Number monochromeBits ? Compare(0, monochromeBits.Value, op2) : null;

            default:
                return null;
        }
    }

    private static (string? Operator, List<Token> Remainder) ReadComparison(List<Token> tokens)
    {
        var op = new StringBuilder();
        var i = 0;
        while (i < tokens.Count && tokens[i] is Delim { Character: '<' or '>' or '=' } d && op.Length < 2)
        {
            op.Append(d.Character);
            i++;
        }

        var text = op.ToString();
        return text is "<" or ">" or "<=" or ">=" or "=" ? (text, tokens.Skip(i).ToList()) : (null, tokens);
    }

    private static bool Compare(double actual, double wanted, string op) => op switch
    {
        ">=" => actual >= wanted,
        "<=" => actual <= wanted,
        ">" => actual > wanted,
        "<" => actual < wanted,
        _ => actual == wanted,
    };
}
