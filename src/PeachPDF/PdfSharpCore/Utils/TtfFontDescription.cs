#nullable disable warnings

using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.IO;
using System.Text;

namespace PeachPDF.PdfSharpCore.Utils
{
    internal readonly struct TtfFontDescription
    {
        /// <summary>Default CSS Fonts numeric weight (400 = "normal") used when a font has no OS/2 table, or its <see cref="Weight"/> field is out of the valid 1-1000 range.</summary>
        public const int DefaultWeight = 400;

        /// <summary>Default CSS Fonts stretch value (5 = "normal" on the 1-9 <c>usWidthClass</c> scale) used when a font has no OS/2 table, or its value is out of the valid 1-9 range.</summary>
        public const int DefaultStretch = 5;

        public string FontFamilyInvariantCulture { get; init; }
        public string FontNameInvariantCulture { get; init; }
        public XFontStyle Style { get; init; }

        /// <summary>
        /// CSS Fonts Level 4 numeric weight (1-1000), read from the OS/2 table's <c>usWeightClass</c>
        /// field when present and valid; falls back to a value derived from <see cref="Style"/>'s
        /// name-table-subfamily-sniffed Bold bit (700 if bold, else <see cref="DefaultWeight"/>) when
        /// OS/2 is absent or its <c>usWeightClass</c> is 0 (a real font can legitimately omit/zero this
        /// field even though the spec range is 1-1000). Used by <see cref="FontResolver"/>'s nearest-
        /// weight matching (CSS Fonts Level 4 §5.2) instead of the coarser 4-slot <see cref="Style"/>
        /// bucket alone.
        /// </summary>
        public int Weight { get; init; }

        /// <summary>
        /// CSS Fonts Level 3 <c>font-stretch</c> classification (1-9, matching the OS/2 <c>usWidthClass</c>
        /// scale directly: 1=ultra-condensed ... 5=normal ... 9=ultra-expanded), read from the OS/2 table
        /// when present and valid; <see cref="DefaultStretch"/> (normal) otherwise.
        /// </summary>
        public int Stretch { get; init; }

        public static TtfFontDescription LoadDescription(string path)
        {
            using var stream = File.OpenRead(path);
            return LoadDescription(stream);
        }

        public static TtfFontDescription LoadDescription(Stream stream)
        {
            // TTF/OTF files are big-endian. Read the offset table to locate the name/OS2 tables.
            Span<byte> buf4 = stackalloc byte[4];
            Span<byte> buf2 = stackalloc byte[2];

            stream.ReadExactly(buf4); // sfVersion — skip
            stream.ReadExactly(buf2);
            int numTables = ReadUInt16BE(buf2);
            stream.ReadExactly(buf2); // searchRange
            stream.ReadExactly(buf2); // entrySelector
            stream.ReadExactly(buf2); // rangeShift

            long nameTableOffset = -1;
            long os2TableOffset = -1;
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
                else if (tag == "OS/2")
                    os2TableOffset = tableOffset;
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

            var (weight, stretch) = ReadOs2WeightAndStretch(stream, os2TableOffset);
            if (weight == 0)
                weight = style is XFontStyle.Bold or XFontStyle.BoldItalic ? 700 : DefaultWeight;

            return new TtfFontDescription
            {
                FontFamilyInvariantCulture = familyName ?? fullName ?? string.Empty,
                FontNameInvariantCulture   = fullName   ?? familyName ?? string.Empty,
                Style                      = style,
                Weight                     = weight,
                Stretch                    = stretch
            };
        }

        /// <summary>
        /// Reads <c>usWeightClass</c> (offset 4) and <c>usWidthClass</c> (offset 6) from the OS/2 table,
        /// per the OpenType spec's OS/2 table layout (both fields are present in every OS/2 table
        /// version, including the oldest version 0). Returns (0, <see cref="DefaultStretch"/>) - a
        /// sentinel the caller substitutes a Style-derived default for - when there's no OS/2 table at
        /// all, or a value is outside its spec-valid range (weight: 1-1000, stretch: 1-9).
        /// </summary>
        private static (int Weight, int Stretch) ReadOs2WeightAndStretch(Stream stream, long os2TableOffset)
        {
            if (os2TableOffset < 0) return (0, DefaultStretch);

            Span<byte> buf2 = stackalloc byte[2];
            stream.Seek(os2TableOffset + 4, SeekOrigin.Begin);
            stream.ReadExactly(buf2);
            var weightClass = ReadUInt16BE(buf2);
            stream.ReadExactly(buf2);
            var widthClass = ReadUInt16BE(buf2);

            var weight = weightClass is >= 1 and <= 1000 ? weightClass : 0;
            var stretch = widthClass is >= 1 and <= 9 ? widthClass : DefaultStretch;
            return (weight, stretch);
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
