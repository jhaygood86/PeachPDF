using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.Apple
{
    /// <summary>
    /// Builds a standalone sfnt font file from a set of tables. CoreText hands a font out table by table
    /// (<c>CTFontCopyTable</c>) rather than as a file, so a font that has no readable file behind it (every
    /// system font on iOS) is put back together here into the shape the rest of the pipeline reads.
    /// </summary>
    internal static class SfntAssembler
    {
        private const uint TrueTypeVersion = 0x00010000;
        private const uint CffVersion = 0x4F54544F; // 'OTTO'
        private const uint CffTag = 0x43464620;     // 'CFF '
        private const uint Cff2Tag = 0x43464632;    // 'CFF2'

        /// <summary>Packs a four-character table tag into the big-endian number an sfnt directory stores.</summary>
        internal static uint Tag(string tag)
        {
            if (tag.Length != 4)
                throw new ArgumentException("A table tag is four characters.", nameof(tag));

            return ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
        }

        /// <summary>
        /// The font file made of <paramref name="tables"/>. Tables are written in tag order with each starting on
        /// a four-byte boundary, as the format requires; an empty table is left out; a repeated tag keeps its first
        /// occurrence. A font with a <c>CFF </c> or <c>CFF2</c> table is marked as OpenType with CFF outlines.
        /// </summary>
        internal static byte[] Build(IReadOnlyList<(uint Tag, byte[] Data)> tables)
        {
            var sorted = new SortedDictionary<uint, byte[]>();
            foreach (var (tag, data) in tables)
            {
                if (data.Length > 0)
                    sorted.TryAdd(tag, data);
            }

            var count = sorted.Count;
            var directorySize = 12 + 16 * count;

            var total = directorySize;
            foreach (var data in sorted.Values)
                total += Align4(data.Length);

            var file = new byte[total];
            var span = file.AsSpan();

            var version = sorted.ContainsKey(CffTag) || sorted.ContainsKey(Cff2Tag) ? CffVersion : TrueTypeVersion;
            BinaryPrimitives.WriteUInt32BigEndian(span, version);
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)count);

            // searchRange, entrySelector and rangeShift as the sfnt format defines them.
            var entrySelector = 0;
            while (count > 0 && (1 << (entrySelector + 1)) <= count)
                entrySelector++;
            var searchRange = count == 0 ? 0 : (1 << entrySelector) * 16;
            BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)searchRange);
            BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)entrySelector);
            BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)(count * 16 - searchRange));

            var record = 12;
            var offset = directorySize;
            foreach (var (tag, data) in sorted)
            {
                BinaryPrimitives.WriteUInt32BigEndian(span[record..], tag);
                BinaryPrimitives.WriteUInt32BigEndian(span[(record + 4)..], Checksum(data));
                BinaryPrimitives.WriteUInt32BigEndian(span[(record + 8)..], (uint)offset);
                BinaryPrimitives.WriteUInt32BigEndian(span[(record + 12)..], (uint)data.Length);

                data.CopyTo(span[offset..]);
                record += 16;
                offset += Align4(data.Length);
            }

            return file;
        }

        private static int Align4(int length) => (length + 3) & ~3;

        /// <summary>The sfnt table checksum: the sum of the data as big-endian 32-bit words, the last one padded with zeros.</summary>
        internal static uint Checksum(ReadOnlySpan<byte> data)
        {
            uint sum = 0;
            var i = 0;
            for (; i + 4 <= data.Length; i += 4)
                sum += BinaryPrimitives.ReadUInt32BigEndian(data[i..]);

            if (i < data.Length)
            {
                Span<byte> last = stackalloc byte[4];
                data[i..].CopyTo(last);
                sum += BinaryPrimitives.ReadUInt32BigEndian(last);
            }

            return sum;
        }
    }
}
