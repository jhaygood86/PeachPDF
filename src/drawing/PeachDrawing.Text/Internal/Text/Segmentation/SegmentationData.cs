using System;

namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// Looks a code point up in the property tables that <c>generate_segmentation_tables.py</c> writes. Each table is a sorted
    /// array of range starts with a parallel array of values, complete over U+0000..U+10FFFF.
    /// </summary>
    internal static partial class SegmentationData
    {
        internal static int LineBreak(int codePoint) => (uint)codePoint < DirectLimit ? LineBreakDirect.Values[codePoint] : Find(LineBreakStarts, LineBreakValues, codePoint);

        /// <summary>Whether a code point is a nonspacing or spacing mark of the Complex_Context class (a dependent vowel or sign of Thai, Lao, Khmer, Burmese and the like).</summary>
        internal static bool IsComplexContextMark(int codePoint)
        {
            int value = LineBreak(codePoint);
            return (LineBreakClass)(value & 0x3F) == LineBreakClass.SA && (value & LineBreakMark) != 0;
        }

        internal static int Grapheme(int codePoint) => (uint)codePoint < DirectLimit ? GraphemeDirect.Values[codePoint] : Find(GraphemeStarts, GraphemeValues, codePoint);

        internal static int Word(int codePoint) => (uint)codePoint < DirectLimit ? WordDirect.Values[codePoint] : Find(WordStarts, WordValues, codePoint);

        internal static int Sentence(int codePoint) => (uint)codePoint < DirectLimit ? SentenceDirect.Values[codePoint] : Find(SentenceStarts, SentenceValues, codePoint);

        /// <summary>The code points below this are answered from an array indexed by code point, one per table, instead of the binary search.</summary>
        private const int DirectLimit = 0x2100;

        // One holder class per table so that a table is built only when it is first used.
        private static class LineBreakDirect { public static readonly ushort[] Values = Build(LineBreakStarts, LineBreakValues); }

        private static class GraphemeDirect { public static readonly ushort[] Values = Build(GraphemeStarts, GraphemeValues); }

        private static class WordDirect { public static readonly ushort[] Values = Build(WordStarts, WordValues); }

        private static class SentenceDirect { public static readonly ushort[] Values = Build(SentenceStarts, SentenceValues); }

        /// <summary>What <see cref="Find"/> answers for every code point below <see cref="DirectLimit"/>, worked out in one sweep over the range starts.</summary>
        private static ushort[] Build(ReadOnlySpan<int> starts, ReadOnlySpan<ushort> values)
        {
            var table = new ushort[DirectLimit];
            var index = 0;
            for (var codePoint = 0; codePoint < DirectLimit; codePoint++)
            {
                // Find answers with the last range whose start is at or below the code point (the first range when none is).
                while (index + 1 < starts.Length && starts[index + 1] <= codePoint)
                    index++;

                table[codePoint] = values[index];
            }

            return table;
        }

        private static int Find(ReadOnlySpan<int> starts, ReadOnlySpan<ushort> values, int codePoint)
        {
            int low = 0;
            int high = starts.Length - 1;
            while (low < high)
            {
                int middle = (low + high + 1) >> 1;
                if (starts[middle] <= codePoint)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return values[low];
        }
    }
}
