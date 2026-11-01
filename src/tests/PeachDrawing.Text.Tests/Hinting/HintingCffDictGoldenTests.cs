using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The DICTs of the CFF table against FreeType: fonts whose Top DICT or Private DICT says something deliberate (an operator without its
    /// operands, a stack that is full, a FontBBox of three numbers, a MultipleMaster operator with an impossible number of designs), made by
    /// <c>assets/fonts/generate_hinting_cff_fixtures.py</c>, which records whether FreeType 2.14.3 opens each. A font FreeType refuses the
    /// port must refuse, and a font FreeType opens the port must open. A mismatch is a bug in the port.
    /// </summary>
    public class HintingCffDictGoldenTests
    {
        private static readonly Lazy<DictGoldenFile> Golden = new(() => HintingGoldenData.Load<DictGoldenFile>("HintingCffDicts.golden.json.gz"));

        public static TheoryData<string> Variants()
        {
            var data = new TheoryData<string>();
            foreach (var font in Golden.Value.Fonts)
                data.Add(font.Name);
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
            // both answers occur, and the fonts FreeType refuses are refused for the DICT alone: the base font is opened
            Assert.True(Golden.Value.Fonts.Count(f => f.Error == 0) > 20);
            Assert.True(Golden.Value.Fonts.Count(f => f.Error != 0) > 20);
            Assert.Equal(0, Golden.Value.Fonts.Single(f => f.Name == "valid").Error);
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void AFontIsOpenedOnlyWhereFreeTypeOpensIt(string name)
        {
            var expected = Golden.Value.Fonts.Single(f => f.Name == name);
            var face = CffFace.TryCreate(FontFileData.GetOrCreateFrom(Convert.FromBase64String(expected.Font)).Fontface, _ => 500);

            if (expected.Error != 0)
            {
                Assert.True(face is null, $"{name}: FreeType refuses the font (error {expected.Error}) but the port opened it");
                return;
            }

            Assert.True(face is not null, $"{name}: FreeType opens the font but the port refused it");

            // and the glyph FreeType loads the port loads
            Assert.Equal(0, expected.Glyph);
            Assert.True(CffGlyphLoader.Load(new CffSize(face, 16 * 64), 1).NPoints > 0);
        }
    }

    internal sealed class DictGoldenFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("fonts")]
        public List<DictGoldenFont> Fonts { get; set; } = [];
    }

    internal sealed class DictGoldenFont
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("font")]
        public string Font { get; set; } = "";

        [JsonPropertyName("error")]
        public int Error { get; set; }

        [JsonPropertyName("glyph")]
        public int Glyph { get; set; }
    }
}
