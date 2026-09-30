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
            TtFace.TryCreate(typeface.Face.Fontface, typeface.Face.FamilyName, TtVarTables.NormalizedCoordinates(typeface.Face.Fontface, typeface.Face.Variation))!.Cvt;

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
                    // FreeType refuses to set a location when the cvar table is invalid (an invalid header, an invalid tuple index): there is no face then
                    _ = TtFace.TryCreate(instance.Face.Fontface, instance.Face.FamilyName, TtVarTables.NormalizedCoordinates(instance.Face.Fontface, instance.Face.Variation));
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
        }
    }
}
