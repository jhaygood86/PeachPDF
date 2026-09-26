using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text.Json;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Variable fonts: the axes a font declares, reading it at a location, and that what it draws and measures there is what
    /// fontTools' own instancer produces for the same location (<c>VariableTest.golden.json</c>, written by
    /// <c>assets/fonts/generate_variable_fixture.py</c>). The instancer rounds coordinates to integers and this library does not, so
    /// values agree within one design unit.
    /// </summary>
    public class VariableFontTests
    {
        private const double Tolerance = 1.0;

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Variable-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static JsonElement Golden() => JsonDocument.Parse(File.ReadAllText(BundledFonts.VariableTestGolden)).RootElement;

        private static readonly string[] GlyphNames = [".notdef", "space", "A", "B", "C", "acute", "Aacute"];

        private static AxisSetting[] Settings(JsonElement location) =>
            location.EnumerateObject().Select(p => new AxisSetting(p.Name, p.Value.GetDouble())).ToArray();

        [Fact]
        public void AVariableFont_DeclaresItsAxes()
        {
            var face = Load(BundledFonts.VariableTest);

            Assert.True(face.IsVariable);
            Assert.Equal(["wght", "wdth"], face.Axes.Select(a => a.Tag));
            Assert.Equal((100d, 400d, 900d), (face.Axes[0].Minimum, face.Axes[0].Default, face.Axes[0].Maximum));
            Assert.Equal((75d, 100d, 125d), (face.Axes[1].Minimum, face.Axes[1].Default, face.Axes[1].Maximum));
            Assert.All(face.Axes, a => Assert.False(a.IsHidden));
            Assert.Equal([new AxisSetting("wght", 400), new AxisSetting("wdth", 100)], face.AxisSettings);
            Assert.Equal(["Weight", "Width"], face.Axes.Select(a => a.Name));
            Assert.Equal(["Light", "Bold", "Bold Condensed"], face.NamedVariations.Select(v => v.Name));
        }

        [Fact]
        public void AFontThatIsNotVariable_HasNoAxes_AndWithAxesReturnsItself()
        {
            var face = Load(BundledFonts.Ttf);

            Assert.False(face.IsVariable);
            Assert.Empty(face.Axes);
            Assert.Empty(face.AxisSettings);
            Assert.Same(face, face.WithAxes([new AxisSetting("wght", 700)]));
        }

        [Fact]
        public void AtTheDefaults_WithAxesReturnsTheDefaultTypeface()
        {
            var face = Load(BundledFonts.VariableTest);

            Assert.Same(face, face.WithAxes([new AxisSetting("wght", 400), new AxisSetting("wdth", 100)]));
            Assert.Same(face, face.WithAxes([]));
        }

        [Fact]
        public void ALocation_IsOneTypeface_AndDiffersFromTheDefault()
        {
            var face = Load(BundledFonts.VariableTest);

            var bold = face.WithAxes([new AxisSetting("wght", 700)]);
            var again = face.WithAxes([new AxisSetting("wdth", 100), new AxisSetting("wght", 700)]);

            Assert.Same(bold, again);
            Assert.NotSame(face, bold);
            Assert.NotEqual(face, bold);
            Assert.Equal(bold.GetHashCode(), again.GetHashCode());
            Assert.Equal([new AxisSetting("wght", 700), new AxisSetting("wdth", 100)], bold.AxisSettings);
            Assert.Contains("wght=700", bold.ToString());
        }

        [Fact]
        public void WithAxes_BuildsOnWhatTheTypefaceAlreadyHas()
        {
            var face = Load(BundledFonts.VariableTest);

            var condensedBold = face.WithAxes([new AxisSetting("wght", 700)]).WithAxes([new AxisSetting("wdth", 80)]);

            Assert.Same(face.WithAxes([new AxisSetting("wght", 700), new AxisSetting("wdth", 80)]), condensedBold);
        }

        [Fact]
        public void WithAxes_ClampsToTheRange_AndIgnoresAxesTheFontLacks()
        {
            var face = Load(BundledFonts.VariableTest);

            var clamped = face.WithAxes([new AxisSetting("wght", 5000), new AxisSetting("wdth", 1), new AxisSetting("opsz", 12)]);

            Assert.Equal([new AxisSetting("wght", 900), new AxisSetting("wdth", 75)], clamped.AxisSettings);
            Assert.Same(clamped, face.WithAxes([new AxisSetting("wght", 900), new AxisSetting("wdth", 75)]));
        }

        [Fact]
        public void WithAxes_RejectsBadArguments()
        {
            var face = Load(BundledFonts.VariableTest);

            Assert.Throws<ArgumentNullException>(() => face.WithAxes(null!));
            Assert.Throws<ArgumentException>(() => face.WithAxes([new AxisSetting("weight", 700)]));
            Assert.Throws<ArgumentException>(() => face.WithAxes([new AxisSetting(null!, 700)]));
        }

        [Fact]
        public void AnInstance_KeepsWhatTheFontFileSays_ButNotItsIdentity()
        {
            var face = Load(BundledFonts.VariableTest);
            var bold = face.WithAxes([new AxisSetting("wght", 700)]);

            Assert.Equal(face.FamilyName, bold.FamilyName);
            Assert.Equal(face.ContentHash, bold.ContentHash);
            Assert.True(bold.IsVariable);
            Assert.Equal(face.Axes.Count, bold.Axes.Count);
        }

        [Theory]
        [InlineData("VariableTest")]
        [InlineData("VariableTestNoHvar")]
        public void OutlinesAndAdvances_MatchTheInstancerAtEveryLocation(string which)
        {
            var path = which == "VariableTest" ? BundledFonts.VariableTest : BundledFonts.VariableTestNoHvar;
            var face = Load(path);

            foreach (var location in Golden().GetProperty("locations").EnumerateArray())
            {
                var instance = face.WithAxes(Settings(location.GetProperty("location")));
                var description = location.GetProperty("location").ToString();

                foreach (var glyph in location.GetProperty("glyphs").EnumerateObject())
                {
                    ushort id = (ushort)Array.IndexOf(GlyphNames, glyph.Name);
                    Assert.True(id < GlyphNames.Length, glyph.Name);

                    int advance = glyph.Value.GetProperty("advance").GetInt32();
                    Assert.True(Math.Abs(advance - instance.GetAdvance(id)) <= Tolerance,
                        $"{description} {glyph.Name}: advance {instance.GetAdvance(id)}, expected {advance}");

                    AssertOutline(glyph.Value.GetProperty("outline"), instance, id, $"{description} {glyph.Name}");
                }
            }
        }

        private static void AssertOutline(JsonElement expected, Typeface instance, ushort glyph, string what)
        {
            var contours = expected.EnumerateArray().ToArray();
            if (contours.Length == 0)
            {
                Assert.False(instance.TryGetOutline(glyph, out _), what + ": expected no outline");
                return;
            }

            Assert.True(instance.TryGetOutline(glyph, out var outline), what + ": no outline");
            Assert.Equal(contours.Length, outline.Contours.Count);

            for (int c = 0; c < contours.Length; c++)
            {
                var segments = contours[c].EnumerateArray().ToArray();
                var actual = outline.Contours[c];
                Assert.Equal(segments.Length - 1, actual.Segments.Count);

                AssertPoint(segments[0], 1, actual.Start, $"{what} contour {c} start");
                for (int s = 1; s < segments.Length; s++)
                {
                    var expectedSegment = segments[s];
                    var actualSegment = actual.Segments[s - 1];
                    if (expectedSegment[0].GetString() == "C")
                    {
                        Assert.True(actualSegment.IsCubic, $"{what} contour {c} segment {s}");
                        AssertPoint(expectedSegment, 1, actualSegment.Control1, $"{what} contour {c} segment {s} control 1");
                        AssertPoint(expectedSegment, 3, actualSegment.Control2, $"{what} contour {c} segment {s} control 2");
                        AssertPoint(expectedSegment, 5, actualSegment.End, $"{what} contour {c} segment {s} end");
                    }
                    else
                    {
                        Assert.False(actualSegment.IsCubic, $"{what} contour {c} segment {s}");
                        AssertPoint(expectedSegment, 1, actualSegment.End, $"{what} contour {c} segment {s} end");
                    }
                }
            }
        }

        private static void AssertPoint(JsonElement segment, int at, OutlinePoint actual, string what)
        {
            double x = segment[at].GetDouble();
            double y = segment[at + 1].GetDouble();
            Assert.True(Math.Abs(x - actual.X) <= Tolerance && Math.Abs(y - actual.Y) <= Tolerance,
                $"{what}: ({actual.X}, {actual.Y}), expected ({x}, {y})");
        }

        [Fact]
        public void FontWideMetrics_FollowTheLocation_LikeTheInstancer()
        {
            var face = Load(BundledFonts.VariableTest);

            foreach (var location in Golden().GetProperty("locations").EnumerateArray())
            {
                var metrics = face.WithAxes(Settings(location.GetProperty("location"))).Metrics;
                var expected = location.GetProperty("metrics");
                var description = location.GetProperty("location").ToString();

                void Check(string name, int actual) =>
                    Assert.True(Math.Abs(expected.GetProperty(name).GetInt32() - actual) <= Tolerance,
                        $"{description} {name}: {actual}, expected {expected.GetProperty(name).GetInt32()}");

                // The fixture does not ask for the typographic metrics, so the cell is the Windows one and the line is the hhea one.
                Check("winAscent", metrics.CellAscent);
                Check("winDescent", metrics.CellDescent);
                Check("hheaAscender", metrics.NormalLineAscent);
                Check("hheaLineGap", metrics.NormalLineGap);
                Check("xHeight", metrics.XHeight);
                Check("capHeight", metrics.CapHeight);
                Check("underlinePosition", metrics.UnderlinePosition);
                Check("underlineThickness", metrics.UnderlineThickness);
                Check("strikeoutPosition", metrics.StrikeoutPosition);
                Check("strikeoutSize", metrics.StrikeoutThickness);
            }
        }

        [Fact]
        public void AnInstance_MeasuresWiderThanTheDefaultWhenBold_AndNarrowerWhenCondensed()
        {
            var face = Load(BundledFonts.VariableTest);
            face.TryMapRune(new System.Text.Rune('A'), out var a);

            Assert.True(face.WithAxes([new AxisSetting("wght", 900)]).GetAdvance(a) > face.GetAdvance(a));
            Assert.True(face.WithAxes([new AxisSetting("wdth", 75)]).GetAdvance(a) < face.GetAdvance(a));
        }

        [Fact]
        public void ShapingAnInstance_UsesItsAdvances()
        {
            var face = Load(BundledFonts.VariableTest);
            var black = face.WithAxes([new AxisSetting("wght", 900)]);

            var run = PeachDrawing.Text.Shaping.Shaper.Shape(black, "AB", default);

            Assert.Equal(2, run.Glyphs.Count);
            face.TryMapRune(new System.Text.Rune('A'), out var a);
            face.TryMapRune(new System.Text.Rune('B'), out var b);
            Assert.Equal(black.GetAdvance(a) + black.GetAdvance(b), run.Advance, precision: 6);
        }
    }
}
