using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The port of Adobe's CFF engine against FreeType itself: for two real CFF fonts and synthetic ones whose glyphs are random Type 2
    /// charstrings (<c>assets/fonts/generate_hinting_cff_fixtures.py</c>), the points the port hints must equal the points FreeType 2.14.3
    /// hints, exactly, in 26.6 units, and a charstring FreeType refuses the port must refuse. A mismatch is a bug in the port.
    /// </summary>
    public class HintingCffGoldenTests
    {
        private static readonly Lazy<CffGoldenFile> Golden = new(() => HintingGoldenData.Load<CffGoldenFile>("HintingCff.golden.json.gz"));

        public static TheoryData<string, string, int> Runs()
        {
            var data = new TheoryData<string, string, int>();
            foreach (var font in Golden.Value.Fonts)
                foreach (var (mode, runs) in font.Modes)
                    foreach (var run in runs)
                        data.Add(font.File, mode, run.Size);
            return data;
        }

        [Fact]
        public void TheGoldenDataWasMadeByTheFreeTypeTheEngineWasPortedFrom()
        {
            Assert.Equal("2.14.3", Golden.Value.FreeType.Version);
            Assert.Equal("VER-2-14-3", Golden.Value.FreeType.Tag);
        }

        [Fact]
        public void TheReferenceIsNotVacuous()
        {
            // a comparison over nothing would pass whatever the port does; the fixtures have glyphs FreeType loads and glyphs it refuses
            int loaded = 0, refused = 0;
            foreach (var font in Golden.Value.Fonts)
                foreach (var run in font.Modes.Values.SelectMany(r => r))
                    foreach (var glyph in run.Glyphs.Values)
                    {
                        if (glyph.Error == 0)
                            loaded++;
                        else
                            refused++;
                    }

            Assert.True(loaded > 7000, $"only {loaded} glyph loads in the reference");
            Assert.True(refused > 400, $"only {refused} refusals in the reference");
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void HintedOutlinesEqualFreeTypesExactly(string fontFile, string mode, int size26Dot6)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var run = font.Modes[mode].Single(r => r.Size == size26Dot6);

            var face = HintingCffFixtures.Face(fontFile);
            var size = new CffSize(face, run.Size, stemDarkening: mode == "darkened");

            var problems = new List<string>();
            foreach (var (glyphText, expected) in run.Glyphs)
            {
                int glyph = int.Parse(glyphText);

                if (expected.Error != 0)
                {
                    try
                    {
                        CffGlyphLoader.Load(size, glyph);
                        problems.Add($"glyph {glyph}: FreeType fails with {expected.Error} but the port loaded it");
                    }
                    catch (HintingException)
                    {
                    }

                    continue;
                }

                CffHintedGlyph actual;
                try
                {
                    actual = CffGlyphLoader.Load(size, glyph);
                }
                catch (HintingException ex)
                {
                    problems.Add($"glyph {glyph}: the port fails ({ex.Message}) where FreeType loads it");
                    continue;
                }

                Compare(problems, glyph, expected, actual);
                if (problems.Count > 12)
                    break;
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {run.Size / 64.0} ppem: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }

        private static void Compare(List<string> problems, int glyph, GlyphGolden expected, CffHintedGlyph actual)
        {
            if (actual.NPoints != expected.X.Length)
            {
                problems.Add($"glyph {glyph}: {actual.NPoints} points, FreeType has {expected.X.Length}");
                return;
            }

            if (actual.Advance != expected.Advance)
                problems.Add($"glyph {glyph}: advance {actual.Advance}, FreeType has {expected.Advance}");

            if (!actual.ContourEnds.SequenceEqual(expected.Ends))
                problems.Add($"glyph {glyph}: contour ends [{string.Join(",", actual.ContourEnds)}], FreeType has [{string.Join(",", expected.Ends)}]");

            for (int i = 0; i < expected.X.Length; i++)
            {
                if (actual.X[i] != expected.X[i] || actual.Y[i] != expected.Y[i])
                {
                    problems.Add($"glyph {glyph} point {i}: ({actual.X[i]}, {actual.Y[i]}), FreeType has ({expected.X[i]}, {expected.Y[i]})");
                    break;
                }

                if (actual.Tags[i] != expected.Tags[i])
                {
                    problems.Add($"glyph {glyph} point {i}: tag {actual.Tags[i]}, FreeType has {expected.Tags[i]}");
                    break;
                }
            }
        }
    }

    internal sealed class CffGoldenFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("fonts")]
        public List<FontGolden> Fonts { get; set; } = [];
    }

    /// <summary>The CFF fonts as the hinting engine reads them.</summary>
    internal static class HintingCffFixtures
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CffFace> Faces = new();

        public static CffFace Face(string fileName) => Faces.GetOrAdd(fileName, name =>
        {
            var typeface = PeachPDF.Tests.TestSupport.TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, name));
            return CffFace.TryCreate(typeface.Face.Fontface, glyph => typeface.GetAdvance((ushort)glyph))
                ?? throw new InvalidOperationException(name + " has no CFF outlines to hint.");
        });
    }
}
