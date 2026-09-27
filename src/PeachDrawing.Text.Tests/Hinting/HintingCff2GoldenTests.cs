using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The CFF2 branches of the port of Adobe's CFF engine against FreeType itself: for fonts with CFF2 outlines
    /// (<c>assets/fonts/generate_hinting_cff2_fixtures.py</c>) whose glyphs are random Type 2 charstrings with blended operands, hints included,
    /// and whose Private DICTs blend as well, at many locations of the design space, the points the port hints must equal the points FreeType
    /// 2.14.3 hints, exactly, in 26.6 units, and a glyph FreeType refuses the port must refuse. A mismatch is a bug in the port.
    /// </summary>
    public class HintingCff2GoldenTests
    {
        private static readonly Lazy<Cff2GoldenFile> Golden = new(() => HintingGoldenData.Load<Cff2GoldenFile>("HintingCff2.golden.json.gz"));

        public static TheoryData<string, string, int, int> Runs()
        {
            var data = new TheoryData<string, string, int, int>();
            foreach (var font in Golden.Value.Fonts)
                foreach (var (mode, runs) in font.Modes)
                    foreach (var run in runs)
                        data.Add(font.File, mode, run.Size, run.Loc);
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
            // a comparison over nothing would pass whatever the port does: the fixtures have glyphs FreeType loads and glyphs it refuses,
            // and the location matters to what FreeType makes of them
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

            Assert.True(loaded > 5000, $"only {loaded} glyph loads in the reference");
            Assert.True(refused > 800, $"only {refused} refusals in the reference");

            var multi = Golden.Value.Fonts.Single(f => f.File == "HintingCff2.otf");
            var atDefault = multi.Modes["standard"].Single(r => r.Loc == 0 && r.Size == 16 * 64);
            var atWeight = multi.Modes["standard"].Single(r => r.Loc == 2 && r.Size == 16 * 64);
            int different = atDefault.Glyphs.Count(g => g.Value.Error == 0 && atWeight.Glyphs[g.Key].Error == 0 && !g.Value.X.SequenceEqual(atWeight.Glyphs[g.Key].X));
            Assert.True(different > 30, $"only {different} glyphs differ between the two locations of the reference");
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void HintedOutlinesEqualFreeTypesExactly(string fontFile, string mode, int size26Dot6, int location)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var run = font.Modes[mode].Single(r => r.Size == size26Dot6 && r.Loc == location);

            var face = HintingCff2Fixtures.Face(fontFile, font.Locations[location]);
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

                HintingCffGoldenTests.Compare(problems, glyph, expected, actual);
                if (problems.Count > 12)
                    break;
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {run.Size / 64.0} ppem at {font.Locations[location].Name}: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }

        public static TheoryData<string, string, int, int> ApiRuns()
        {
            var data = new TheoryData<string, string, int, int>();
            foreach (var font in Golden.Value.Fonts)
                foreach (var (mode, runs) in font.Modes)
                    foreach (var run in runs.Where(r => HintingCff2Fixtures.IsReachableByTheApi(font, font.Locations[r.Loc])))
                        data.Add(font.File, mode, run.Size, run.Loc);
            return data;
        }

        [Theory]
        [MemberData(nameof(ApiRuns))]
        public void TheOutlinesOfTheApiEqualFreeTypes(string fontFile, string mode, int size26Dot6, int location)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var run = font.Modes[mode].Single(r => r.Size == size26Dot6 && r.Loc == location);
            var loc = font.Locations[location];

            var typeface = HintingCff2Fixtures.TypefaceAt(fontFile, loc);
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

                HintingCffApiGoldenTests.CompareGlyph(problems, glyph, expected, outline);
                if (problems.Count > 12)
                    break;
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {size26Dot6 / 64.0} ppem at {loc.Name}: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }

        [Fact]
        public void TheLocationsOfTheApiAreTheOnesFreeTypeNormalized()
        {
            // The golden data is made with design coordinates that FreeType normalizes in 16.16 and the package in 2.14 (as fontTools does), so
            // the two are the same number only where FreeType's is a multiple of 4 in 16.16 (an avar map of a real font can give one that is
            // not, and then the package's differs by at most 2). The API comparison above is over the locations where they are the same.
            foreach (var font in Golden.Value.Fonts)
            {
                foreach (var loc in font.Locations.Where(l => l.Design is not null))
                {
                    var typeface = HintingCff2Fixtures.TypefaceAt(font.File, loc);
                    var coordinates = typeface.Face.Descriptor.Variation;
                    var expected = loc.Ndv!;

                    if (expected.All(v => v == 0))
                    {
                        Assert.Null(coordinates); // the default location is not a variation
                        continue;
                    }

                    Assert.NotNull(coordinates);
                    var actual = coordinates.Normalized.Select(n => (int)Math.Round(n * 65536)).ToArray();
                    if (HintingCff2Fixtures.IsReachableByTheApi(font, loc))
                        Assert.Equal(expected, actual);
                    else
                        Assert.All(expected.Zip(actual), pair => Assert.InRange(Math.Abs(pair.First - pair.Second), 0, 2));
                }
            }
        }
    }

    internal sealed class Cff2GoldenFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("fonts")]
        public List<Cff2FontGolden> Fonts { get; set; } = [];
    }

    internal sealed class Cff2FontGolden
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        /// <summary>The number of axes FreeType sees in the font.</summary>
        [JsonPropertyName("axes")]
        public int Axes { get; set; }

        [JsonPropertyName("locations")]
        public List<Cff2LocationGolden> Locations { get; set; } = [];

        [JsonPropertyName("modes")]
        public Dictionary<string, List<Cff2RunGolden>> Modes { get; set; } = [];
    }

    internal sealed class Cff2LocationGolden
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>The design coordinates FreeType was given, or null for raw normalized coordinates (and a font that has no axes).</summary>
        [JsonPropertyName("design")]
        public double[]? Design { get; set; }

        /// <summary>The normalized vector FreeType kept, in 16.16, one for each axis; null for a font without axes.</summary>
        [JsonPropertyName("ndv")]
        public int[]? Ndv { get; set; }
    }

    internal sealed class Cff2RunGolden
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("loc")]
        public int Loc { get; set; }

        [JsonPropertyName("glyphs")]
        public Dictionary<string, GlyphGolden> Glyphs { get; set; } = [];
    }

    /// <summary>The CFF2 fonts as the hinting engine reads them, at the locations of the reference.</summary>
    internal static class HintingCff2Fixtures
    {
        private static readonly ConcurrentDictionary<(string File, string Location), CffFace> Faces = new();
        private static readonly ConcurrentDictionary<string, Typeface> Typefaces = new();

        public static Typeface Default(string fileName) =>
            Typefaces.GetOrAdd(fileName, name => TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, name)));

        /// <summary>The typeface at the design coordinates of a location.</summary>
        public static Typeface TypefaceAt(string fileName, Cff2LocationGolden location)
        {
            var typeface = Default(fileName);
            return location.Design is null ? typeface : typeface.WithAxes(typeface.Axes.Select((axis, i) => new AxisSetting(axis.Tag, location.Design[i])));
        }

        /// <summary>Whether the API's location is FreeType's exactly: the design coordinates are normalized to multiples of 1/16384 by both.</summary>
        public static bool IsReachableByTheApi(Cff2FontGolden font, Cff2LocationGolden location) =>
            font.Axes == 0 || (location.Design is not null && location.Ndv!.All(v => v % 4 == 0));

        /// <summary>The face the engine has at a location: the normalized vector FreeType kept, and the advances at it.</summary>
        public static CffFace Face(string fileName, Cff2LocationGolden location) => Faces.GetOrAdd((fileName, location.Name), _ =>
        {
            var typeface = Default(fileName);
            return CffFace.TryCreate(typeface.Face.Fontface, AdvanceAt(typeface, location.Ndv), location.Ndv)
                ?? throw new InvalidOperationException(fileName + " has no CFF outlines to hint.");
        });

        /// <summary>The advance of a glyph in font units at a normalized location: its hmtx entry and what the font's HVAR makes of it.</summary>
        private static Func<int, int> AdvanceAt(Typeface typeface, int[]? ndv)
        {
            var descriptor = typeface.Face.Descriptor;
            var variations = descriptor.FontFace.Variations;
            if (ndv is null || variations is null || ndv.All(v => v == 0))
                return glyph => descriptor.GlyphIndexToWidth(glyph);

            var coordinates = new VariationCoordinates(new double[ndv.Length], ndv.Select(v => v / 65536.0).ToArray(), variations.Axes.Select(a => a.Tag).ToArray());
            return glyph => descriptor.GlyphIndexToWidth(glyph) + FontVariations.Round(variations.GetAdvanceDelta(descriptor.FontFace, glyph, coordinates));
        }
    }
}
