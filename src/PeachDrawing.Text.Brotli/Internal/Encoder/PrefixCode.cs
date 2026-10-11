using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.Text.Brotli.Internal.Encoder
{
    /// <summary>
    /// One prefix (Huffman) code of a meta-block: built from symbol counts, written in the form RFC 7932 section 3 defines (a
    /// "simple" code for up to four symbols, a "complex" code otherwise), and then used to write symbols.
    /// </summary>
    internal sealed class PrefixCode
    {
        private const int MaxCodeLength = 15;
        private const int MaxCodeLengthCodeLength = 5;
        private const int CodeLengthSymbols = 18;
        private const int RepeatPreviousLength = 16;
        private const int RepeatZeroLength = 17;
        private const int InitialPreviousLength = 8;

        // Order in which the code length code lengths are stored (RFC 7932 section 3.5).
        private static readonly int[] CodeLengthCodeOrder = [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];

        // The fixed prefix code the code length code lengths (values 0-5) are themselves written with: value -> (bit count, bits).
        private static readonly (int Count, uint Bits)[] CodeLengthCodeLengthCode =
            [(2, 0), (4, 7), (3, 3), (2, 2), (2, 1), (4, 15)];

        private readonly int _alphabetSize;
        private readonly byte[] _lengths;
        private readonly ushort[] _codes;
        private readonly int[]? _simpleSymbols;
        private readonly bool _simpleTreeSelect;

        private PrefixCode(int alphabetSize, byte[] lengths, ushort[] codes, int[]? simpleSymbols, bool simpleTreeSelect)
        {
            _alphabetSize = alphabetSize;
            _lengths = lengths;
            _codes = codes;
            _simpleSymbols = simpleSymbols;
            _simpleTreeSelect = simpleTreeSelect;
        }

        /// <summary>Builds the code for <paramref name="counts"/>, the number of times each symbol of the alphabet occurs.</summary>
        public static PrefixCode Build(int[] counts, int alphabetSize)
        {
            var used = Enumerable.Range(0, alphabetSize).Where(s => counts[s] > 0).ToList();
            var lengths = new byte[alphabetSize];

            if (used.Count > 4)
            {
                ComputeLengths(counts, alphabetSize, MaxCodeLength, lengths);
                return new PrefixCode(alphabetSize, lengths, AssignCodes(lengths), null, false);
            }

            // Up to four symbols: a simple code, listing the symbols most frequent first.
            if (used.Count == 0)
            {
                used.Add(0);
            }

            var symbols = used.OrderByDescending(s => counts[s]).ThenBy(s => s).ToArray();
            var treeSelect = false;
            switch (symbols.Length)
            {
                case 2:
                    lengths[symbols[0]] = 1;
                    lengths[symbols[1]] = 1;
                    break;
                case 3:
                    lengths[symbols[0]] = 1;
                    lengths[symbols[1]] = 2;
                    lengths[symbols[2]] = 2;
                    break;
                case 4:
                    long flat = 2L * (counts[symbols[0]] + counts[symbols[1]] + counts[symbols[2]] + counts[symbols[3]]);
                    long skewed = counts[symbols[0]] + 2L * counts[symbols[1]] + 3L * (counts[symbols[2]] + counts[symbols[3]]);
                    treeSelect = skewed < flat;
                    lengths[symbols[0]] = (byte)(treeSelect ? 1 : 2);
                    lengths[symbols[1]] = 2;
                    lengths[symbols[2]] = (byte)(treeSelect ? 3 : 2);
                    lengths[symbols[3]] = (byte)(treeSelect ? 3 : 2);
                    break;
            }

            // A single symbol has length 0 here: using it writes no bits at all.
            return new PrefixCode(alphabetSize, lengths, AssignCodes(lengths), symbols, treeSelect);
        }

        /// <summary>Writes the code's definition into the stream.</summary>
        public void WriteHeader(BitWriter writer)
        {
            if (_simpleSymbols is not null)
            {
                writer.Write(2, 1);
                writer.Write(2, (uint)(_simpleSymbols.Length - 1));
                var symbolBits = BitLength(_alphabetSize - 1);
                foreach (var symbol in _simpleSymbols)
                {
                    writer.Write(symbolBits, (uint)symbol);
                }

                if (_simpleSymbols.Length == 4)
                {
                    writer.Write(1, _simpleTreeSelect ? 1u : 0u);
                }

                return;
            }

            WriteComplexHeader(writer);
        }

        /// <summary>Writes one symbol.</summary>
        public void WriteSymbol(BitWriter writer, int symbol)
        {
            var length = _lengths[symbol];
            if (length > 0)
            {
                writer.Write(length, _codes[symbol]);
            }
        }

        private void WriteComplexHeader(BitWriter writer)
        {
            var count = _alphabetSize;
            while (count > 0 && _lengths[count - 1] == 0)
            {
                count--;
            }

            var tokens = RunLengthEncode(_lengths, count);

            var histogram = new int[CodeLengthSymbols];
            foreach (var token in tokens)
            {
                histogram[token.Symbol]++;
            }

            var codeLengthLengths = new byte[CodeLengthSymbols];
            var distinct = histogram.Count(h => h > 0);
            if (distinct == 1)
            {
                // One code length symbol: any non-zero length will do, and using it costs no bits.
                codeLengthLengths[Array.FindIndex(histogram, h => h > 0)] = 4;
            }
            else
            {
                ComputeLengths(histogram, CodeLengthSymbols, MaxCodeLengthCodeLength, codeLengthLengths);
            }

            var codeLengthCodes = AssignCodes(codeLengthLengths);

            var skip = 0;
            if (codeLengthLengths[CodeLengthCodeOrder[0]] == 0 && codeLengthLengths[CodeLengthCodeOrder[1]] == 0)
            {
                skip = codeLengthLengths[CodeLengthCodeOrder[2]] == 0 ? 3 : 2;
            }

            writer.Write(2, (uint)skip);

            // The reader stops as soon as the code length code is complete, so the writer must stop at the same place.
            var space = 32;
            for (var i = skip; i < CodeLengthSymbols && space > 0; i++)
            {
                var value = codeLengthLengths[CodeLengthCodeOrder[i]];
                var (bitCount, bits) = CodeLengthCodeLengthCode[value];
                writer.Write(bitCount, bits);
                if (value != 0)
                {
                    space -= 32 >> value;
                }
            }

            foreach (var token in tokens)
            {
                var length = codeLengthLengths[token.Symbol];
                if (distinct > 1 && length > 0)
                {
                    writer.Write(length, codeLengthCodes[token.Symbol]);
                }

                if (token.ExtraBitCount > 0)
                {
                    writer.Write(token.ExtraBitCount, token.ExtraBits);
                }
            }
        }

        private readonly record struct Token(int Symbol, int ExtraBitCount, uint ExtraBits);

        /// <summary>Encodes code lengths with the repeat codes 16 (previous non-zero length) and 17 (zeros).</summary>
        private static List<Token> RunLengthEncode(byte[] lengths, int count)
        {
            var tokens = new List<Token>();
            var previous = InitialPreviousLength;
            var i = 0;
            while (i < count)
            {
                var value = lengths[i];
                var end = i;
                while (end < count && lengths[end] == value)
                {
                    end++;
                }

                var run = end - i;
                if (value == 0)
                {
                    WriteZeros(tokens, run);
                }
                else
                {
                    WriteRepeats(tokens, previous, value, run);
                    previous = value;
                }

                i = end;
            }

            return tokens;
        }

        private static void WriteRepeats(List<Token> tokens, int previous, int value, int repetitions)
        {
            if (previous != value)
            {
                tokens.Add(new Token(value, 0, 0));
                repetitions--;
            }

            if (repetitions == 7)
            {
                tokens.Add(new Token(value, 0, 0));
                repetitions--;
            }

            if (repetitions < 3)
            {
                for (var i = 0; i < repetitions; i++)
                {
                    tokens.Add(new Token(value, 0, 0));
                }

                return;
            }

            // Consecutive repeat codes extend one run: (previous count - 2) * 4 + extra bits + 3. The most significant
            // digit is written first, so build the digits low to high and reverse them.
            var start = tokens.Count;
            repetitions -= 3;
            while (true)
            {
                tokens.Add(new Token(RepeatPreviousLength, 2, (uint)(repetitions & 3)));
                repetitions >>= 2;
                if (repetitions == 0)
                {
                    break;
                }

                repetitions--;
            }

            tokens.Reverse(start, tokens.Count - start);
        }

        private static void WriteZeros(List<Token> tokens, int repetitions)
        {
            if (repetitions == 11)
            {
                tokens.Add(new Token(0, 0, 0));
                repetitions--;
            }

            if (repetitions < 3)
            {
                for (var i = 0; i < repetitions; i++)
                {
                    tokens.Add(new Token(0, 0, 0));
                }

                return;
            }

            var start = tokens.Count;
            repetitions -= 3;
            while (true)
            {
                tokens.Add(new Token(RepeatZeroLength, 3, (uint)(repetitions & 7)));
                repetitions >>= 3;
                if (repetitions == 0)
                {
                    break;
                }

                repetitions--;
            }

            tokens.Reverse(start, tokens.Count - start);
        }

        /// <summary>
        /// Computes Huffman code lengths no longer than <paramref name="maxBits"/>. When the optimal tree is too deep, the counts
        /// are floored at a doubling minimum until it fits, which flattens the tree while keeping it complete.
        /// </summary>
        private static void ComputeLengths(int[] counts, int alphabetSize, int maxBits, byte[] lengths)
        {
            var symbols = new List<int>();
            for (var s = 0; s < alphabetSize; s++)
            {
                if (counts[s] > 0)
                {
                    symbols.Add(s);
                }
            }

            for (long floor = 1; ; floor <<= 1)
            {
                var nodeCount = symbols.Count * 2 - 1;
                var weight = new long[nodeCount];
                var parent = new int[nodeCount];
                var queue = new PriorityQueue<int, (long Weight, int Id)>();
                for (var i = 0; i < symbols.Count; i++)
                {
                    weight[i] = Math.Max(counts[symbols[i]], floor);
                    queue.Enqueue(i, (weight[i], i));
                }

                var next = symbols.Count;
                while (queue.Count > 1)
                {
                    var a = queue.Dequeue();
                    var b = queue.Dequeue();
                    weight[next] = weight[a] + weight[b];
                    parent[a] = next;
                    parent[b] = next;
                    queue.Enqueue(next, (weight[next], next));
                    next++;
                }

                var root = next - 1;
                var depths = new int[symbols.Count];
                var deepest = 0;
                for (var i = 0; i < symbols.Count; i++)
                {
                    var depth = 0;
                    for (var node = i; node != root; node = parent[node])
                    {
                        depth++;
                    }

                    depths[i] = depth;
                    deepest = Math.Max(deepest, depth);
                }

                if (deepest <= maxBits)
                {
                    Array.Clear(lengths);
                    for (var i = 0; i < symbols.Count; i++)
                    {
                        lengths[symbols[i]] = (byte)depths[i];
                    }

                    return;
                }
            }
        }

        /// <summary>Assigns canonical codes (shorter first, then by symbol), bit-reversed because Brotli writes codes most significant bit first.</summary>
        private static ushort[] AssignCodes(byte[] lengths)
        {
            var countPerLength = new int[MaxCodeLength + 2];
            foreach (var length in lengths)
            {
                if (length > 0)
                {
                    countPerLength[length]++;
                }
            }

            var nextCode = new int[MaxCodeLength + 2];
            var code = 0;
            for (var length = 1; length <= MaxCodeLength; length++)
            {
                code = (code + countPerLength[length - 1]) << 1;
                nextCode[length] = code;
            }

            var codes = new ushort[lengths.Length];
            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                var length = lengths[symbol];
                if (length > 0)
                {
                    codes[symbol] = Reverse((ushort)nextCode[length]++, length);
                }
            }

            return codes;
        }

        private static ushort Reverse(ushort code, int length)
        {
            var reversed = 0;
            for (var i = 0; i < length; i++)
            {
                reversed = (reversed << 1) | (code & 1);
                code >>= 1;
            }

            return (ushort)reversed;
        }

        internal static int BitLength(int value)
        {
            var bits = 0;
            while (value != 0)
            {
                value >>= 1;
                bits++;
            }

            return bits;
        }
    }
}
