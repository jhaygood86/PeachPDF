using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Fonts.OpenType;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Parses a CSS <c>@font-face</c> <c>unicode-range</c> descriptor (or an equivalent
    /// programmatically-supplied list) into a compact set of inclusive codepoint <see cref="Range"/>s,
    /// reusing the existing CSS tokenizer's <c>U+</c> grammar (<see cref="CssValueParser.GetCssTokens"/>
    /// → <see cref="RangeToken"/>) rather than re-implementing it - the same shared-grammar convention as
    /// <c>CalcParser</c>. Only the hex <see cref="RangeToken.Start"/>/<see cref="RangeToken.End"/> bounds
    /// are read (never the token's materialized per-codepoint list), so a wide range costs nothing.
    /// </summary>
    internal static class UnicodeRangeParser
    {
        /// <summary>
        /// Parses a <c>unicode-range</c> descriptor into inclusive codepoint ranges, or returns null when
        /// the descriptor is absent/blank or contains no valid range (meaning "no explicit subset - the
        /// face applies to whatever its font actually covers"). A <see cref="Range"/>'s
        /// <see cref="Index"/> bounds hold the raw codepoint numbers; both ends are inclusive (see
        /// <see cref="Covers"/>).
        /// </summary>
        internal static IReadOnlyList<RuneRange>? Parse(string? descriptor)
        {
            if (string.IsNullOrWhiteSpace(descriptor))
                return null;

            List<RuneRange>? ranges = null;

            // The value can arrive either in its CSS source form ("U+41-5A, U+61-7A") or, once it has been
            // round-tripped through the CSS-OM, as the tokenizer's stored serialization with the "U+"
            // prefix dropped ("41-5A, 61-7A"). Split on the top-level commas and re-tokenize each segment
            // with the "U+" prefix the lexer's range grammar expects, so both forms parse identically and
            // we still reuse the one shared tokenizer rather than re-implementing the U+ grammar.
            foreach (var segment in descriptor.Split(','))
            {
                var normalized = segment.Trim();

                if (normalized.Length == 0)
                    continue;

                if (!normalized.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
                    normalized = "U+" + normalized;

                var rangeToken = CssValueParser.GetCssTokens(normalized).OfType<RangeToken>().FirstOrDefault();

                if (rangeToken is null)
                    continue;

                if (!int.TryParse(rangeToken.Start, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var start) ||
                    !int.TryParse(rangeToken.End, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var end))
                    continue;

                if (start > Symbols.MaximumCodepoint)
                    continue;

                if (end > Symbols.MaximumCodepoint)
                    end = Symbols.MaximumCodepoint;

                if (end < start)
                    continue;

                // A declared range whose bound lands on a surrogate can't be a Rune (surrogates aren't
                // scalar values); nudge inward, and skip a range that is nothing but surrogates.
                if (start is >= 0xD800 and <= 0xDFFF)
                    start = 0xE000;
                if (end is >= 0xD800 and <= 0xDFFF)
                    end = 0xD7FF;
                if (start > end)
                    continue;

                (ranges ??= []).Add(new RuneRange(new Rune(start), new Rune(end)));
            }

            return ranges;
        }

        /// <summary>
        /// Whether <paramref name="rune"/> falls inside any of <paramref name="ranges"/>. This is the one
        /// place the inclusive-on-both-ends convention lives - a codepoint range <c>U+41-5A</c> covers
        /// both <c>U+41</c> and <c>U+5A</c>, unlike a <see cref="Range"/>'s half-open slicing semantics.
        /// </summary>
        internal static bool Covers(IReadOnlyList<RuneRange> ranges, Rune rune) => CMapCoverage.Contains(ranges, rune);
    }
}
