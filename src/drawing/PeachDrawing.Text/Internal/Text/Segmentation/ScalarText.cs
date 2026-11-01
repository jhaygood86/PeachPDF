using System;
using System.Text;

namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// A string as the scalar values the segmentation rules read, with where each starts in the UTF-16 text. A lone surrogate
    /// stands for itself.
    /// </summary>
    internal readonly struct ScalarText
    {
        /// <summary>The longest text whose buffers a scratch conversion keeps for the next one, so a huge paragraph is not retained.</summary>
        internal const int ScratchLimit = 4096;

        [ThreadStatic]
        private static int[]? t_scratchCode;

        [ThreadStatic]
        private static int[]? t_scratchOffset;

        /// <summary>
        /// Converts <paramref name="text"/>. With <paramref name="scratch"/> the two arrays are this thread's own, reused by its next scratch
        /// conversion, so they are valid only until then: for a caller that is finished with the result before it converts again, and
        /// which was allocating them for every run of text it broke into lines.
        /// </summary>
        internal ScalarText(ReadOnlySpan<char> text, bool scratch = false)
        {
            int[] code, offset;
            if (scratch && text.Length <= ScratchLimit)
            {
                if (t_scratchCode is null || t_scratchCode.Length < text.Length)
                {
                    t_scratchCode = new int[Math.Max(text.Length, 256)];
                    t_scratchOffset = new int[t_scratchCode.Length + 1];
                }

                code = t_scratchCode;
                offset = t_scratchOffset!;
            }
            else
            {
                code = new int[text.Length];
                offset = new int[text.Length + 1];
            }

            int count = 0;
            int index = 0;
            while (index < text.Length)
            {
                offset[count] = index;
                if (Rune.DecodeFromUtf16(text.Slice(index), out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done)
                {
                    code[count] = rune.Value;
                    index += consumed;
                }
                else
                {
                    code[count] = text[index];
                    index++;
                }

                count++;
            }

            offset[count] = text.Length;
            Length = text.Length;
            Count = count;
            Code = code;
            Offset = offset;
        }

        /// <summary>The scalar values; only the first <see cref="Count"/> are meaningful.</summary>
        internal int[] Code { get; }

        /// <summary>The UTF-16 index at which each scalar starts, and after the last one, the length of the text.</summary>
        internal int[] Offset { get; }

        /// <summary>The number of scalar values.</summary>
        internal int Count { get; }

        /// <summary>The length of the text in UTF-16 code units.</summary>
        internal int Length { get; }
    }
}
