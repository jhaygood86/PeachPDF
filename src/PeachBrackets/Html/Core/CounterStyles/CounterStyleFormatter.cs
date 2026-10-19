#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.CounterStyles
{
    /// <summary>
    /// Turns a counter value into text with an author-defined <see cref="CounterStyle"/>, following CSS
    /// Counter Styles Level 3 §2: range check, the system's algorithm, the negative sign, padding, and the
    /// <c>fallback</c> chain for anything the style cannot represent.
    /// </summary>
    internal static class CounterStyleFormatter
    {
        // A fallback chain longer than this is treated as a cycle and ends in decimal (§2.1).
        private const int MaxFallbackDepth = 8;

        /// <summary>A style with its <c>extends</c> chain resolved: the chain head's own descriptors win.</summary>
        private readonly record struct Flattened(
            CounterStyle Root,
            string? BuiltinBase,
            (string Prefix, string Suffix)? Negative,
            IReadOnlyList<(long Lower, long Upper)>? Range,
            (int Length, string Symbol)? Pad,
            string? Fallback,
            string? Prefix,
            string? Suffix);

        public static string Format(int number, CounterStyle style, CounterStyleRegistry? registry) =>
            Format(number, style, registry, 0);

        /// <summary>The marker <c>prefix</c> and <c>suffix</c> (defaults: empty and <c>". "</c>, §3.4).</summary>
        public static (string Prefix, string Suffix) GetAffixes(CounterStyle style, CounterStyleRegistry? registry)
        {
            var flat = Flatten(style, registry);
            return (flat.Prefix ?? string.Empty, flat.Suffix ?? ". ");
        }

        private static Flattened Flatten(CounterStyle style, CounterStyleRegistry? registry)
        {
            var negative = style.Negative;
            var range = style.Range;
            var pad = style.Pad;
            var fallback = style.Fallback;
            var prefix = style.Prefix;
            var suffix = style.Suffix;

            var current = style;
            string? builtinBase = null;
            HashSet<CounterStyle>? seen = null;

            while (current.System == CounterSystem.Extends)
            {
                (seen ??= []).Add(current);

                if (current.ExtendsName is null || registry is null || !registry.TryGet(current.ExtendsName, out var next))
                {
                    // Extending a predefined style (or one that doesn't exist, which behaves as decimal).
                    builtinBase = current.ExtendsName ?? Keywords.Decimal;
                    break;
                }

                if (seen.Contains(next))
                {
                    // §2.1: an extends cycle makes every style in it extend decimal.
                    builtinBase = Keywords.Decimal;
                    break;
                }

                current = next;
                negative ??= current.Negative;
                range ??= current.Range;
                pad ??= current.Pad;
                fallback ??= current.Fallback;
                prefix ??= current.Prefix;
                suffix ??= current.Suffix;
            }

            return new Flattened(current, builtinBase, negative, range, pad, fallback, prefix, suffix);
        }

        private static string Format(int number, CounterStyle style, CounterStyleRegistry? registry, int depth)
        {
            var flat = Flatten(style, registry);
            var system = flat.Root.System;
            var isBuiltinBase = flat.BuiltinBase is not null;

            if (!InRange(number, flat, isBuiltinBase)) return FormatFallback(number, flat, registry, depth);

            // cyclic and fixed are sign-less: a negative value simply indexes the list. The rest count
            // magnitudes and mark a negative value with the negative sign.
            var signed = isBuiltinBase || system is CounterSystem.Symbolic or CounterSystem.Alphabetic
                or CounterSystem.Numeric or CounterSystem.Additive;
            var magnitude = signed ? Math.Abs((long)number) : number;

            var body = isBuiltinBase
                ? CssCounterEngine.FormatCounterValue((int)Math.Min(magnitude, int.MaxValue), flat.BuiltinBase!)
                : Represent(magnitude, flat.Root);

            if (body is null) return FormatFallback(number, flat, registry, depth);

            if (signed && number < 0)
            {
                var (negPrefix, negSuffix) = flat.Negative ?? ("-", string.Empty);
                body = negPrefix + body + negSuffix;
            }

            if (flat.Pad is { } pad)
            {
                var length = new StringInfo(body).LengthInTextElements;
                if (length < pad.Length)
                {
                    var padding = new StringBuilder();
                    for (var i = length; i < pad.Length; i++) padding.Append(pad.Symbol);
                    body = padding + body;
                }
            }

            return body;
        }

        private static bool InRange(int number, in Flattened flat, bool isBuiltinBase)
        {
            if (flat.Range is { } ranges)
            {
                foreach (var (lower, upper) in ranges)
                {
                    if (number >= lower && number <= upper) return true;
                }

                return false;
            }

            if (isBuiltinBase) return true;

            // §2.4 "auto" range per system.
            return flat.Root.System switch
            {
                CounterSystem.Alphabetic or CounterSystem.Symbolic => number >= 1,
                CounterSystem.Additive => number >= 0,
                _ => true,
            };
        }

        private static string FormatFallback(int number, in Flattened flat, CounterStyleRegistry? registry, int depth)
        {
            var name = flat.Fallback ?? Keywords.Decimal;

            if (depth < MaxFallbackDepth && registry is not null && registry.TryGet(name, out var fallbackStyle))
            {
                return Format(number, fallbackStyle, registry, depth + 1);
            }

            // Nothing author-defined to fall back to: a predefined style, or decimal when the name is
            // unknown or the chain looped.
            return CssCounterEngine.FormatCounterValue(number, depth < MaxFallbackDepth ? name : Keywords.Decimal);
        }

        /// <summary>The system's algorithm (§2.1.x) for a value already in range; null when it cannot be represented.</summary>
        private static string? Represent(long value, CounterStyle style)
        {
            var symbols = style.Symbols;

            // Symbolic and alphabetic only count upward from 1; an explicit range that admits 0 (or less)
            // still cannot be represented, so it falls back (§2.1.2, §2.1.3).
            if (value < 1 && style.System is CounterSystem.Symbolic or CounterSystem.Alphabetic) return null;

            switch (style.System)
            {
                case CounterSystem.Cyclic:
                    return symbols[(int)(((value - 1) % symbols.Count + symbols.Count) % symbols.Count)];

                case CounterSystem.Fixed:
                {
                    var index = value - style.FixedFirst;
                    return index >= 0 && index < symbols.Count ? symbols[(int)index] : null;
                }

                case CounterSystem.Symbolic:
                {
                    var symbol = symbols[(int)((value - 1) % symbols.Count)];
                    var repeats = (value - 1) / symbols.Count + 1;
                    return new StringBuilder().Insert(0, symbol, (int)Math.Min(repeats, 10000)).ToString();
                }

                case CounterSystem.Alphabetic:
                {
                    var sb = new StringBuilder();
                    while (value > 0)
                    {
                        value--;
                        sb.Insert(0, symbols[(int)(value % symbols.Count)]);
                        value /= symbols.Count;
                    }

                    return sb.ToString();
                }

                case CounterSystem.Numeric:
                {
                    if (value == 0) return symbols[0];

                    var sb = new StringBuilder();
                    while (value > 0)
                    {
                        sb.Insert(0, symbols[(int)(value % symbols.Count)]);
                        value /= symbols.Count;
                    }

                    return sb.ToString();
                }

                case CounterSystem.Additive:
                {
                    var sb = new StringBuilder();

                    if (value == 0)
                    {
                        foreach (var (weight, symbol) in style.AdditiveSymbols)
                        {
                            if (weight == 0) return symbol;
                        }

                        return null;
                    }

                    foreach (var (weight, symbol) in style.AdditiveSymbols)
                    {
                        if (weight <= 0) continue;

                        for (var times = value / weight; times > 0; times--) sb.Append(symbol);
                        value %= weight;
                        if (value == 0) break;
                    }

                    return value == 0 ? sb.ToString() : null;
                }

                default:
                    return null;
            }
        }
    }
}
