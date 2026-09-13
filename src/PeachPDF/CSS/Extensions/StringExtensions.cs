#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.CSS
{
    internal static class StringExtensions
    {
        public static bool Has(this string value, char chr, int index = 0)
        {
            return value != null && value.Length > index && value[index] == chr;
        }

        public static bool Has(this ReadOnlySpan<char> value, char chr, int index = 0)
        {
            return value.Length > index && value[index] == chr;
        }

        public static bool Contains(this string[] list, string element,
            StringComparison comparison = StringComparison.Ordinal)
        {
            return list.Any(t => t.Equals(element, comparison));
        }

        public static bool Is(this string current, string other)
        {
            return string.Equals(current, other, StringComparison.Ordinal);
        }

        public static bool Is(this ReadOnlySpan<char> current, string other)
        {
            return current.Equals(other, StringComparison.Ordinal);
        }

        public static bool Isi(this string current, string other)
        {
            return string.Equals(current, other, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Isi(this ReadOnlySpan<char> current, string other)
        {
            return current.Equals(other, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Isi(this ReadOnlySpan<char> current, ReadOnlySpan<char> other)
        {
            return current.Equals(other, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOneOf(this string element, string item1, string item2)
        {
            return element.Is(item1) || element.Is(item2);
        }

        public static bool IsOneOf(this ReadOnlySpan<char> element, string item1, string item2)
        {
            return element.Is(item1) || element.Is(item2);
        }

        /// <summary>
        /// Whether <paramref name="value"/> case-insensitively matches any string in
        /// <paramref name="candidates"/> - the allocation-free equivalent of
        /// <c>candidates.Contains(value.ToString().ToLowerInvariant())</c> (which allocates twice: once
        /// to materialize the span, once to lower it) for a token whose content is only ever used to
        /// check membership in a small known keyword set.
        /// </summary>
        /// <remarks>
        /// Every real candidate list in this codebase (a <c>string[]</c> keyword table) also satisfies
        /// <see cref="IReadOnlyList{T}"/>'s own overload below, which the compiler prefers - iterating an
        /// array through the bare <see cref="IEnumerable{T}"/> interface (this overload) boxes a heap
        /// enumerator per call, defeating the point of an "allocation-free" helper. Kept only for a
        /// candidate sequence that is genuinely not list-shaped (e.g. an unmaterialized LINQ query).
        /// </remarks>
        public static bool ContainsIsi(this IEnumerable<string> candidates, ReadOnlySpan<char> value)
        {
            foreach (var candidate in candidates)
            {
                if (value.Isi(candidate)) return true;
            }
            return false;
        }

        /// <summary>Allocation-free overload for the common case - see the <see cref="IEnumerable{T}"/> overload's remarks.</summary>
        public static bool ContainsIsi(this IReadOnlyList<string> candidates, ReadOnlySpan<char> value)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (value.Isi(candidates[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// The first string in <paramref name="candidates"/> that case-insensitively matches
        /// <paramref name="value"/>, or <see langword="null"/> if none does - the allocation-free way to
        /// both validate a token against a keyword set AND get back its canonical (already-known)
        /// casing, instead of allocating a lowered copy of the authored text via
        /// <c>value.ToString().ToLowerInvariant()</c> and using that as the result. Case-insensitive
        /// matching against a set of already-lowercase candidates is equivalent to lowering the input
        /// and comparing ordinally, so this is a drop-in, behavior-preserving replacement for that
        /// pattern. See <see cref="ContainsIsi(IEnumerable{string}, ReadOnlySpan{char})"/>'s remarks on
        /// why the <see cref="IReadOnlyList{T}"/> overload below is what every real caller here hits.
        /// </summary>
        public static string FindIsi(this IEnumerable<string> candidates, ReadOnlySpan<char> value)
        {
            foreach (var candidate in candidates)
            {
                if (value.Isi(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>Allocation-free overload for the common case - see the <see cref="IEnumerable{T}"/> overload's remarks.</summary>
        public static string FindIsi(this IReadOnlyList<string> candidates, ReadOnlySpan<char> value)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (value.Isi(candidates[i])) return candidates[i];
            }
            return null;
        }

        /// <summary>Case-sensitive counterpart to <see cref="FindIsi(IReadOnlyList{string}, ReadOnlySpan{char})"/>.</summary>
        public static string FindIs(this IReadOnlyList<string> candidates, ReadOnlySpan<char> value)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (value.Is(candidates[i])) return candidates[i];
            }
            return null;
        }

        /// <summary>
        /// Lowers <paramref name="value"/> into one newly-allocated string - the single-allocation
        /// equivalent of <c>value.ToString().ToLowerInvariant()</c> (two allocations: the materialized
        /// original-case copy, then the lowered copy of that). Use only when the lowered text itself
        /// must be kept (e.g. no known candidate set to match against via
        /// <see cref="FindIsi(IReadOnlyList{string}, ReadOnlySpan{char})"/>/
        /// <see cref="ContainsIsi(IReadOnlyList{string}, ReadOnlySpan{char})"/> instead, which need no
        /// lowering at all).
        /// </summary>
        public static string ToLowerInvariantString(this ReadOnlySpan<char> value)
        {
            if (value.IsEmpty) return string.Empty;
            Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
            value.ToLowerInvariant(buffer);
            return new string(buffer);
        }

        public static string StylesheetString(this string value)
        {
            var builder = Pool.NewStringBuilder();
            builder.Append(Symbols.DoubleQuote);

            if (!string.IsNullOrEmpty(value))
                for (var i = 0; i < value.Length; i++)
                {
                    var character = value[i];

                    switch (character)
                    {
                        case Symbols.Null:
                            throw new ParseException("Unable to parse null symbol");
                        case Symbols.DoubleQuote:
                        case Symbols.ReverseSolidus:
                            builder.Append(Symbols.ReverseSolidus).Append(character);
                            break;
                        default:
                            if (character.IsInRange(Symbols.StartOfHeading, Symbols.UnitSeparator)
                                || character == Symbols.CurlyBracketOpen)
                            {
                                builder.Append(Symbols.ReverseSolidus)
                                    .Append(character.ToHex())
                                    .Append(i + 1 != value.Length ? " " : "");
                            }
                            else
                            {
                                builder.Append(character);
                            }

                            break;
                    }
                }

            builder.Append(Symbols.DoubleQuote);
            return builder.ToPool();
        }

        public static string StylesheetFunction(this string value, string argument)
        {
            return string.Concat(value, "(", argument, ")");
        }

        public static string StylesheetUrl(this string value)
        {
            var argument = value.StylesheetString();
            return FunctionNames.Url.StylesheetFunction(argument);
        }

        public static string StylesheetUnit(this string value, out float result)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var firstLetter = value.Length;

                while (!value[firstLetter - 1].IsDigit() && --firstLetter > 0)
                {
                    // Intentional empty.
                }

                var parsed = float.TryParse(value.Substring(0, firstLetter), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out result);

                if (firstLetter > 0 && parsed) return value.Substring(firstLetter);
            }

            result = default;
            return null;
        }
    }
}