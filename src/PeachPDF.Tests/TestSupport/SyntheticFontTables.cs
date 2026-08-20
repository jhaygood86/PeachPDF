namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Shared helpers for splicing synthetic OpenType vertical-metrics tables (<c>vhea</c>/<c>vmtx</c>/
    /// <c>VORG</c>) directly into a real font file's own SFNT byte stream - needed by any test that embeds
    /// the resulting bytes through the real font-loading pipeline (<c>@font-face</c>/<c>AddFont</c>), as
    /// opposed to <c>VerticalMetricsTablesTests</c>'s own <c>BuildFaceWithSyntheticTable</c>, which only
    /// registers appended bytes in an already-parsed <see cref="PeachPDF.Fonts.OpenType.OpenTypeFontface"/>'s
    /// in-memory table dictionary and so never touches a real byte stream. Originally a private helper in
    /// <c>VerticalMetricsTablesTests.cs</c>; extracted here once a second file
    /// (<c>TextOrientationIntegrationTests.cs</c>) needed the identical technique.
    /// </summary>
    internal static class SyntheticFontTables
    {
        /// <summary>
        /// Splices one new table directory entry into a real, already-valid font file's own SFNT header.
        /// Table data is expected to start immediately after the last existing directory entry (true of
        /// every well-formed SFNT font, including all bundled fixtures) - inserting one more 16-byte entry
        /// there shifts every existing table's offset by 16, so each is rewritten accordingly. Safe to call
        /// repeatedly on its own output to splice in more than one table.
        /// </summary>
        public static byte[] InsertTableDirectoryEntry(byte[] fontBytes, string tag, byte[] tableBytes)
        {
            int numTables = (fontBytes[4] << 8) | fontBytes[5];
            int directoryEnd = 12 + numTables * 16;

            var result = new SfntByteBuilder();
            for (int i = 0; i < 4; i++) // sfnt version, unchanged
                result.Byte(fontBytes[i]);
            result.U16(numTables + 1);
            for (int i = 6; i < 12; i++) // searchRange/entrySelector/rangeShift, unchanged (unvalidated on read)
                result.Byte(fontBytes[i]);

            for (int i = 0; i < numTables; i++)
            {
                int entryStart = 12 + i * 16;
                for (int j = 0; j < 8; j++) // tag + checksum, unchanged
                    result.Byte(fontBytes[entryStart + j]);

                int offset = (fontBytes[entryStart + 8] << 24) | (fontBytes[entryStart + 9] << 16) |
                             (fontBytes[entryStart + 10] << 8) | fontBytes[entryStart + 11];
                result.U32((uint)(offset + 16));

                for (int j = 12; j < 16; j++) // length, unchanged
                    result.Byte(fontBytes[entryStart + j]);
            }

            foreach (char c in tag)
                result.Byte((byte)c);
            result.U32(0); // checksum - unvalidated on read, see TableDirectoryEntry.ReadFrom
            result.U32((uint)(fontBytes.Length + 16));
            result.U32((uint)tableBytes.Length);

            for (int i = directoryEnd; i < fontBytes.Length; i++)
                result.Byte(fontBytes[i]);

            foreach (byte b in tableBytes)
                result.Byte(b);

            return result.ToArray();
        }

        /// <summary>Builds a minimal <c>vhea</c> table's bytes (only <c>ascent</c>/<c>descent</c>/
        /// <c>numOfLongVerMetrics</c> vary; every other field is a spec-harmless zero).</summary>
        public static byte[] BuildVhea(short ascent, short descent, ushort numOfLongVerMetrics)
        {
            var b = new SfntByteBuilder();
            b.U32(0x00010000); // version
            b.S16(ascent);
            b.S16(descent);
            b.S16(0); // lineGap
            b.U16(0); // advanceHeightMax
            b.S16(0); // minTopSideBearing
            b.S16(0); // minBottomSideBearing
            b.S16(0); // yMaxExtent
            b.S16(0); // caretSlopeRise
            b.S16(0); // caretSlopeRun
            b.S16(0); // caretOffset
            b.S16(0); b.S16(0); b.S16(0); b.S16(0); // reserved1-4
            b.S16(0); // metricDataFormat
            b.U16(numOfLongVerMetrics);
            return b.ToArray();
        }

        /// <summary>Builds a <c>vmtx</c> table with one long metric record (advance height + top-side-
        /// bearing 0) *per glyph* in the font. <see cref="VerticalMetricsTable.Read"/> reads
        /// <c>maxp.numGlyphs - vhea.numOfLongVerMetrics</c> trailing top-side-bearing-only entries after
        /// the long metrics array - giving every glyph its own long metric here (paired with
        /// <see cref="BuildVhea"/>'s <c>numOfLongVerMetrics</c> set to the same <paramref name="numGlyphs"/>)
        /// means that trailing array is empty, so a caller embedding these bytes through the real
        /// font-loading pipeline (not <c>VerticalMetricsTablesTests.cs</c>'s own trick of temporarily
        /// patching <c>face.maxp.numGlyphs</c> around a direct constructor call) doesn't have to also
        /// fabricate a real per-glyph top-side-bearing tail.</summary>
        public static byte[] BuildVmtxUniform(ushort advanceHeight, int numGlyphs)
        {
            var b = new SfntByteBuilder();
            for (var i = 0; i < numGlyphs; i++)
            {
                b.U16(advanceHeight);
                b.S16(0); // topSideBearing
            }
            return b.ToArray();
        }

        /// <summary>Builds a <c>VORG</c> table with only a <c>defaultVertOriginY</c> (no per-glyph
        /// overrides) - every glyph in the font shares this one vertical origin.</summary>
        public static byte[] BuildVorg(short defaultVertOriginY)
        {
            var b = new SfntByteBuilder();
            b.U16(1); // majorVersion
            b.U16(0); // minorVersion
            b.S16(defaultVertOriginY);
            b.U16(0); // numVertOriginYMetrics
            return b.ToArray();
        }
    }
}
