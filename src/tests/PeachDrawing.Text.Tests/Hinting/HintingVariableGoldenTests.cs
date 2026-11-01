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
    /// The variation code of the port of FreeType's TrueType driver against FreeType itself. For variable fonts with TrueType outlines
    /// (<c>assets/fonts/generate_hinting_variable_fixtures.py</c>: a synthetic font whose glyphs run random programs and vary in dense and sparse tuples, and whose
    /// <c>cvar</c>, <c>HVAR</c>, <c>VVAR</c>, <c>MVAR</c> and <c>avar</c> tables have the shapes a real font has, and the real variable fonts of the repository), at many
    /// locations of the design space, the normalized coordinates, the control values, the points, the advances and the ranges of <c>gasp</c> the port makes must equal what
    /// FreeType 2.14.3 makes, exactly, in 16.16 and 26.6 units, and a glyph or a location FreeType refuses the port must refuse. A mismatch is a bug in the port.
    /// </summary>
    public class HintingVariableGoldenTests
    {
        private static readonly Lazy<VariableGoldenFile> Golden = new(() => HintingGoldenData.Load<VariableGoldenFile>("HintingVariable.golden.json.gz"));

        public static TheoryData<string, string, int, int> Runs()
        {
            var data = new TheoryData<string, string, int, int>();
            foreach (var font in Golden.Value.Fonts)
                foreach (var (mode, runs) in font.Modes)
                    foreach (var run in runs)
                        data.Add(font.File, mode, run.Size, run.Loc);
            return data;
        }

        public static TheoryData<string, string, int, int> ApiRuns()
        {
            var data = new TheoryData<string, string, int, int>();
            foreach (var font in Golden.Value.Fonts)
                foreach (var (mode, runs) in font.Modes)
                    foreach (var run in runs.Where(r => HintingVariableFixtures.IsReachableByTheApi(font.Locations[r.Loc])))
                        data.Add(font.File, mode, run.Size, run.Loc);
            return data;
        }

        public static TheoryData<string> Files()
        {
            var data = new TheoryData<string>();
            foreach (var font in Golden.Value.Fonts)
                data.Add(font.File);
            return data;
        }

        [Fact]
        public void TheGoldenDataWasMadeByTheFreeTypeTheVariationCodeWasPortedFrom()
        {
            Assert.Equal("2.14.3", Golden.Value.FreeType.Version);
            Assert.Equal("VER-2-14-3", Golden.Value.FreeType.Tag);
        }

        [Fact]
        public void TheReferenceIsNotVacuous()
        {
            // a comparison over nothing would pass whatever the port does: the fixtures have many glyphs FreeType loads, and the location changes what it makes of them
            int loaded = Golden.Value.Fonts.SelectMany(f => f.Modes.Values.SelectMany(r => r)).SelectMany(r => r.Glyphs.Values).Count(g => g.Error == 0);
            Assert.True(loaded > 30000, $"only {loaded} glyph loads in the reference");
            Assert.True(Golden.Value.Fonts.Count >= 14, "the fixtures are missing");

            var main = Golden.Value.Fonts.Single(f => f.File == "HintingVariable.ttf");
            Assert.True(main.Locations.Count >= 20);
            var atDefault = main.Modes["standard"].Single(r => r.Loc == 0 && r.Size == 13 * 64);
            int locationsThatMove = 0;
            foreach (var run in main.Modes["standard"].Where(r => r.Size == 13 * 64 && r.Loc > 1))
            {
                int different = run.Glyphs.Count(g => g.Value.Error == 0 && atDefault.Glyphs[g.Key].Error == 0 && !g.Value.X.SequenceEqual(atDefault.Glyphs[g.Key].X));
                if (different > 40)
                    locationsThatMove++;
            }

            Assert.True(locationsThatMove >= main.Locations.Count - 3, $"the location changes the glyphs at only {locationsThatMove} of {main.Locations.Count} locations");

            // and the recorded advances differ, so the metrics tables are exercised
            var advances = main.Modes["standard"].Where(r => r.Size == 13 * 64).Select(r => string.Join(",", r.Glyphs.Values.Select(g => g.Advance))).Distinct().Count();
            Assert.True(advances > 10, $"only {advances} different sets of advances");

            // the ranges of gasp move with MVAR
            Assert.True(main.Locations.Where(l => l.Gasp is not null).Select(l => string.Join(",", l.Gasp!)).Distinct().Count() > 3);
        }

        [Theory]
        [MemberData(nameof(Files))]
        public void TheNormalizedCoordinatesEqualFreeTypes(string fontFile)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var typeface = HintingVariableFixtures.Default(fontFile);
            var tables = TtVarTables.For(typeface.Face.Fontface);
            Assert.NotNull(tables);
            Assert.Equal(font.Axes, tables!.NumAxis);

            var problems = new List<string>();
            foreach (var location in font.Locations.Where(l => l.Design is not null))
            {
                var design = location.Design!.Select(v => (int)Math.Round(v * 65536)).ToArray();
                var actual = tables.Normalize(design);
                if (!actual.SequenceEqual(location.Ndv!))
                    problems.Add($"{location.Name}: design {string.Join(",", location.Design!)}: [{string.Join(",", actual)}], FreeType has [{string.Join(",", location.Ndv!)}]");

                // and the API's location gives the same vector
                var located = HintingVariableFixtures.TypefaceAt(fontFile, location);
                var viaApi = TtVarTables.NormalizedCoordinates(located.Face.Fontface, located.Face.Variation);
                if (!(viaApi ?? new int[tables.NumAxis]).SequenceEqual(location.Ndv!))
                    problems.Add($"{location.Name}: the API's location gives [{string.Join(",", viaApi ?? [])}], FreeType has [{string.Join(",", location.Ndv!)}]");
            }

            Assert.True(problems.Count == 0, $"{fontFile}: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(10)));
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void HintedOutlinesEqualFreeTypesExactly(string fontFile, string mode, int size26Dot6, int location)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var run = font.Modes[mode].Single(r => r.Size == size26Dot6 && r.Loc == location);
            var loc = font.Locations[location];

            var face = HintingVariableFixtures.Face(fontFile, loc);
            Assert.True(face is not null, $"{fontFile} at {loc.Name}: FreeType sets the location and the port refuses it");

            var (version, renderMode) = HintingGoldenData.ModeOf(mode);
            var size = TtSize.Create(face!, run.Size, version, renderMode);

            var problems = new List<string>();
            foreach (var (glyphText, expected) in run.Glyphs)
            {
                int glyph = int.Parse(glyphText);

                if (expected.Error != 0)
                {
                    try
                    {
                        TtGlyphLoader.Load(size, glyph);
                        problems.Add($"glyph {glyph}: FreeType fails with {expected.Error} but the port loaded it");
                    }
                    catch (HintingException)
                    {
                    }

                    continue;
                }

                TtHintedGlyph actual;
                try
                {
                    actual = TtGlyphLoader.Load(size, glyph);
                }
                catch (HintingException ex)
                {
                    problems.Add($"glyph {glyph}: the port fails ({ex.Message}) where FreeType loads it");
                    continue;
                }

                HintingGoldenData.Compare(problems, glyph, expected, actual);
                if (problems.Count > 12)
                    break;
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {run.Size / 64.0} ppem at {loc.Name}: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }

        [Theory]
        [MemberData(nameof(ApiRuns))]
        public void TheOutlinesOfTheApiEqualFreeTypes(string fontFile, string mode, int size26Dot6, int location)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var run = font.Modes[mode].Single(r => r.Size == size26Dot6 && r.Loc == location);
            var loc = font.Locations[location];

            var typeface = HintingVariableFixtures.TypefaceAt(fontFile, loc);
            var request = new OutlineRequest { PixelsPerEm = size26Dot6 / 64.0, GridFitting = mode == "monochrome" ? GridFitting.Monochrome : GridFitting.Standard };

            // the font's own word on which sizes want fitting, at this location (MVAR moves the ranges of its gasp table)
            int gridFit = loc.Gasp is { } gasp ? gasp[(int)((size26Dot6 + 32L) >> 6)] : 1;
            bool fitted = gridFit == -1 || (gridFit & 1) != 0;

            var problems = new List<string>();
            int compared = 0;
            foreach (var (glyphText, expected) in run.Glyphs)
            {
                var glyph = ushort.Parse(glyphText);
                bool found = typeface.TryGetOutline(glyph, request, out var outline);

                if (!fitted)
                {
                    if (found && outline.IsGridFitted)
                        problems.Add($"glyph {glyph}: the font's gasp ranges say no grid-fitting at this size and location, but the API fitted it");
                    continue;
                }

                if (expected.Error != 0)
                {
                    if (found && outline.IsGridFitted)
                        problems.Add($"glyph {glyph}: FreeType fails with {expected.Error} but the API fitted it");
                    continue;
                }

                if (expected.X.Length == 0)
                    continue; // no ink: there is no outline to compare

                // a program of the font can turn hinting off at a size (the outline is only scaled then); the reference cannot tell, and the engine-level comparison covers the points
                if (!found || !outline.IsGridFitted)
                    continue;

                compared++;
                CompareGlyph(problems, glyph, expected, outline);
                if (problems.Count > 12)
                    break;
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {size26Dot6 / 64.0} ppem at {loc.Name}: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }

        [Fact]
        public void TheApiFitsGlyphsAtTheLocationsOfTheReference()
        {
            // a check that the API comparison above compared something: it skips a glyph that is not fitted
            var typeface = HintingVariableFixtures.TypefaceAt("HintingVariable.ttf", Golden.Value.Fonts.Single(f => f.File == "HintingVariable.ttf").Locations[4]);
            int fitted = 0;
            for (ushort glyph = 1; glyph < 100; glyph++)
            {
                if (typeface.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 24, GridFitting = GridFitting.Standard }, out var outline) && outline.IsGridFitted)
                    fitted++;
            }

            Assert.True(fitted > 40, $"only {fitted} glyphs are fitted");
        }

        [Fact]
        public void TheRangesOfGaspAreMovedByMvarAsFreeTypeMovesThem()
        {
            var problems = new List<string>();
            foreach (var font in Golden.Value.Fonts.Where(f => f.Locations.Any(l => l.Gasp is not null)))
            {
                var typeface = HintingVariableFixtures.Default(font.File);
                var fontface = typeface.Face.Fontface;
                var gasp = TtGasp.TryRead(fontface);
                Assert.NotNull(gasp);

                foreach (var location in font.Locations.Where(l => l.Gasp is not null))
                {
                    var face = HintingVariableFixtures.Face(font.File, location);
                    var moved = gasp!.AtLocation(face!.Blend);
                    for (int ppem = 0; ppem < location.Gasp!.Length; ppem++)
                    {
                        if (moved.GetFlags(ppem) != location.Gasp[ppem])
                            problems.Add($"{font.File} {location.Name} at {ppem} ppem: {moved.GetFlags(ppem)}, FreeType has {location.Gasp[ppem]}");
                    }
                }
            }

            Assert.True(problems.Count == 0, $"{problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(10)));
        }

        /// <summary>
        /// Compares the outline of a TrueType glyph with FreeType's points: the outline's points where a segment starts and ends are the on-curve points of FreeType's, and the
        /// points between two of its control points (which a conic outline leaves out), as a set, in 1/128 of a pixel; and the advance.
        /// </summary>
        internal static void CompareGlyph(List<string> problems, ushort glyph, GlyphGolden expected, GlyphOutline outline)
        {
            long Half(double value) => (long)Math.Round(value * 128);

            if (outline.Contours.Count != expected.Ends.Length)
            {
                problems.Add($"glyph {glyph}: {outline.Contours.Count} contours, FreeType has {expected.Ends.Length}");
                return;
            }

            int first = 0;
            for (int c = 0; c < expected.Ends.Length; c++)
            {
                int end = expected.Ends[c];
                int count = end + 1 - first;

                var wanted = new SortedSet<(long, long)>();
                for (int i = 0; i < count; i++)
                {
                    int a = first + i, b = first + (i + 1) % count;
                    if (expected.Tags[a] == 1)
                        wanted.Add((expected.X[a] * 2L, expected.Y[a] * 2L));
                    else if (expected.Tags[b] == 0)
                        wanted.Add((expected.X[a] + (long)expected.X[b], expected.Y[a] + (long)expected.Y[b]));
                }

                var actual = new SortedSet<(long, long)> { (Half(outline.Contours[c].Start.X), Half(outline.Contours[c].Start.Y)) };
                foreach (var segment in outline.Contours[c].Segments)
                    actual.Add((Half(segment.End.X), Half(segment.End.Y)));

                if (!wanted.SetEquals(actual))
                {
                    problems.Add($"glyph {glyph} contour {c}: the outline's end points differ from FreeType's: {string.Join(" ", actual.Except(wanted).Take(3))} against {string.Join(" ", wanted.Except(actual).Take(3))} (in 1/128 px)");
                    return;
                }

                first = end + 1;
            }

            if (outline.GridFittedAdvance is not { } advance || (long)Math.Round(advance * 64) != expected.Advance)
                problems.Add($"glyph {glyph}: advance {outline.GridFittedAdvance}, FreeType has {expected.Advance / 64.0}");
        }
    }

    internal sealed class VariableGoldenFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("fonts")]
        public List<VariableFontGolden> Fonts { get; set; } = [];
    }

    internal sealed class VariableFontGolden
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        /// <summary>The number of axes FreeType sees in the font.</summary>
        [JsonPropertyName("axes")]
        public int Axes { get; set; }

        [JsonPropertyName("locations")]
        public List<VariableLocationGolden> Locations { get; set; } = [];

        [JsonPropertyName("modes")]
        public Dictionary<string, List<VariableRunGolden>> Modes { get; set; } = [];
    }

    internal sealed class VariableLocationGolden
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>The design coordinates FreeType was given, or null for raw normalized coordinates.</summary>
        [JsonPropertyName("design")]
        public double[]? Design { get; set; }

        [JsonPropertyName("blend")]
        public int[]? Blend { get; set; }

        /// <summary>FreeType's error from setting the location.</summary>
        [JsonPropertyName("set")]
        public int Set { get; set; }

        /// <summary>The normalized vector FreeType kept, in 16.16, one for each axis.</summary>
        [JsonPropertyName("ndv")]
        public int[]? Ndv { get; set; }

        /// <summary><c>FT_Get_Gasp</c> for the ppems 0 to 40 (-1: no table).</summary>
        [JsonPropertyName("gasp")]
        public int[]? Gasp { get; set; }
    }

    internal sealed class VariableRunGolden
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("loc")]
        public int Loc { get; set; }

        [JsonPropertyName("glyphs")]
        public Dictionary<string, GlyphGolden> Glyphs { get; set; } = [];
    }

    /// <summary>The variable TrueType fonts as the hinting engine reads them, at the locations of the reference.</summary>
    internal static class HintingVariableFixtures
    {
        private static readonly ConcurrentDictionary<(string File, string Location), TtFace?> Faces = new();
        private static readonly ConcurrentDictionary<string, Typeface> Typefaces = new();

        public static Typeface Default(string fileName) =>
            Typefaces.GetOrAdd(fileName, name => TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, name)));

        /// <summary>The typeface at the design coordinates of a location (the font as it opens for the one that has none).</summary>
        public static Typeface TypefaceAt(string fileName, VariableLocationGolden location)
        {
            var typeface = Default(fileName);
            return location.Design is null ? typeface : typeface.WithAxes(typeface.Axes.Select((axis, i) => new AxisSetting(axis.Tag, location.Design[i])));
        }

        /// <summary>
        /// Whether the API can be at a location: it has design coordinates, or it is the face nothing is set on. A face that was set to the defaults has a blend that varies
        /// nothing, which the API's default typeface does not have.
        /// </summary>
        public static bool IsReachableByTheApi(VariableLocationGolden location) =>
            (location.Design is null && location.Blend is null) || (location.Design is not null && !location.Ndv!.All(v => v == 0));

        /// <summary>The face the engine has at a location: the normalized vector FreeType kept, or null when the port refuses it.</summary>
        public static TtFace? Face(string fileName, VariableLocationGolden location) => Faces.GetOrAdd((fileName, location.Name), _ =>
        {
            var typeface = Default(fileName);
            return TtFace.TryCreate(typeface.Face.Fontface, typeface.Face.FamilyName, location.Ndv);
        });
    }
}
