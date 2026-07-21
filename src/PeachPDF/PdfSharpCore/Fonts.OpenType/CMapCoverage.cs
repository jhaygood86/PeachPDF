using System.Collections.Generic;
using System.Text;

namespace PeachPDF.PdfSharpCore.Fonts.OpenType
{
    /// <summary>
    /// Derives the set of codepoints a font actually supports from its format-4 <c>cmap</c> subtable, as a
    /// compact list of inclusive <see cref="RuneRange"/>s (the same representation an <c>@font-face</c>
    /// <c>unicode-range</c> produces). Used as a face's effective coverage when it declares no explicit
    /// <c>unicode-range</c>, so per-codepoint font matching can fall back to whichever family actually
    /// covers a character.
    /// </summary>
    internal static class CMapCoverage
    {
        /// <summary>
        /// Extracts covered codepoints as coalesced inclusive ranges from <paramref name="cmap4"/>.
        /// Uses each segment's <c>[startCount, endCount]</c> bounds (the standard 0xFFFF terminator
        /// segment excluded); this may over-report the rare intra-segment glyph hole - which degrades to
        /// today's no-fallback behavior for that codepoint - but never under-reports, so a genuinely
        /// covered character is never wrongly skipped during fallback. Format-4 is BMP-only, so no
        /// codepoint above U+FFFF is ever reported. Returns an empty list when the font has no format-4
        /// cmap.
        /// </summary>
        public static IReadOnlyList<RuneRange> Extract(CMap4? cmap4)
        {
            if (cmap4?.startCount is null || cmap4.endCount is null)
                return [];

            int segCount = cmap4.segCountX2 / 2;
            var ranges = new List<RuneRange>(segCount);

            for (int seg = 0; seg < segCount; seg++)
            {
                int start = cmap4.startCount[seg];
                int end = cmap4.endCount[seg];

                if (start > end)
                    continue;

                // The last segment of a conformant format-4 cmap is a required [0xFFFF, 0xFFFF]
                // terminator that maps U+FFFF (a noncharacter) to .notdef - never real coverage.
                if (start == 0xFFFF)
                    continue;

                if (end == 0xFFFF)
                    end = 0xFFFE;

                // A segment bound landing exactly on a surrogate can't be a Rune; nudge the bounds inward
                // to the nearest scalar values (only reachable via a malformed cmap - surrogates are never
                // legitimately mapped). If that empties the segment, skip it.
                if (start is >= 0xD800 and <= 0xDFFF)
                    start = 0xE000;
                if (end is >= 0xD800 and <= 0xDFFF)
                    end = 0xD7FF;
                if (start > end)
                    continue;

                // Segments are sorted ascending and non-overlapping; coalesce ones that abut so the
                // resulting list is minimal.
                if (ranges.Count > 0 && start <= ranges[^1].End.Value + 1)
                {
                    var prev = ranges[^1];
                    if (end > prev.End.Value)
                        ranges[^1] = prev with { End = new Rune(end) };
                }
                else
                {
                    ranges.Add(new RuneRange(new Rune(start), new Rune(end)));
                }
            }

            return ranges;
        }

        /// <summary>
        /// Whether <paramref name="rune"/> falls inside any of <paramref name="ranges"/>. This is the
        /// single membership test both the codepoint-aware font resolver and the CSS <c>unicode-range</c>
        /// parser share; the inclusive-on-both-ends convention lives in <see cref="RuneRange.Contains"/>.
        /// </summary>
        public static bool Contains(IReadOnlyList<RuneRange> ranges, Rune rune)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Contains(rune))
                    return true;
            }

            return false;
        }
    }
}
