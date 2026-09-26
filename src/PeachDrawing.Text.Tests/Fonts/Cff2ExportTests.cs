using PeachDrawing.Text.Export;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text;
using static PeachDrawing.Text.Tests.Fonts.SyntheticCff2;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Embedding a variable font with CFF2 outlines: a PDF cannot embed CFF2, so <see cref="TypefaceExporter.ExportSubset"/> writes the
    /// glyphs at the typeface's location as a static OpenType font with CFF outlines. The tests read the exported file's tables directly
    /// (an embedded subset has no name to find it by) and read its outlines back with the engine's own CFF reader and, for one test, load
    /// the file as a font.
    /// </summary>
    public class Cff2ExportTests
    {
        private static readonly string[] GlyphNames = [".notdef", "space", "A", "B", "C", "D", "E", "F"];

        private static Typeface Load(byte[]? data = null)
        {
            var set = new FontSet();
            var options = new AddOptions { FamilyName = "Cff2Export-" + Guid.NewGuid().ToString("N") };
            var family = data is null ? set.AddFile(BundledFonts.VariableCff2Test, options) : set.AddData(data, options);
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static ushort GlyphOf(char c)
        {
            Assert.True(Load().TryMapRune(new Rune(c), out var glyph));
            return glyph;
        }

        private sealed class SfntFile(byte[] bytes)
        {
            public byte[] Bytes { get; } = bytes;

            public IReadOnlyDictionary<string, (int Offset, int Length)> Tables { get; } = Read(bytes);

            private static Dictionary<string, (int, int)> Read(byte[] b)
            {
                var tables = new Dictionary<string, (int, int)>();
                int count = (b[4] << 8) | b[5];
                for (int i = 0; i < count; i++)
                {
                    int at = 12 + i * 16;
                    tables[Encoding.ASCII.GetString(b, at, 4)] = (I32(b, at + 8), I32(b, at + 12));
                }

                return tables;
            }

            public static int I32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
            public int U16(int at) => (Bytes[at] << 8) | Bytes[at + 1];

            public CffTable Cff => new(Bytes, Tables["CFF "].Offset);
        }

        private static ExportedFont Export(Typeface typeface, IEnumerable<int> glyphs, bool keepCharacterMap = false) =>
            TypefaceExporter.ExportSubset(typeface, glyphs, keepCharacterMap);

        [Fact]
        public void AnInstance_IsWrittenAsAStaticFontWithCffOutlines()
        {
            var instance = Load().WithAxes([new AxisSetting("wght", 800), new AxisSetting("wdth", 90)]);

            var exported = Export(instance, [GlyphOf('A'), GlyphOf('B')]);
            var file = new SfntFile(exported.Data.ToArray());

            Assert.True(exported.HasCffOutlines);
            Assert.True(exported.IsSubset);
            Assert.Equal("OTTO", Encoding.ASCII.GetString(file.Bytes, 0, 4));
            foreach (var kept in new[] { "CFF ", "head", "hhea", "hmtx", "maxp", "OS/2", "name", "post" })
            {
                Assert.Contains(kept, file.Tables.Keys);
            }

            foreach (var dropped in new[] { "CFF2", "fvar", "avar", "HVAR", "STAT", "glyf", "loca", "cmap" })
            {
                Assert.DoesNotContain(dropped, file.Tables.Keys);
            }
        }

        [Fact]
        public void TheDefaultTypefaceOfACff2Font_IsWrittenAsAStaticFontToo()
        {
            var exported = Export(Load(), [GlyphOf('A')]);
            var file = new SfntFile(exported.Data.ToArray());

            Assert.True(exported.HasCffOutlines);
            Assert.True(exported.IsSubset);
            Assert.Contains("CFF ", file.Tables.Keys);
            Assert.DoesNotContain("CFF2", file.Tables.Keys);
        }

        [Fact]
        public void TheCharacterMap_IsKeptWhenAsked()
        {
            var instance = Load().WithAxes([new AxisSetting("wght", 800)]);

            var without = new SfntFile(Export(instance, [GlyphOf('A')], keepCharacterMap: false).Data.ToArray());
            var with = new SfntFile(Export(instance, [GlyphOf('A')], keepCharacterMap: true).Data.ToArray());

            Assert.DoesNotContain("cmap", without.Tables.Keys);
            Assert.Contains("cmap", with.Tables.Keys);
        }

        [Fact]
        public void TheExportedOutlines_AreThoseOfTheInstance_RoundedToWholeUnits()
        {
            var face = Load();
            var instance = face.WithAxes([new AxisSetting("wght", 650), new AxisSetting("wdth", 110)]);
            ushort[] wanted = [GlyphOf('A'), GlyphOf('B'), GlyphOf('C'), GlyphOf('D'), GlyphOf('E'), GlyphOf('F')];

            var file = new SfntFile(Export(instance, wanted.Select(g => (int)g)).Data.ToArray());
            var cff = file.Cff;

            Assert.True(cff.IsSupported);
            Assert.True(cff.IsCidKeyed);
            Assert.Equal(GlyphNames.Length, cff.CharStrings.Count);

            foreach (ushort glyph in wanted)
            {
                Assert.True(instance.TryGetOutline(glyph, out var expected), GlyphNames[glyph]);
                Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, glyph, out var actual), GlyphNames[glyph]);

                Assert.Equal(expected.Contours.Count, actual.Contours.Count);
                for (int c = 0; c < expected.Contours.Count; c++)
                {
                    var e = expected.Contours[c];
                    var a = actual.Contours[c];
                    Assert.Equal(e.Segments.Count, a.Segments.Count);
                    AssertNear(e.Start, a.Start, GlyphNames[glyph]);
                    for (int s = 0; s < e.Segments.Count; s++)
                    {
                        Assert.Equal(e.Segments[s].IsCubic, a.Segments[s].IsCubic);
                        AssertNear(e.Segments[s].End, a.Segments[s].End, GlyphNames[glyph]);
                        if (e.Segments[s].IsCubic)
                        {
                            AssertNear(e.Segments[s].Control1, a.Segments[s].Control1, GlyphNames[glyph]);
                            AssertNear(e.Segments[s].Control2, a.Segments[s].Control2, GlyphNames[glyph]);
                        }
                    }
                }
            }
        }

        private static void AssertNear(OutlinePoint expected, OutlinePoint actual, string glyph)
        {
            // Each coordinate is rounded to a whole unit, and made relative to the rounded one before it, so it is within half a unit.
            Assert.True(Math.Abs(expected.X - actual.X) <= 0.5 && Math.Abs(expected.Y - actual.Y) <= 0.5,
                $"{glyph}: ({actual.X}, {actual.Y}), expected ({expected.X}, {expected.Y})");
            Assert.Equal(Math.Round(actual.X), actual.X);
            Assert.Equal(Math.Round(actual.Y), actual.Y);
        }

        [Fact]
        public void AGlyphThatWasNotAskedFor_IsEmpty_AndKeepsItsPlace()
        {
            var instance = Load().WithAxes([new AxisSetting("wght", 800)]);

            var cff = new SfntFile(Export(instance, [GlyphOf('A')]).Data.ToArray()).Cff;

            Assert.Equal(GlyphNames.Length, cff.CharStrings.Count);
            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, GlyphOf('A'), out _));
            foreach (char other in "BCDEF")
            {
                Assert.False(Type2CharstringInterpreter.TryGetGlyphOutline(cff, GlyphOf(other), out _), other.ToString());
                Assert.Equal([14], cff.CharStrings[GlyphOf(other)].ToArray()); // endchar and nothing else
            }
        }

        [Fact]
        public void TheAdvances_AreTheInstances_AndTheBearingIsTheLeftmostPoint()
        {
            var instance = Load().WithAxes([new AxisSetting("wght", 800), new AxisSetting("wdth", 120)]);
            ushort a = GlyphOf('A');
            ushort e = GlyphOf('E');

            var file = new SfntFile(Export(instance, [a, e]).Data.ToArray());
            int hmtx = file.Tables["hmtx"].Offset;

            foreach (ushort glyph in new[] { a, e })
            {
                Assert.Equal(instance.GetAdvance(glyph), file.U16(hmtx + glyph * 4));

                Assert.True(instance.TryGetOutline(glyph, out var outline));
                double left = outline.Contours.SelectMany(c => new[] { c.Start }.Concat(c.Segments.SelectMany(s => s.IsCubic ? new[] { s.End, s.Control1, s.Control2 } : new[] { s.End }))).Min(p => p.X);
                int leftBearing = (short)file.U16(hmtx + glyph * 4 + 2);
                Assert.True(Math.Abs(leftBearing - left) <= 1.5, $"{GlyphNames[glyph]}: {leftBearing} against {left}");
            }

            // The metrics stop at the last glyph asked for; hhea says how many there are.
            Assert.Equal(e + 1, file.U16(file.Tables["hhea"].Offset + 34));
            Assert.Equal(GlyphNames.Length, file.U16(file.Tables["maxp"].Offset + 4));
            Assert.Equal(0x00005000, SfntFile.I32(file.Bytes, file.Tables["maxp"].Offset));
        }

        [Fact]
        public void TheFileHasValidChecksums_AndAWholeFileChecksumOfB1B0AFBA()
        {
            var file = new SfntFile(Export(Load().WithAxes([new AxisSetting("wght", 800)]), [GlyphOf('A')], keepCharacterMap: true).Data.ToArray());

            static uint Sum(byte[] bytes, int offset, int length)
            {
                uint sum = 0;
                for (int i = 0; i < length; i += 4)
                {
                    sum += (uint)((bytes[offset + i] << 24) | (bytes[offset + i + 1] << 16) | (bytes[offset + i + 2] << 8) | bytes[offset + i + 3]);
                }

                return sum;
            }

            Assert.Equal(0, file.Bytes.Length % 4);
            Assert.Equal(0xB1B0AFBAu, Sum(file.Bytes, 0, file.Bytes.Length));

            int count = file.U16(4);
            string previous = "";
            for (int i = 0; i < count; i++)
            {
                int at = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(file.Bytes, at, 4);
                Assert.True(string.CompareOrdinal(previous, tag) < 0, "the directory is sorted by tag");
                previous = tag;

                int offset = SfntFile.I32(file.Bytes, at + 8);
                int length = SfntFile.I32(file.Bytes, at + 12);
                Assert.Equal(0, offset % 4);
                if (tag != "head") // the checksum adjustment is part of head's own bytes
                {
                    Assert.Equal((uint)SfntFile.I32(file.Bytes, at + 4), Sum(file.Bytes, offset, (length + 3) & ~3));
                }
            }
        }

        [Fact]
        public void TheExportedFile_IsAFontThatLoadsAndDrawsTheInstance()
        {
            var instance = Load().WithAxes([new AxisSetting("wght", 700)]);
            ushort b = GlyphOf('B');

            var exported = Export(instance, [b, GlyphOf('A')], keepCharacterMap: true);
            var loaded = Load(exported.Data.ToArray());

            Assert.False(loaded.IsVariable);
            Assert.True(loaded.TryMapRune(new Rune('B'), out var mapped));
            Assert.Equal(b, mapped);
            Assert.True(loaded.TryGetOutline(b, out var drawn));
            Assert.True(instance.TryGetOutline(b, out var expected));
            Assert.Equal(expected.Contours.Count, drawn.Contours.Count);
            Assert.Equal(instance.GetAdvance(b), loaded.GetAdvance(b));
        }

        [Fact]
        public void TheSameExport_IsTheSameBytes()
        {
            var instance = Load().WithAxes([new AxisSetting("wght", 800)]);

            var first = Export(instance, [GlyphOf('A'), GlyphOf('C')]).Data.ToArray();
            var second = Export(instance, [GlyphOf('C'), GlyphOf('A')]).Data.ToArray();

            Assert.Equal(first, second);
        }

        [Fact]
        public void TwoLocations_AreWrittenDifferently()
        {
            var face = Load();

            var regular = Export(face, [GlyphOf('B')]).Data.ToArray();
            var black = Export(face.WithAxes([new AxisSetting("wght", 900)]), [GlyphOf('B')]).Data.ToArray();

            Assert.NotEqual(regular, black);
        }

        [Fact]
        public void AGlyphThatTheFontDoesNotHave_IsRefused()
        {
            var face = Load();

            Assert.Throws<ArgumentOutOfRangeException>(() => Export(face, [GlyphNames.Length]));
            Assert.Throws<ArgumentOutOfRangeException>(() => Export(face, [-1]));
        }

        [Fact]
        public void WithNothingAskedFor_OnlyNotdefHasAnOutline()
        {
            var cff = new SfntFile(Export(Load(), []).Data.ToArray()).Cff;

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, 0, out _));
            for (int glyph = 1; glyph < GlyphNames.Length; glyph++)
            {
                Assert.False(Type2CharstringInterpreter.TryGetGlyphOutline(cff, glyph, out _));
            }
        }

        [Fact]
        public void ACff2FontWhoseTableCannotBeRead_IsHandedOverWhole_LikeAnyOtherFontWithCffOutlines()
        {
            byte[] font = File.ReadAllBytes(BundledFonts.VariableCff2Test);
            var damaged = font.ToArray();
            var directory = new SfntFile(damaged);
            int count = directory.U16(4);
            for (int i = 0; i < count; i++)
            {
                int at = 12 + i * 16;
                if (Encoding.ASCII.GetString(damaged, at, 4) == "CFF2")
                {
                    damaged[at + 12] = 0x7F;
                    damaged[at + 13] = damaged[at + 14] = damaged[at + 15] = 0xFF;
                }
            }

            var exported = Export(Load(damaged), [2]);

            Assert.True(exported.HasCffOutlines);
            Assert.False(exported.IsSubset);
            Assert.Equal(damaged, exported.Data.ToArray());
        }

        [Fact]
        public void AnOutlineWithHugeCoordinates_IsKeptInsideWhatACharstringCanHold()
        {
            // Glyph 2 moves 30000 units three times: 90000 from the origin, beyond a 16-bit charstring operand.
            var (font, offset, length) = ReplaceCff2(File.ReadAllBytes(BundledFonts.VariableCff2Test), Table(
            [
                Cs(0, 0, Op.RMoveTo, 0, 0, Op.RLineTo),
                [],
                Cs(30000, 30000, Op.RMoveTo, 30000, 30000, Op.RLineTo, 30000, -30000, Op.RLineTo, -30000, -30000, Op.RLineTo),
                [], [], [], [], [],
            ]));
            _ = (offset, length);

            var instance = Load(font);
            ushort glyph = 2;
            Assert.True(instance.TryGetOutline(glyph, out var drawn));
            Assert.True(drawn.Contours[0].Start.X >= 30000);

            var cff = new SfntFile(Export(instance, [glyph]).Data.ToArray()).Cff;

            Assert.True(Type2CharstringInterpreter.TryGetGlyphOutline(cff, glyph, out var written));
            var points = written.Contours.SelectMany(c => new[] { c.Start }.Concat(c.Segments.Select(s => s.End))).ToArray();
            Assert.All(points, p => Assert.InRange(Math.Abs(p.X), 0, 16383));
            Assert.All(points, p => Assert.InRange(Math.Abs(p.Y), 0, 16383));
        }

        /// <summary>The fixture with its CFF2 table replaced: the new table goes at the end of the file and the directory entry follows it.</summary>
        private static (byte[] Font, int Offset, int Length) ReplaceCff2(byte[] font, byte[] table)
        {
            var directory = new SfntFile(font);
            int count = directory.U16(4);
            int padded = (font.Length + 3) & ~3;
            var result = new byte[padded + ((table.Length + 3) & ~3)];
            Array.Copy(font, result, font.Length);
            Array.Copy(table, 0, result, padded, table.Length);

            for (int i = 0; i < count; i++)
            {
                int at = 12 + i * 16;
                if (Encoding.ASCII.GetString(result, at, 4) != "CFF2")
                {
                    continue;
                }

                for (int b = 0; b < 4; b++)
                {
                    result[at + 8 + b] = (byte)(padded >> (24 - 8 * b));
                    result[at + 12 + b] = (byte)(table.Length >> (24 - 8 * b));
                }
            }

            return (result, padded, table.Length);
        }
    }
}
