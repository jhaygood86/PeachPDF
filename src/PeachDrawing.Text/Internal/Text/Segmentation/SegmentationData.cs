using System;

namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// Looks a code point up in the property tables that <c>generate_segmentation_tables.py</c> writes. Each table is a sorted
    /// array of range starts with a parallel array of values, complete over U+0000..U+10FFFF.
    /// </summary>
    internal static partial class SegmentationData
    {
        internal static int LineBreak(int codePoint) => Find(LineBreakStarts, LineBreakValues, codePoint);

        internal static int Grapheme(int codePoint) => Find(GraphemeStarts, GraphemeValues, codePoint);

        internal static int Word(int codePoint) => Find(WordStarts, WordValues, codePoint);

        internal static int Sentence(int codePoint) => Find(SentenceStarts, SentenceValues, codePoint);

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
