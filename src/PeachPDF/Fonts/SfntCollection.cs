using System;
using System.Buffers.Binary;
using System.IO;

namespace PeachPDF.Fonts
{
    /// <summary>
    /// Reads OpenType/TrueType <b>font collections</b> (<c>.ttc</c>/<c>.otc</c>, tag <c>ttcf</c>): one file
    /// holding several fonts ("faces") that usually share tables. Every table offset inside a collection is
    /// absolute from the start of the file, and each face has its own table directory, so a collection is
    /// not itself a font the rest of the pipeline can read.
    /// </summary>
    /// <remarks>
    /// The pipeline (<c>FontFileData</c>, the OpenType table readers, subsetting, embedding) works on the
    /// bytes of one standalone sfnt font. Rather than teach every one of those about collection offsets,
    /// <see cref="ExtractFace(Stream, int)"/> rebuilds one face as an ordinary standalone font - same tables, new
    /// directory - so a face of a collection is, from then on, indistinguishable from a <c>.ttf</c>.
    /// </remarks>
    internal static class SfntCollection
    {
        private const uint CollectionTag = 0x74746366; // 'ttcf'

        /// <summary>Far above any real collection (CJK collections hold on the order of ten faces); bounds an allocation sized from the file.</summary>
        private const int MaxFaces = 1024;

        private const int MaxTablesPerFace = 4096;

        /// <summary>Whether <paramref name="bytes"/> begins with the collection tag.</summary>
        internal static bool IsCollection(ReadOnlySpan<byte> bytes) =>
            bytes.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(bytes) == CollectionTag;

        /// <summary>
        /// The absolute file offset of each face's table directory, or null when the stream is an ordinary
        /// single font. Leaves the stream position unspecified.
        /// </summary>
        internal static uint[]? ReadFaceOffsets(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);

            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header[..4]);
            if (!IsCollection(header))
                return null;

            stream.ReadExactly(header[4..]); // version, numFonts
            uint numFonts = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (numFonts is 0 or > MaxFaces || 12 + (long)numFonts * 4 > stream.Length)
                throw new InvalidDataException("Font collection has an invalid face count.");

            var offsets = new uint[numFonts];
            Span<byte> value = stackalloc byte[4];
            for (var i = 0; i < offsets.Length; i++)
            {
                stream.ReadExactly(value);
                offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(value);
            }

            return offsets;
        }

        /// <summary>
        /// How many faces the font file holds: the collection's face count, or 1 for an ordinary font.
        /// </summary>
        internal static int FaceCount(Stream stream) => ReadFaceOffsets(stream)?.Length ?? 1;

        /// <summary>
        /// The absolute file offset of face <paramref name="faceIndex"/>'s table directory: the collection's
        /// own offset for it, or 0 for the one face of an ordinary font.
        /// </summary>
        internal static long FaceOffset(Stream stream, int faceIndex)
        {
            var offsets = ReadFaceOffsets(stream);
            if (offsets is null)
            {
                return faceIndex == 0
                    ? 0
                    : throw new ArgumentOutOfRangeException(nameof(faceIndex), "An ordinary font file has only face 0.");
            }

            return (uint)faceIndex < (uint)offsets.Length
                ? offsets[faceIndex]
                : throw new ArgumentOutOfRangeException(nameof(faceIndex), "Font collection has no such face.");
        }

        /// <summary>
        /// Rebuilds face <paramref name="faceIndex"/> as a standalone sfnt font: its tables copied, in the
        /// face's own directory order, behind a fresh directory. Only that face's tables are read, so a
        /// collection of many large faces costs one face's worth of bytes.
        /// </summary>
        /// <exception cref="InvalidDataException">the directory or a table lies outside the file</exception>
        internal static byte[] ExtractFace(Stream stream, int faceIndex)
        {
            var faceOffset = FaceOffset(stream, faceIndex);

            stream.Seek(faceOffset, SeekOrigin.Begin);
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);

            var sfntVersion = BinaryPrimitives.ReadUInt32BigEndian(header);
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);
            if (numTables is 0 or > MaxTablesPerFace)
                throw new InvalidDataException("Font face has an invalid table count.");

            var records = new (uint Tag, uint Checksum, long Offset, int Length)[numTables];
            Span<byte> record = stackalloc byte[16];
            var directoryEnd = 12 + 16 * numTables;
            long dataSize = directoryEnd;

            for (var i = 0; i < numTables; i++)
            {
                stream.ReadExactly(record);
                var offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                var length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                if ((long)offset + length > stream.Length || length > int.MaxValue)
                    throw new InvalidDataException("Font table lies outside the file.");

                records[i] = (BinaryPrimitives.ReadUInt32BigEndian(record), BinaryPrimitives.ReadUInt32BigEndian(record[4..]), offset, (int)length);
                dataSize += (length + 3) & ~3u;
            }

            if (dataSize > int.MaxValue)
                throw new InvalidDataException("Font face is too large.");

            var result = new byte[dataSize];
            var entrySelector = (int)Math.Floor(Math.Log2(numTables));
            var searchRange = (1 << entrySelector) * 16;

            BinaryPrimitives.WriteUInt32BigEndian(result, sfntVersion);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), (ushort)numTables);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6), (ushort)searchRange);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(8), (ushort)entrySelector);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10), (ushort)(numTables * 16 - searchRange));

            var next = directoryEnd;
            for (var i = 0; i < numTables; i++)
            {
                var entry = result.AsSpan(12 + 16 * i, 16);
                BinaryPrimitives.WriteUInt32BigEndian(entry, records[i].Tag);
                BinaryPrimitives.WriteUInt32BigEndian(entry[4..], records[i].Checksum);
                BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)next);
                BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)records[i].Length);

                stream.Seek(records[i].Offset, SeekOrigin.Begin);
                stream.ReadExactly(result.AsSpan(next, records[i].Length));

                next += (records[i].Length + 3) & ~3;
            }

            return result;
        }

        /// <summary>
        /// <see cref="ExtractFace(Stream, int)"/> over bytes already in memory.
        /// </summary>
        internal static byte[] ExtractFace(byte[] collection, int faceIndex)
        {
            using var stream = new MemoryStream(collection, writable: false);
            return ExtractFace(stream, faceIndex);
        }
    }
}
