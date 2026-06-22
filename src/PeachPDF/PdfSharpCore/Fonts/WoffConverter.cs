using System.IO.Compression;
using System;
using System.IO;

namespace PeachPDF.PdfSharpCore.Fonts
{
    /// <summary>
    /// Converts WOFF font data to OpenType/TrueType format.
    /// WOFF wraps OpenType tables with per-table zlib compression (W3C WOFF spec).
    /// </summary>
    internal static class WoffConverter
    {
        private const uint WoffSignature = 0x774F4646; // 'wOFF'

        public static bool IsWoff(byte[] data) =>
            data.Length >= 4 && ReadUInt32BE(data, 0) == WoffSignature;

        public static byte[] Convert(byte[] woff)
        {
            // WOFF header (44 bytes total)
            // 0  signature   UInt32  0x774F4646
            // 4  flavor      UInt32  sfVersion of the original font
            // 8  length      UInt32  total size of WOFF file
            // 12 numTables   UInt16
            // 14 reserved    UInt16  must be 0
            // 16 totalSfntSize UInt32
            // 20 majorVersion UInt16
            // 22 minorVersion UInt16
            // 24 metaOffset  UInt32
            // 28 metaLength  UInt32
            // 32 metaOrigLength UInt32
            // 36 privOffset  UInt32
            // 40 privLength  UInt32

            if (woff.Length < 44)
                throw new InvalidDataException("WOFF: file too short to contain a valid header.");

            uint flavor = ReadUInt32BE(woff, 4);
            int numTables = (int)ReadUInt16BE(woff, 12);

            int minDirEnd = 44 + numTables * 20;
            if (woff.Length < minDirEnd)
                throw new InvalidDataException("WOFF: file truncated before end of table directory.");

            // Table directory entries start at offset 44, each 20 bytes:
            // 0  tag         UInt32
            // 4  offset      UInt32
            // 8  compLength  UInt32
            // 12 origLength  UInt32
            // 16 origChecksum UInt32

            var tags = new uint[numTables];
            var offsets = new uint[numTables];
            var compLengths = new uint[numTables];
            var origLengths = new uint[numTables];
            var checksums = new uint[numTables];

            int dirBase = 44;
            for (int i = 0; i < numTables; i++)
            {
                int e = dirBase + i * 20;
                tags[i] = ReadUInt32BE(woff, e);
                offsets[i] = ReadUInt32BE(woff, e + 4);
                compLengths[i] = ReadUInt32BE(woff, e + 8);
                origLengths[i] = ReadUInt32BE(woff, e + 12);
                checksums[i] = ReadUInt32BE(woff, e + 16);

                if (compLengths[i] > origLengths[i])
                    throw new InvalidDataException(
                        $"WOFF: table {i} has compLength ({compLengths[i]}) > origLength ({origLengths[i]}), which is invalid per spec.");

                long tableEnd = (long)offsets[i] + compLengths[i];
                if (tableEnd > woff.Length)
                    throw new InvalidDataException(
                        $"WOFF: table {i} data extends beyond end of file.");
            }

            // Decompress each table
            var tableData = new byte[numTables][];
            for (int i = 0; i < numTables; i++)
            {
                uint compLen = compLengths[i];
                uint origLen = origLengths[i];

                if (compLen < origLen)
                {
                    // Decompress with zlib (raw deflate wrapped in zlib header)
                    int origLenInt = checked((int)origLen);
                    using var compressed = new MemoryStream(woff, (int)offsets[i], (int)compLen);
                    using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
                    var decompressed = new byte[origLenInt];
                    int totalRead = 0;
                    while (totalRead < origLenInt)
                    {
                        int read = inflater.Read(decompressed, totalRead, origLenInt - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    if (totalRead != origLenInt)
                        throw new InvalidDataException(
                            $"WOFF: table {i} decompressed to {totalRead} bytes but expected {origLen}.");
                    tableData[i] = decompressed;
                }
                else
                {
                    // Not compressed: copy verbatim (compLen == origLen per spec after the check above)
                    int origLenInt = checked((int)origLen);
                    var raw = new byte[origLenInt];
                    Array.Copy(woff, (int)offsets[i], raw, 0, origLenInt);
                    tableData[i] = raw;
                }
            }

            // Reconstruct OpenType font
            // Offset table: 12 bytes
            //   sfVersion   UInt32  (= flavor from WOFF)
            //   numTables   UInt16
            //   searchRange UInt16  = (maximum power of 2 <= numTables) * 16
            //   entrySelector UInt16 = log2(maximum power of 2 <= numTables)
            //   rangeShift  UInt16  = numTables*16 - searchRange

            int entrySelector = 0;
            while ((1 << (entrySelector + 1)) <= numTables)
                entrySelector++;
            int searchRange = (1 << entrySelector) * 16;
            int rangeShift = numTables * 16 - searchRange;

            // Compute table offsets in output: header(12) + directory(16*n) + padded table data
            int headerSize = 12 + 16 * numTables;
            var tableOffsets = new int[numTables];
            int currentOffset = headerSize;
            for (int i = 0; i < numTables; i++)
            {
                tableOffsets[i] = currentOffset;
                // Pad to 4-byte boundary
                currentOffset += (int)origLengths[i];
                int pad = (4 - (currentOffset % 4)) % 4;
                currentOffset += pad;
            }

            byte[] output = new byte[currentOffset];
            int pos = 0;

            // Write offset table
            WriteUInt32BE(output, pos, flavor); pos += 4;
            WriteUInt16BE(output, pos, (ushort)numTables); pos += 2;
            WriteUInt16BE(output, pos, (ushort)searchRange); pos += 2;
            WriteUInt16BE(output, pos, (ushort)entrySelector); pos += 2;
            WriteUInt16BE(output, pos, (ushort)rangeShift); pos += 2;

            // Write table directory (16 bytes each: tag, checksum, offset, length)
            for (int i = 0; i < numTables; i++)
            {
                WriteUInt32BE(output, pos, tags[i]); pos += 4;
                WriteUInt32BE(output, pos, checksums[i]); pos += 4;
                WriteUInt32BE(output, pos, (uint)tableOffsets[i]); pos += 4;
                WriteUInt32BE(output, pos, origLengths[i]); pos += 4;
            }

            // Write table data
            for (int i = 0; i < numTables; i++)
            {
                Array.Copy(tableData[i], 0, output, tableOffsets[i], tableData[i].Length);
            }

            return output;
        }

        private static uint ReadUInt32BE(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) | data[offset + 3];

        private static ushort ReadUInt16BE(byte[] data, int offset) =>
            (ushort)(((ushort)data[offset] << 8) | data[offset + 1]);

        private static void WriteUInt32BE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        private static void WriteUInt16BE(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }
    }
}
