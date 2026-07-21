using System;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.PdfSharpCore.Fonts.OpenType
{
    /// <summary>
    /// Derives the set of codepoints a font actually supports from its <c>cmap</c> subtables (format 4 for
    /// the BMP, plus format 12 for supplementary-plane / astral codepoints such as emoji), as a compact
    /// list of inclusive <see cref="RuneRange"/>s (the same representation an <c>@font-face</c>
    /// <c>unicode-range</c> produces). Used as a face's effective coverage when it declares no explicit
    /// <c>unicode-range</c>, so per-codepoint font matching can fall back to whichever family actually
    /// covers a character.
    /// </summary>
    internal static class CMapCoverage
    {
        /// <summary>
        /// Extracts covered codepoints from a whole <c>cmap</c> table: the format-4 BMP ranges plus, when
        /// the font has a format-12 subtable, its astral ranges - sorted and coalesced into one list. Fast
        /// path (the common case): a font with no format-12 subtable returns exactly its BMP coverage.
        /// </summary>
        public static IReadOnlyList<RuneRange> Extract(CMapTable? cmap)
        {
            if (cmap is null)
                return [];

            var bmp = Extract(cmap.cmap4);

            if (cmap.cmap12?.startCharCode is null || cmap.cmap12.numGroups == 0)
                return bmp;

            var cmap12 = cmap.cmap12;

            // Merge the BMP ranges and the format-12 groups by sorting on start, then coalescing - a
            // format-12 subtable may itself include BMP codepoints, so a naive append wouldn't stay sorted.
            var segments = new List<(long Start, long End)>(bmp.Count + (int)cmap12.numGroups);
            foreach (var r in bmp)
                segments.Add((r.Start.Value, r.End.Value));

            for (int g = 0; g < cmap12.numGroups; g++)
            {
                long start = cmap12.startCharCode[g];
                long end = cmap12.endCharCode[g];

                if (start > end || start > 0x10FFFF)
                    continue;
                if (end > 0x10FFFF)
                    end = 0x10FFFF;

                // Split around the surrogate block (0xD800-0xDFFF, never legitimately mapped) so no emitted
                // range ever contains a surrogate - a format-12 group may land inside it or span across it.
                long belowEnd = Math.Min(end, 0xD7FF);
                if (start <= belowEnd)
                    segments.Add((start, belowEnd));
                long aboveStart = Math.Max(start, 0xE000);
                if (aboveStart <= end)
                    segments.Add((aboveStart, end));
            }

            segments.Sort((a, b) => a.Start.CompareTo(b.Start));

            var ranges = new List<RuneRange>(segments.Count);
            foreach (var (start, end) in segments)
            {
                if (ranges.Count > 0 && start <= ranges[^1].End.Value + 1)
                {
                    var prev = ranges[^1];
                    if (end > prev.End.Value)
                        ranges[^1] = prev with { End = new Rune((int)end) };
                }
                else
                {
                    ranges.Add(new RuneRange(new Rune((int)start), new Rune((int)end)));
                }
            }

            return ranges;
        }

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
