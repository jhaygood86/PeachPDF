using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Derives a test font from a real sfnt font by adding a cmap <b>format 14</b> (Unicode Variation
    /// Sequences) subtable to its existing cmap - the bundled fonts carry none for U+FE0E/U+FE0F, and the
    /// glyph outlines, GSUB and everything else stay the source font's own. The rebuilt font is loaded by
    /// the same parser as any other, so a test through it exercises the real format-14 reader.
    /// </summary>
    internal static class SyntheticUvsFont
    {
        /// <summary>One variation sequence the derived font should declare.</summary>
        /// <param name="Base">the base character</param>
        /// <param name="Selector">the variation selector (U+FE0E or U+FE0F)</param>
        /// <param name="Glyph">the dedicated glyph id for a non-default UVS record, or null for a default one</param>
        internal readonly record struct Sequence(int Base, int Selector, int? Glyph = null);

        internal static byte[] Build(byte[] sourceFont, params Sequence[] sequences) => Build(sourceFont, sequences, false, false);

        /// <param name="sourceFont">the font to derive from</param>
        /// <param name="sequences">the sequences the format-14 subtable declares (none: no format-14 subtable is added)</param>
        /// <param name="corruptRecordCount">writes an absurd record count into the format-14 subtable</param>
        /// <param name="addDanglingRecord">appends a trailing cmap encoding record whose offset points past the end of the font</param>
        internal static byte[] Build(byte[] sourceFont, Sequence[] sequences, bool corruptRecordCount = false, bool addDanglingRecord = false)
        {
            var tables = ReadTables(sourceFont, out var sfntVersion);
            if (sequences.Length > 0)
            {
                var format14 = BuildFormat14(sequences);
                if (corruptRecordCount)
                {
                    // numRecords lives at bytes 6..9 of the subtable.
                    format14[6] = 0x7F; format14[7] = 0xFF; format14[8] = 0xFF; format14[9] = 0xFF;
                }
                tables["cmap"] = WithFormat14(tables["cmap"], format14);
            }

            if (addDanglingRecord)
                tables["cmap"] = WithDanglingRecord(tables["cmap"]);

            return WriteTables(tables, sfntVersion);
        }

        /// <summary>The cmap plus one more encoding record (platform 3, encoding 10) whose offset is far past the end of the font.</summary>
        private static byte[] WithDanglingRecord(byte[] cmap)
        {
            int oldCount = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(2));
            int oldHeader = 4 + 8 * oldCount;
            var result = new List<byte>(cmap.Length + 8);

            void U16(int v) { result.Add((byte)(v >> 8)); result.Add((byte)v); }
            void U32(int v) { U16(v >> 16); U16(v & 0xFFFF); }

            U16(0);
            U16(oldCount + 1);
            for (int i = 0; i < oldCount; i++)
            {
                var record = cmap.AsSpan(4 + 8 * i, 8);
                U16(BinaryPrimitives.ReadUInt16BigEndian(record));
                U16(BinaryPrimitives.ReadUInt16BigEndian(record[2..]));
                U32((int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]) + 8);
            }

            U16(3);
            U16(10);
            U32(0x7FFFFF00);
            result.AddRange(cmap.AsSpan(oldHeader).ToArray());
            return [.. result];
        }

        internal static SortedDictionary<string, byte[]> ReadTables(byte[] font, out uint sfntVersion)
        {
            sfntVersion = BinaryPrimitives.ReadUInt32BigEndian(font);
            int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

            for (int i = 0; i < count; i++)
            {
                var record = font.AsSpan(12 + 16 * i, 16);
                var tag = System.Text.Encoding.ASCII.GetString(record[..4]);
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                tables[tag] = font.AsSpan(offset, length).ToArray();
            }

            return tables;
        }

        private static byte[] WriteTables(SortedDictionary<string, byte[]> tables, uint sfntVersion)
        {
            using var stream = new MemoryStream();
            void U16(int v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); stream.Write(b); }
            void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); stream.Write(b); }

            int count = tables.Count;
            int entrySelector = (int)Math.Floor(Math.Log2(count));
            int searchRange = (1 << entrySelector) * 16;

            U32(sfntVersion);
            U16(count);
            U16(searchRange);
            U16(entrySelector);
            U16(count * 16 - searchRange);

            int offset = 12 + 16 * count;
            foreach (var (tag, data) in tables)
            {
                stream.Write(System.Text.Encoding.ASCII.GetBytes(tag));
                U32(0); // checksum: not verified by the reader
                U32((uint)offset);
                U32((uint)data.Length);
                offset += (data.Length + 3) & ~3;
            }

            foreach (var data in tables.Values)
            {
                stream.Write(data);
                for (int pad = data.Length; (pad & 3) != 0; pad++)
                    stream.WriteByte(0);
            }

            return stream.ToArray();
        }

        /// <summary>The original cmap plus one Unicode-platform (0, 5) record pointing at <paramref name="format14"/>.</summary>
        private static byte[] WithFormat14(byte[] cmap, byte[] format14)
        {
            int oldCount = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(2));
            int oldHeader = 4 + 8 * oldCount;
            var result = new List<byte>(cmap.Length + 8 + format14.Length);

            void U16(int v) { result.Add((byte)(v >> 8)); result.Add((byte)v); }
            void U32(int v) { U16(v >> 16); U16(v & 0xFFFF); }

            U16(0);
            U16(oldCount + 1);

            // The new record goes first (platform 0); every original subtable moves 8 bytes later.
            U16(0);
            U16(5);
            U32(oldHeader + 8 + (cmap.Length - oldHeader));

            for (int i = 0; i < oldCount; i++)
            {
                var record = cmap.AsSpan(4 + 8 * i, 8);
                U16(BinaryPrimitives.ReadUInt16BigEndian(record));
                U16(BinaryPrimitives.ReadUInt16BigEndian(record[2..]));
                U32((int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]) + 8);
            }

            result.AddRange(cmap.AsSpan(oldHeader).ToArray());
            result.AddRange(format14);
            return [.. result];
        }

        private static byte[] BuildFormat14(Sequence[] sequences)
        {
            var bySelector = sequences.GroupBy(s => s.Selector).OrderBy(g => g.Key).ToList();

            var body = new List<byte>();
            void U16(int v) { body.Add((byte)(v >> 8)); body.Add((byte)v); }
            void U24(int v) { body.Add((byte)(v >> 16)); body.Add((byte)(v >> 8)); body.Add((byte)v); }
            void U32(int v) { U16(v >> 16); U16(v & 0xFFFF); }

            // Layout: format(2) length(4) numRecords(4), records of 11 bytes, then each selector's tables.
            int headerLength = 10 + 11 * bySelector.Count;
            var tableBytes = new List<byte>();
            var directory = new List<(int Selector, int DefaultOffset, int NonDefaultOffset)>();

            foreach (var group in bySelector)
            {
                var defaults = group.Where(s => s.Glyph is null).OrderBy(s => s.Base).ToList();
                var nonDefaults = group.Where(s => s.Glyph is not null).OrderBy(s => s.Base).ToList();

                int defaultOffset = 0, nonDefaultOffset = 0;

                if (defaults.Count > 0)
                {
                    defaultOffset = headerLength + tableBytes.Count;
                    Append(tableBytes, 4, defaults.Count);
                    foreach (var s in defaults)
                    {
                        tableBytes.Add((byte)(s.Base >> 16)); tableBytes.Add((byte)(s.Base >> 8)); tableBytes.Add((byte)s.Base);
                        tableBytes.Add(0); // additionalCount
                    }
                }

                if (nonDefaults.Count > 0)
                {
                    nonDefaultOffset = headerLength + tableBytes.Count;
                    Append(tableBytes, 4, nonDefaults.Count);
                    foreach (var s in nonDefaults)
                    {
                        tableBytes.Add((byte)(s.Base >> 16)); tableBytes.Add((byte)(s.Base >> 8)); tableBytes.Add((byte)s.Base);
                        Append(tableBytes, 2, s.Glyph!.Value);
                    }
                }

                directory.Add((group.Key, defaultOffset, nonDefaultOffset));
            }

            U16(14);
            U32(headerLength + tableBytes.Count);
            U32(bySelector.Count);
            foreach (var (selector, defaultOffset, nonDefaultOffset) in directory)
            {
                U24(selector);
                U32(defaultOffset);
                U32(nonDefaultOffset);
            }

            body.AddRange(tableBytes);
            return [.. body];
        }

        private static void Append(List<byte> bytes, int width, int value)
        {
            for (int shift = (width - 1) * 8; shift >= 0; shift -= 8)
                bytes.Add((byte)(value >> shift));
        }
    }
}
