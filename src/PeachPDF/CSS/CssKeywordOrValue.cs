#nullable enable

using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A resolved <see cref="CssProperty{T}"/> value for a property whose grammar is a keyword <b>or</b> a
    /// typed value (e.g. <c>z-index</c>'s <c>&lt;integer&gt; | auto</c>, <c>column-width</c>'s
    /// <c>&lt;length&gt; | auto</c>) — exactly one of <see cref="Keyword"/>/<see cref="Value"/> is set. A
    /// manual stand-in for a real C# union type, which would replace this once the language has one. A
    /// record struct so the outer <see cref="CssProperty{T}"/>'s own record equality sees through to real
    /// value equality here too, instead of falling back to the default (reflection-based) struct equality.
    /// <para>
    /// The "exactly one is set" invariant is enforced by <see cref="CssKeywordOrValue{TEnum,TValue}(TEnum?,TValue?)"/>,
    /// not by the type itself — <c>default(CssKeywordOrValue{TEnum,TValue})</c> (both null) bypasses it
    /// entirely, and is exactly what <see cref="CssProperty{T}"/>'s <c>Value</c> holds whenever
    /// <c>IsGlobalValue</c>/<c>IsUnresolved</c> is set instead (see <see cref="CssKeywordOrValueParser.FromCssText{TEnum,TValue}"/>).
    /// A caller must check <see cref="CssProperty{T}.IsGlobalValue"/>/<see cref="CssProperty{T}.IsUnresolved"/>
    /// before trusting <see cref="IsKeyword"/>/<see cref="IsValue"/> to mean "the property actually has a
    /// keyword/value" rather than "neither, because there's no resolved value at all right now".
    /// </para>
    /// </summary>
    internal readonly record struct CssKeywordOrValue<TEnum, TValue>
        where TEnum : struct, Enum
        where TValue : struct
    {
        public CssKeywordOrValue(TEnum? keyword, TValue? value)
        {
            if (keyword is null == value is null)
                throw new ArgumentException("Exactly one of keyword or value must be provided.");

            Keyword = keyword;
            Value = value;
        }

        public TEnum? Keyword { get; init; }
        public TValue? Value { get; init; }

        public bool IsKeyword => Keyword is not null;
        public bool IsValue => Value is not null;
    }

    internal static class CssKeywordOrValueParser
    {
        public delegate bool TryParseValue<TValue>(string text, out TValue value) where TValue : struct;

        /// <summary>
        /// Parses a <see cref="CssKeywordOrValue{TEnum,TValue}"/>-backed <see cref="CssProperty{T}"/> from
        /// its authored text, mirroring <see cref="CssProperty{T}.FromCssText"/>'s CSS-wide-keyword/
        /// <c>var()</c>/keyword-map handling — the only addition is a <paramref name="tryParseValue"/>
        /// fallback for the non-keyword side of the union, tried once the keyword map misses.
        /// </summary>
        public static CssProperty<CssKeywordOrValue<TEnum, TValue>> FromCssText<TEnum, TValue>(
            string value, IReadOnlyDictionary<string, TEnum> keywordMap, TryParseValue<TValue> tryParseValue,
            TEnum fallback)
            where TEnum : struct, Enum
            where TValue : struct
        {
            if (CssGlobalKeywords.TryParse(value, out var globalKeyword))
                return CssProperty<CssKeywordOrValue<TEnum, TValue>>.Global(globalKeyword);

            if (value.Contains("var(", StringComparison.OrdinalIgnoreCase))
                return CssProperty<CssKeywordOrValue<TEnum, TValue>>.Unresolved(value);

            var trimmed = value.Trim();
            if (keywordMap.TryGetValue(trimmed, out var keyword))
                // Stored canonicalized (lowercase), not the raw input, mirroring CssProperty<T>.FromCssText's
                // own reasoning (issue #598) - ToString() must round-trip to the canonical spelling
                // regardless of how the keyword side of this union was cased.
                return CssProperty<CssKeywordOrValue<TEnum, TValue>>.FromValue(
                    trimmed.ToLowerInvariant(), new CssKeywordOrValue<TEnum, TValue>(keyword, null));

            if (tryParseValue(value, out var parsed))
                return CssProperty<CssKeywordOrValue<TEnum, TValue>>.FromValue(
                    value, new CssKeywordOrValue<TEnum, TValue>(null, parsed));

            // Set_* only ever calls this after Validate_* has confirmed the value matches the keyword map
            // or the value grammar, so this is unreachable in practice — kept total, like FromCssText.
            return CssProperty<CssKeywordOrValue<TEnum, TValue>>.FromValue(
                value, new CssKeywordOrValue<TEnum, TValue>(fallback, null));
        }
    }
}
