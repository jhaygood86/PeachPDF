using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The variation table readers on tables written by hand (so every layout the specification allows is exercised, not only the ones
    /// the fixture font happens to use), and on the fixture font damaged in the ways a font in the wild can be.
    /// </summary>
    public class VariationTablesTests
    {
        private sealed class Writer
        {
            private readonly List<byte> _bytes = [];

            public int Position => _bytes.Count;
            public Writer U8(int v) { _bytes.Add((byte)v); return this; }
            public Writer U16(int v) { _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)v); return this; }
            public Writer U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); return this; }
            public Writer F2Dot14(double v) => U16((short)Math.Round(v * 16384));
            public byte[] ToArray() => _bytes.ToArray();
        }

        /// <summary>One axis; regions 0 = (0, 1, 1) and 1 = (-1, -1, 0); two data sets, the second with long words.</summary>
        private static byte[] BuildStore()
        {
            // header: format u16, regionListOffset u32, dataCount u16, then dataCount offsets
            var header = 2 + 4 + 2 + 2 * 4;
            var regionList = new Writer()
                .U16(1).U16(2)                                  // axisCount, regionCount
                .F2Dot14(0).F2Dot14(1).F2Dot14(1)               // region 0
                .F2Dot14(-1).F2Dot14(-1).F2Dot14(0)             // region 1
                .ToArray();

            var data0 = new Writer()
                .U16(2).U16(1).U16(2)                           // itemCount, wordDeltaCount, regionIndexCount
                .U16(0).U16(1)                                  // region indexes
                .U16(100).U8(unchecked((byte)-10))              // item 0: word 100, byte -10
                .U16(unchecked((ushort)-50)).U8(20)             // item 1
                .ToArray();

            var data1 = new Writer()
                .U16(1).U16(0x8000 | 1).U16(2)                  // long words: one 32-bit delta, the rest 16-bit
                .U16(0).U16(1)
                .U32(70000).U16(unchecked((ushort)-300))
                .ToArray();

            var w = new Writer();
            w.U16(1).U32((uint)header).U16(2);
            w.U32((uint)(header + regionList.Length)).U32((uint)(header + regionList.Length + data0.Length));
            var all = w.ToArray().Concat(regionList).Concat(data0).Concat(data1).ToArray();
            return all;
        }

        [Fact]
        public void ItemVariationStore_SumsTheRegionsThatReachTheLocation()
        {
            var store = ItemVariationStore.TryParse(BuildStore(), 0);
            Assert.NotNull(store);

            // At +0.5 only region 0 reaches (half way to its peak at 1): item 0 is 100 * 0.5.
            Assert.Equal(50, store.GetDelta(0, 0, [0.5]));
            // At the peak of region 0 and at -1, the peak of region 1.
            Assert.Equal(100, store.GetDelta(0, 0, [1.0]));
            Assert.Equal(-10, store.GetDelta(0, 0, [-1.0]));
            Assert.Equal(-50, store.GetDelta(0, 1, [1.0]));
            Assert.Equal(20, store.GetDelta(0, 1, [-1.0]));
            // At the default nothing reaches.
            Assert.Equal(0, store.GetDelta(0, 0, [0.0]));
            // Long words.
            Assert.Equal(70000, store.GetDelta(1, 0, [1.0]));
            Assert.Equal(-300, store.GetDelta(1, 0, [-1.0]));
            // A pair that is not in the store, or a location that is not on any axis of it.
            Assert.Equal(0, store.GetDelta(5, 0, [1.0]));
            Assert.Equal(0, store.GetDelta(0, 9, [1.0]));
            // An axis the location does not mention is at its default, where nothing reaches.
            Assert.Equal(0, store.GetDelta(0, 0, []));
        }

        [Fact]
        public void ItemVariationStore_RejectsWhatIsNotOne()
        {
            var bytes = BuildStore();

            var wrongFormat = (byte[])bytes.Clone();
            wrongFormat[1] = 2;
            Assert.Null(ItemVariationStore.TryParse(wrongFormat, 0));

            Assert.Null(ItemVariationStore.TryParse(bytes.AsSpan(0, 20), 0));
            Assert.Null(ItemVariationStore.TryParse(bytes, 1000));
        }

        [Fact]
        public void ItemVariationStore_ReadsADataSetOffsetOfZeroAsEmpty()
        {
            var bytes = BuildStore();
            // The first data set's offset (at byte 8) set to 0.
            bytes[8] = bytes[9] = bytes[10] = bytes[11] = 0;

            var store = ItemVariationStore.TryParse(bytes, 0);

            Assert.NotNull(store);
            Assert.Equal(0, store.GetDelta(0, 0, [1.0]));
            Assert.Equal(70000, store.GetDelta(1, 0, [1.0]));
        }

        [Fact]
        public void DeltaSetIndexMap_Format0_ReadsPackedEntries()
        {
            // entryFormat 0x11: two-byte entries, four bits of inner index... (entryFormat & 0xF) + 1 = 2 inner bits, ((>> 4) & 3) + 1 = 2 bytes.
            var bytes = new Writer().U8(0).U8(0x11).U16(3).U16((1 << 2) | 3).U16((2 << 2) | 1).U16(0).ToArray();

            var map = DeltaSetIndexMap.TryParse(bytes, 0);

            Assert.NotNull(map);
            Assert.Equal((1, 3), map.Map(0));
            Assert.Equal((2, 1), map.Map(1));
            Assert.Equal((0, 0), map.Map(2));
            // An index past the end uses the last entry.
            Assert.Equal((0, 0), map.Map(50));
        }

        [Fact]
        public void DeltaSetIndexMap_Format1_HasA32BitCount_AndOneByteEntries()
        {
            var bytes = new Writer().U8(1).U8(0x03).U32(2).U8((5 << 4) | 9).U8((6 << 4) | 2).ToArray();

            var map = DeltaSetIndexMap.TryParse(bytes, 0);

            Assert.NotNull(map);
            Assert.Equal((5, 9), map.Map(0));
            Assert.Equal((6, 2), map.Map(1));
        }

        [Fact]
        public void DeltaSetIndexMap_RejectsAnUnknownFormat_AndATruncatedMap()
        {
            Assert.Null(DeltaSetIndexMap.TryParse(new Writer().U8(2).U8(0).U16(1).ToArray(), 0));
            Assert.Null(DeltaSetIndexMap.TryParse(new Writer().U8(0).U8(0x11).U16(5).U16(1).ToArray(), 0));
        }

        [Fact]
        public void DeltaSetIndexMap_WithNoEntries_MapsAnIndexToItself()
        {
            var map = DeltaSetIndexMap.TryParse(new Writer().U8(0).U8(0x00).U16(0).ToArray(), 0);

            Assert.NotNull(map);
            Assert.Equal((0, 7), map.Map(7));
        }

        [Fact]
        public void Hvar_UsesItsAdvanceMap_OrTheGlyphIndexWithoutOne()
        {
            var store = BuildStore();
            // header: major, minor, storeOffset, advanceMapOffset, lsbMap, rsbMap = 20 bytes
            var map = new Writer().U8(0).U8(0x00).U16(2).U8(1).U8(0).ToArray();     // one-byte entries with one inner bit: 0 -> (0, 1), 1 -> (0, 0)
            var withMap = new Writer().U16(1).U16(0).U32(20).U32((uint)(20 + store.Length)).U32(0).U32(0).ToArray()
                .Concat(store).Concat(map).ToArray();
            var withoutMap = new Writer().U16(1).U16(0).U32(20).U32(0).U32(0).U32(0).ToArray().Concat(store).ToArray();

            var mapped = HvarTable.TryParse(withMap);
            var plain = HvarTable.TryParse(withoutMap);

            Assert.NotNull(mapped);
            Assert.NotNull(plain);
            // Without a map glyph 0 and 1 are items 0 and 1 of the first data set.
            Assert.Equal(100, plain.GetAdvanceDelta(0, [1.0]));
            Assert.Equal(-50, plain.GetAdvanceDelta(1, [1.0]));
            // The map here sends glyph 0 to item 1 and glyph 1 to item 0.
            Assert.Equal(-50, mapped.GetAdvanceDelta(0, [1.0]));
            Assert.Equal(100, mapped.GetAdvanceDelta(1, [1.0]));
            // A glyph past the end of the map uses its last entry.
            Assert.Equal(100, mapped.GetAdvanceDelta(30, [1.0]));
        }

        [Fact]
        public void Hvar_RejectsATableThatIsTooShortOrTheWrongVersion()
        {
            Assert.Null(HvarTable.TryParse(new byte[10]));
            Assert.Null(HvarTable.TryParse(new Writer().U16(2).U16(0).U32(20).U32(0).U32(0).U32(0).ToArray()));
            Assert.Null(HvarTable.TryParse(new Writer().U16(1).U16(0).U32(900).U32(0).U32(0).U32(0).ToArray()));
        }

        [Fact]
        public void Mvar_FindsAMetricByItsTag()
        {
            var store = BuildStore();
            var table = new Writer().U16(1).U16(0).U16(0).U16(8).U16(2).U16(12 + 16).ToArray();
            foreach (var (tag, outer, inner) in new[] { ("hasc", 0, 0), ("xhgt", 0, 1) })
            {
                var record = new Writer();
                foreach (char c in tag) record.U8(c);
                table = table.Concat(record.U16(outer).U16(inner).ToArray()).ToArray();
            }

            table = table.Concat(store).ToArray();

            var mvar = MvarTable.TryParse(table);

            Assert.NotNull(mvar);
            Assert.Equal(100, mvar.GetDelta("hasc", [1.0]));
            Assert.Equal(-50, mvar.GetDelta("xhgt", [1.0]));
            Assert.Equal(0, mvar.GetDelta("cpht", [1.0]));
        }

        [Fact]
        public void Mvar_RejectsATableThatIsTooShortEmptyOrTheWrongVersion()
        {
            Assert.Null(MvarTable.TryParse(new byte[6]));
            Assert.Null(MvarTable.TryParse(new Writer().U16(2).U16(0).U16(0).U16(8).U16(1).U16(20).ToArray()));
            Assert.Null(MvarTable.TryParse(new Writer().U16(1).U16(0).U16(0).U16(8).U16(0).U16(20).ToArray()));
            Assert.Null(MvarTable.TryParse(new Writer().U16(1).U16(0).U16(0).U16(8).U16(1).U16(900).ToArray()));
        }

        // ---- the fixture font, damaged --------------------------------------------------------------------------------------------

        private static byte[] FixtureBytes() => File.ReadAllBytes(BundledFonts.VariableTest);

        /// <summary>The offset of a table's directory record in a font file.</summary>
        private static int RecordOf(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
                {
                    return record;
                }
            }

            throw new InvalidOperationException(tag);
        }

        private static Typeface LoadBytes(byte[] font)
        {
            var set = new FontSet();
            var family = set.AddData(font, new AddOptions { FamilyName = "Damaged-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        [Fact]
        public void ANamedVariation_AndAnAxisName_ComeFromTheNameTable()
        {
            var face = LoadBytes(FixtureBytes());

            Assert.Equal(["Weight", "Width"], face.Axes.Select(a => a.Name));
            Assert.Equal(["Light", "Bold", "Bold Condensed"], face.NamedVariations.Select(v => v.Name));
            Assert.Equal([new AxisSetting("wght", 700), new AxisSetting("wdth", 75)], face.NamedVariations[2].Settings);

            // A named variation is a location: the typeface at it reads the same as the one WithAxes builds.
            Assert.Same(face.WithAxes(face.NamedVariations[1].Settings), face.WithAxes([new AxisSetting("wght", 700)]));
        }

        [Fact]
        public void AnAxisTheFontFlagsHidden_IsReportedHidden()
        {
            var font = FixtureBytes();
            int record = RecordOf(font, "fvar");
            int fvar = (int)(((uint)font[record + 8] << 24) | ((uint)font[record + 9] << 16) | ((uint)font[record + 10] << 8) | font[record + 11]);
            int axesAt = fvar + ((font[fvar + 4] << 8) | font[fvar + 5]);
            font[axesAt + 20 + 16 + 1] = 1;     // the second axis's flags: axes are 20 bytes each, and the flags are at 16

            var face = LoadBytes(font);

            Assert.False(face.Axes[0].IsHidden);
            Assert.True(face.Axes[1].IsHidden);
        }

        [Fact]
        public void AFontWithoutFvar_IsNotVariable()
        {
            var font = FixtureBytes();
            font[RecordOf(font, "fvar") + 3] = (byte)'X';

            Assert.False(LoadBytes(font).IsVariable);
        }

        [Theory]
        [InlineData("gvar")]
        [InlineData("HVAR")]
        [InlineData("MVAR")]
        [InlineData("avar")]
        public void ATruncatedVariationTable_IsIgnored_NotAnError(string tag)
        {
            var font = FixtureBytes();
            int record = RecordOf(font, tag);
            font[record + 12] = font[record + 13] = font[record + 14] = 0;
            font[record + 15] = 6;      // the table's length: too short to hold anything

            var face = LoadBytes(font);
            var bold = face.WithAxes([new AxisSetting("wght", 900)]);
            face.TryMapRune(new System.Text.Rune('A'), out var a);

            Assert.True(face.IsVariable);
            Assert.True(bold.TryGetOutline(a, out var outline));
            Assert.NotEmpty(outline.Contours);
            Assert.True(bold.GetAdvance(a) > 0);
        }

        [Fact]
        public void WithoutGvar_AnInstanceDrawsTheDefaultOutline()
        {
            var font = FixtureBytes();
            int record = RecordOf(font, "gvar");
            font[record + 12] = font[record + 13] = font[record + 14] = 0;
            font[record + 15] = 6;

            var face = LoadBytes(font);
            face.TryMapRune(new System.Text.Rune('A'), out var a);
            Assert.True(face.TryGetOutline(a, out var regular));
            Assert.True(face.WithAxes([new AxisSetting("wght", 900)]).TryGetOutline(a, out var black));

            Assert.Equal(regular.Contours[0].Start, black.Contours[0].Start);
        }
    }
}
