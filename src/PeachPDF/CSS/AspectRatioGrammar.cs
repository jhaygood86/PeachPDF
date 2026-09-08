#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The shared grammar for the <c>aspect-ratio</c> value (<c>[ auto || &lt;ratio&gt; ]</c>, where
    /// <c>&lt;ratio&gt; = &lt;number [0,∞]&gt; [ / &lt;number [0,∞]&gt; ]?</c>). Used by both Layer A (to
    /// validate/accept-or-reject at parse time) and Layer B (to compute the used ratio during layout), so the
    /// grammar is defined once — the <see cref="BasicShapeGrammar"/> precedent.
    /// </summary>
    internal static class AspectRatioGrammar
    {
        /// <summary>
        /// Validates the token stream as an <c>aspect-ratio</c> value. Returns false for anything that is not a
        /// valid value. On success, <paramref name="ratio"/> is the used width/height ratio, or null when there
        /// is no usable ratio (a bare <c>auto</c>, or a ratio with a zero term — both of which mean "no
        /// preferred aspect ratio" for a non-replaced box).
        /// </summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens, out double? ratio) =>
            TryParse(tokens, out ratio, out _);

        /// <summary>
        /// Validates the token stream as an <c>aspect-ratio</c> value, additionally reporting whether the
        /// <c>auto</c> keyword was present (<c>[ auto || &lt;ratio&gt; ]</c>). <paramref name="hasAuto"/> lets a
        /// replaced element prefer its natural aspect ratio and fall back to the specified <c>&lt;ratio&gt;</c>
        /// (CSS Box Sizing 4 §5): <c>auto &lt;ratio&gt;</c> ⇒ <c>hasAuto=true</c> with a usable
        /// <paramref name="ratio"/>; a bare <c>&lt;ratio&gt;</c> ⇒ <c>hasAuto=false</c> (the ratio always
        /// overrides); a bare <c>auto</c> ⇒ <c>hasAuto=true</c> with a null <paramref name="ratio"/>.
        /// </summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens, out double? ratio, out bool hasAuto)
        {
            ratio = null;
            hasAuto = false;

            Token[] toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (toks.Length == 0) return false;

            var i = 0;

            if (IsAuto(toks[i])) { hasAuto = true; i++; }

            if (i == toks.Length) return hasAuto; // bare `auto`

            // <ratio> = <number> [ / <number> ]?
            if (!TryRatioCore(toks, ref i, out ratio)) return false;

            // An optional trailing `auto` (the `||` allows either order: `<ratio> auto`).
            if (i < toks.Length && !hasAuto && IsAuto(toks[i])) { hasAuto = true; i++; }

            if (i != toks.Length) return false; // trailing junk

            return true;
        }

        /// <summary>
        /// Validates a bare <c>&lt;ratio&gt;</c> value (<c>&lt;number [0,∞]&gt; [ / &lt;number [0,∞]&gt; ]?</c>),
        /// with no <c>auto</c> — the CSS Values 4 <c>&lt;ratio&gt;</c> data type, as used by the <c>@property</c>
        /// <c>syntax: "&lt;ratio&gt;"</c> matcher. Distinct from <see cref="TryParse(IReadOnlyList{Token}, out double?)"/>, whose <c>aspect-ratio</c>
        /// grammar additionally permits <c>auto</c>. On success <paramref name="ratio"/> is the used width/height
        /// ratio, or null when a term is zero.
        /// </summary>
        internal static bool TryParseRatio(IReadOnlyList<Token> tokens, out double? ratio)
        {
            ratio = null;

            Token[] toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();

            var i = 0;
            if (!TryRatioCore(toks, ref i, out ratio)) return false;
            return i == toks.Length; // no trailing junk (and, since we never consume `auto`, no `auto`)
        }

        private static bool TryRatioCore(Token[] toks, ref int i, out double? ratio)
        {
            ratio = null;

            // <number> [ / <number> ]?
            if (i >= toks.Length || !TryNonNegativeNumber(toks[i], out var width)) return false;
            double height = 1;
            i++;

            if (i < toks.Length && IsSlash(toks[i]))
            {
                i++;
                if (i >= toks.Length || !TryNonNegativeNumber(toks[i], out height)) return false;
                i++;
            }

            // A zero term degenerates to "no preferred aspect ratio".
            ratio = width <= 0 || height <= 0 ? null : width / height;
            return true;
        }

        /// <summary>
        /// Same grammar as <see cref="TryParse(IReadOnlyList{Token}, out double?, out bool)"/> ([ auto ||
        /// &lt;ratio&gt; ]) but validated directly against the raw string via span scanning, for
        /// css-properties.json's "ratio" cssDataType (ValidatorExpressionBuilder) — which only needs
        /// accept/reject, not the full CSS-OM tokenizer. Kept in this same class (not a separate parser) so
        /// a future grammar change only has one place to update; AspectRatioGrammarEquivalenceTests proves
        /// the two entry points agree.
        /// </summary>
        internal static bool TryParseFast(string value, out double? ratio, out bool hasAuto)
        {
            ratio = null;
            hasAuto = false;
            if (string.IsNullOrEmpty(value)) return false;

            var span = value.AsSpan().Trim();
            if (span.IsEmpty) return false;

            if (TryStripLeadingAuto(ref span)) hasAuto = true;

            if (span.IsEmpty) return hasAuto; // bare `auto`

            if (!hasAuto && TryStripTrailingAuto(ref span))
            {
                hasAuto = true;
                span = span.TrimEnd();
            }

            if (span.IsEmpty) return false; // "auto" consumed twice, or malformed

            if (!TryParseRatioSpan(span, out ratio)) return false;

            return true;
        }

        private static bool TryStripLeadingAuto(ref ReadOnlySpan<char> span)
        {
            if (!span.StartsWith("auto", StringComparison.OrdinalIgnoreCase)) return false;
            if (span.Length > 4 && !char.IsWhiteSpace(span[4])) return false; // whole-word only ("autox")
            span = span[4..].TrimStart();
            return true;
        }

        private static bool TryStripTrailingAuto(ref ReadOnlySpan<char> span)
        {
            if (!span.EndsWith("auto", StringComparison.OrdinalIgnoreCase)) return false;
            if (span.Length > 4 && !char.IsWhiteSpace(span[^5])) return false; // whole-word only ("banauto")
            span = span[..^4];
            return true;
        }

        private static bool TryParseRatioSpan(ReadOnlySpan<char> span, out double? ratio)
        {
            ratio = null;
            var slash = span.IndexOf('/');

            var widthSpan = (slash < 0 ? span : span[..slash]).Trim();
            if (widthSpan.IsEmpty || !TryNonNegativeNumber(widthSpan, out var width)) return false;

            double height = 1;
            if (slash >= 0)
            {
                var heightSpan = span[(slash + 1)..].Trim();
                if (heightSpan.IsEmpty || !TryNonNegativeNumber(heightSpan, out height)) return false;
            }

            ratio = width <= 0 || height <= 0 ? null : width / height;
            return true;
        }

        private static bool TryNonNegativeNumber(ReadOnlySpan<char> span, out double value)
        {
            if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0)
                return true;
            value = 0;
            return false;
        }

        private static bool IsAuto(Token token) => token.Type == TokenType.Ident && token.Data.Isi(Keywords.Auto);

        private static bool IsSlash(Token token) => token.Type == TokenType.Delim && token.Data == "/";

        private static bool TryNonNegativeNumber(Token token, out double value)
        {
            if (token is { Type: TokenType.Number } number && number.Value >= 0f)
            {
                value = number.Value;
                return true;
            }

            value = 0;
            return false;
        }
    }
}
