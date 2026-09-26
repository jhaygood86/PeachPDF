using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Tests.Hinting;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The port of FreeType's TrueType interpreter against FreeType itself: for a grid of fonts, sizes, glyphs and modes, the
    /// points the port hints must equal the points FreeType 2.14.3 hints, exactly, in 26.6 units. The reference is
    /// <c>HintingGolden.json.gz</c>, made by <c>assets/fonts/generate_hinting_golden.py</c>. A mismatch is a bug in the port.
    /// </summary>
    public class HintingGoldenTests
    {
        private static readonly Lazy<GoldenFile> Golden = new(() =>
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "HintingGolden.json.gz"));
            using var zip = new GZipStream(stream, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<GoldenFile>(zip)!;
        });

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
        public void TheGoldenDataWasMadeByTheFreeTypeTheInterpreterWasPortedFrom()
        {
            Assert.Equal("2.14.3", Golden.Value.FreeType.Version);
            Assert.Equal("VER-2-14-3", Golden.Value.FreeType.Tag);
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void HintedOutlinesEqualFreeTypesExactly(string fontFile, string mode, int size26Dot6)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var run = font.Modes[mode].Single(r => r.Size == size26Dot6);

            var (version, renderMode) = mode switch
            {
                "standard" => (TtInterpreterVersion.V40, TtRenderMode.Normal),
                "monochrome" => (TtInterpreterVersion.V35, TtRenderMode.Mono),
                "v40mono" => (TtInterpreterVersion.V40, TtRenderMode.Mono),
                "v35normal" => (TtInterpreterVersion.V35, TtRenderMode.Normal),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };

            var face = HintingFixtures.Face(fontFile);
            var size = TtSize.Create(face, size26Dot6, version, renderMode);

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

                var actual = TtGlyphLoader.Load(size, glyph);
                if (actual.ProgramError != 0)
                    problems.Add($"glyph {glyph}: the glyph program stopped with error {actual.ProgramError}");
                Compare(problems, glyph, expected, actual);
                if (problems.Count > 12)
                    break;
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {size26Dot6 / 64.0} ppem: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }

        private static void Compare(List<string> problems, int glyph, GlyphGolden expected, TtHintedGlyph actual)
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

                if ((actual.Tags[i] & 1) != expected.Tags[i])
                {
                    problems.Add($"glyph {glyph} point {i}: on-curve {actual.Tags[i] & 1}, FreeType has {expected.Tags[i]}");
                    break;
                }
            }
        }

        private sealed class GoldenFile
        {
            [JsonPropertyName("freetype")]
            public FreeTypeInfo FreeType { get; set; } = new();

            [JsonPropertyName("fonts")]
            public List<FontGolden> Fonts { get; set; } = [];
        }

        private sealed class FreeTypeInfo
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = "";

            [JsonPropertyName("tag")]
            public string Tag { get; set; } = "";
        }

        private sealed class FontGolden
        {
            [JsonPropertyName("file")]
            public string File { get; set; } = "";

            [JsonPropertyName("modes")]
            public Dictionary<string, List<RunGolden>> Modes { get; set; } = [];
        }

        private sealed class RunGolden
        {
            [JsonPropertyName("size")]
            public int Size { get; set; }

            [JsonPropertyName("glyphs")]
            public Dictionary<string, GlyphGolden> Glyphs { get; set; } = [];
        }

        private sealed class GlyphGolden
        {
            [JsonPropertyName("a")]
            public int Advance { get; set; }

            [JsonPropertyName("e")]
            public int[] Ends { get; set; } = [];

            [JsonPropertyName("x")]
            public int[] X { get; set; } = [];

            [JsonPropertyName("y")]
            public int[] Y { get; set; } = [];

            [JsonPropertyName("t")]
            public int[] Tags { get; set; } = [];

            [JsonPropertyName("err")]
            public int Error { get; set; }
        }
    }

    /// <summary>The bundled fonts as the hinting engine reads them.</summary>
    internal static class HintingFixtures
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TtFace> Faces = new();

        public static TtFace Face(string fileName) => Faces.GetOrAdd(fileName, name =>
        {
            var typeface = PeachPDF.Tests.TestSupport.TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, name));
            return TtFace.TryCreate(typeface.Face.Fontface, typeface.Face.FamilyName, null, null)
                ?? throw new InvalidOperationException(name + " has no TrueType outlines to hint.");
        });
    }
}
