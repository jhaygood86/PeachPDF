using System;
using Jint;
using PeachBrackets.ScriptHosting;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;

namespace PeachBrackets.Scripting.Internal;

// The IDL attributes of HTML elements that reflect a content attribute (the HTML standard's "reflect" rules): what a property reads from and writes to the
// attribute for each type (string, url, boolean, the integers, double, enumerated, token list), driven by ReflectionTable.
internal sealed partial class ScriptRuntime
{
    private enum ReflectKind { String, NullableString, Url, Boolean, Long, LimitedLong, UnsignedLong, LimitedUnsignedLong, LimitedUnsignedLongFallback, ClampedUnsignedLong, Double, LimitedDouble, Enum, TokenList, SettableTokenList }

    private sealed class ReflectedAttribute
    {
        public required string Idl { get; init; }

        public required string Attribute { get; init; }

        public required ReflectKind Kind { get; init; }

        public string[] Keywords { get; init; } = [];

        public Dictionary<string, string?> NonCanonical { get; init; } = [];

        public bool Nullable { get; init; }

        // The value when the attribute is missing (and, when none is said, for an invalid one): text for the enumerated and string kinds, a number for the others.
        public string? DefaultText { get; init; } = string.Empty;

        public bool HasInvalid { get; init; }

        public string? InvalidText { get; init; }

        public double DefaultNumber { get; init; }

        public double Min { get; init; }

        public double Max { get; init; } = int.MaxValue;

        public bool TreatNullAsEmpty { get; init; }
    }

    private static Dictionary<string, List<ReflectedAttribute>>? _reflectionTable;

    private static Dictionary<string, List<ReflectedAttribute>> ReflectionByTag()
    {
        if (_reflectionTable is { } existing)
            return existing;

        var table = new Dictionary<string, List<ReflectedAttribute>>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(ReflectionTable.Json);
        foreach (var tag in document.RootElement.EnumerateObject())
        {
            var list = new List<ReflectedAttribute>();
            foreach (var property in tag.Value.EnumerateObject())
                list.Add(ParseReflected(property.Name, property.Value));
            table[tag.Name] = list;
        }

        return _reflectionTable = table;
    }

    private static ReflectedAttribute ParseReflected(string idl, JsonElement spec)
    {
        // A few entries nest the real description under "type".
        if (spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("type", out var nested) && nested.ValueKind == JsonValueKind.Object)
            spec = nested;

        var typeName = spec.ValueKind == JsonValueKind.String ? spec.GetString()! : spec.GetProperty("type").GetString()!;
        var kind = typeName switch
        {
            "url" => ReflectKind.Url,
            "boolean" => ReflectKind.Boolean,
            "long" => ReflectKind.Long,
            "limited long" => ReflectKind.LimitedLong,
            "nullable string" => ReflectKind.NullableString,
            "unsigned long" => ReflectKind.UnsignedLong,
            "limited unsigned long" => ReflectKind.LimitedUnsignedLong,
            "limited unsigned long with fallback" => ReflectKind.LimitedUnsignedLongFallback,
            "clamped unsigned long" => ReflectKind.ClampedUnsignedLong,
            "double" => ReflectKind.Double,
            "limited double" => ReflectKind.LimitedDouble,
            "enum" => ReflectKind.Enum,
            "tokenlist" => ReflectKind.TokenList,
            "settable tokenlist" => ReflectKind.SettableTokenList,
            _ => ReflectKind.String,
        };

        var attribute = idl.ToLowerInvariant();
        var keywords = Array.Empty<string>();
        var nonCanonical = new Dictionary<string, string?>();
        var nullable = false;
        var treatNullAsEmpty = false;
        var hasDefault = false;
        string? defaultText = string.Empty;
        double defaultNumber = kind switch { ReflectKind.LimitedUnsignedLong or ReflectKind.LimitedUnsignedLongFallback => 1, ReflectKind.LimitedLong => -1, _ => 0 };
        var hasInvalid = false;
        string? invalidText = null;
        double min = 0, max = int.MaxValue;
        if (spec.ValueKind == JsonValueKind.Object)
        {
            if (spec.TryGetProperty("domAttrName", out var dom))
                attribute = dom.GetString()!.ToLowerInvariant();
            if (spec.TryGetProperty("keywords", out var words))
                keywords = [.. words.EnumerateArray().Select(w => w.GetString()!)];
            if (spec.TryGetProperty("nonCanon", out var canonical))
            {
                foreach (var pair in canonical.EnumerateObject())
                    nonCanonical[pair.Name] = pair.Value.ValueKind == JsonValueKind.Null ? null : pair.Value.GetString();
            }

            nullable = spec.TryGetProperty("isNullable", out var n) && n.GetBoolean();
            treatNullAsEmpty = spec.TryGetProperty("treatNullAsEmptyString", out var t) && t.GetBoolean();
            if (spec.TryGetProperty("defaultVal", out var d))
            {
                hasDefault = true;
                if (d.ValueKind == JsonValueKind.Null)
                    defaultText = null;
                else if (d.ValueKind == JsonValueKind.String)
                    defaultText = d.GetString();
                else if (d.ValueKind is JsonValueKind.Number)
                    defaultNumber = d.GetDouble();
                else if (d.ValueKind is JsonValueKind.Array)
                    defaultText = d.GetArrayLength() > 1 ? d[1].GetString() : d[0].GetString(); // a default the user agent chooses among these
            }

            if (spec.TryGetProperty("invalidVal", out var invalid))
            {
                hasInvalid = true;
                invalidText = invalid.ValueKind == JsonValueKind.Null ? null : invalid.GetString();
            }

            if (spec.TryGetProperty("min", out var low)) min = low.GetDouble();
            if (spec.TryGetProperty("max", out var high)) max = high.GetDouble();
        }

        _ = hasDefault;
        return new ReflectedAttribute
        {
            Idl = idl, Attribute = attribute, Kind = kind, Keywords = keywords, NonCanonical = nonCanonical, Nullable = nullable, DefaultText = defaultText, HasInvalid = hasInvalid,
            InvalidText = invalidText, DefaultNumber = defaultNumber, Min = min, Max = max, TreatNullAsEmpty = treatNullAsEmpty,
        };
    }

    private static List<ReflectedAttribute>? _globalReflection;

    private static List<ReflectedAttribute> GlobalReflected()
    {
        if (_globalReflection is { } existing)
            return existing;

        using var document = JsonDocument.Parse(ReflectionTable.GlobalJson);
        return _globalReflection = [.. document.RootElement.EnumerateObject().Select(p => ParseReflected(p.Name, p.Value))];
    }

    private static readonly HashSet<string> FocusableTags = new(StringComparer.Ordinal) { "a", "area", "button", "input", "select", "textarea", "iframe", "summary", "object", "embed" };

    private void InstallReflected(ObjectInstance prototype, IEnumerable<ReflectedAttribute> attributes)
    {
        foreach (var reflected in attributes)
        {
            var spec = reflected;
            Js.Accessor(Engine, prototype, spec.Idl, self => ReflectGet(Sn(self), spec, self), (self, value) => ReflectSet(Sn(self), spec, value, self));
        }
    }

    // Attributes of the tags an interface stands for.
    private IEnumerable<ReflectedAttribute> ReflectedFor(string interfaceName)
    {
        var table = ReflectionByTag();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (tag, name) in ElementInterfaces)
        {
            if (name != interfaceName || !table.TryGetValue(tag, out var list))
                continue;

            foreach (var attribute in list)
            {
                if (seen.Add(attribute.Idl))
                    yield return attribute;
            }
        }
    }

    private static bool IsAsciiSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    // The rules for parsing integers: optional white space and sign, then digits; anything after the digits is ignored. Null on an error.
    private static double? ParseIntegerRules(string input)
    {
        var position = 0;
        while (position < input.Length && IsAsciiSpace(input[position]))
            position++;
        if (position >= input.Length)
            return null;

        var sign = 1;
        if (input[position] == '-') { sign = -1; position++; }
        else if (input[position] == '+') position++;
        if (position >= input.Length || input[position] is < '0' or > '9')
            return null;

        double value = 0;
        while (position < input.Length && input[position] is >= '0' and <= '9')
        {
            value = (value * 10) + (input[position] - '0');
            position++;
        }

        return sign * value == 0 ? 0 : sign * value;
    }

    // The rules for parsing floating-point number values. Null on an error.
    private static double? ParseFloatRules(string input)
    {
        var position = 0;
        while (position < input.Length && IsAsciiSpace(input[position]))
            position++;
        if (position >= input.Length)
            return null;

        var start = position;
        if (input[position] == '-' || input[position] == '+')
            position++;
        if (position >= input.Length)
            return null;

        if (input[position] == '.' && position + 1 < input.Length && input[position + 1] is >= '0' and <= '9')
        {
        }
        else if (input[position] is < '0' or > '9')
        {
            return null;
        }
        else
        {
            while (position < input.Length && input[position] is >= '0' and <= '9')
                position++;
        }

        if (position < input.Length && input[position] == '.')
        {
            var afterDot = position + 1;
            var digits = afterDot;
            while (digits < input.Length && input[digits] is >= '0' and <= '9')
                digits++;
            if (digits > afterDot)
                position = digits;
            else
                position++;
        }

        if (position < input.Length && (input[position] == 'e' || input[position] == 'E'))
        {
            var exponent = position + 1;
            if (exponent < input.Length && (input[exponent] == '-' || input[exponent] == '+'))
                exponent++;
            var digits = exponent;
            while (digits < input.Length && input[digits] is >= '0' and <= '9')
                digits++;
            if (digits > exponent)
                position = digits;
        }

        var text = input[start..position].TrimEnd('.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && !double.IsInfinity(parsed) ? parsed : null;
    }

    private JsValue ReflectNumber(double value) => Js.Num(value);

    private string? ReflectEnumGet(ReflectedAttribute spec, string? raw)
    {
        // Missing: its default. Present: the keyword it matches (ASCII case-insensitively), else the invalid default, which a non-canonical keyword maps on.
        string? result;
        if (raw is null)
        {
            result = spec.DefaultText;
        }
        else
        {
            result = spec.HasInvalid ? spec.InvalidText : spec.DefaultText;
            foreach (var keyword in spec.Keywords)
            {
                if (string.Equals(AsciiLower(raw), AsciiLower(keyword), StringComparison.Ordinal))
                {
                    result = keyword;
                    break;
                }
            }
        }

        return result is not null && spec.NonCanonical.TryGetValue(result, out var canonical) ? canonical : result;
    }

    private static string AsciiLower(string text) => string.Create(text.Length, text, (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
            span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
    });

    private JsValue ReflectGet(ScriptNode node, ReflectedAttribute spec, JsValue self)
    {
        var raw = node.GetAttribute(spec.Attribute);
        switch (spec.Kind)
        {
            case ReflectKind.Boolean:
                return Js.Bool(raw is not null);
            case ReflectKind.NullableString:
                return Js.Str(raw);
            case ReflectKind.LimitedLong:
                return ReflectNumber(raw is not null && ParseIntegerRules(raw) is { } ll && ll >= 0 && ll <= int.MaxValue ? ll : spec.DefaultNumber);
            case ReflectKind.Url:
                return Js.Str(raw is null ? string.Empty : ResolveReflectedUrl(node, raw));
            case ReflectKind.Enum:
                return Js.Str(ReflectEnumGet(spec, raw));
            case ReflectKind.Long:
            {
                var defaultNumber = spec.Idl == "tabIndex" ? (FocusableTags.Contains(node.TagName?.ToLowerInvariant() ?? string.Empty) ? 0 : -1) : spec.DefaultNumber;
                return ReflectNumber(raw is not null && ParseIntegerRules(raw) is { } v && v >= int.MinValue && v <= int.MaxValue ? v : defaultNumber);
            }
            case ReflectKind.UnsignedLong:
                return ReflectNumber(raw is not null && ParseIntegerRules(raw) is { } u && u >= 0 && u <= int.MaxValue ? u : spec.DefaultNumber);
            case ReflectKind.LimitedUnsignedLong or ReflectKind.LimitedUnsignedLongFallback:
                return ReflectNumber(raw is not null && ParseIntegerRules(raw) is { } l && l >= 1 && l <= int.MaxValue ? l : spec.DefaultNumber);
            case ReflectKind.ClampedUnsignedLong:
                return ReflectNumber(raw is not null && ParseIntegerRules(raw) is { } c ? Math.Clamp(c, spec.Min, spec.Max) : spec.DefaultNumber);
            case ReflectKind.Double:
                return ReflectNumber(raw is not null && ParseFloatRules(raw) is { } d ? d : spec.DefaultNumber);
            case ReflectKind.LimitedDouble:
                return ReflectNumber(raw is not null && ParseFloatRules(raw) is { } ld && ld > 0 ? ld : spec.DefaultNumber);
            case ReflectKind.TokenList or ReflectKind.SettableTokenList:
            {
                var wrapper = Node(self);
                wrapper.TokenLists ??= [];
                if (!wrapper.TokenLists.TryGetValue(spec.Attribute, out var list))
                    wrapper.TokenLists[spec.Attribute] = list = new TokenListObject(this, node, spec.Attribute);
                return list;
            }
            default:
                return Js.Str(raw ?? string.Empty);
        }
    }

    private void ReflectSet(ScriptNode node, ReflectedAttribute spec, JsValue value, JsValue self)
    {
        switch (spec.Kind)
        {
            case ReflectKind.Boolean:
                if (TypeConverter.ToBoolean(value)) node.SetAttribute(spec.Attribute, string.Empty);
                else node.RemoveAttribute(spec.Attribute);
                break;
            case ReflectKind.Enum:
                if (spec.Nullable && Js.IsNullish(value)) node.RemoveAttribute(spec.Attribute);
                else node.SetAttribute(spec.Attribute, Js.AsText(value));
                break;
            case ReflectKind.NullableString:
                if (Js.IsNullish(value)) node.RemoveAttribute(spec.Attribute);
                else node.SetAttribute(spec.Attribute, Js.AsText(value));
                break;
            case ReflectKind.LimitedLong:
            {
                var number = TypeConverter.ToInt32(value);
                if (number < 0)
                    throw DomError("IndexSizeError", "The value provided is negative.");
                node.SetAttribute(spec.Attribute, number.ToString(CultureInfo.InvariantCulture));
                break;
            }
            case ReflectKind.Long:
                node.SetAttribute(spec.Attribute, TypeConverter.ToInt32(value).ToString(CultureInfo.InvariantCulture));
                break;
            case ReflectKind.UnsignedLong or ReflectKind.ClampedUnsignedLong:
            {
                var number = TypeConverter.ToUint32(value);
                node.SetAttribute(spec.Attribute, (number > int.MaxValue ? spec.DefaultNumber : number).ToString(CultureInfo.InvariantCulture));
                break;
            }

            case ReflectKind.LimitedUnsignedLong:
            {
                var number = TypeConverter.ToUint32(value);
                if (number == 0)
                    throw DomError("IndexSizeError", "The value provided is 0, which is an invalid size.");
                node.SetAttribute(spec.Attribute, (number > int.MaxValue ? spec.DefaultNumber : number).ToString(CultureInfo.InvariantCulture));
                break;
            }

            case ReflectKind.LimitedUnsignedLongFallback:
            {
                var number = TypeConverter.ToUint32(value);
                node.SetAttribute(spec.Attribute, (number is 0 or > int.MaxValue ? spec.DefaultNumber : number).ToString(CultureInfo.InvariantCulture));
                break;
            }

            case ReflectKind.Double or ReflectKind.LimitedDouble:
            {
                var number = TypeConverter.ToNumber(value);
                if (double.IsNaN(number) || double.IsInfinity(number))
                    throw TypeError("The provided double value is non-finite.");
                if (spec.Kind == ReflectKind.LimitedDouble && number <= 0)
                    number = spec.DefaultNumber;
                node.SetAttribute(spec.Attribute, Js.AsText(Js.Num(number)));
                break;
            }

            case ReflectKind.SettableTokenList:
                node.SetAttribute(spec.Attribute, Js.AsText(value));
                break;
            case ReflectKind.TokenList:
                break;
            default:
                node.SetAttribute(spec.Attribute, spec.TreatNullAsEmpty && value.IsNull() ? string.Empty : Js.AsText(value));
                break;
        }
    }

    // A URL attribute as the page sees it: resolved against the document's base, or as written when it does not parse.
    private string ResolveReflectedUrl(ScriptNode node, string raw)
    {
        LoadWebBundle();
        _resolveUrl ??= Engine.Evaluate("(function (v, b) { try { return new URL(v, b).href; } catch (e) { return null; } })");
        var result = _resolveUrl.Call(JsValue.Undefined, [Js.Str(raw), Js.Str(BaseOf(node.OwnerDocument))]);
        return result.IsNull() ? raw : Js.AsText(result);
    }

    private JsValue? _resolveUrl;
}
