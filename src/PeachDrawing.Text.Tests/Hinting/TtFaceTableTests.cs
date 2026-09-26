using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// How the hinter reads a font's tables: optional tables that are present and absent, the repairs of broken <c>loca</c> and
    /// <c>hdmx</c> data that FreeType makes, and the recognition of the "tricky" fonts. Each test starts from the bundled synthetic
    /// font and changes one table.
    /// </summary>
    public class TtFaceTableTests
    {
        private const int Glyphs = 391;

        private static TtFace? Face(byte[] font, string? family = "Fixture")
        {
            // the font read directly, so that a font the public loader would not accept (a required table cut short) still reaches the hinter
            var fontface = new OpenTypeFontface(FontFileData.CreateCompiledFont(font));
            return TtFace.TryCreate(fontface, family, null, null);
        }

        private static byte[] SetUInt16(byte[] table, int offset, int value)
        {
            var copy = (byte[])table.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(offset), (ushort)value);
            return copy;
        }

        // ------------------------------------------------------------------------------------------------ tricky fonts

        [Theory]
        [InlineData("MingLiU", true)]
        [InlineData("ABCDEF+DFKaiShu-Regular", true)] // a PDF subset tag is skipped
        [InlineData("abcdef+MingLiU", true)] // a tag that is not six capitals is not
        [InlineData("Liberation Sans", false)]
        [InlineData("ABCDEF+Liberation Sans", false)]
        [InlineData("AB1DEF+Sans", false)]
        public void TrickyFontsAreRecognizedByTheirFamilyName(string family, bool tricky)
        {
            Assert.Equal(tricky, Face(HostileFonts.Original(), family)!.IsTricky);
        }

        [Fact]
        public void AFontWithoutAFamilyNameIsNotTricky() => Assert.False(Face(HostileFonts.Original(), null)!.IsTricky);

        /// <summary>Bytes of a given length whose 32-bit big-endian word sum (the sfnt table checksum) is a given value.</summary>
        private static byte[] WithChecksum(int length, uint checksum)
        {
            var bytes = new byte[length];
            int words = length / 4;
            uint others = 0;
            for (int i = 0; i < words - 1; i++)
            {
                uint word = (uint)(i * 2654435761u);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), word);
                others = unchecked(others + word);
            }

            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan((words - 1) * 4), unchecked(checksum - others));
            return bytes;
        }

        [Fact]
        public void TrickyFontsAreRecognizedByTheChecksumsOfTheirProgramTables()
        {
            // "MingLiU 1995" of FreeType's list: cvt, fpgm and prep of exactly those lengths and checksums, whatever the name says
            var font = HostileFonts.Original();
            font = HostileFonts.WithTable(font, "cvt ", WithChecksum(0x2E4, 0x05BCF058));
            font = HostileFonts.WithTable(font, "fpgm", WithChecksum(0x87C4, 0x28233BF1));
            font = HostileFonts.WithTable(font, "prep", WithChecksum(0x1E1, 0xA344A1EA));

            Assert.True(Face(font, "Anything")!.IsTricky);
            Assert.True(Face(font, null)!.IsTricky);

            // two of the three tables are not enough
            var almost = HostileFonts.WithTable(font, "prep", WithChecksum(0x1E1, 0xA344A1E0));
            Assert.False(Face(almost, "Anything")!.IsTricky);
        }

        [Fact]
        public void TrickyFontsWithNoCvtTableAreRecognizedByTheOtherTwo()
        {
            // "NEC fadpop7.ttf" of FreeType's list: no cvt table at all, an fpgm of 0xE5 bytes and a prep of 0x117C bytes
            var font = HostileFonts.WithoutTable(HostileFonts.Original(), "cvt ");
            font = HostileFonts.WithTable(font, "fpgm", WithChecksum(0xE5, 0x40C92555));
            font = HostileFonts.WithTable(font, "prep", WithChecksum(0x117C, 0xA39B58E3));
            Assert.True(Face(font, "Anything")!.IsTricky);

            // a font that merely has no cvt table is not
            Assert.False(Face(HostileFonts.WithoutTable(HostileFonts.Original(), "cvt "), "Anything")!.IsTricky);
        }

        // -------------------------------------------------------------------------------------------- what makes a face

        [Fact]
        public void AFontWithoutTheTablesOfATrueTypeFontIsNotHinted()
        {
            foreach (var tag in new[] { "glyf", "loca", "hhea", "hmtx" })
                Assert.Null(Face(HostileFonts.WithoutTable(HostileFonts.Original(), tag)));
        }

        [Fact]
        public void TheOptionalTablesMayBeAbsent()
        {
            foreach (var tag in new[] { "cvt ", "fpgm", "prep", "OS/2", "post", "hdmx", "vhea", "vmtx" })
            {
                var face = Face(HostileFonts.WithoutTable(HostileFonts.Original(), tag));
                Assert.NotNull(face);
            }

            var bare = Face(HostileFonts.WithoutTable(HostileFonts.WithoutTable(HostileFonts.WithoutTable(HostileFonts.Original(), "cvt "), "fpgm"), "prep"))!;
            Assert.Empty(bare.Cvt);
            Assert.Empty(bare.FontProgram);
            Assert.Empty(bare.CvtProgram);
        }

        [Fact]
        public void ATruncatedMaxpTableOfVersionOneMakesTheFontUnhintable()
        {
            var maxp = HostileFonts.TableBytes(HostileFonts.Original(), "maxp");
            Assert.Null(Face(HostileFonts.WithTable(HostileFonts.Original(), "maxp", maxp[..20])));
        }

        [Fact]
        public void AMaxpTableOfVersionHalfHasNoLimitsAndTheFontStillLoads()
        {
            var maxp = HostileFonts.TableBytes(HostileFonts.Original(), "maxp")[..6];
            maxp[0] = 0; maxp[1] = 0; maxp[2] = 0x50; maxp[3] = 0;

            var face = Face(HostileFonts.WithTable(HostileFonts.Original(), "maxp", maxp))!;
            Assert.Equal(0, face.MaxStackElements);
            Assert.Equal(Glyphs, face.NumGlyphs);
        }

        [Fact]
        public void AZeroUnitsPerEmMakesTheFontUnhintable()
        {
            var head = HostileFonts.TableBytes(HostileFonts.Original(), "head");
            Assert.Null(Face(HostileFonts.WithTable(HostileFonts.Original(), "head", SetUInt16(head, 18, 0))));
        }

        [Fact]
        public void ATooShortHeadTableMakesTheFontUnhintable()
        {
            var head = HostileFonts.TableBytes(HostileFonts.Original(), "head");
            Assert.Null(Face(HostileFonts.WithTable(HostileFonts.Original(), "head", head[..40])));
        }

        [Fact]
        public void AnAbsurdNumberOfTwilightPointsAndFunctionsIsClamped()
        {
            var maxp = HostileFonts.TableBytes(HostileFonts.Original(), "maxp");
            maxp = SetUInt16(maxp, 16, 0xFFFF); // twilight points
            maxp = SetUInt16(maxp, 20, 3); // function definitions: fewer than the 64 FreeType always allows

            var face = Face(HostileFonts.WithTable(HostileFonts.Original(), "maxp", maxp))!;
            Assert.Equal(0xFFFF - 4, face.MaxTwilightPoints);
            Assert.Equal(64, face.MaxFunctionDefs);
        }

        // --------------------------------------------------------------------------------------------------- metrics

        [Fact]
        public void GlyphsBeyondTheLongMetricsShareTheLastAdvanceAndKeepTheirOwnBearings()
        {
            var original = HostileFonts.Original();
            var hhea = SetUInt16(HostileFonts.TableBytes(original, "hhea"), 34, 2); // two long metrics
            var hmtx = new byte[4 * 2 + 2 * (Glyphs - 2)];
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(0), 500);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(2), 10);
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(4), 700);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(6), 20);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(8 + 2 * 3), 33); // the bearing of glyph 5

            var font = HostileFonts.WithTable(HostileFonts.WithTable(original, "hhea", hhea), "hmtx", hmtx);
            var face = Face(font)!;

            face.GetHMetrics(1, out int lsb, out int advance);
            Assert.Equal((20, 700), (lsb, advance));

            face.GetHMetrics(5, out lsb, out advance);
            Assert.Equal((33, 700), (lsb, advance));
        }

        [Fact]
        public void MetricsThatReachPastTheEndOfTheTableAreZero()
        {
            var original = HostileFonts.Original();
            var hhea = SetUInt16(HostileFonts.TableBytes(original, "hhea"), 34, 4);
            var hmtx = new byte[10]; // room for two long metrics and a half
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(0), 500);

            var face = Face(HostileFonts.WithTable(HostileFonts.WithTable(original, "hhea", hhea), "hmtx", hmtx))!;

            face.GetHMetrics(0, out int lsb, out int advance);
            Assert.Equal(500, advance);

            face.GetHMetrics(3, out lsb, out advance); // the fourth long metric is not there
            Assert.Equal((0, 0), (lsb, advance));

            face.GetHMetrics(9, out lsb, out advance); // past the long metrics: the last one is not there either
            Assert.Equal((0, 0), (lsb, advance));

            // with room for the last long metric but not for the bearing of a glyph after it: the advance only
            var roomier = new byte[18];
            BinaryPrimitives.WriteUInt16BigEndian(roomier.AsSpan(12), 640);
            var second = Face(HostileFonts.WithTable(HostileFonts.WithTable(original, "hhea", hhea), "hmtx", roomier))!;
            second.GetHMetrics(9, out lsb, out advance);
            Assert.Equal((0, 640), (lsb, advance));
        }

        [Fact]
        public void VerticalMetricsComeFromVmtxWhenTheFontHasThem()
        {
            var original = HostileFonts.Original();
            var vhea = new byte[36];
            BinaryPrimitives.WriteUInt16BigEndian(vhea.AsSpan(34), 3);
            var vmtx = new byte[4 * 3 + 2 * (Glyphs - 3)];
            BinaryPrimitives.WriteUInt16BigEndian(vmtx.AsSpan(4), 900); // glyph 1: advance height
            BinaryPrimitives.WriteInt16BigEndian(vmtx.AsSpan(6), 77); // glyph 1: top side bearing
            BinaryPrimitives.WriteUInt16BigEndian(vmtx.AsSpan(8), 950); // the last long metric
            BinaryPrimitives.WriteInt16BigEndian(vmtx.AsSpan(12 + 2), 55); // glyph 4's bearing

            var font = HostileFonts.WithTable(HostileFonts.WithTable(original, "vhea", vhea), "vmtx", vmtx);
            var face = Face(font)!;

            Assert.True(face.VerticalInfo);
            face.GetVMetrics(1, 0, out int tsb, out int advanceHeight);
            Assert.Equal((77, 900), (tsb, advanceHeight));

            face.GetVMetrics(4, 0, out tsb, out advanceHeight);
            Assert.Equal((55, 950), (tsb, advanceHeight));
        }

        [Fact]
        public void VerticalMetricsAreDerivedFromOs2WhenThereIsNoVmtx()
        {
            var face = Face(HostileFonts.Original())!;
            Assert.False(face.VerticalInfo);
            Assert.NotEqual(0xFFFF, face.Os2Version);

            face.GetVMetrics(1, 100, out int tsb, out int advanceHeight);

            Assert.Equal((short)(face.TypoAscender - 100), tsb);
            Assert.Equal(Math.Abs(face.TypoAscender - face.TypoDescender), advanceHeight);
        }

        [Fact]
        public void VerticalMetricsAreDerivedFromHheaWhenThereIsNoOs2Either()
        {
            var face = Face(HostileFonts.WithoutTable(HostileFonts.Original(), "OS/2"))!;
            Assert.Equal(0xFFFF, face.Os2Version);

            face.GetVMetrics(1, 100, out int tsb, out int advanceHeight);

            Assert.Equal((short)(face.HheaAscender - 100), tsb);
            Assert.Equal(Math.Abs(face.HheaAscender - face.HheaDescender), advanceHeight);
        }

        [Fact]
        public void TheFixedPitchFlagOfPostIsRead()
        {
            var post = HostileFonts.TableBytes(HostileFonts.Original(), "post");
            Assert.False(Face(HostileFonts.Original())!.IsFixedPitch);

            BinaryPrimitives.WriteUInt32BigEndian(post.AsSpan(12), 1);
            Assert.True(Face(HostileFonts.WithTable(HostileFonts.Original(), "post", post))!.IsFixedPitch);
        }

        // ---------------------------------------------------------------------------------------------------- hdmx

        private static byte[] Hdmx(int numRecords, uint recordSize, params (int Ppem, int Width)[] records)
        {
            var table = new byte[8 + records.Length * (int)(recordSize & 0xFFFF)];
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)numRecords);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4), recordSize);

            int p = 8;
            foreach (var (ppem, width) in records)
            {
                table[p] = (byte)ppem;
                table[p + 1] = (byte)width;
                for (int g = 0; g < Glyphs; g++)
                    table[p + 2 + g] = (byte)width;
                p += (int)(recordSize & 0xFFFF);
            }

            return table;
        }

        private const uint RecordSize = (Glyphs + 2 + 3) & ~3;

        [Fact]
        public void TheDeviceMetricsOfASizeAreFoundWhateverTheOrderOfTheRecords()
        {
            var font = HostileFonts.WithTable(HostileFonts.Original(), "hdmx", Hdmx(3, RecordSize, (12, 7), (8, 5), (20, 11)));
            var face = Face(font)!;

            foreach (var (ppem, width) in new[] { (8, 5), (12, 7), (20, 11) })
            {
                int offset = face.GetDeviceMetrics(ppem);
                Assert.True(offset >= 0, $"no record for {ppem}");
                Assert.Equal(width, face.Data[offset + 3]);
            }

            Assert.Equal(-1, face.GetDeviceMetrics(10));
            Assert.Equal(-1, face.GetDeviceMetrics(4));
            Assert.Equal(-1, face.GetDeviceMetrics(40));
        }

        [Fact]
        public void HannomFontsWithARecordSizeWhoseUpperBytesAreSetAreRepaired()
        {
            var font = HostileFonts.WithTable(HostileFonts.Original(), "hdmx", Hdmx(1, 0xFFFF0000 | RecordSize, (12, 7)));

            Assert.True(Face(font)!.GetDeviceMetrics(12) >= 0);
        }

        [Fact]
        public void OutOfSpecHdmxTablesAreIgnored()
        {
            Assert.Equal(-1, Face(HostileFonts.WithTable(HostileFonts.Original(), "hdmx", Hdmx(1, RecordSize + 4, (12, 7))))!.GetDeviceMetrics(12));
            Assert.Equal(-1, Face(HostileFonts.WithTable(HostileFonts.Original(), "hdmx", Hdmx(0, RecordSize, (12, 7))))!.GetDeviceMetrics(12));
            Assert.Equal(-1, Face(HostileFonts.WithTable(HostileFonts.Original(), "hdmx", Hdmx(300, RecordSize, (12, 7))))!.GetDeviceMetrics(12));
            Assert.Equal(-1, Face(HostileFonts.WithTable(HostileFonts.Original(), "hdmx", new byte[6]))!.GetDeviceMetrics(12));
        }

        [Fact]
        public void RecordsThatAreNotInTheTableAreNotUsed()
        {
            // the table says three records but has room for one
            var face = Face(HostileFonts.WithTable(HostileFonts.Original(), "hdmx", Hdmx(3, RecordSize, (12, 7))))!;

            Assert.True(face.GetDeviceMetrics(12) >= 0);
            Assert.Equal(-1, face.GetDeviceMetrics(14));
        }

        [Fact]
        public void TheAdvanceOfAHintedGlyphComesFromHdmxWhereTheInterpreterIsNotInBackwardCompatibilityMode()
        {
            var font = HostileFonts.WithTable(HostileFonts.Original(), "hdmx", Hdmx(1, RecordSize, (12, 7)));
            var face = Face(font)!;

            var monochrome = TtSize.Create(face, 12 * 64, TtInterpreterVersion.V35, TtRenderMode.Mono);
            Assert.Equal(7 * 64, TtGlyphLoader.Load(monochrome, 3).Advance);

            // another size has no record and the advance is the scaled one, rounded to a pixel
            var other = TtSize.Create(face, 13 * 64, TtInterpreterVersion.V35, TtRenderMode.Mono);
            Assert.Equal(0, TtGlyphLoader.Load(other, 3).Advance % 64);
            Assert.NotEqual(7 * 64, TtGlyphLoader.Load(other, 3).Advance);
        }

        // ---------------------------------------------------------------------------------------------------- loca

        private static byte[] Loca(bool longFormat, params uint[] offsets)
        {
            var table = new byte[offsets.Length * (longFormat ? 4 : 2)];
            for (int i = 0; i < offsets.Length; i++)
            {
                if (longFormat)
                    BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(i * 4), offsets[i]);
                else
                    BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(i * 2), (ushort)(offsets[i] / 2));
            }

            return table;
        }

        private static byte[] WithLoca(byte[] font, bool longFormat, byte[] loca)
        {
            var head = HostileFonts.TableBytes(font, "head");
            var patched = SetUInt16(head, 50, longFormat ? 1 : 0);
            return HostileFonts.WithTable(HostileFonts.WithTable(font, "head", patched), "loca", loca);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BrokenLocationsAreRepairedAsFreeTypeRepairsThem(bool longFormat)
        {
            var original = HostileFonts.Original();
            int glyf = HostileFonts.TableBytes(original, "glyf").Length;

            // ordinary locations (8 bytes a glyph, in the range of the table), and four that are broken
            var offsets = new uint[Glyphs + 1];
            for (int i = 1; i <= Glyphs; i++)
                offsets[i] = (uint)Math.Min(glyf, i * 8);

            offsets[2] = (uint)glyf + 2;          // glyph 1 ends beyond the glyf table, and glyph 2 begins there
            offsets[10] = 100;                    // glyph 10 begins at 100...
            offsets[11] = 40;                     // ...and ends at 40
            offsets[Glyphs] = (uint)glyf + 1000;  // the last location reaches past the table

            var face = Face(WithLoca(original, longFormat, Loca(longFormat, offsets)))!;

            // pos1 beyond the table: no data
            Assert.Equal(0, face.GetLocation(2, out int size));
            Assert.Equal(0, size);

            // pos2 beyond the table, and not the last glyph: no data
            Assert.Equal(0, face.GetLocation(1, out size));
            Assert.Equal(0, size);

            // pos2 before pos1: the rest of the glyf table is the upper bound
            Assert.Equal(100, face.GetLocation(10, out size));
            Assert.Equal(glyf - 100, size);

            // pos2 beyond the table for the last glyph is repaired to the end of the table
            Assert.Equal((int)offsets[Glyphs - 1], face.GetLocation(Glyphs - 1, out size));
            Assert.Equal(glyf - (int)offsets[Glyphs - 1], size);

            // an ordinary glyph
            Assert.Equal(24, face.GetLocation(3, out size));
            Assert.Equal(8, size);

            // a glyph index outside the table
            Assert.Equal(0, face.GetLocation(Glyphs + 500, out size));
            Assert.Equal(0, size);
        }

        [Fact]
        public void ALocaTableWithFewerEntriesThanTheFontHasGlyphsShrinksTheGlyphCount()
        {
            var original = HostileFonts.Original();
            var loca = HostileFonts.TableBytes(original, "loca")[..(2 * 101)]; // 101 entries: glyphs 0 to 99
            var face = Face(HostileFonts.WithTable(original, "loca", loca))!;

            Assert.Equal(100, face.NumGlyphs);
            Assert.Equal(101, face.NumLocations);
        }

        [Fact]
        public void ALocaTableThatIsOnlyCutShortWithinItsPaddingIsExtended()
        {
            // one entry (two bytes) short, but the table is followed by two bytes of padding before the next one
            var original = HostileFonts.Original();
            var loca = HostileFonts.TableBytes(original, "loca");
            var face = Face(HostileFonts.WithTable(original, "loca", loca[..^2]))!;

            Assert.Equal(Glyphs, face.NumGlyphs);
            Assert.Equal(Glyphs + 1, face.NumLocations);
        }
    }
}
