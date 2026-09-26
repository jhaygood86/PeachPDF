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
        internal ScalarText(ReadOnlySpan<char> text)
        {
            var code = new int[text.Length];
            var offset = new int[text.Length + 1];
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
