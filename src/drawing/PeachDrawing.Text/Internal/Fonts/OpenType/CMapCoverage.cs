using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// Derives the set of codepoints a font actually supports from its <c>cmap</c> subtables (format 4 for
    /// the BMP, plus format 12 for supplementary-plane / astral codepoints such as emoji), as a compact
    /// list of inclusive <see cref="RuneInterval"/>s (the same representation an <c>@font-face</c>
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
        public static IReadOnlyList<RuneInterval> Extract(CMapTable? cmap)
        {
            if (cmap is null)
                return [];

            var bmp = Extract(cmap.cmap4);

            if (cmap.cmap12?.startCharCode is null || cmap.cmap12.numGroups == 0)
                return bmp;

            return Merge(bmp, cmap.cmap12.startCharCode, cmap.cmap12.endCharCode, (int)cmap.cmap12.numGroups);
        }

        /// <summary>
        /// Merges the BMP ranges and the format-12 groups by sorting on start, then coalescing - a
        /// format-12 subtable may itself include BMP codepoints, so a naive append wouldn't stay sorted.
        /// </summary>
        private static IReadOnlyList<RuneInterval> Merge(IReadOnlyList<RuneInterval> bmp, uint[] startCharCode, uint[] endCharCode, int numGroups)
        {
            var segments = new List<(long Start, long End)>(bmp.Count + numGroups);
            foreach (var r in bmp)
                segments.Add((r.Start.Value, r.End.Value));

            for (int g = 0; g < numGroups; g++)
            {
                long start = startCharCode[g];
                long end = endCharCode[g];

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

            var ranges = new List<RuneInterval>(segments.Count);
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
                    ranges.Add(new RuneInterval(new Rune((int)start), new Rune((int)end)));
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
        public static IReadOnlyList<RuneInterval> Extract(CMap4? cmap4)
        {
            if (cmap4?.startCount is null || cmap4.endCount is null)
                return [];

            return ExtractSegments(cmap4.startCount, cmap4.endCount, cmap4.segCountX2 / 2);
        }

        private static IReadOnlyList<RuneInterval> ExtractSegments(ushort[] startCount, ushort[] endCount, int segCount)
        {
            var ranges = new List<RuneInterval>(segCount);

            for (int seg = 0; seg < segCount; seg++)
            {
                int start = startCount[seg];
                int end = endCount[seg];

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
                    ranges.Add(new RuneInterval(new Rune(start), new Rune(end)));
                }
            }

            return ranges;
        }

        /// <summary>
        /// The same coverage as <see cref="Extract(CMapTable?)"/>, read straight from the bytes of a
        /// <c>cmap</c> table without building a font: used to scan faces for coverage only, which must not
        /// load (or keep) the rest of each font. Chooses subtables by the same rules <c>CMapTable</c> does;
        /// a table that cannot be read, or has no usable subtable, covers nothing.
        /// </summary>
        public static IReadOnlyList<RuneInterval> ExtractFromTable(ReadOnlySpan<byte> table)
        {
            try
            {
                return ReadTable(table);
            }
            catch (ArgumentOutOfRangeException)
            {
                return [];
            }
        }

        private static IReadOnlyList<RuneInterval> ReadTable(ReadOnlySpan<byte> table)
        {
            if (table.Length < 4)
                return [];

            int numTables = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
            if (4 + numTables * 8 > table.Length)
                return [];

            IReadOnlyList<RuneInterval>? bmp = null;
            uint[]? starts = null, ends = null;
            var cmap12IsUnicode = false;

            for (int idx = 0; idx < numTables; idx++)
            {
                var record = table.Slice(4 + idx * 8, 8);
                int platformId = BinaryPrimitives.ReadUInt16BigEndian(record);
                int encodingId = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
                int offset = BinaryPrimitives.ReadInt32BigEndian(record[4..]);

                if (offset < 0 || (long)offset + 2 > table.Length)
                    continue;

                var subtable = table[offset..];
                int format = BinaryPrimitives.ReadUInt16BigEndian(subtable);

                // PlatformId.Apple (0) / Win (3); WinEncodingId.Symbol (0) / Unicode (1); AppleEncodingId.Unicode20BmpOnly (3).
                bool isBmpCandidate = (platformId == 3 && encodingId is 0 or 1) || (platformId == 0 && encodingId == 3);
                bool isFullUnicodeCandidate = (platformId == 3 && encodingId is 1 or 10) || platformId == 0;

                if (bmp is null && format == 4 && isBmpCandidate)
                {
                    bmp = ReadFormat4(subtable);
                }
                else if (format == 12 && (starts is null || (!cmap12IsUnicode && isFullUnicodeCandidate)))
                {
                    (starts, ends) = ReadFormat12(subtable);
                    cmap12IsUnicode = isFullUnicodeCandidate;
                }

                if (bmp is not null && cmap12IsUnicode)
                    break;
            }

            // A font with no BMP subtable is usable when its format-12 subtable is a Unicode one: that maps the BMP too.
            if (bmp is null && !cmap12IsUnicode)
                return [];

            bmp ??= [];
            return starts is null || starts.Length == 0 ? bmp : Merge(bmp, starts, ends!, starts.Length);
        }

        private static IReadOnlyList<RuneInterval> ReadFormat4(ReadOnlySpan<byte> subtable)
        {
            int segCount = BinaryPrimitives.ReadUInt16BigEndian(subtable[6..]) / 2;
            // endCount[segCount], a reserved pad, then startCount[segCount].
            var endAt = 14;
            var startAt = endAt + segCount * 2 + 2;

            var end = new ushort[segCount];
            var start = new ushort[segCount];
            for (int seg = 0; seg < segCount; seg++)
            {
                end[seg] = BinaryPrimitives.ReadUInt16BigEndian(subtable[(endAt + seg * 2)..]);
                start[seg] = BinaryPrimitives.ReadUInt16BigEndian(subtable[(startAt + seg * 2)..]);
            }

            return ExtractSegments(start, end, segCount);
        }

        private static (uint[] Start, uint[] End) ReadFormat12(ReadOnlySpan<byte> subtable)
        {
            uint numGroups = BinaryPrimitives.ReadUInt32BigEndian(subtable[12..]);
            if (16 + (long)numGroups * 12 > subtable.Length)
                throw new ArgumentOutOfRangeException(nameof(subtable));

            var start = new uint[numGroups];
            var end = new uint[numGroups];
            for (int g = 0; g < start.Length; g++)
            {
                start[g] = BinaryPrimitives.ReadUInt32BigEndian(subtable[(16 + g * 12)..]);
                end[g] = BinaryPrimitives.ReadUInt32BigEndian(subtable[(20 + g * 12)..]);
            }

            return (start, end);
        }

        /// <summary>
        /// Whether <paramref name="rune"/> falls inside any of <paramref name="ranges"/>. This is the
        /// single membership test both the codepoint-aware font resolver and the CSS <c>unicode-range</c>
        /// parser share; the inclusive-on-both-ends convention lives in <see cref="RuneInterval.Contains"/>.
        /// </summary>
        public static bool Contains(IReadOnlyList<RuneInterval> ranges, Rune rune)
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
