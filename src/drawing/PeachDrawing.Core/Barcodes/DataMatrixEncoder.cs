// Portions derived from FreePDF (https://github.com/io7/FreePDF), MIT License,
// Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany.
// See THIRD-PARTY-LICENSES.md in this package.

using System.Text;

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// Encodes text as a Data Matrix ECC 200 symbol, written from ISO/IEC 16022:2006. The result is the module matrix
    /// with its finder and clock patterns, without quiet zone, indexed [row, column], true for dark.
    /// </summary>
    internal static class DataMatrixEncoder
    {
        /// <summary>
        /// One ECC 200 symbol size (ISO/IEC 16022 table 7).
        /// </summary>
        sealed class SymbolSize(int rows, int columns, int regionRows, int regionColumns, int dataCodewords, int eccCodewords, int blocks)
        {
            public readonly int Rows = rows;
            public readonly int Columns = columns;
            public readonly int RegionRows = regionRows;         // data region size without finder and clock
            public readonly int RegionColumns = regionColumns;
            public readonly int DataCodewords = dataCodewords;
            public readonly int EccCodewords = eccCodewords;     // all blocks together
            public readonly int Blocks = blocks;

            public int RegionsVertically => Rows / (RegionRows + 2);
            public int RegionsHorizontally => Columns / (RegionColumns + 2);
        }

        // Square sizes first, smallest first, then the rectangular ones.
        static readonly SymbolSize[] Sizes =
        [
            new(10, 10, 8, 8, 3, 5, 1),
            new(12, 12, 10, 10, 5, 7, 1),
            new(14, 14, 12, 12, 8, 10, 1),
            new(16, 16, 14, 14, 12, 12, 1),
            new(18, 18, 16, 16, 18, 14, 1),
            new(20, 20, 18, 18, 22, 18, 1),
            new(22, 22, 20, 20, 30, 20, 1),
            new(24, 24, 22, 22, 36, 24, 1),
            new(26, 26, 24, 24, 44, 28, 1),
            new(32, 32, 14, 14, 62, 36, 1),
            new(36, 36, 16, 16, 86, 42, 1),
            new(40, 40, 18, 18, 114, 48, 1),
            new(44, 44, 20, 20, 144, 56, 1),
            new(48, 48, 22, 22, 174, 68, 1),
            new(52, 52, 24, 24, 204, 84, 2),
            new(64, 64, 14, 14, 280, 112, 2),
            new(72, 72, 16, 16, 368, 144, 4),
            new(80, 80, 18, 18, 456, 192, 4),
            new(88, 88, 20, 20, 576, 224, 4),
            new(96, 96, 22, 22, 696, 272, 4),
            new(104, 104, 24, 24, 816, 336, 6),
            new(120, 120, 18, 18, 1050, 408, 6),
            new(132, 132, 20, 20, 1304, 496, 8),
            new(144, 144, 22, 22, 1558, 620, 10),
            new(8, 18, 6, 16, 5, 7, 1),
            new(8, 32, 6, 14, 10, 11, 1),
            new(12, 26, 10, 24, 16, 14, 1),
            new(12, 36, 10, 16, 22, 18, 1),
            new(16, 36, 14, 16, 32, 24, 1),
            new(16, 48, 14, 22, 49, 28, 1),
        ];

        const int PadCodeword = 129;
        const int UpperShift = 235;
        const int Fnc1Codeword = 232;
        const int EciCodeword = 241;
        const int Base256Latch = 231;
        const int Utf8Eci = 26;

        // Throws for a lone surrogate instead of writing U+FFFD in its place.
        static readonly UTF8Encoding StrictUtf8 = new(false, true);

        /// <summary>
        /// The character that stands for FNC1, as for Code 128.
        /// </summary>
        internal const char Fnc1 = Code128.Fnc1;

        /// <summary>
        /// Encodes the text. Rows and columns 0 pick the smallest square symbol the data fits in. The encoding string
        /// has one letter per character; 'b' marks Base256, every other letter ASCII encodation.
        /// </summary>
        public static bool[,] Encode(string text, string encoding, int rows, int columns)
        {
            byte[] data = EncodeData(text, encoding);

            SymbolSize size;
            if (rows == 0 && columns == 0)
            {
                size = Sizes.FirstOrDefault(s => s.Rows == s.Columns && s.DataCodewords >= data.Length)
                       ?? throw new ArgumentException("The given data is too big for the matrix size.");
            }
            else
            {
                size = Sizes.FirstOrDefault(s => s.Rows == rows && s.Columns == columns)
                       ?? throw new ArgumentException($"'{rows}'x'{columns}' is an invalid ECC 200 Data Matrix size.");
                if (data.Length > size.DataCodewords)
                    throw new ArgumentException("The given data is too big for the matrix size.");
            }

            byte[] codewords = AddErrorCorrection(Pad(data, size.DataCodewords), size);
            bool[,] mapping = Place(codewords, size.RegionsVertically * size.RegionRows, size.RegionsHorizontally * size.RegionColumns);
            return Assemble(mapping, size);
        }

        /// <summary>
        /// ASCII encodation: two digits in one codeword (130 + value), other ASCII as value + 1, FNC1 as 232. Text with
        /// other characters is UTF-8 after ECI 26, its bytes above 127 with Upper Shift; without the ECI, readers guess
        /// the character set of such bytes (ISO 8859-1 by the standard, Shift_JIS by some readers). Runs marked 'b'
        /// use Base256.
        /// </summary>
        static byte[] EncodeData(string text, string encoding)
        {
            bool ascii = text.All(ch => ch < 128 || ch == Fnc1);
            var result = new List<byte>(text.Length + 4);
            if (!ascii)
            {
                // GS1 data starts with FNC1 and is ASCII; an ECI in front of FNC1 would make it ordinary data.
                if (text.Length > 0 && text[0] == Fnc1)
                    throw new ArgumentException("GS1 data (text that starts with FNC1) may hold only ASCII characters.");
                result.Add(EciCodeword);
                result.Add(Utf8Eci + 1); // ECI numbers 0 to 126 take one codeword, the number plus 1
            }

            int i = 0;
            while (i < text.Length)
            {
                if (i < encoding.Length && Char.ToLowerInvariant(encoding[i]) == 'b' && text[i] != Fnc1)
                {
                    int end = i;
                    while (end < text.Length && end < encoding.Length && Char.ToLowerInvariant(encoding[end]) == 'b' && text[end] != Fnc1)
                        end++;
                    // A run never ends between the two halves of a surrogate pair.
                    if (end < text.Length && Char.IsHighSurrogate(text[end - 1]) && Char.IsLowSurrogate(text[end]))
                        end++;
                    AppendBase256(result, Bytes(text.Substring(i, end - i), ascii));
                    i = end;
                    continue;
                }

                char ch = text[i];
                if (ch == Fnc1)
                {
                    result.Add(Fnc1Codeword);
                    i++;
                }
                else if (i + 1 < text.Length && IsDigit(ch) && IsDigit(text[i + 1]) && !IsBase256(encoding, i + 1))
                {
                    result.Add((byte)(130 + (ch - '0') * 10 + (text[i + 1] - '0')));
                    i += 2;
                }
                else
                {
                    // One character may be one UTF-8 byte sequence; each byte is a separate codeword.
                    int length = ascii || !Char.IsHighSurrogate(ch) || i + 1 >= text.Length ? 1 : 2;
                    foreach (byte b in Bytes(text.Substring(i, length), ascii))
                    {
                        if (b < 128)
                            result.Add((byte)(b + 1));
                        else
                        {
                            result.Add(UpperShift);
                            result.Add((byte)(b - 127));
                        }
                    }
                    i += length;
                }
            }
            return result.ToArray();
        }

        static bool IsDigit(char ch) => ch is >= '0' and <= '9';

        static bool IsBase256(string encoding, int index)
            => index < encoding.Length && Char.ToLowerInvariant(encoding[index]) == 'b';

        static byte[] Bytes(string text, bool ascii)
            => ascii ? text.Select(ch => (byte)ch).ToArray() : StrictUtf8.GetBytes(text);

        /// <summary>
        /// Base256: the latch, a length of one (up to 249) or two codewords, the bytes, all after the latch randomized
        /// by the 255-state algorithm (annex B), which depends on each codeword's position. Back to ASCII after.
        /// </summary>
        static void AppendBase256(List<byte> result, byte[] bytes)
        {
            result.Add(Base256Latch);
            var field = new List<int>();
            if (bytes.Length <= 249)
                field.Add(bytes.Length);
            else
            {
                field.Add(bytes.Length / 250 + 249);
                field.Add(bytes.Length % 250);
            }
            field.AddRange(bytes.Select(b => (int)b));
            foreach (int value in field)
            {
                int position = result.Count + 1;
                int pseudoRandom = 149 * position % 255 + 1;
                result.Add((byte)((value + pseudoRandom) % 256));
            }
        }

        /// <summary>
        /// Fills the data capacity: one plain pad codeword, the following ones randomized by the 253-state
        /// algorithm (annex B).
        /// </summary>
        static byte[] Pad(byte[] data, int capacity)
        {
            var result = new byte[capacity];
            Array.Copy(data, result, data.Length);
            for (int i = data.Length; i < capacity; i++)
            {
                if (i == data.Length)
                    result[i] = PadCodeword;
                else
                {
                    int position = i + 1;
                    int value = PadCodeword + 149 * position % 253 + 1;
                    result[i] = (byte)(value > 254 ? value - 254 : value);
                }
            }
            return result;
        }

        /// <summary>
        /// Splits the data among the blocks (codeword i goes to block i mod blocks), computes each block's
        /// error correction codewords, and interleaves them the same way after the data.
        /// </summary>
        static byte[] AddErrorCorrection(byte[] data, SymbolSize size)
        {
            int blocks = size.Blocks;
            int eccPerBlock = size.EccCodewords / blocks;
            var result = new byte[data.Length + size.EccCodewords];
            Array.Copy(data, result, data.Length);
            for (int block = 0; block < blocks; block++)
            {
                var blockData = new List<byte>();
                for (int i = block; i < data.Length; i += blocks)
                    blockData.Add(data[i]);
                byte[] ecc = ReedSolomon.Remainder(blockData.ToArray(), eccPerBlock);
                for (int k = 0; k < eccPerBlock; k++)
                    result[data.Length + block + k * blocks] = ecc[k];
            }
            return result;
        }

        /// <summary>
        /// Reed-Solomon over GF(2^8) with the field polynomial x^8 + x^5 + x^3 + x^2 + 1 (0x12D) and the generator
        /// polynomial (x - a^1)(x - a^2)...(x - a^n).
        /// </summary>
        static class ReedSolomon
        {
            static readonly byte[] Exp = new byte[512];
            static readonly byte[] Log = new byte[256];

            static ReedSolomon()
            {
                int x = 1;
                for (int i = 0; i < 255; i++)
                {
                    Exp[i] = (byte)x;
                    Log[x] = (byte)i;
                    x <<= 1;
                    if ((x & 0x100) != 0)
                        x ^= 0x12D;
                }
                for (int i = 255; i < Exp.Length; i++)
                    Exp[i] = Exp[i - 255];
            }

            static byte Multiply(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

            public static byte[] Remainder(byte[] data, int degree)
            {
                byte[] generator = [1];
                for (int i = 1; i <= degree; i++)
                {
                    var next = new byte[generator.Length + 1];
                    for (int j = 0; j < generator.Length; j++)
                    {
                        next[j] ^= generator[j];
                        next[j + 1] ^= Multiply(generator[j], Exp[i]);
                    }
                    generator = next;
                }

                var remainder = new byte[degree];
                foreach (byte codeword in data)
                {
                    byte factor = (byte)(codeword ^ remainder[0]);
                    Array.Copy(remainder, 1, remainder, 0, degree - 1);
                    remainder[degree - 1] = 0;
                    for (int i = 0; i < degree; i++)
                        remainder[i] ^= Multiply(generator[i + 1], factor);
                }
                return remainder;
            }
        }

        /// <summary>
        /// Places the codewords in the mapping matrix (the data regions joined, without finders and clocks) along
        /// the diagonal sweeps of ISO/IEC 16022 annex F, with its four corner shapes. A corner left unfilled gets
        /// the fixed 2 x 2 pattern.
        /// </summary>
        static bool[,] Place(byte[] codewords, int rows, int columns)
        {
            var dark = new bool[rows, columns];
            var placed = new bool[rows, columns];
            int index = 0;

            void Module(int row, int column, int bit)
            {
                if (row < 0)
                {
                    row += rows;
                    column += 4 - (rows + 4) % 8;
                }
                if (column < 0)
                {
                    column += columns;
                    row += 4 - (columns + 4) % 8;
                }
                // Bit 1 is the most significant bit of the codeword.
                dark[row, column] = index < codewords.Length && (codewords[index] & (0x80 >> (bit - 1))) != 0;
                placed[row, column] = true;
            }

            void Utah(int row, int column)
            {
                Module(row - 2, column - 2, 1);
                Module(row - 2, column - 1, 2);
                Module(row - 1, column - 2, 3);
                Module(row - 1, column - 1, 4);
                Module(row - 1, column, 5);
                Module(row, column - 2, 6);
                Module(row, column - 1, 7);
                Module(row, column, 8);
                index++;
            }

            void Corner(params (int Row, int Column)[] modules)
            {
                for (int k = 0; k < 8; k++)
                    Module(modules[k].Row, modules[k].Column, k + 1);
                index++;
            }

            int r = 4;
            int c = 0;
            do
            {
                if (r == rows && c == 0)
                    Corner((rows - 1, 0), (rows - 1, 1), (rows - 1, 2), (0, columns - 2), (0, columns - 1), (1, columns - 1), (2, columns - 1), (3, columns - 1));
                if (r == rows - 2 && c == 0 && columns % 4 != 0)
                    Corner((rows - 3, 0), (rows - 2, 0), (rows - 1, 0), (0, columns - 4), (0, columns - 3), (0, columns - 2), (0, columns - 1), (1, columns - 1));
                if (r == rows - 2 && c == 0 && columns % 8 == 4)
                    Corner((rows - 3, 0), (rows - 2, 0), (rows - 1, 0), (0, columns - 2), (0, columns - 1), (1, columns - 1), (2, columns - 1), (3, columns - 1));
                if (r == rows + 4 && c == 2 && columns % 8 == 0)
                    Corner((rows - 1, 0), (rows - 1, columns - 1), (0, columns - 3), (0, columns - 2), (0, columns - 1), (1, columns - 3), (1, columns - 2), (1, columns - 1));

                // Up and to the right.
                do
                {
                    if (r < rows && c >= 0 && !placed[r, c])
                        Utah(r, c);
                    r -= 2;
                    c += 2;
                } while (r >= 0 && c < columns);
                r += 1;
                c += 3;

                // Down and to the left.
                do
                {
                    if (r >= 0 && c < columns && !placed[r, c])
                        Utah(r, c);
                    r += 2;
                    c -= 2;
                } while (r < rows && c >= 0);
                r += 3;
                c += 1;
            } while (r < rows || c < columns);

            if (!placed[rows - 1, columns - 1])
            {
                dark[rows - 1, columns - 1] = true;
                dark[rows - 2, columns - 2] = true;
            }
            return dark;
        }

        /// <summary>
        /// Puts the data regions into the symbol: each region has a solid finder on its left and bottom edges and
        /// alternating clock modules on its top and right edges.
        /// </summary>
        static bool[,] Assemble(bool[,] mapping, SymbolSize size)
        {
            var symbol = new bool[size.Rows, size.Columns];
            int regionHeight = size.RegionRows + 2;
            int regionWidth = size.RegionColumns + 2;
            for (int row = 0; row < size.Rows; row++)
            {
                for (int column = 0; column < size.Columns; column++)
                {
                    int r = row % regionHeight;
                    int c = column % regionWidth;
                    bool dark;
                    if (c == 0 || r == regionHeight - 1)
                        dark = true;                                // finder
                    else if (r == 0)
                        dark = c % 2 == 0;                          // top clock
                    else if (c == regionWidth - 1)
                        dark = r % 2 == 1;                          // right clock
                    else
                        dark = mapping[row / regionHeight * size.RegionRows + r - 1, column / regionWidth * size.RegionColumns + c - 1];
                    symbol[row, column] = dark;
                }
            }
            return symbol;
        }
    }
}
