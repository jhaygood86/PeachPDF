using PeachPDF.Tests.TestSupport;
using System.Text.Json;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// What an instance of a variable font reads besides its outlines and horizontal advances: the font bounding box, the vertical
    /// advances and origins (<c>VVAR</c>, or the phantom points of <c>gvar</c>) and the <c>avar</c> version 2 mapping of the axes. The
    /// references are fontTools' own instancer's, written by <c>assets/fonts/generate_variable_fixture.py</c>; it rounds to integers and this
    /// library does not, so values agree within one design unit.
    /// </summary>
    public class VariableFontBoxVerticalAndAvar2Tests
    {
        private const double Tolerance = 1.0;

        private static readonly string[] TrueTypeGlyphs = [".notdef", "space", "A", "B", "C", "acute", "Aacute"];
        private static readonly string[] Cff2Glyphs = [".notdef", "space", "A", "B", "C", "D", "E", "F"];

        private static JsonElement Locations(string golden) => JsonDocument.Parse(File.ReadAllText(golden)).RootElement.GetProperty("locations");

        private static void AssertNear(double expected, double actual, string what) =>
            Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{what}: {actual}, expected {expected}");

        private static void AssertBox(JsonElement expected, TypefaceMetrics metrics, string what)
        {
            var box = expected.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            AssertNear(box[0], metrics.XMin, what + " xMin");
            AssertNear(box[1], metrics.YMin, what + " yMin");
            AssertNear(box[2], metrics.XMax, what + " xMax");
            AssertNear(box[3], metrics.YMax, what + " yMax");
        }

        // ---- the font bounding box ---------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("TrueType")]
        [InlineData("TrueTypeNoVvar")]
        [InlineData("Cff2")]
        public void TheFontBox_OfAnInstance_IsTheBoxOfItsGlyphs_LikeTheInstancers(string which)
        {
            var (path, golden) = Fixture(which);
            var face = VariableFontTests.Load(path);

            foreach (var location in Locations(golden).EnumerateArray())
            {
                var settings = VariableFontTests.Settings(location.GetProperty("location"));
                AssertBox(location.GetProperty("box"), face.WithAxes(settings).Metrics, location.GetProperty("location").ToString());
            }
        }

        private static (string Path, string Golden) Fixture(string which) => which switch
        {
            "TrueType" => (BundledFonts.VariableVerticalTest, BundledFonts.VariableVerticalTestGolden),
            "TrueTypeNoVvar" => (BundledFonts.VariableVerticalTestNoVvar, BundledFonts.VariableVerticalTestGolden),
            "Cff2" => (BundledFonts.VariableCff2VerticalTest, BundledFonts.VariableCff2VerticalTestGolden),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };

        [Fact]
        public void TheFontBox_OfTheDefaultTypeface_IsTheHeadBox_AndAnInstanceMovesIt()
        {
            var face = VariableFontTests.Load(BundledFonts.VariableVerticalTest);
            var black = face.WithAxes([new AxisSetting("wght", 900)]);

            Assert.Equal((0, 0, 545, 882), (face.Metrics.XMin, face.Metrics.YMin, face.Metrics.XMax, face.Metrics.YMax));
            Assert.True(black.Metrics.XMax > face.Metrics.XMax);
            Assert.True(black.Metrics.YMax > face.Metrics.YMax);
        }

        [Fact]
        public void TheFontBox_HoldsEveryGlyphOfTheInstance()
        {
            var face = VariableFontTests.Load(BundledFonts.VariableVerticalTest);
            var instance = face.WithAxes([new AxisSetting("wght", 900), new AxisSetting("wdth", 125)]);
            var metrics = instance.Metrics;

            for (ushort glyph = 0; glyph < TrueTypeGlyphs.Length; glyph++)
            {
                if (!instance.TryGetOutline(glyph, out var outline))
                    continue;

                foreach (var contour in outline.Contours)
                {
                    var points = new List<PeachDrawing.Text.Outlines.OutlinePoint> { contour.Start };
                    points.AddRange(contour.Segments.Select(s => s.End));
                    foreach (var p in points)
                    {
                        Assert.InRange(p.X, metrics.XMin - Tolerance, metrics.XMax + Tolerance);
                        Assert.InRange(p.Y, metrics.YMin - Tolerance, metrics.YMax + Tolerance);
                    }
                }
            }
        }

        // ---- vertical metrics --------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("TrueType")]
        [InlineData("TrueTypeNoVvar")]
        [InlineData("Cff2")]
        public void VerticalAdvances_OfAnInstance_MatchTheInstancer(string which)
        {
            var (path, golden) = Fixture(which);
            var glyphs = which == "Cff2" ? Cff2Glyphs : TrueTypeGlyphs;
            var face = VariableFontTests.Load(path);
            Assert.True(face.HasVerticalMetrics);

            foreach (var location in Locations(golden).EnumerateArray())
            {
                var instance = face.WithAxes(VariableFontTests.Settings(location.GetProperty("location")));
                foreach (var advance in location.GetProperty("advances").EnumerateObject())
                {
                    ushort glyph = (ushort)Array.IndexOf(glyphs, advance.Name);
                    AssertNear(advance.Value.GetInt32(), instance.GetVerticalAdvance(glyph), $"{location.GetProperty("location")} {advance.Name} vertical advance");
                }
            }
        }

        [Fact]
        public void TheVerticalAdvance_OfTheDefaultTypeface_IsVmtxs_AndAnInstanceDiffers()
        {
            var face = VariableFontTests.Load(BundledFonts.VariableVerticalTest);
            var black = face.WithAxes([new AxisSetting("wght", 900)]);

            Assert.Equal(990, face.GetVerticalAdvance(2));
            Assert.True(black.GetVerticalAdvance(2) > 990);
        }

        [Fact]
        public void TheVerticalOrigin_OfATrueTypeInstance_FollowsTheVerticalAscent()
        {
            var face = VariableFontTests.Load(BundledFonts.VariableVerticalTest);

            foreach (var location in Locations(BundledFonts.VariableVerticalTestGolden).EnumerateArray())
            {
                var instance = face.WithAxes(VariableFontTests.Settings(location.GetProperty("location")));
                AssertNear(location.GetProperty("ascent").GetInt32(), instance.GetVerticalOrigin(2).Y, $"{location.GetProperty("location")} origin");
            }
        }

        [Fact]
        public void TheVerticalOrigin_OfACff2Instance_FollowsVorgThroughVvar()
        {
            var face = VariableFontTests.Load(BundledFonts.VariableCff2VerticalTest);
            Assert.True(face.HasVerticalOrigin);

            foreach (var location in Locations(BundledFonts.VariableCff2VerticalTestGolden).EnumerateArray())
            {
                var instance = face.WithAxes(VariableFontTests.Settings(location.GetProperty("location")));
                foreach (var origin in location.GetProperty("origins").EnumerateObject())
                {
                    ushort glyph = (ushort)Array.IndexOf(Cff2Glyphs, origin.Name);
                    AssertNear(origin.Value.GetInt32(), instance.GetVerticalOrigin(glyph).Y, $"{location.GetProperty("location")} {origin.Name} origin");
                }
            }
        }

        [Fact]
        public void ADamagedVvar_LeavesTheVerticalAdvanceToThePhantomPoints()
        {
            var font = File.ReadAllBytes(BundledFonts.VariableVerticalTest);
            int record = RecordOf(font, "VVAR");
            font[record + 12] = font[record + 13] = font[record + 14] = 0;
            font[record + 15] = 6;      // too short to hold anything

            var face = LoadBytes(font);
            var bold = face.WithAxes([new AxisSetting("wght", 900)]);

            // The advance still grows, from the phantom points of gvar.
            Assert.True(bold.GetVerticalAdvance(2) > face.GetVerticalAdvance(2));
        }

        [Fact]
        public void AnyOneDamagedByteInAVerticalTable_NeverMakesReadingAnInstanceThrow()
        {
            foreach (var (path, tags) in new[]
            {
                (BundledFonts.VariableVerticalTest, new[] { "VVAR", "avar", "glyf", "gvar" }),
                (BundledFonts.VariableCff2VerticalTest, new[] { "VVAR" }),
            })
            {
                var original = File.ReadAllBytes(path);
                foreach (var tag in tags)
                {
                    int record = RecordOf(original, tag);
                    int offset = (original[record + 8] << 24) | (original[record + 9] << 16) | (original[record + 10] << 8) | original[record + 11];
                    int length = (original[record + 12] << 24) | (original[record + 13] << 16) | (original[record + 14] << 8) | original[record + 15];

                    for (int at = offset; at < offset + length; at++)
                    {
                        foreach (byte value in new byte[] { 0x00, 0xFF, 0x80 })
                        {
                            var font = (byte[])original.Clone();
                            font[at] = value;

                            var instance = LoadBytes(font).WithAxes([new AxisSetting("wght", 850), new AxisSetting("wdth", 90)]);
                            for (ushort glyph = 0; glyph < 8; glyph++)
                            {
                                instance.GetVerticalAdvance(glyph);
                                instance.GetVerticalOrigin(glyph);
                            }

                            _ = instance.Metrics.XMax;
                        }
                    }
                }
            }
        }

        [Fact]
        public void ACompositeThatNamesItself_HasAFontBox_AndDoesNotHang()
        {
            var font = File.ReadAllBytes(BundledFonts.VariableVerticalTest);
            int Table(string tag)
            {
                int record = RecordOf(font, tag);
                return (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
            }

            // Aacute is glyph 6, a composite of A and acute: make its first component Aacute itself.
            int loca = Table("loca");
            bool longOffsets = font[Table("head") + 51] != 0;
            int start = longOffsets
                ? (font[loca + 24] << 24) | (font[loca + 25] << 16) | (font[loca + 26] << 8) | font[loca + 27]
                : ((font[loca + 12] << 8) | font[loca + 13]) * 2;
            int glyphAt = Table("glyf") + start;
            Assert.True((short)((font[glyphAt] << 8) | font[glyphAt + 1]) < 0, "glyph 6 should be a composite");
            font[glyphAt + 12] = 0;
            font[glyphAt + 13] = 6;

            var instance = LoadBytes(font).WithAxes([new AxisSetting("wght", 700)]);

            Assert.True(instance.Metrics.XMax > 0);
            Assert.True(instance.TryGetOutline(2, out _));
        }

        // ---- avar version 2 -----------------------------------------------------------------------------------------------------------

        [Fact]
        public void AnAvar2Font_MapsTheAxesThroughEachOther_LikeFontTools()
        {
            var face = VariableFontTests.Load(BundledFonts.VariableAvar2Test);
            Assert.True(face.IsVariable);

            foreach (var location in Locations(BundledFonts.VariableAvar2TestGolden).EnumerateArray())
            {
                var instance = face.WithAxes(VariableFontTests.Settings(location.GetProperty("location")));
                var description = location.GetProperty("location").ToString();

                foreach (var glyph in location.GetProperty("glyphs").EnumerateObject())
                {
                    ushort id = (ushort)Array.IndexOf(VariableFontTests.GlyphNames, glyph.Name);
                    AssertNear(glyph.Value.GetProperty("advance").GetInt32(), instance.GetAdvance(id), $"{description} {glyph.Name} advance");
                    VariableFontTests.AssertOutline(glyph.Value.GetProperty("outline"), instance, id, $"{description} {glyph.Name}");
                }
            }
        }

        [Fact]
        public void TheSameLocation_ReadsDifferentlyWithAndWithoutTheCrossAxisMapping()
        {
            var plain = VariableFontTests.Load(BundledFonts.VariableTest).WithAxes([new AxisSetting("wght", 900), new AxisSetting("wdth", 100)]);
            var mapped = VariableFontTests.Load(BundledFonts.VariableAvar2Test).WithAxes([new AxisSetting("wght", 900), new AxisSetting("wdth", 100)]);

            plain.TryMapRune(new System.Text.Rune('A'), out var glyph);

            // Full weight narrows the width by a quarter of its range through the store: the advance is not the plain font's.
            Assert.NotEqual(plain.GetAdvance(glyph), mapped.GetAdvance(glyph));
        }

        [Fact]
        public void AnyOneDamagedByteInAnAvar2Table_NeverMakesReadingAnInstanceThrow()
        {
            var original = File.ReadAllBytes(BundledFonts.VariableAvar2Test);
            int record = RecordOf(original, "avar");
            int offset = (original[record + 8] << 24) | (original[record + 9] << 16) | (original[record + 10] << 8) | original[record + 11];
            int length = (original[record + 12] << 24) | (original[record + 13] << 16) | (original[record + 14] << 8) | original[record + 15];

            for (int at = offset; at < offset + length; at++)
            {
                foreach (byte value in new byte[] { 0x00, 0xFF, 0x80, 0x01 })
                {
                    var font = (byte[])original.Clone();
                    font[at] = value;

                    var instance = LoadBytes(font).WithAxes([new AxisSetting("wght", 850), new AxisSetting("wdth", 90)]);
                    for (ushort glyph = 0; glyph < 7; glyph++)
                    {
                        instance.TryGetOutline(glyph, out _);
                        instance.GetAdvance(glyph);
                    }
                }
            }
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------------------

        private static int RecordOf(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
                    return record;
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
    }
}
