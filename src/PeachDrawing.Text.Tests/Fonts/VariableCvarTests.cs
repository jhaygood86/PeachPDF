using PeachDrawing.Text.Export;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text;
using System.Text.Json;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// <c>cvar</c>, the variation of the control values a TrueType font's hinting programs measure with: the table read on tables written
    /// by hand (every layout the specification allows), on the fixture font at a grid of locations against fontTools' own instancer
    /// (<c>VariableCvarTest.golden.json</c>, written by <c>assets/fonts/generate_variable_cvar_fixture.py</c>), and damaged. The instancer
    /// rounds every control value to a whole number of font units; the engine keeps the fraction (in 26.6), so values agree within one unit.
    /// </summary>
    public class VariableCvarTests
    {
        private const double Tolerance = 1.0;

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Cvar-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static Typeface LoadBytes(byte[] font)
        {
            var set = new FontSet();
            var family = set.AddData(font, new AddOptions { FamilyName = "Damaged-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static AxisSetting[] Settings(JsonElement location) =>
            location.EnumerateObject().Select(p => new AxisSetting(p.Name, p.Value.GetDouble())).ToArray();

        /// <summary>The control values (26.6, before any size scales them) the hinting engine starts from at a typeface's location.</summary>
        private static int[] Cvt(Typeface typeface) =>
            TtFace.TryCreate(typeface.Face.Fontface, typeface.Face.FamilyName, typeface.Face.Variation, null)!.Cvt;

        private static JsonElement Locations() => JsonDocument.Parse(File.ReadAllText(BundledFonts.VariableCvarTestGolden)).RootElement.GetProperty("locations");

        // ---- the fixture font --------------------------------------------------------------------------------------------------------

        [Fact]
        public void TheControlValues_AtEveryLocation_MatchTheInstancer()
        {
            var face = Load(BundledFonts.VariableCvarTest);

            foreach (var location in Locations().EnumerateArray())
            {
                var cvt = Cvt(face.WithAxes(Settings(location.GetProperty("location"))));
                var expected = location.GetProperty("cvt").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var where = location.GetProperty("location").ToString();

                Assert.Equal(expected.Length, cvt.Length);
                for (int i = 0; i < expected.Length; i++)
                    Assert.True(Math.Abs(expected[i] - cvt[i] / 64.0) <= Tolerance, $"{where} cvt[{i}]: {cvt[i] / 64.0}, expected {expected[i]}");
            }
        }

        [Fact]
        public void TheDefaultTypeface_HasTheFontsOwnControlValues()
        {
            var face = Load(BundledFonts.VariableCvarTest);

            Assert.Equal([700 * 64, 480 * 64, 90 * 64, -10 * 64, 1000 * 64, 333 * 64], Cvt(face));
            Assert.Equal(Cvt(face), Cvt(face.WithAxes([new AxisSetting("wght", 400), new AxisSetting("wdth", 100)])));
        }

        [Fact]
        public void AControlValueNoTupleNamesAPointFor_IsUnchanged()
        {
            var face = Load(BundledFonts.VariableCvarTest);

            // Entry 5 (333) is in no tuple, entry 4 moves only with the width.
            Assert.Equal(333 * 64, Cvt(face.WithAxes([new AxisSetting("wght", 900), new AxisSetting("wdth", 125)]))[5]);
            Assert.Equal(1000 * 64, Cvt(face.WithAxes([new AxisSetting("wght", 900)]))[4]);
        }

        [Fact]
        public void TheHintedOutline_OfAnInstance_UsesItsControlValues()
        {
            // The glyph's instructions move its top edge to control value 0, which is 700 in the default design and 732 at weight 900. At 1000
            // pixels to the em (one design unit to a pixel) the fitted edge is the control value itself.
            var face = Load(BundledFonts.VariableCvarTest);
            face.TryMapRune(new Rune('H'), out var glyph);
            var request = new OutlineRequest { PixelsPerEm = 1000, GridFitting = GridFitting.Standard };

            double Top(Typeface typeface)
            {
                Assert.True(typeface.TryGetOutline(glyph, request, out var outline));
                Assert.True(outline.IsGridFitted);
                return outline.Contours.SelectMany(c => c.Segments.Select(s => s.End.Y).Append(c.Start.Y)).Max();
            }

            Assert.InRange(Top(face), 699, 701);
            Assert.InRange(Top(face.WithAxes([new AxisSetting("wght", 900)])), 731, 733);
            Assert.InRange(Top(face.WithAxes([new AxisSetting("wght", 100)])), 684, 686);

            // Unfitted, the outline is the design (the font's gvar moves nothing), so the difference is the control value's.
            face.WithAxes([new AxisSetting("wght", 900)]).TryGetOutline(glyph, out var design);
            Assert.InRange(design.Contours.SelectMany(c => c.Segments.Select(s => s.End.Y)).Max(), 699, 701);
        }

        [Fact]
        public void AnyOneDamagedByteInTheCvarTable_NeverMakesReadingAnInstanceThrow()
        {
            var original = File.ReadAllBytes(BundledFonts.VariableCvarTest);
            int record = RecordOf(original, "cvar");
            int offset = (original[record + 8] << 24) | (original[record + 9] << 16) | (original[record + 10] << 8) | original[record + 11];
            int length = (original[record + 12] << 24) | (original[record + 13] << 16) | (original[record + 14] << 8) | original[record + 15];
            var request = new OutlineRequest { PixelsPerEm = 24, GridFitting = GridFitting.Standard };

            for (int at = offset; at < offset + length; at++)
            {
                foreach (byte value in new byte[] { 0x00, 0xFF, 0x80, 0x01 })
                {
                    var font = (byte[])original.Clone();
                    font[at] = value;

                    var instance = LoadBytes(font).WithAxes([new AxisSetting("wght", 850), new AxisSetting("wdth", 90)]);
                    Cvt(instance);
                    instance.TryGetOutline(2, request, out _);
                }
            }
        }

        [Fact]
        public void AnInstance_IsEmbeddedWithoutControlValuesOrInstructions_AndTheDefaultKeepsThem()
        {
            // The glyphs of an embedded instance are written afresh with no instructions (its points have moved, so the font's programs, which
            // were written for the default design, would misfit them), so the control values and programs that only those instructions use go
            // too, and the instance is consistent: nothing in it refers to a table that is not there.
            var face = Load(BundledFonts.VariableCvarTest);
            face.TryMapRune(new Rune('H'), out var glyph);

            var instance = Tables(TypefaceExporter.ExportSubset(face.WithAxes([new AxisSetting("wght", 800)]), [glyph], keepCharacterMap: false).Data.ToArray());
            var fallback = Tables(TypefaceExporter.ExportSubset(face, [glyph], keepCharacterMap: false).Data.ToArray());

            foreach (var table in new[] { "cvt ", "cvar", "fpgm", "prep", "fvar", "gvar" })
                Assert.DoesNotContain(table, instance.Keys);

            // The default typeface is written as a static font always was: with the font's own control values and program.
            Assert.Contains("cvt ", fallback.Keys);
        }

        private static Dictionary<string, int> Tables(byte[] font)
        {
            var tables = new Dictionary<string, int>();
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
                tables[Encoding.ASCII.GetString(font, 12 + i * 16, 4)] = i;

            return tables;
        }

        private static int RecordOf(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (Encoding.ASCII.GetString(font, record, 4) == tag)
                    return record;
            }

            throw new InvalidOperationException(tag);
        }

        [Fact]
        public void AFontWithoutCvar_KeepsItsControlValuesAtEveryLocation()
        {
            var font = File.ReadAllBytes(BundledFonts.VariableCvarTest);
            font[RecordOf(font, "cvar") + 3] = (byte)'X';

            var face = LoadBytes(font);

            Assert.Equal(Cvt(face), Cvt(face.WithAxes([new AxisSetting("wght", 900)])));
        }

        // ---- tables written by hand --------------------------------------------------------------------------------------------------

        private sealed class Writer
        {
            private readonly List<byte> _bytes = [];

            public Writer U8(int v) { _bytes.Add((byte)v); return this; }
            public Writer U16(int v) { _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)v); return this; }
            public Writer F2Dot14(double v) => U16((short)Math.Round(v * 16384));
            public Writer Bytes(params int[] values) { foreach (var v in values) _bytes.Add((byte)v); return this; }
            public byte[] ToArray() => _bytes.ToArray();
        }

        /// <summary>The packed point numbers of <paramref name="points"/> (each less than 128 apart, so one byte each).</summary>
        private static byte[] Points(params int[] points)
        {
            var w = new Writer().U8(points.Length).U8(points.Length - 1);
            int previous = 0;
            foreach (int p in points)
            {
                w.U8(p - previous);
                previous = p;
            }

            return w.ToArray();
        }

        /// <summary>The packed deltas of <paramref name="deltas"/> as bytes (each -128 to 127).</summary>
        private static byte[] Deltas(params int[] deltas)
        {
            var w = new Writer().U8(deltas.Length - 1);
            foreach (int d in deltas)
                w.U8(d & 0xFF);

            return w.ToArray();
        }

        /// <summary>One tuple of a one-axis table: its header fields and its serialized data.</summary>
        private sealed record Tuple(double Peak, byte[] Data, bool PrivatePoints, (double Start, double End)? Region = null, bool EmbeddedPeak = true);

        private static byte[] BuildCvar(Tuple[] tuples, byte[]? sharedPoints = null)
        {
            int headerSize = tuples.Sum(t => 4 + (t.EmbeddedPeak ? 2 : 0) + (t.Region is null ? 0 : 4));
            int dataOffset = 8 + headerSize;
            var w = new Writer().U16(1).U16(0).U16(tuples.Length | (sharedPoints is null ? 0 : 0x8000)).U16(dataOffset);
            foreach (var t in tuples)
            {
                w.U16(t.Data.Length).U16((t.EmbeddedPeak ? 0x8000 : 0) | (t.Region is null ? 0 : 0x4000) | (t.PrivatePoints ? 0x2000 : 0));
                if (t.EmbeddedPeak) w.F2Dot14(t.Peak);
                if (t.Region is { } r) w.F2Dot14(r.Start).F2Dot14(r.End);
            }

            var bytes = w.ToArray().ToList();
            if (sharedPoints is not null) bytes.AddRange(sharedPoints);
            foreach (var t in tuples) bytes.AddRange(t.Data);
            return bytes.ToArray();
        }

        private static CvarTable Parse(byte[] bytes)
        {
            var table = CvarTable.TryParse(bytes, 1);
            Assert.NotNull(table);
            return table;
        }

        [Fact]
        public void PrivatePoints_MoveOnlyTheControlValuesTheyNameAPointFor()
        {
            var table = Parse(BuildCvar(
            [
                new Tuple(1.0, Points(0, 2).Concat(Deltas(10, -4)).ToArray(), PrivatePoints: true),
                new Tuple(-1.0, Points(1).Concat(Deltas(7)).ToArray(), PrivatePoints: true),
            ]));

            Assert.Equal([5, 0, -2], table.GetDeltas([0.5], 3)!);
            Assert.Equal([0, 7, 0], table.GetDeltas([-1.0], 3)!);
            Assert.Equal([10, 0, -4], table.GetDeltas([1.0], 3)!);
            // At the default no tuple reaches.
            Assert.Null(table.GetDeltas([0.0], 3));
        }

        [Fact]
        public void APointNumberBeyondTheControlValues_IsIgnored()
        {
            var table = Parse(BuildCvar([new Tuple(1.0, Points(0, 9).Concat(Deltas(10, 5)).ToArray(), PrivatePoints: true)]));

            Assert.Equal([10, 0], table.GetDeltas([1.0], 2)!);
        }

        [Fact]
        public void SharedPoints_AreUsedByATupleThatHasNoPrivateOnes()
        {
            var table = Parse(BuildCvar([new Tuple(1.0, Deltas(3, 9), PrivatePoints: false)], Points(1, 2)));

            Assert.Equal([0, 3, 9], table.GetDeltas([1.0], 3)!);
        }

        [Fact]
        public void AllPoints_ArePackedAsACountOfZero()
        {
            var table = Parse(BuildCvar([new Tuple(1.0, new Writer().U8(0).ToArray().Concat(Deltas(1, 2, 3)).ToArray(), PrivatePoints: true)]));

            Assert.Equal([0.25, 0.5, 0.75], table.GetDeltas([0.25], 3)!);
        }

        [Fact]
        public void AnIntermediateRegion_ScalesBetweenItsEdgesAndItsPeak()
        {
            var table = Parse(BuildCvar([new Tuple(0.5, new Writer().U8(0).ToArray().Concat(Deltas(100)).ToArray(), PrivatePoints: true, Region: (0.25, 1.0))]));

            Assert.Null(table.GetDeltas([0.25], 1));                 // at the edge of the region nothing reaches
            Assert.Equal([50], table.GetDeltas([0.375], 1)!);         // half way up to the peak
            Assert.Equal([100], table.GetDeltas([0.5], 1)!);          // the peak
            Assert.Equal([50], table.GetDeltas([0.75], 1)!);          // half way down
            Assert.Null(table.GetDeltas([1.0], 1));
        }

        [Fact]
        public void TuplesThatReachTheLocation_AddUp()
        {
            var table = Parse(BuildCvar(
            [
                new Tuple(1.0, new Writer().U8(0).ToArray().Concat(Deltas(10)).ToArray(), PrivatePoints: true),
                new Tuple(0.5, new Writer().U8(0).ToArray().Concat(Deltas(20)).ToArray(), PrivatePoints: true, Region: (0.0, 1.0)),
            ]));

            // At 0.5: the first tuple at half strength (5) and the second at its peak (20).
            Assert.Equal([25], table.GetDeltas([0.5], 1)!);
        }

        [Fact]
        public void ATupleWithoutAPeak_IsNotUsable()
        {
            // cvar has no shared tuples to take a peak from.
            var table = Parse(BuildCvar([new Tuple(1.0, Points(0).Concat(Deltas(5)).ToArray(), PrivatePoints: true, EmbeddedPeak: false)]));

            Assert.Null(table.GetDeltas([1.0], 1));
        }

        [Fact]
        public void ATableThatIsTooShort_OrTheWrongVersion_OrForTheWrongAxisCount_IsRejected()
        {
            Assert.Null(CvarTable.TryParse(new byte[6], 1));
            Assert.Null(CvarTable.TryParse(new Writer().U16(2).U16(0).U16(0).U16(8).ToArray(), 1));
            Assert.Null(CvarTable.TryParse(new Writer().U16(1).U16(0).U16(0).U16(900).ToArray(), 1));
            Assert.Null(CvarTable.TryParse(new Writer().U16(1).U16(0).U16(0).U16(8).ToArray(), 0));
        }

        [Fact]
        public void ATruncatedTable_GivesNoDeltas_NotAnException()
        {
            var bytes = BuildCvar([new Tuple(1.0, Points(0, 1).Concat(Deltas(4, 5)).ToArray(), PrivatePoints: true)]);

            for (int length = 8; length < bytes.Length; length++)
            {
                var table = CvarTable.TryParse(bytes.AsMemory(0, length), 1);
                Assert.Null(table?.GetDeltas([1.0], 2));
            }
        }

        /// <summary>A table whose <paramref name="tupleCount"/> tuples all have the size 0, so that every one of them reads the same data: all the points, and 65,535 zero deltas.</summary>
        private static byte[] BuildOverlappingTuples(int tupleCount)
        {
            int dataOffset = 8 + tupleCount * 6;
            var w = new Writer().U16(1).U16(0).U16(tupleCount).U16(dataOffset);
            for (int i = 0; i < tupleCount; i++)
                w.U16(0).U16(0x8000 | 0x2000).F2Dot14(1.0);

            w.U8(0);                                // all points
            for (int run = 0; run < 1024; run++)
                w.U8(0x80 | 0x3F);                  // 64 zero deltas

            return w.ToArray();
        }

        [Fact]
        public void ATableWhoseTuplesAllReadTheSameData_IsRefusedOnceItsWorkPassesTheBound()
        {
            // Tuples of size 0 make the size of the table no bound on the work: 4095 tuples would each read 65,535 deltas.
            Assert.Null(Parse(BuildOverlappingTuples(4095)).GetDeltas([1.0], 65535));

            // A table of a hundred (6.5 million deltas) is within it.
            var within = Parse(BuildOverlappingTuples(100)).GetDeltas([1.0], 65535);
            Assert.NotNull(within);
            Assert.Equal(65535, within.Length);
            Assert.Null(Parse(BuildOverlappingTuples(100)).GetDeltas([1.0], 0));
        }

        [Fact]
        public void TheHintedOutline_AtALocationBetweenTheMasters_UsesTheFractionalDeltasAsFreeTypeDoes()
        {
            // At weight 850 the deltas are fractional (700 + 29.67 for control value 0). The 26.6 value is 729.67, and the size scales the
            // whole units of it, as FreeType's `cvt / 64` does, so the edge is fitted to 729; the instancer's rounding says 730.
            var face = Load(BundledFonts.VariableCvarTest);
            face.TryMapRune(new Rune('H'), out var glyph);
            var request = new OutlineRequest { PixelsPerEm = 1000, GridFitting = GridFitting.Standard };

            Assert.True(face.WithAxes([new AxisSetting("wght", 850)]).TryGetOutline(glyph, request, out var outline));

            Assert.InRange(outline.Contours.SelectMany(c => c.Segments.Select(s => s.End.Y).Append(c.Start.Y)).Max(), 728.5, 730.5);
        }    }
}
