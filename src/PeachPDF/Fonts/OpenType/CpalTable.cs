#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `CPAL` (Color Palette) table. Backs COLR color-glyph
// rendering: a COLR layer/stop references a palette entry index, which this
// table resolves to an RGBA color. Records are stored on disk as BGRA; the
// whole record array is swizzled to RGBA once at load time using SIMD where
// available.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/cpal
//
#endregion

using System.Runtime.Intrinsics;

namespace PeachPDF.Fonts.OpenType
{
    internal sealed class CpalTable
    {
        // Palette-type flag bits (CPAL v1, per-palette). See the spec link above.
        private const uint UsableWithLightBackground = 0x0001;
        private const uint UsableWithDarkBackground = 0x0002;

        // Color records, already swizzled from on-disk BGRA to RGBA (4 bytes per entry).
        private readonly byte[] _rgba;
        private readonly ushort[] _firstColorRecordIndex; // per palette
        private readonly int _numColorRecords;
        private readonly uint[] _paletteTypes;            // per palette (CPAL v1); empty for v0

        public int PaletteCount => _firstColorRecordIndex.Length;
        public int EntriesPerPalette { get; }

        public CpalTable(OpenTypeFontface face)
        {
            int tableStart = face.TableDictionary[TableTagNames.Cpal].Offset;
            face.Position = tableStart;

            int version = face.ReadUShort();
            EntriesPerPalette = face.ReadUShort();      // numPaletteEntries
            int numPalettes = face.ReadUShort();
            _numColorRecords = face.ReadUShort();
            uint colorRecordsArrayOffset = face.ReadULong();

            _firstColorRecordIndex = new ushort[numPalettes];
            for (int i = 0; i < numPalettes; i++)
                _firstColorRecordIndex[i] = face.ReadUShort();

            _paletteTypes = [];
            if (version >= 1)
            {
                // CPAL v1 header extension: three array offsets follow the v0 fields. Only the palette-type
                // flags (light/dark suitability) are consumed; the label arrays are ignored.
                uint paletteTypesArrayOffset = face.ReadULong();
                face.ReadULong();                       // paletteLabelsArrayOffset (unused)
                face.ReadULong();                       // paletteEntryLabelsArrayOffset (unused)

                if (paletteTypesArrayOffset != 0 && numPalettes > 0)
                {
                    face.Position = tableStart + (int)paletteTypesArrayOffset;
                    _paletteTypes = new uint[numPalettes];
                    for (int i = 0; i < numPalettes; i++)
                        _paletteTypes[i] = face.ReadULong();
                }
            }

            _rgba = new byte[_numColorRecords * 4];
            if (_numColorRecords > 0 && colorRecordsArrayOffset != 0)
            {
                face.Position = tableStart + (int)colorRecordsArrayOffset;
                for (int i = 0; i < _rgba.Length; i++)
                    _rgba[i] = face.ReadByte();
                SwizzleBgraToRgba(_rgba);
            }
        }

        /// <summary>
        /// The index of the first palette flagged <c>USABLE_WITH_LIGHT_BACKGROUND</c> (CPAL v1), or null when no
        /// palette carries the flag (a v0 font, or none flagged). Backs <c>font-palette: light</c>.
        /// </summary>
        public int? FirstLightPalette() => FirstPaletteWithFlag(UsableWithLightBackground);

        /// <summary>
        /// The index of the first palette flagged <c>USABLE_WITH_DARK_BACKGROUND</c> (CPAL v1), or null when no
        /// palette carries the flag. Backs <c>font-palette: dark</c>.
        /// </summary>
        public int? FirstDarkPalette() => FirstPaletteWithFlag(UsableWithDarkBackground);

        private int? FirstPaletteWithFlag(uint flag)
        {
            for (int i = 0; i < _paletteTypes.Length; i++)
                if ((_paletteTypes[i] & flag) != 0)
                    return i;
            return null;
        }

        /// <summary>
        /// Resolves a palette entry to its RGBA color. <paramref name="paletteIndex"/> selects the
        /// palette (defaults to 0 when out of range).
        /// </summary>
        public bool TryGetColor(int paletteIndex, int entryIndex, out (byte R, byte G, byte B, byte A) color)
        {
            color = default;
            if (entryIndex < 0 || entryIndex >= EntriesPerPalette)
                return false;
            if (paletteIndex < 0 || paletteIndex >= _firstColorRecordIndex.Length)
                paletteIndex = 0;
            if (_firstColorRecordIndex.Length == 0)
                return false;

            int record = _firstColorRecordIndex[paletteIndex] + entryIndex;
            if (record < 0 || record >= _numColorRecords)
                return false;

            int b = record * 4;
            color = (_rgba[b], _rgba[b + 1], _rgba[b + 2], _rgba[b + 3]);
            return true;
        }

        /// <summary>
        /// In-place BGRA-&gt;RGBA byte swizzle. Uses a portable <see cref="Vector128"/> shuffle (JITs
        /// to SSSE3 <c>pshufb</c> / ARM <c>tbl</c>) for the bulk of the array, with a scalar tail.
        /// </summary>
        private static void SwizzleBgraToRgba(byte[] data)
        {
            int n = data.Length; // always a multiple of 4
            int i = 0;

            if (Vector128.IsHardwareAccelerated && n >= Vector128<byte>.Count)
            {
                // Per 4-byte pixel: swap byte 0 (B) and byte 2 (R), keep G and A.
                Vector128<byte> mask = Vector128.Create(
                    (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
                int limit = n - (n % Vector128<byte>.Count);
                for (; i < limit; i += Vector128<byte>.Count)
                {
                    Vector128<byte> v = Vector128.LoadUnsafe(ref data[i]);
                    Vector128.Shuffle(v, mask).StoreUnsafe(ref data[i]);
                }
            }

            for (; i < n; i += 4)
                (data[i], data[i + 2]) = (data[i + 2], data[i]);
        }
    }
}
