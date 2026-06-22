using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.IO;
using System.Text;

namespace PeachPDF.PdfSharpCore.Utils
{
    internal readonly struct TtfFontDescription
    {
        public string FontFamilyInvariantCulture { get; init; }
        public string FontNameInvariantCulture { get; init; }
        public XFontStyle Style { get; init; }

        public static TtfFontDescription LoadDescription(string path)
        {
            using var stream = File.OpenRead(path);
            return LoadDescription(stream);
        }

        public static TtfFontDescription LoadDescription(Stream stream)
        {
            // TTF/OTF files are big-endian. Read the offset table to locate the name table.
            Span<byte> buf4 = stackalloc byte[4];
            Span<byte> buf2 = stackalloc byte[2];

            stream.ReadExactly(buf4); // sfVersion — skip
            stream.ReadExactly(buf2);
            int numTables = ReadUInt16BE(buf2);
            stream.ReadExactly(buf2); // searchRange
            stream.ReadExactly(buf2); // entrySelector
            stream.ReadExactly(buf2); // rangeShift

            long nameTableOffset = -1;
            for (int i = 0; i < numTables; i++)
            {
                stream.ReadExactly(buf4);
                var tag = Encoding.ASCII.GetString(buf4);
                stream.ReadExactly(buf4); // checkSum
                stream.ReadExactly(buf4);
                uint tableOffset = ReadUInt32BE(buf4);
                stream.ReadExactly(buf4); // length

                if (tag == "name")
                    nameTableOffset = tableOffset;
            }

            if (nameTableOffset < 0)
                throw new InvalidOperationException("Font file does not contain a name table.");

            stream.Seek(nameTableOffset, SeekOrigin.Begin);
            stream.ReadExactly(buf2); // format
            stream.ReadExactly(buf2);
            int count = ReadUInt16BE(buf2);
            stream.ReadExactly(buf2);
            int stringOffset = ReadUInt16BE(buf2);
            long storageBase = nameTableOffset + stringOffset;

            // Read all name records (6 uint16 fields each)
            var platformIDs  = new ushort[count];
            var encodingIDs  = new ushort[count];
            var languageIDs  = new ushort[count];
            var nameIDs      = new ushort[count];
            var lengths      = new ushort[count];
            var offsets      = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                stream.ReadExactly(buf2); platformIDs[i] = ReadUInt16BE(buf2);
                stream.ReadExactly(buf2); encodingIDs[i] = ReadUInt16BE(buf2);
                stream.ReadExactly(buf2); languageIDs[i] = ReadUInt16BE(buf2);
                stream.ReadExactly(buf2); nameIDs[i]     = ReadUInt16BE(buf2);
                stream.ReadExactly(buf2); lengths[i]     = ReadUInt16BE(buf2);
                stream.ReadExactly(buf2); offsets[i]     = ReadUInt16BE(buf2);
            }

            string familyName    = ReadBestNameRecord(stream, platformIDs, encodingIDs, languageIDs, nameIDs, lengths, offsets, count, storageBase, 1);
            string subfamilyName = ReadBestNameRecord(stream, platformIDs, encodingIDs, languageIDs, nameIDs, lengths, offsets, count, storageBase, 2);
            string fullName      = ReadBestNameRecord(stream, platformIDs, encodingIDs, languageIDs, nameIDs, lengths, offsets, count, storageBase, 4);

            var style = subfamilyName?.ToLowerInvariant() switch
            {
                "bold italic" or "bold oblique" => XFontStyle.BoldItalic,
                "bold"                          => XFontStyle.Bold,
                "italic" or "oblique"           => XFontStyle.Italic,
                _                               => XFontStyle.Regular
            };

            return new TtfFontDescription
            {
                FontFamilyInvariantCulture = familyName ?? fullName ?? string.Empty,
                FontNameInvariantCulture   = fullName   ?? familyName ?? string.Empty,
                Style                      = style
            };
        }

        // Prefers platformID=3/encodingID=1 (Windows Unicode) with en-US, then any language,
        // then platformID=1 (Mac Roman), then whatever is available.
        private static string ReadBestNameRecord(
            Stream stream,
            ushort[] platformIDs, ushort[] encodingIDs, ushort[] languageIDs,
            ushort[] nameIDs, ushort[] lengths, ushort[] offsets,
            int count, long storageBase, ushort targetNameID)
        {
            int best = -1;
            int bestPriority = int.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (nameIDs[i] != targetNameID) continue;

                int priority;
                if (platformIDs[i] == 3 && encodingIDs[i] == 1 && languageIDs[i] == 0x0409)
                    priority = 0;
                else if (platformIDs[i] == 3 && encodingIDs[i] == 1)
                    priority = 1;
                else if (platformIDs[i] == 1)
                    priority = 2;
                else
                    priority = 3;

                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    best = i;
                }
            }

            if (best < 0) return null;

            stream.Seek(storageBase + offsets[best], SeekOrigin.Begin);
            var bytes = new byte[lengths[best]];
            stream.ReadExactly(bytes);

            return platformIDs[best] == 1
                ? Encoding.Latin1.GetString(bytes)
                : Encoding.BigEndianUnicode.GetString(bytes);
        }

        private static ushort ReadUInt16BE(ReadOnlySpan<byte> b) =>
            (ushort)((b[0] << 8) | b[1]);

        private static uint ReadUInt32BE(ReadOnlySpan<byte> b) =>
            ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}
