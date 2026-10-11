using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PeachDrawing.Text.Brotli.Internal.Encoder
{
    /// <summary>
    /// A Brotli (RFC 7932) encoder written for this repository: LZ77 matching with a hash chain, one prefix code per
    /// meta-block for literals, one for insert-and-copy commands and one for distances, and a stored-meta-block fallback for
    /// data that does not shrink. It does not use the static dictionary, context modeling or block splitting, so it compresses
    /// less tightly than the reference encoder at the same quality, but every stream it writes is ordinary Brotli.
    /// </summary>
    internal sealed class ManagedEncoder
    {
        private const int CommandAlphabetSize = 704;
        private const int LiteralAlphabetSize = 256;
        private const int DistanceAlphabetSize = 64;
        private const int MinWindowBits = 10;
        private const int MaxWindowBits = 22;
        private const int MetaBlockSize = 1 << 20;
        private const int MinMatch = 4;
        private const int MaxCopyLength = 1 << 16;
        private const int InitialLastDistance = 4;

        // Maximum hash chain length searched per position, by quality 0-11.
        private static readonly int[] ChainDepth = [1, 2, 3, 4, 6, 10, 16, 32, 64, 128, 256, 512];

        private readonly byte[] _data;
        private readonly int _quality;
        private readonly bool _lazy;
        private readonly int _windowBits;
        private readonly int _maxDistance;
        private readonly int _hashBits;
        private readonly int[] _head;
        private readonly int[] _previous;
        private readonly int _previousMask;
        private int _nextToInsert;
        private int _lastDistance = InitialLastDistance;

        private ManagedEncoder(byte[] data, int quality)
        {
            _data = data;
            _quality = quality;
            _lazy = quality >= 5;
            _hashBits = quality <= 3 ? 15 : quality <= 6 ? 16 : 17;
            _windowBits = ChooseWindowBits(data.Length);
            _maxDistance = (1 << _windowBits) - 16;
            _head = new int[1 << _hashBits];
            var chainSize = 1;
            while (chainSize < Math.Min(data.Length, 1 << _windowBits))
            {
                chainSize <<= 1;
            }

            _previous = new int[chainSize];
            _previousMask = chainSize - 1;
        }

        /// <summary>Compresses <paramref name="data"/> as one complete Brotli stream.</summary>
        public static byte[] Compress(ReadOnlySpan<byte> data, int quality)
        {
            quality = Math.Clamp(quality, 0, 11);
            var encoder = new ManagedEncoder(data.ToArray(), quality);
            return encoder.Encode();
        }

        private static int ChooseWindowBits(int length)
        {
            var bits = MinWindowBits;
            while (bits < MaxWindowBits && (1L << bits) - 16 < length)
            {
                bits++;
            }

            return bits;
        }

        private byte[] Encode()
        {
            var writer = new BitWriter();
            WriteWindowBits(writer, _windowBits);

            if (_data.Length == 0)
            {
                writer.Write(1, 1); // ISLAST
                writer.Write(1, 1); // ISLASTEMPTY
                return writer.ToArray();
            }

            for (var start = 0; start < _data.Length; start += MetaBlockSize)
            {
                var end = Math.Min(start + MetaBlockSize, _data.Length);
                var isLast = end == _data.Length;
                var mark = writer.Mark();
                var lastDistanceBefore = _lastDistance;

                var commands = FindCommands(start, end);
                WriteCompressedMetaBlock(writer, start, end, isLast, commands);

                // Data that does not shrink goes in stored; the distance state the commands left behind then never happened.
                if (writer.BitCount - ((long)mark.Length * 8 + mark.PendingBits) > ((long)(end - start) + 8) * 8)
                {
                    writer.Rewind(mark);
                    _lastDistance = lastDistanceBefore;
                    WriteStoredMetaBlock(writer, start, end);
                    if (isLast)
                    {
                        writer.Write(1, 1); // ISLAST
                        writer.Write(1, 1); // ISLASTEMPTY
                    }
                }
            }

            return writer.ToArray();
        }

        private static void WriteWindowBits(BitWriter writer, int windowBits)
        {
            if (windowBits == 16)
            {
                writer.Write(1, 0);
            }
            else if (windowBits == 17)
            {
                writer.Write(7, 1);
            }
            else if (windowBits > 17)
            {
                writer.Write(4, (uint)(((windowBits - 17) << 1) | 1));
            }
            else
            {
                writer.Write(7, (uint)(((windowBits - 8) << 4) | 1));
            }
        }

        // ------------------------------------------------------------------ matching

        private struct Command
        {
            public int Insert;
            public int Copy; // 0: an insert-only command ending the meta-block
            public int Distance;
        }

        private List<Command> FindCommands(int start, int end)
        {
            var commands = new List<Command>();
            var depth = ChainDepth[_quality];
            var literalStart = start;
            var position = start;
            while (position < end)
            {
                var (length, distance) = FindMatch(position, end, depth);
                if (length > 0 && _lazy && position + 1 < end)
                {
                    var (nextLength, _) = FindMatch(position + 1, end, depth);
                    if (nextLength > length)
                    {
                        length = 0;
                    }
                }

                if (length == 0)
                {
                    position++;
                    continue;
                }

                commands.Add(new Command { Insert = position - literalStart, Copy = length, Distance = distance });
                position += length;
                literalStart = position;
            }

            if (literalStart < end)
            {
                commands.Add(new Command { Insert = end - literalStart, Copy = 0, Distance = 0 });
            }

            return commands;
        }

        private (int Length, int Distance) FindMatch(int position, int end, int depth)
        {
            InsertUpTo(position);
            if (position + MinMatch > _data.Length)
            {
                return (0, 0);
            }

            var limit = Math.Min(end - position, MaxCopyLength);
            if (limit < MinMatch)
            {
                return (0, 0);
            }

            var maxDistance = Math.Min(position, _maxDistance);
            var best = 0;
            var bestDistance = 0;
            var candidate = _head[Hash(position)] - 1;
            for (var chain = 0; candidate >= 0 && chain < depth; chain++)
            {
                var distance = position - candidate;
                if (distance > maxDistance)
                {
                    break;
                }

                if (best < limit && _data[candidate + best] == _data[position + best])
                {
                    var length = _data.AsSpan(candidate, limit).CommonPrefixLength(_data.AsSpan(position, limit));
                    if (length > best)
                    {
                        best = length;
                        bestDistance = distance;
                        if (best == limit)
                        {
                            break;
                        }
                    }
                }

                candidate = _previous[candidate & _previousMask] - 1;
            }

            // Short, far matches cost more to describe than the literals they replace.
            var worthwhile = best >= 6 || (best == 5 && bestDistance < (1 << 18)) || (best == MinMatch && bestDistance < (1 << 12));
            return worthwhile ? (best, bestDistance) : (0, 0);
        }

        private void InsertUpTo(int position)
        {
            var lastHashable = _data.Length - MinMatch;
            while (_nextToInsert < position)
            {
                if (_nextToInsert <= lastHashable)
                {
                    var hash = Hash(_nextToInsert);
                    _previous[_nextToInsert & _previousMask] = _head[hash];
                    _head[hash] = _nextToInsert + 1;
                }

                _nextToInsert++;
            }
        }

        private int Hash(int position) =>
            (int)((BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(position)) * 0x1E35A7BDu) >> (32 - _hashBits));

        // ------------------------------------------------------------------ meta-blocks

        private void WriteStoredMetaBlock(BitWriter writer, int start, int end)
        {
            writer.Write(1, 0); // ISLAST
            WriteMetaBlockLength(writer, end - start);
            writer.Write(1, 1); // ISUNCOMPRESSED
            writer.AlignToByte();
            writer.WriteBytes(_data.AsSpan(start, end - start));
        }

        private static void WriteMetaBlockLength(BitWriter writer, int length)
        {
            var nibbles = 4;
            while ((length - 1) >= (1 << (4 * nibbles)))
            {
                nibbles++;
            }

            writer.Write(2, (uint)(nibbles - 4));
            writer.Write(4 * nibbles, (uint)(length - 1));
        }

        private void WriteCompressedMetaBlock(BitWriter writer, int start, int end, bool isLast, List<Command> commands)
        {
            // Symbol statistics for the three prefix codes.
            var literalCounts = new int[LiteralAlphabetSize];
            var commandCounts = new int[CommandAlphabetSize];
            var distanceCounts = new int[DistanceAlphabetSize];
            var lastDistance = _lastDistance;
            var cursor = start;
            foreach (var command in commands)
            {
                for (var i = 0; i < command.Insert; i++)
                {
                    literalCounts[_data[cursor + i]]++;
                }

                cursor += command.Insert + command.Copy;
                commandCounts[CommandSymbol(command)]++;
                if (command.Copy > 0)
                {
                    distanceCounts[DistanceSymbol(command.Distance, ref lastDistance, out _, out _)]++;
                }
            }

            var literalCode = PrefixCode.Build(literalCounts, LiteralAlphabetSize);
            var commandCode = PrefixCode.Build(commandCounts, CommandAlphabetSize);
            var distanceCode = PrefixCode.Build(distanceCounts, DistanceAlphabetSize);

            // Meta-block header.
            writer.Write(1, isLast ? 1u : 0u);
            if (isLast)
            {
                writer.Write(1, 0); // ISLASTEMPTY
            }

            WriteMetaBlockLength(writer, end - start);
            if (!isLast)
            {
                writer.Write(1, 0); // ISUNCOMPRESSED
            }

            writer.Write(1, 0); // NBLTYPESL = 1
            writer.Write(1, 0); // NBLTYPESI = 1
            writer.Write(1, 0); // NBLTYPESD = 1
            writer.Write(2, 0); // NPOSTFIX
            writer.Write(4, 0); // NDIRECT
            writer.Write(2, 0); // context mode of the one literal block type
            writer.Write(1, 0); // NTREESL = 1
            writer.Write(1, 0); // NTREESD = 1

            literalCode.WriteHeader(writer);
            commandCode.WriteHeader(writer);
            distanceCode.WriteHeader(writer);

            // Commands.
            lastDistance = _lastDistance;
            cursor = start;
            foreach (var command in commands)
            {
                var insertCode = LengthCode(command.Insert, Prefix.InsertLengthOffset);
                var copyCode = LengthCode(Math.Max(command.Copy, 2), Prefix.CopyLengthOffset);
                commandCode.WriteSymbol(writer, CommandSymbol(insertCode, copyCode));
                writer.Write(Prefix.InsertLengthNBits[insertCode], (uint)(command.Insert - Prefix.InsertLengthOffset[insertCode]));
                writer.Write(Prefix.CopyLengthNBits[copyCode], (uint)(Math.Max(command.Copy, 2) - Prefix.CopyLengthOffset[copyCode]));

                for (var i = 0; i < command.Insert; i++)
                {
                    literalCode.WriteSymbol(writer, _data[cursor + i]);
                }

                cursor += command.Insert + command.Copy;
                if (command.Copy > 0)
                {
                    var symbol = DistanceSymbol(command.Distance, ref lastDistance, out var extraBitCount, out var extraBits);
                    distanceCode.WriteSymbol(writer, symbol);
                    writer.Write(extraBitCount, extraBits);
                }
            }

            _lastDistance = lastDistance;
        }

        private static int CommandSymbol(Command command) =>
            CommandSymbol(
                LengthCode(command.Insert, Prefix.InsertLengthOffset),
                LengthCode(Math.Max(command.Copy, 2), Prefix.CopyLengthOffset));

        /// <summary>
        /// The insert-and-copy symbol for an insert length code and a copy length code, always from the cells that read an
        /// explicit distance next (RFC 7932 section 5: cells 2 to 10 of the 704-symbol alphabet).
        /// </summary>
        private static int CommandSymbol(int insertCode, int copyCode)
        {
            var cell = (insertCode >> 3, copyCode >> 3) switch
            {
                (0, 0) => 2,
                (0, 1) => 3,
                (1, 0) => 4,
                (1, 1) => 5,
                (0, 2) => 6,
                (2, 0) => 7,
                (1, 2) => 8,
                (2, 1) => 9,
                _ => 10,
            };
            return cell * 64 + (insertCode & 7) * 8 + (copyCode & 7);
        }

        private static int LengthCode(int length, int[] offsets)
        {
            var code = offsets.Length - 1;
            while (offsets[code] > length)
            {
                code--;
            }

            return code;
        }

        /// <summary>
        /// The distance symbol and extra bits for a distance, using code 0 (the last distance) when it repeats it. With no
        /// postfix bits and no direct codes, a distance d above the 16 ring-buffer codes is coded as x = d + 3: the symbol is
        /// 16 + 2 * (bits - 1) + the bit below the leading one, with the remaining low bits as extra bits.
        /// </summary>
        private static int DistanceSymbol(int distance, ref int lastDistance, out int extraBitCount, out uint extraBits)
        {
            if (distance == lastDistance)
            {
                extraBitCount = 0;
                extraBits = 0;
                return 0;
            }

            lastDistance = distance;
            var x = distance + 3;
            var highBit = PrefixCode.BitLength(x) - 1;
            extraBitCount = highBit - 1;
            var next = (x >> extraBitCount) & 1;
            extraBits = (uint)(x & ((1 << extraBitCount) - 1));
            return 16 + 2 * (extraBitCount - 1) + next;
        }
    }
}
