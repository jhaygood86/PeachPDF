#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PeachDrawing.Text.Internal.Fonts
{
    internal readonly struct TtfFontDescription
    {
        /// <summary>Default CSS Fonts numeric weight (400 = "normal") used when a font has no OS/2 table, or its <see cref="Weight"/> field is out of the valid 1-1000 range.</summary>
        public const int DefaultWeight = 400;

        /// <summary>Default CSS Fonts stretch value (5 = "normal" on the 1-9 <c>usWidthClass</c> scale) used when a font has no OS/2 table, or its value is out of the valid 1-9 range.</summary>
        public const int DefaultStretch = 5;

        public string FontFamilyInvariantCulture { get; init; }
        public string FontNameInvariantCulture { get; init; }
        public FaceStyle Style { get; init; }

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

        /// <summary>The weights the font's <c>wght</c> axis covers, or null for a font that has no such axis (or is not variable).</summary>
        public AxisRange? WeightRange { get; init; }

        /// <summary>The widths, as percentages of the normal width, the font's <c>wdth</c> axis covers, or null for a font that has no such axis.</summary>
        public AxisRange? WidthRange { get; init; }

        /// <summary>
        /// The oblique angles, in degrees leaning to the right, the font's <c>slnt</c> axis covers, or null for a font that has no such
        /// axis. The axis counts degrees counter-clockwise from vertical, so a lean to the right is negative there.
        /// </summary>
        public AxisRange? ObliqueRange { get; init; }

        public static TtfFontDescription LoadDescription(string path)
        {
            using var stream = File.OpenRead(path);
            return LoadDescription(stream);
        }

        /// <summary>
        /// The description of every face in the font file at <paramref name="path"/>, paired with its face
        /// index: one entry for an ordinary <c>.ttf</c>/<c>.otf</c>, one per face for a <c>.ttc</c>/<c>.otc</c>
        /// collection. A face of a collection that cannot be read is skipped (the rest of the collection is
        /// still usable); an ordinary font that cannot be read throws, as <see cref="LoadDescription(string)"/> does.
        /// </summary>
        public static IReadOnlyList<(int FaceIndex, TtfFontDescription Description)> LoadDescriptions(string path)
        {
            using var stream = File.OpenRead(path);

            var offsets = SfntCollection.ReadFaceOffsets(stream);
            if (offsets is null)
                return [(0, LoadDescription(stream, 0))];

            var faces = new List<(int, TtfFontDescription)>(offsets.Length);
            for (var i = 0; i < offsets.Length; i++)
            {
                try
                {
                    faces.Add((i, LoadDescription(stream, i)));
                }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
                {
                    Debug.WriteLine($"{path} face {i}: {e.Message}");
                }
            }

            return faces;
        }

        /// <summary>
        /// The description of face 0 - the only face of an ordinary font, and the first face of a collection.
        /// </summary>
        public static TtfFontDescription LoadDescription(Stream stream) => LoadDescription(stream, 0);

        /// <summary>
        /// The description of face <paramref name="faceIndex"/> of the font at the start of
        /// <paramref name="stream"/>: index 0 for an ordinary font, any face of a collection.
        /// </summary>
        public static TtfFontDescription LoadDescription(Stream stream, int faceIndex)
        {
            // TTF/OTF files are big-endian. Read the offset table to locate the name/OS2 tables. Table
            // offsets are absolute from the start of the file, in a collection as in a single font, so only
            // the directory's own position depends on the face.
            stream.Seek(SfntCollection.FaceOffset(stream, faceIndex), SeekOrigin.Begin);

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
            long fvarTableOffset = -1;
            long fvarTableLength = 0;
            for (int i = 0; i < numTables; i++)
            {
                stream.ReadExactly(buf4);
                var tag = Encoding.ASCII.GetString(buf4);
                stream.ReadExactly(buf4); // checkSum
                stream.ReadExactly(buf4);
                uint tableOffset = ReadUInt32BE(buf4);
                stream.ReadExactly(buf4);
                uint tableLength = ReadUInt32BE(buf4);

                if (tag == "name")
                    nameTableOffset = tableOffset;
                else if (tag == "OS/2")
                    os2TableOffset = tableOffset;
                else if (tag == "fvar")
                {
                    fvarTableOffset = tableOffset;
                    fvarTableLength = tableLength;
                }
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
                "bold italic" or "bold oblique" => FaceStyle.BoldItalic,
                "bold"                          => FaceStyle.Bold,
                "italic" or "oblique"           => FaceStyle.Italic,
                _                               => FaceStyle.Regular
            };

            var (weight, stretch) = ReadOs2WeightAndStretch(stream, os2TableOffset);
            if (weight == 0)
                weight = style is FaceStyle.Bold or FaceStyle.BoldItalic ? 700 : DefaultWeight;

            var (weightRange, widthRange, obliqueRange) = ReadAxisRanges(stream, fvarTableOffset, fvarTableLength);

            return new TtfFontDescription
            {
                FontFamilyInvariantCulture = familyName ?? fullName ?? string.Empty,
                FontNameInvariantCulture   = fullName   ?? familyName ?? string.Empty,
                Style                      = style,
                Weight                     = weight,
                Stretch                    = stretch,
                WeightRange                = weightRange,
                WidthRange                 = widthRange,
                ObliqueRange               = obliqueRange
            };
        }

        /// <summary>
        /// The ranges the <c>wght</c>, <c>wdth</c> and <c>slnt</c> axes of a variable font cover, read from its <c>fvar</c> table, so a
        /// font that is discovered rather than added can be matched for every weight, width and slant it can draw. Nothing for a font with
        /// no <c>fvar</c> table, and nothing for one whose table is malformed: the font stays usable at its default, as it was before.
        /// </summary>
        private static (AxisRange? Weight, AxisRange? Width, AxisRange? Oblique) ReadAxisRanges(Stream stream, long fvarTableOffset, long fvarTableLength)
        {
            const int HeaderSize = 16;
            const int AxisRecordSize = 20;
            const int MostAxes = 64;

            if (fvarTableOffset < 0 || fvarTableLength < HeaderSize)
                return default;

            try
            {
                Span<byte> header = stackalloc byte[HeaderSize];
                stream.Seek(fvarTableOffset, SeekOrigin.Begin);
                stream.ReadExactly(header);

                int axesOffset = ReadUInt16BE(header[4..]);
                int axisCount = ReadUInt16BE(header[8..]);
                int axisSize = ReadUInt16BE(header[10..]);
                if (axisCount is 0 or > MostAxes || axisSize < AxisRecordSize
                    || axesOffset + (long)axisCount * axisSize > fvarTableLength)
                {
                    return default;
                }

                AxisRange? weight = null;
                AxisRange? width = null;
                AxisRange? oblique = null;
                Span<byte> record = stackalloc byte[AxisRecordSize];
                for (int i = 0; i < axisCount; i++)
                {
                    stream.Seek(fvarTableOffset + axesOffset + (long)i * axisSize, SeekOrigin.Begin);
                    stream.ReadExactly(record);

                    // Minimum and maximum are 16.16 fixed-point numbers after the tag and before the default value.
                    var minimum = (int)ReadUInt32BE(record[4..]) / 65536.0;
                    var maximum = (int)ReadUInt32BE(record[12..]) / 65536.0;
                    switch (Encoding.ASCII.GetString(record[..4]))
                    {
                        case AxisTags.Weight:
                            weight = new AxisRange(minimum, maximum);
                            break;
                        case AxisTags.Width:
                            width = new AxisRange(minimum, maximum);
                            break;
                        case AxisTags.Slant:
                            oblique = new AxisRange(-maximum, -minimum);
                            break;
                    }
                }

                return (weight, width, oblique);
            }
            catch (Exception e) when (e is IOException or ArgumentException)
            {
                return default;
            }
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
