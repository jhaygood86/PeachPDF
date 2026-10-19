#nullable enable

using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>The five CSS-wide keywords (CSS Cascade &amp; Inheritance §7.3) a property value can be.</summary>
    internal enum CssGlobalKeyword
    {
        Inherit,
        Initial,
        Unset,
        Revert,
        RevertLayer
    }

    /// <summary>
    /// A typed box-side property value that carries the <b>parsed</b> value through the cascade to the box,
    /// so a layout consumer reads the object rather than re-parsing the authored string. A value is in exactly
    /// one of three states:
    /// <list type="bullet">
    /// <item>a CSS-wide keyword (<see cref="GlobalValue"/> non-null),</item>
    /// <item><i>unresolved</i> — the authored text still contains <c>var()</c> and has not been resolved yet
    /// (<see cref="IsUnresolved"/>; <see cref="Value"/> is default), or</item>
    /// <item>resolved — <see cref="Value"/> holds the parsed <typeparamref name="T"/>.</item>
    /// </list>
    /// The authored/keyword text is retained so the string-based getter/snapshot/revert cascade path keeps
    /// working (<see cref="ToString"/>). Grid templates (<c>T = GridTemplate</c>) are the first adopter; the
    /// mechanism is intended to eventually back every box property.
    /// <para>
    /// A <c>record</c>, not a plain class: the cascade's copy-on-write comparison (<c>ComputedStyleAreas
    /// .SetPropertyValue</c>) needs to see that re-parsing the <i>same authored text</i> twice (e.g. the
    /// cascade defaulting loop re-applying a property's own initial value to a fresh box) produces an
    /// <i>equal</i> value, not just an equal-looking one — that's what lets it stay a real no-op instead of
    /// always cloning the area. Because a record's synthesized equality compares every instance field —
    /// including the raw authored text (<c>_cssText</c>), not just <see cref="Value"/> — this is "same
    /// authored text and same resolved state", not pure semantic equality: two differently-cased or
    /// differently-whitespaced spellings of the same value (e.g. <c>"AUTO"</c> vs <c>"auto"</c>) do NOT
    /// compare equal even when they'd resolve to the same <typeparamref name="T"/>. That's sufficient for
    /// the defaulting-loop no-op (which always re-applies the literal initial-value string), but a future
    /// caller comparing two independently-authored declarations for semantic equality needs to compare
    /// <see cref="Value"/> directly instead. Whether <typeparamref name="T"/> itself then compares by value
    /// depends on T — an enum, or a record/record-struct T like <see cref="CssKeywordOrValue{TEnum,TValue}"/>
    /// or <c>GridTemplate</c>, gets real structural equality; a plain class T with no equality override
    /// would fall back to reference equality on <see cref="Value"/>.
    /// </para>
    /// </summary>
    internal sealed record CssProperty<T>
    {
        private readonly string _cssText;

        private CssProperty(CssGlobalKeyword? global, bool unresolved, T? value, string cssText)
        {
            GlobalValue = global;
            IsUnresolved = unresolved;
            Value = value;
            _cssText = cssText;
        }

        /// <summary>The CSS-wide keyword, when this value is one; otherwise null.</summary>
        public CssGlobalKeyword? GlobalValue { get; }

        public bool IsGlobalValue => GlobalValue.HasValue;

        /// <summary>The authored text still contains an unresolved <c>var()</c> — <see cref="Value"/> is not
        /// meaningful until resolution replaces this with a resolved value.</summary>
        public bool IsUnresolved { get; }

        /// <summary>The parsed value — meaningful only when the value is neither global nor unresolved.</summary>
        public T? Value { get; }

        public static CssProperty<T> Global(CssGlobalKeyword keyword) =>
            new(keyword, unresolved: false, value: default, CssGlobalKeywords.ToText(keyword));

        public static CssProperty<T> Unresolved(string rawText) =>
            new(global: null, unresolved: true, value: default, rawText);

        public static CssProperty<T> FromValue(string cssText, T? value) =>
            new(global: null, unresolved: false, value, cssText);

        /// <summary>
        /// Builds a <see cref="CssProperty{T}"/> for a simple single-keyword enum property (e.g.
        /// <c>direction</c>, <c>unicode-bidi</c>, <c>writing-mode</c>) directly from its authored string, for
        /// the cascade paths that apply a plain string rather than a Layer A parse result (the initial-value
        /// seed, a resolved global keyword, an inherited copy, a var()-resolved string, a presentational HTML
        /// attribute). Unlike <see cref="GridTemplateValueConverter.FromCssText"/> this is a shared generic
        /// helper rather than one bespoke method per property, since a single-keyword lookup has no grammar of
        /// its own to duplicate — every caller just needs a case-insensitive keyword-to-enum map (already a
        /// <c>Map.*</c> <see cref="System.Collections.Frozen.FrozenDictionary{TKey,TValue}"/> for each of these
        /// properties) and a fallback for an unrecognized keyword.
        /// </summary>
        public static CssProperty<T> FromCssText(string value, IReadOnlyDictionary<string, T> keywordMap, T fallback)
        {
            if (CssGlobalKeywords.TryParse(value, out var keyword))
                return Global(keyword);
            if (value.Contains("var(", StringComparison.OrdinalIgnoreCase))
                return Unresolved(value);

            var trimmed = value.Trim();
            if (!keywordMap.TryGetValue(trimmed, out var parsed))
                return FromValue(value, fallback);

            // The matched keyword is stored canonicalized (lowercase — every keyword this codebase's
            // Map.* dictionaries recognize is authored in lowercase ASCII), not the raw input, so
            // ToString() round-trips to the canonical spelling regardless of how it was cased (issue
            // #598) — a case-insensitively-matched "LTR" must not silently become "LTR" again on readback.
            return FromValue(trimmed.ToLowerInvariant(), parsed);
        }

        public override string ToString() => _cssText;
    }

    /// <summary>Maps between the CSS-wide keyword string literals and <see cref="CssGlobalKeyword"/>.</summary>
    internal static class CssGlobalKeywords
    {
        public static bool TryParse(string value, out CssGlobalKeyword keyword)
        {
            if (value.Isi(Keywords.Inherit)) { keyword = CssGlobalKeyword.Inherit; return true; }
            if (value.Isi(Keywords.Initial)) { keyword = CssGlobalKeyword.Initial; return true; }
            if (value.Isi(Keywords.Unset)) { keyword = CssGlobalKeyword.Unset; return true; }
            if (value.Isi(Keywords.Revert)) { keyword = CssGlobalKeyword.Revert; return true; }
            if (value.Isi(Keywords.RevertLayer)) { keyword = CssGlobalKeyword.RevertLayer; return true; }
            keyword = default;
            return false;
        }

        public static string ToText(CssGlobalKeyword keyword) => keyword switch
        {
            CssGlobalKeyword.Inherit => Keywords.Inherit,
            CssGlobalKeyword.Initial => Keywords.Initial,
            CssGlobalKeyword.Unset => Keywords.Unset,
            CssGlobalKeyword.Revert => Keywords.Revert,
            CssGlobalKeyword.RevertLayer => Keywords.RevertLayer,
            _ => Keywords.Initial
        };
    }
}
