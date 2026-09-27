using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The public API on fonts with CFF outlines against FreeType 2.14.3, exactly, in 26.6 units: for the runs of
    /// <c>HintingCff.golden.json.gz</c> made with stem darkening off (<c>standard</c>) and on (<c>darkened</c>), the outline
    /// <see cref="Typeface.TryGetOutline(ushort, in OutlineRequest, out GlyphOutline)"/> gives has the points FreeType has, and a glyph FreeType
    /// refuses is answered with an outline that is not grid-fitted. <see cref="OutlineRequest.StemDarkening"/> is what selects the run.
    /// </summary>
    public class HintingCffApiGoldenTests
    {
        private static readonly Lazy<CffGoldenFile> Golden = new(() => HintingGoldenData.Load<CffGoldenFile>("HintingCff.golden.json.gz"));

        public static TheoryData<string, string, int> Runs()
        {
            var data = new TheoryData<string, string, int>();
            foreach (var font in Golden.Value.Fonts)
                foreach (var (mode, runs) in font.Modes.Where(m => m.Key is "standard" or "darkened"))
                    foreach (var run in runs)
                        data.Add(font.File, mode, run.Size);
            return data;
        }

        [Fact]
        public void BothAnswersAreRecordedAndTheyDiffer()
        {
            // a comparison that the darkening does not show in would pass whatever the flag does: the golden runs must differ in the points
            var font = Golden.Value.Fonts.Single(f => f.File == "SourceCodePro-Regular.otf");
            var plain = font.Modes["standard"].Single(r => r.Size == 16 * 64);
            var darkened = font.Modes["darkened"].Single(r => r.Size == 16 * 64);

            int different = darkened.Glyphs.Count(g => g.Value.Error == 0 && plain.Glyphs[g.Key].Error == 0 && !g.Value.X.SequenceEqual(plain.Glyphs[g.Key].X));
            Assert.True(different > 50, $"only {different} glyphs differ between the two references");
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void TheOutlinesOfTheApiEqualFreeTypes(string fontFile, string mode, int size26Dot6)
        {
            var run = Golden.Value.Fonts.Single(f => f.File == fontFile).Modes[mode].Single(r => r.Size == size26Dot6);
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, fontFile));
            var request = new OutlineRequest { PixelsPerEm = size26Dot6 / 64.0, GridFitting = GridFitting.Standard, StemDarkening = mode == "darkened" };

            var problems = new List<string>();
            foreach (var (glyphText, expected) in run.Glyphs)
            {
                var glyph = ushort.Parse(glyphText);
                bool found = typeface.TryGetOutline(glyph, request, out var outline);

                if (expected.Error != 0)
                {
                    if (found && outline.IsGridFitted)
                        problems.Add($"glyph {glyph}: FreeType fails with {expected.Error} but the API fitted it");
                    continue;
                }

                if (expected.X.Length == 0)
                    continue; // no ink: there is no outline to compare

                if (!found || !outline.IsGridFitted)
                {
                    problems.Add($"glyph {glyph}: FreeType loads it but the API gave " + (found ? "an outline that is not fitted" : "nothing"));
                    continue;
                }

                CompareGlyph(problems, glyph, expected, outline);
                if (problems.Count > 12)
                    break;
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {size26Dot6 / 64.0} ppem: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }

        /// <summary>Walks the points FreeType made against the contours of the outline: a line takes one point, a cubic curve three.</summary>
        internal static void CompareGlyph(List<string> problems, ushort glyph, GlyphGolden expected, GlyphOutline outline)
        {
            long Fixed(double value) => (long)Math.Round(value * 64);

            if (outline.Contours.Count != expected.Ends.Length)
            {
                problems.Add($"glyph {glyph}: {outline.Contours.Count} contours, FreeType has {expected.Ends.Length}");
                return;
            }

            int first = 0;
            for (int c = 0; c < expected.Ends.Length; c++)
            {
                int end = expected.Ends[c];
                var contour = outline.Contours[c];
                bool Same(OutlinePoint point, int index) => Fixed(point.X) == expected.X[index] && Fixed(point.Y) == expected.Y[index];
                bool SameAsStart(OutlinePoint point) => Same(point, first);

                if (!Same(contour.Start, first))
                {
                    problems.Add($"glyph {glyph} contour {c}: starts at ({contour.Start.X}, {contour.Start.Y}), FreeType at ({expected.X[first]}, {expected.Y[first]}) in 26.6");
                    return;
                }

                int i = first + 1;
                foreach (var segment in contour.Segments)
                {
                    if (!segment.IsCubic)
                    {
                        if (i > end || !Same(segment.End, i))
                        {
                            problems.Add($"glyph {glyph} contour {c}: a line ends where FreeType has no such point (index {i})");
                            return;
                        }

                        i++;
                    }
                    else
                    {
                        // a contour that ends on two control points closes on its own start: the segment has no end point of its own
                        bool closesOnStart = i + 1 == end;
                        if (i + 1 > end || !Same(segment.Control1, i) || !Same(segment.Control2, i + 1) ||
                            (closesOnStart ? !SameAsStart(segment.End) : (i + 2 > end || !Same(segment.End, i + 2))))
                        {
                            problems.Add($"glyph {glyph} contour {c}: a curve differs from FreeType's points at index {i}");
                            return;
                        }

                        i += closesOnStart ? 2 : 3;
                    }
                }

                if (i != end + 1)
                {
                    problems.Add($"glyph {glyph} contour {c}: the outline uses {i - first} points, FreeType has {end + 1 - first}");
                    return;
                }

                first = end + 1;
            }

            if (outline.GridFittedAdvance is not { } advance || Fixed(advance) != expected.Advance)
                problems.Add($"glyph {glyph}: advance {outline.GridFittedAdvance}, FreeType has {expected.Advance / 64.0}");
        }
    }
}
