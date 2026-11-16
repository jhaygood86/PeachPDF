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
    /// Encodes text as a QR code (model 2), written from ISO/IEC 18004:2015.
    /// The result is the module matrix without quiet zone, indexed [row, column], true for dark.
    /// </summary>
    internal static class QrEncoder
    {
        internal const int MinVersion = 1;
        internal const int MaxVersion = 40;

        const string AlphanumericCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

        // ECI designator 26 marks the bytes that follow as UTF-8.
        const int Utf8Eci = 26;

        // Throws for a lone surrogate instead of writing U+FFFD in its place.
        static readonly UTF8Encoding StrictUtf8 = new(false, true);

        // Error correction codewords per block (ISO/IEC 18004:2015, table 9), by level L, M, Q, H and version 1 to 40.
        static readonly byte[][] EccCodewordsPerBlock =
        [
            [7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
            [10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
            [13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
            [17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        ];

        // Number of error correction blocks (same table), by level L, M, Q, H and version 1 to 40.
        static readonly byte[][] BlockCount =
        [
            [1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
            [1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
            [1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
            [1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
        ];

        /// <summary>
        /// Encodes the text. A requested version of 0 picks the smallest version the text fits in.
        /// </summary>
        public static bool[,] Encode(string text, QrErrorCorrectionLevel level, int requestedVersion, bool markUtf8)
        {
            var segment = Segment.Create(text, markUtf8);
            int version = ChooseVersion(segment, level, requestedVersion);
            byte[] codewords = AddErrorCorrection(BuildDataCodewords(segment, version, level), version, level);

            var symbol = new Symbol(version);
            symbol.DrawFunctionPatterns(level);
            symbol.PlaceCodewords(codewords);
            symbol.ApplyBestMask(level);
            return symbol.Modules;
        }

        /// <summary>
        /// The number of data codewords a symbol of this version and level holds.
        /// </summary>
        internal static int DataCodewords(int version, QrErrorCorrectionLevel level)
            => TotalCodewords(version) - EccCodewordsPerBlock[(int)level][version - 1] * BlockCount[(int)level][version - 1];

        /// <summary>
        /// The number of codewords (data and error correction) a symbol of this version holds:
        /// its modules outside the function patterns, divided by 8. The bits left over are remainder bits.
        /// </summary>
        internal static int TotalCodewords(int version)
        {
            int size = 17 + 4 * version;
            int modules = size * size
                          - 3 * 64           // finder patterns with their separators
                          - 31               // format information (two copies) and the dark module
                          - 2 * (size - 16); // timing patterns between the separators
            int alignmentPerSide = AlignmentPositions(version).Length;
            if (alignmentPerSide > 0)
            {
                int patterns = alignmentPerSide * alignmentPerSide - 3;
                int onTiming = 2 * (alignmentPerSide - 2);
                // Alignment patterns on the timing lines share 5 modules with them.
                modules -= 25 * patterns - 5 * onTiming;
            }
            if (version >= 7)
                modules -= 2 * 18; // version information
            return modules / 8;
        }

        /// <summary>
        /// The row and column coordinates of the alignment pattern centres. All gaps but the first are equal
        /// and even; version 32 is the one exception to the rounding below (ISO/IEC 18004:2015, annex E).
        /// </summary>
        internal static int[] AlignmentPositions(int version)
        {
            if (version == 1)
                return [];

            int count = version / 7 + 2;
            int last = 4 * version + 10;
            int step;
            if (version == 32)
                step = 26;
            else
            {
                int gap = (last - 6 + count - 2) / (count - 1); // rounded up
                step = gap + gap % 2;                           // and made even
            }

            var positions = new int[count];
            positions[0] = 6;
            for (int i = count - 1, position = last; i >= 1; i--, position -= step)
                positions[i] = position;
            return positions;
        }

        static int ChooseVersion(Segment segment, QrErrorCorrectionLevel level, int requestedVersion)
        {
            int first = requestedVersion == 0 ? MinVersion : requestedVersion;
            int last = requestedVersion == 0 ? MaxVersion : requestedVersion;
            for (int version = first; version <= last; version++)
            {
                if (segment.Fits(version, DataCodewords(version, level) * 8))
                    return version;
            }
            throw new ArgumentException(requestedVersion == 0 ? $"The text is too long for a QR code with error correction level {level}." : $"The text is too long for a version {requestedVersion} QR code with error correction level {level}.");
        }

        static byte[] BuildDataCodewords(Segment segment, int version, QrErrorCorrectionLevel level)
        {
            int capacity = DataCodewords(version, level);
            var bits = new BitWriter(capacity);
            segment.Write(bits, version);

            // Terminator of up to four zero bits, then zero bits to the next codeword boundary.
            bits.Append(0, Math.Min(4, capacity * 8 - bits.Length));
            bits.Append(0, (8 - bits.Length % 8) % 8);

            // Pad codewords 11101100 and 00010001 in turn fill the remaining capacity.
            for (int pad = 0xEC; bits.Length < capacity * 8; pad ^= 0xEC ^ 0x11)
                bits.Append(pad, 8);

            return bits.ToBytes();
        }

        /// <summary>
        /// Splits the data codewords into blocks, adds each block's error correction codewords and interleaves
        /// them: data codewords block by block, then error correction codewords block by block.
        /// </summary>
        static byte[] AddErrorCorrection(byte[] data, int version, QrErrorCorrectionLevel level)
        {
            int eccLength = EccCodewordsPerBlock[(int)level][version - 1];
            int blocks = BlockCount[(int)level][version - 1];
            int total = TotalCodewords(version);

            // The short blocks come first; the long ones hold one data codeword more.
            int shortBlocks = blocks - total % blocks;
            int shortDataLength = total / blocks - eccLength;

            var dataBlocks = new byte[blocks][];
            var eccBlocks = new byte[blocks][];
            int offset = 0;
            for (int block = 0; block < blocks; block++)
            {
                int length = block < shortBlocks ? shortDataLength : shortDataLength + 1;
                dataBlocks[block] = new byte[length];
                Array.Copy(data, offset, dataBlocks[block], 0, length);
                eccBlocks[block] = ReedSolomon.Remainder(dataBlocks[block], eccLength);
                offset += length;
            }
            if (offset != data.Length)
                throw new InvalidOperationException("QR block structure does not match the data codewords.");

            var result = new byte[total];
            int index = 0;
            for (int i = 0; i <= shortDataLength; i++)
            {
                foreach (var block in dataBlocks)
                {
                    if (i < block.Length)
                        result[index++] = block[i];
                }
            }
            for (int i = 0; i < eccLength; i++)
            {
                foreach (var block in eccBlocks)
                    result[index++] = block[i];
            }
            return result;
        }

        /// <summary>
        /// One segment of data in a single mode, optionally preceded by the UTF-8 ECI.
        /// </summary>
        sealed class Segment
        {
            Segment(int modeIndicator, int characterCount, BitWriter data, bool utf8)
            {
                _modeIndicator = modeIndicator;
                _characterCount = characterCount;
                _data = data;
                _utf8 = utf8;
            }

            const int NumericMode = 0b0001;
            const int AlphanumericMode = 0b0010;
            const int ByteMode = 0b0100;
            const int EciMode = 0b0111;

            readonly int _modeIndicator;
            readonly int _characterCount;
            readonly BitWriter _data;
            readonly bool _utf8;

            public static Segment Create(string text, bool markUtf8)
            {
                if (text.Length > 0 && text.All(ch => ch is >= '0' and <= '9'))
                    return CreateNumeric(text);
                if (text.Length > 0 && text.All(ch => AlphanumericCharacters.IndexOf(ch) >= 0))
                    return CreateAlphanumeric(text);
                return CreateBytes(text, markUtf8);
            }

            static Segment CreateNumeric(string digits)
            {
                // Groups of three digits take 10 bits; a final group of two takes 7, of one 4.
                var bits = new BitWriter(digits.Length / 3 * 10 + 10);
                for (int i = 0; i < digits.Length; i += 3)
                {
                    int length = Math.Min(3, digits.Length - i);
                    int value = Int32.Parse(digits.Substring(i, length), System.Globalization.CultureInfo.InvariantCulture);
                    bits.Append(value, length * 3 + 1);
                }
                return new Segment(NumericMode, digits.Length, bits, false);
            }

            static Segment CreateAlphanumeric(string text)
            {
                // Pairs of characters take 11 bits (45 * first + second); a final single character takes 6.
                var bits = new BitWriter(text.Length / 2 * 11 + 6);
                int i = 0;
                for (; i + 1 < text.Length; i += 2)
                    bits.Append(AlphanumericCharacters.IndexOf(text[i]) * 45 + AlphanumericCharacters.IndexOf(text[i + 1]), 11);
                if (i < text.Length)
                    bits.Append(AlphanumericCharacters.IndexOf(text[i]), 6);
                return new Segment(AlphanumericMode, text.Length, bits, false);
            }

            static Segment CreateBytes(string text, bool markUtf8)
            {
                // ASCII is the same in every QR character set. Other text is written as UTF-8, by default
                // marked with ECI 26 because the default character set of the byte mode is ISO-8859-1.
                bool ascii = text.All(ch => ch < 128);
                byte[] bytes = ascii ? Encoding.ASCII.GetBytes(text) : StrictUtf8.GetBytes(text);
                var bits = new BitWriter(bytes.Length * 8);
                foreach (byte b in bytes)
                    bits.Append(b, 8);
                return new Segment(ByteMode, bytes.Length, bits, !ascii && markUtf8);
            }

            int CountBits(int version)
            {
                int range = version <= 9 ? 0 : version <= 26 ? 1 : 2;
                return _modeIndicator switch
                {
                    NumericMode => new[] { 10, 12, 14 }[range],
                    AlphanumericMode => new[] { 9, 11, 13 }[range],
                    _ => new[] { 8, 16, 16 }[range],
                };
            }

            public bool Fits(int version, int capacityBits)
            {
                int countBits = CountBits(version);
                if (_characterCount >= 1 << countBits)
                    return false;
                int length = (_utf8 ? 4 + 8 : 0) + 4 + countBits + _data.Length;
                return length <= capacityBits;
            }

            public void Write(BitWriter bits, int version)
            {
                if (_utf8)
                {
                    bits.Append(EciMode, 4);
                    bits.Append(Utf8Eci, 8); // designators below 128 take one byte 0xxxxxxx
                }
                bits.Append(_modeIndicator, 4);
                bits.Append(_characterCount, CountBits(version));
                bits.Append(_data);
            }
        }

        /// <summary>
        /// A growable sequence of bits, most significant bit first.
        /// </summary>
        sealed class BitWriter
        {
            public BitWriter(int capacity)
            {
                _bits = new List<bool>(capacity);
            }

            readonly List<bool> _bits;

            public int Length => _bits.Count;

            public void Append(int value, int count)
            {
                for (int i = count - 1; i >= 0; i--)
                    _bits.Add(((value >> i) & 1) != 0);
            }

            public void Append(BitWriter other) => _bits.AddRange(other._bits);

            public byte[] ToBytes()
            {
                var bytes = new byte[(_bits.Count + 7) / 8];
                for (int i = 0; i < _bits.Count; i++)
                {
                    if (_bits[i])
                        bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
                }
                return bytes;
            }
        }

        /// <summary>
        /// Reed-Solomon error correction over GF(2^8) with the field polynomial x^8 + x^4 + x^3 + x^2 + 1.
        /// </summary>
        internal static class ReedSolomon
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
                        x ^= 0x11D;
                }
                for (int i = 255; i < Exp.Length; i++)
                    Exp[i] = Exp[i - 255];
            }

            static byte Multiply(byte a, byte b)
                => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

            /// <summary>
            /// The generator polynomial (x - a^0)(x - a^1)...(x - a^(degree-1)), coefficients from the highest power down.
            /// </summary>
            static byte[] Generator(int degree)
            {
                byte[] generator = [1];
                for (int i = 0; i < degree; i++)
                {
                    var next = new byte[generator.Length + 1];
                    for (int j = 0; j < generator.Length; j++)
                    {
                        next[j] ^= generator[j];
                        next[j + 1] ^= Multiply(generator[j], Exp[i]);
                    }
                    generator = next;
                }
                return generator;
            }

            /// <summary>
            /// The error correction codewords: the remainder of data(x) * x^degree divided by the generator polynomial.
            /// </summary>
            public static byte[] Remainder(byte[] data, int degree)
            {
                byte[] generator = Generator(degree);
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
        /// The module matrix of one symbol while it is built.
        /// </summary>
        sealed class Symbol
        {
            public Symbol(int version)
            {
                _version = version;
                _size = 17 + 4 * version;
                Modules = new bool[_size, _size];
                _isFunction = new bool[_size, _size];
            }

            readonly int _version;
            readonly int _size;
            readonly bool[,] _isFunction;

            public bool[,] Modules { get; }

            void SetFunction(int row, int column, bool dark)
            {
                Modules[row, column] = dark;
                _isFunction[row, column] = true;
            }

            public void DrawFunctionPatterns(QrErrorCorrectionLevel level)
            {
                // Timing patterns first; the finder patterns and separators then overwrite their ends.
                for (int i = 0; i < _size; i++)
                {
                    SetFunction(6, i, i % 2 == 0);
                    SetFunction(i, 6, i % 2 == 0);
                }

                DrawFinder(3, 3);
                DrawFinder(3, _size - 4);
                DrawFinder(_size - 4, 3);

                int[] positions = AlignmentPositions(_version);
                int last = positions.Length - 1;
                for (int i = 0; i <= last; i++)
                {
                    for (int j = 0; j <= last; j++)
                    {
                        bool overlapsFinder = (i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0);
                        if (!overlapsFinder)
                            DrawAlignment(positions[i], positions[j]);
                    }
                }

                // Reserve the format information area; the mask chosen later fills it in.
                DrawFormatInformation(level, 0);
                DrawVersionInformation();
            }

            /// <summary>
            /// A 7 x 7 finder pattern with the light separator around it, clipped at the symbol edge.
            /// </summary>
            void DrawFinder(int centerRow, int centerColumn)
            {
                for (int dr = -4; dr <= 4; dr++)
                {
                    for (int dc = -4; dc <= 4; dc++)
                    {
                        int row = centerRow + dr;
                        int column = centerColumn + dc;
                        if (row < 0 || row >= _size || column < 0 || column >= _size)
                            continue;
                        int ring = Math.Max(Math.Abs(dr), Math.Abs(dc));
                        SetFunction(row, column, ring != 2 && ring != 4);
                    }
                }
            }

            /// <summary>
            /// A 5 x 5 alignment pattern: dark centre, light ring, dark ring.
            /// </summary>
            void DrawAlignment(int centerRow, int centerColumn)
            {
                for (int dr = -2; dr <= 2; dr++)
                {
                    for (int dc = -2; dc <= 2; dc++)
                        SetFunction(centerRow + dr, centerColumn + dc, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
                }
            }

            /// <summary>
            /// Writes the 15-bit format information (level and mask, BCH(15,5) protected, XOR 101010000010010) in its
            /// two places, and the dark module. Bit 0 is the least significant bit.
            /// </summary>
            public void DrawFormatInformation(QrErrorCorrectionLevel level, int mask)
            {
                int levelBits = level switch
                {
                    QrErrorCorrectionLevel.L => 0b01,
                    QrErrorCorrectionLevel.M => 0b00,
                    QrErrorCorrectionLevel.Q => 0b11,
                    _ => 0b10,
                };
                int data = levelBits << 3 | mask;
                int remainder = data;
                for (int i = 0; i < 10; i++)
                    remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
                int bits = ((data << 10) | remainder) ^ 0x5412;

                // Next to the top-left finder: bits 0 to 7 down column 8 (skipping the timing row),
                // bits 8 to 14 leftwards along row 8 (skipping the timing column).
                for (int i = 0; i <= 5; i++)
                    SetFunction(i, 8, Bit(bits, i));
                SetFunction(7, 8, Bit(bits, 6));
                SetFunction(8, 8, Bit(bits, 7));
                SetFunction(8, 7, Bit(bits, 8));
                for (int i = 9; i < 15; i++)
                    SetFunction(8, 14 - i, Bit(bits, i));

                // Next to the other two finders: bits 0 to 7 leftwards along row 8 from the right edge,
                // bits 8 to 14 down column 8 to the bottom edge.
                for (int i = 0; i < 8; i++)
                    SetFunction(8, _size - 1 - i, Bit(bits, i));
                for (int i = 8; i < 15; i++)
                    SetFunction(_size - 15 + i, 8, Bit(bits, i));

                SetFunction(_size - 8, 8, true); // the dark module
            }

            /// <summary>
            /// Writes the 18-bit version information (BCH(18,6) protected) next to the top-right and bottom-left
            /// finders, from version 7 on.
            /// </summary>
            void DrawVersionInformation()
            {
                if (_version < 7)
                    return;

                int remainder = _version;
                for (int i = 0; i < 12; i++)
                    remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
                int bits = (_version << 12) | remainder;

                for (int i = 0; i < 18; i++)
                {
                    bool dark = Bit(bits, i);
                    int near = i / 3;
                    int far = _size - 11 + i % 3;
                    SetFunction(near, far, dark);  // top right: 6 rows x 3 columns
                    SetFunction(far, near, dark);  // bottom left: 3 rows x 6 columns
                }
            }

            /// <summary>
            /// Places the codewords, most significant bit first, in two-module-wide columns from the bottom right,
            /// upwards and downwards in turn, skipping function modules and the vertical timing column.
            /// Modules left over (remainder bits) stay light.
            /// </summary>
            public void PlaceCodewords(byte[] codewords)
            {
                int totalBits = codewords.Length * 8;
                int bitIndex = 0;
                bool upwards = true;
                for (int right = _size - 1; right >= 1; right -= 2)
                {
                    if (right == 6)
                        right = 5;
                    for (int step = 0; step < _size; step++)
                    {
                        int row = upwards ? _size - 1 - step : step;
                        for (int k = 0; k < 2; k++)
                        {
                            int column = right - k;
                            if (_isFunction[row, column])
                                continue;
                            if (bitIndex < totalBits)
                                Modules[row, column] = (codewords[bitIndex >> 3] & (0x80 >> (bitIndex & 7))) != 0;
                            bitIndex++;
                        }
                    }
                    upwards = !upwards;
                }
                if (bitIndex < totalBits)
                    throw new InvalidOperationException("QR codewords do not fit the symbol.");
            }

            /// <summary>
            /// Applies each of the eight data masks, scores the result and keeps the mask with the lowest penalty.
            /// </summary>
            public void ApplyBestMask(QrErrorCorrectionLevel level)
            {
                int bestMask = 0;
                int bestPenalty = Int32.MaxValue;
                for (int mask = 0; mask < 8; mask++)
                {
                    ApplyMask(mask);
                    DrawFormatInformation(level, mask);
                    int penalty = Penalty();
                    if (penalty < bestPenalty)
                    {
                        bestPenalty = penalty;
                        bestMask = mask;
                    }
                    ApplyMask(mask); // masking twice restores the modules
                }
                ApplyMask(bestMask);
                DrawFormatInformation(level, bestMask);
            }

            void ApplyMask(int mask)
            {
                for (int row = 0; row < _size; row++)
                {
                    for (int column = 0; column < _size; column++)
                    {
                        if (!_isFunction[row, column] && MaskCondition(mask, row, column))
                            Modules[row, column] = !Modules[row, column];
                    }
                }
            }

            static bool MaskCondition(int mask, int i, int j) => mask switch
            {
                0 => (i + j) % 2 == 0,
                1 => i % 2 == 0,
                2 => j % 3 == 0,
                3 => (i + j) % 3 == 0,
                4 => (i / 2 + j / 3) % 2 == 0,
                5 => i * j % 2 + i * j % 3 == 0,
                6 => (i * j % 2 + i * j % 3) % 2 == 0,
                _ => ((i + j) % 2 + i * j % 3) % 2 == 0,
            };

            /// <summary>
            /// The mask penalty of the symbol: runs of five or more modules of one colour, 2 x 2 blocks of one colour,
            /// finder-like 1:1:3:1:1 patterns next to four light modules, and the imbalance of dark and light.
            /// </summary>
            int Penalty()
            {
                int penalty = 0;
                for (int i = 0; i < _size; i++)
                {
                    penalty += LinePenalty(i, true);
                    penalty += LinePenalty(i, false);
                }

                for (int row = 0; row < _size - 1; row++)
                {
                    for (int column = 0; column < _size - 1; column++)
                    {
                        bool color = Modules[row, column];
                        if (Modules[row, column + 1] == color && Modules[row + 1, column] == color && Modules[row + 1, column + 1] == color)
                            penalty += 3;
                    }
                }

                int dark = 0;
                foreach (bool module in Modules)
                {
                    if (module)
                        dark++;
                }
                int total = _size * _size;
                penalty += 10 * (Math.Abs(dark * 20 - total * 10) / total);
                return penalty;
            }

            static readonly bool[] FinderLikeBefore = [false, false, false, false, true, false, true, true, true, false, true];
            static readonly bool[] FinderLikeAfter = [true, false, true, true, true, false, true, false, false, false, false];

            int LinePenalty(int index, bool isRow)
            {
                bool At(int i) => isRow ? Modules[index, i] : Modules[i, index];

                int penalty = 0;
                int runLength = 0;
                bool runColor = false;
                for (int i = 0; i < _size; i++)
                {
                    bool color = At(i);
                    if (i > 0 && color == runColor)
                        runLength++;
                    else
                    {
                        if (runLength >= 5)
                            penalty += runLength - 2;
                        runColor = color;
                        runLength = 1;
                    }
                }
                if (runLength >= 5)
                    penalty += runLength - 2;

                // The light quiet zone continues the line on both sides.
                for (int start = -4; start + 11 <= _size + 4; start++)
                {
                    if (Matches(FinderLikeBefore) || Matches(FinderLikeAfter))
                        penalty += 40;

                    bool Matches(bool[] pattern)
                    {
                        for (int k = 0; k < pattern.Length; k++)
                        {
                            int i = start + k;
                            bool color = i >= 0 && i < _size && At(i);
                            if (color != pattern[k])
                                return false;
                        }
                        return true;
                    }
                }
                return penalty;
            }

            static bool Bit(int value, int index) => ((value >> index) & 1) != 0;
        }
    }
}
