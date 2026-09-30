using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text.Json;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Variable fonts with CFF2 outlines: what the engine draws at a location is what fontTools' own instancer draws of
    /// <c>VariableCff2Test.otf</c> (<c>VariableCff2Test.golden.json</c>, made by <c>assets/fonts/generate_variable_fixture.py</c>).
    /// <para>
    /// At a location where every axis is at its minimum, default or maximum (the masters' own places) every region scalar is 0 or 1, the
    /// blends give whole numbers and the two agree exactly. Elsewhere the instancer rounds each blended operand to a whole number and the
    /// operands are relative, so a point it draws can stray from the exact one by half a unit for each operand that leads to it while the
    /// engine keeps the fractions: <see cref="InterpolatedTolerance"/> is 2 units (the fixture's longest chain is six operands, and the
    /// worst difference measured is 1.5).
    /// </para>
    /// </summary>
    public class Cff2OutlineTests
    {
        private const double ExactTolerance = 1e-6;
        private const double InterpolatedTolerance = 2.0;

        private static readonly string[] GlyphNames = [".notdef", "space", "A", "B", "C", "D", "E", "F"];

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Cff2-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static JsonElement Golden() => JsonDocument.Parse(File.ReadAllText(BundledFonts.VariableCff2TestGolden)).RootElement;

        private static AxisSetting[] Settings(JsonElement location) =>
            location.EnumerateObject().Select(p => new AxisSetting(p.Name, p.Value.GetDouble())).ToArray();

        /// <summary>Whether every axis of <paramref name="instance"/> is at an end of its range or its default.</summary>
        private static bool IsAtAMasterLocation(Typeface instance) =>
            instance.Axes.Zip(instance.AxisSettings).All(pair =>
                pair.Second.Value == pair.First.Minimum || pair.Second.Value == pair.First.Default || pair.Second.Value == pair.First.Maximum);

        [Fact]
        public void ACff2Font_IsVariable_AndDeclaresItsAxes()
        {
            var face = Load(BundledFonts.VariableCff2Test);

            Assert.True(face.IsVariable);
            Assert.Equal(["wght", "wdth"], face.Axes.Select(a => a.Tag));
            Assert.Equal((100d, 400d, 900d), (face.Axes[0].Minimum, face.Axes[0].Default, face.Axes[0].Maximum));
        }

        [Fact]
        public void ACff2Font_HasNoGlyf_ButItsGlyphsAreOutlines_AndItIsNotAColorFont()
        {
            var face = Load(BundledFonts.VariableCff2Test);
            face.TryMapRune(new System.Text.Rune('B'), out var b);
            face.TryMapRune(new System.Text.Rune(' '), out var space);

            Assert.False(face.HasColorGlyphs);
            Assert.True(face.TryGetOutline(b, out var outline));
            Assert.NotEmpty(outline.Contours);
            Assert.False(face.TryGetOutline(space, out _)); // an empty charstring is a glyph with no ink
            Assert.False(face.TryGetOutline(200, out _));   // there is no such glyph
        }

        [Fact]
        public void ACff2Glyph_AtASize_IsGridFittedAtItsLocation_AndAFontWithNoHintsIsOnlyScaled()
        {
            var instance = Load(BundledFonts.VariableCff2Test).WithAxes([new AxisSetting("wght", 700)]);
            instance.TryMapRune(new System.Text.Rune('B'), out var b);

            Assert.True(instance.TryGetOutline(b, new OutlineRequest { PixelsPerEm = 20, GridFitting = GridFitting.Standard }, out var scaled));
            Assert.True(instance.TryGetOutline(b, out var design));

            // the font has no hints and no blue zones, so fitting it scales the outline at its location and rounds the points to 1/64 of a pixel
            Assert.True(scaled.IsGridFitted);
            Assert.Equal(design.Contours.Count, scaled.Contours.Count);
            Assert.InRange(Math.Abs(design.Contours[0].Start.X * 20 / instance.Metrics.UnitsPerEm - scaled.Contours[0].Start.X), 0, 1 / 64.0);
        }

        [Fact]
        public void TwoLocations_DrawTheSameGlyphDifferently_AndEachIsMemoized()
        {
            var face = Load(BundledFonts.VariableCff2Test);
            face.TryMapRune(new System.Text.Rune('A'), out var a);

            Assert.True(face.WithAxes([new AxisSetting("wght", 900)]).TryGetOutline(a, out var black));
            Assert.True(face.WithAxes([new AxisSetting("wght", 100)]).TryGetOutline(a, out var thin));

            Assert.NotEqual(black.Contours[0].Segments[0].End.X, thin.Contours[0].Segments[0].End.X);
        }

        [Fact]
        public void OutlinesAndAdvances_MatchTheInstancerAtEveryLocation()
        {
            var face = Load(BundledFonts.VariableCff2Test);
            int exact = 0;

            foreach (var location in Golden().GetProperty("locations").EnumerateArray())
            {
                var instance = face.WithAxes(Settings(location.GetProperty("location")));
                var description = location.GetProperty("location").ToString();
                double tolerance = IsAtAMasterLocation(instance) ? ExactTolerance : InterpolatedTolerance;
                if (tolerance == ExactTolerance)
                    exact++;

                foreach (var glyph in location.GetProperty("glyphs").EnumerateObject())
                {
                    ushort id = (ushort)Array.IndexOf(GlyphNames, glyph.Name);
                    Assert.True(id < GlyphNames.Length, glyph.Name);

                    int advance = glyph.Value.GetProperty("advance").GetInt32();
                    Assert.True(Math.Abs(advance - instance.GetAdvance(id)) <= 1,
                        $"{description} {glyph.Name}: advance {instance.GetAdvance(id)}, expected {advance}");

                    AssertOutline(glyph.Value.GetProperty("outline"), instance, id, $"{description} {glyph.Name}", tolerance);
                }
            }

            // The default, the four masters on the axes' ends and the two clamped ones are compared exactly.
            Assert.True(exact >= 6, $"{exact} locations were compared exactly");
        }

        private static void AssertOutline(JsonElement expected, Typeface instance, ushort glyph, string what, double tolerance)
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

                AssertPoint(segments[0], 1, actual.Start, $"{what} contour {c} start", tolerance);
                for (int s = 1; s < segments.Length; s++)
                {
                    var expectedSegment = segments[s];
                    var actualSegment = actual.Segments[s - 1];
                    if (expectedSegment[0].GetString() == "C")
                    {
                        Assert.True(actualSegment.IsCubic, $"{what} contour {c} segment {s}");
                        AssertPoint(expectedSegment, 1, actualSegment.Control1, $"{what} contour {c} segment {s} control 1", tolerance);
                        AssertPoint(expectedSegment, 3, actualSegment.Control2, $"{what} contour {c} segment {s} control 2", tolerance);
                        AssertPoint(expectedSegment, 5, actualSegment.End, $"{what} contour {c} segment {s} end", tolerance);
                    }
                    else
                    {
                        Assert.False(actualSegment.IsCubic, $"{what} contour {c} segment {s}");
                        AssertPoint(expectedSegment, 1, actualSegment.End, $"{what} contour {c} segment {s} end", tolerance);
                    }
                }
            }
        }

        private static void AssertPoint(JsonElement segment, int at, OutlinePoint actual, string what, double tolerance)
        {
            double x = segment[at].GetDouble();
            double y = segment[at + 1].GetDouble();
            Assert.True(Math.Abs(x - actual.X) <= tolerance && Math.Abs(y - actual.Y) <= tolerance,
                $"{what}: ({actual.X}, {actual.Y}), expected ({x}, {y})");
        }
    }
}
