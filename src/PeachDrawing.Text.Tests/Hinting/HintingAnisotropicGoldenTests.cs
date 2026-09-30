using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The non-square-pixel paths of the TrueType interpreter port (<c>Current_Ratio</c>, the stretched <c>cvt</c>/ppem routines, the
    /// non-square branches of <c>MD</c>/<c>MDRP</c>/<c>IP</c>) and of Adobe's CFF engine port (independent <c>x_scale</c>/<c>y_scale</c>)
    /// against FreeType itself, at sizes where <c>x_ppem</c> and <c>y_ppem</c> differ: the hinted points must equal FreeType 2.14.3's,
    /// exactly, in 26.6 units. The reference is <c>HintingAnisotropic.golden.json.gz</c>, made by
    /// <c>assets/fonts/generate_hinting_anisotropic_fixtures.py</c>. One size of each font is square, recorded through the same call
    /// shape, which is this file's regression guard that the equal-axis path is unaffected.
    /// </summary>
    public class HintingAnisotropicGoldenTests
    {
        private static readonly Lazy<AnisotropicGoldenFile> Golden =
            new(() => HintingGoldenData.Load<AnisotropicGoldenFile>("HintingAnisotropic.golden.json.gz"));

        public static TheoryData<string, string, int, int> Runs()
        {
            var data = new TheoryData<string, string, int, int>();
            foreach (var font in Golden.Value.Fonts)
                foreach (var (mode, runs) in font.Modes)
                    foreach (var run in runs)
                        data.Add(font.File, mode, run.SizeX, run.SizeY);
            return data;
        }

        [Fact]
        public void TheGoldenDataWasMadeByTheFreeTypeThePortsWereMadeFrom()
        {
            Assert.Equal("2.14.3", Golden.Value.FreeType.Version);
            Assert.Equal("VER-2-14-3", Golden.Value.FreeType.Tag);
        }

        [Fact]
        public void TheReferenceIsNotVacuous()
        {
            int glyphs = Golden.Value.Fonts.Sum(f => f.Modes.Values.Sum(runs => runs.Sum(r => r.Glyphs.Count)));
            Assert.True(glyphs > 1000, $"only {glyphs} glyph loads in the reference");
        }

        [Fact]
        public void ASizeReportsBothOfItsAxesIndependently()
        {
            var ttFace = HintingFixtures.Face("HintingOpcodes.ttf");
            var ttSize = TtSize.Create(ttFace, 12 * 64, 20 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
            Assert.Equal(12 * 64, ttSize.XPpem26Dot6);
            Assert.Equal(20 * 64, ttSize.YPpem26Dot6);
            Assert.Equal(ttSize.Metrics.Scale, ttSize.Scale);

            var cffFace = HintingCffFixtures.Face("HintingCff.otf");
            var cffSize = new CffSize(cffFace, 12 * 64, 20 * 64);
            Assert.Equal(12 * 64, cffSize.XPpem26Dot6);
            Assert.Equal(20 * 64, cffSize.YPpem26Dot6);
            Assert.NotEqual(cffSize.XScale, cffSize.YScale);
        }

        [Fact]
        public void TheReferenceHasBothSquareAndNonSquareSizes()
        {
            // the regression guard (a square size run) has to actually be there for this file to prove anything about it
            bool square = false, nonSquare = false;
            foreach (var font in Golden.Value.Fonts)
                foreach (var run in font.Modes.Values.SelectMany(r => r))
                {
                    if (run.SizeX == run.SizeY)
                        square = true;
                    else
                        nonSquare = true;
                }

            Assert.True(square, "no square-pixel run in the reference");
            Assert.True(nonSquare, "no non-square-pixel run in the reference");
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void HintedOutlinesEqualFreeTypesExactly(string fontFile, string mode, int xPpem26Dot6, int yPpem26Dot6)
        {
            var font = Golden.Value.Fonts.Single(f => f.File == fontFile);
            var run = font.Modes[mode].Single(r => r.SizeX == xPpem26Dot6 && r.SizeY == yPpem26Dot6);

            var problems = new List<string>();

            if (font.Kind == "cff")
            {
                var face = HintingCffFixtures.Face(fontFile);
                var size = new CffSize(face, xPpem26Dot6, yPpem26Dot6);

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
            }
            else
            {
                var (version, renderMode) = HintingGoldenData.ModeOf(mode);
                var face = HintingFixtures.Face(fontFile);
                var size = TtSize.Create(face, xPpem26Dot6, yPpem26Dot6, version, renderMode);

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
                    HintingGoldenData.Compare(problems, glyph, expected, actual);
                    if (problems.Count > 12)
                        break;
                }
            }

            Assert.True(problems.Count == 0,
                $"{fontFile} {mode} {xPpem26Dot6 / 64.0}x{yPpem26Dot6 / 64.0} ppem: {problems.Count} mismatch(es)\n" + string.Join("\n", problems.Take(12)));
        }
    }

    internal sealed class AnisotropicGoldenFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("fonts")]
        public List<AnisotropicFontGolden> Fonts { get; set; } = [];
    }

    internal sealed class AnisotropicFontGolden
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        [JsonPropertyName("modes")]
        public Dictionary<string, List<AnisotropicRunGolden>> Modes { get; set; } = [];
    }

    internal sealed class AnisotropicRunGolden
    {
        [JsonPropertyName("sizeX")]
        public int SizeX { get; set; }

        [JsonPropertyName("sizeY")]
        public int SizeY { get; set; }

        [JsonPropertyName("glyphs")]
        public Dictionary<string, GlyphGolden> Glyphs { get; set; } = [];
    }
}
