using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;
using System.Text;
using System.Text.Json;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Shaping an instance of a variable font with the deltas of its layout tables: the <c>VariationIndex</c> device tables of <c>GPOS</c>
    /// value records and anchors, resolved against the item variation store in <c>GDEF</c>. The expected numbers are what fontTools'
    /// instancer leaves in the <c>GPOS</c> of the instance (<c>VariableLayoutTest.golden.json</c>).
    /// </summary>
    public class VariableLayoutTests
    {
        private static readonly Typeface Face = Load();

        private static Typeface Load()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.VariableLayoutTest, new AddOptions { FamilyName = "Layout-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static JsonElement Golden() => JsonDocument.Parse(File.ReadAllText(BundledFonts.VariableLayoutTestGolden)).RootElement;

        public static TheoryData<int> Weights => new(100, 250, 400, 500, 650, 900);

        private static Typeface At(int weight) => Face.WithAxes([new AxisSetting("wght", weight)]);

        private static IReadOnlyList<PlacedGlyph> Shape(Typeface face, string text) => Shaper.Shape(face, text, ShapeSettings.Default).Glyphs;

        [Fact]
        public void TheFontIsVariable_AndItsGposCarriesVariationData()
        {
            Assert.True(Face.IsVariable);
        }

        [Theory]
        [MemberData(nameof(Weights))]
        public void Kerning_FollowsTheLocation(int weight)
        {
            var expected = Golden().GetProperty(weight.ToString());
            var face = At(weight);

            Assert.Equal(expected.GetProperty("kernAV").GetInt32(), Shape(face, "AV")[0].XAdvanceDelta, 1);
            Assert.Equal(expected.GetProperty("kernVA").GetInt32(), Shape(face, "VA")[0].XAdvanceDelta, 1);
        }

        [Theory]
        [MemberData(nameof(Weights))]
        public void ASingleAdjustment_FollowsTheLocation(int weight)
        {
            var expected = Golden().GetProperty(weight.ToString());
            var glyph = Shape(At(weight), "W")[0];

            Assert.Equal(expected.GetProperty("wPlacement").GetInt32(), glyph.XOffset, 1);
            Assert.Equal(expected.GetProperty("wAdvance").GetInt32(), glyph.XAdvanceDelta, 1);
        }

        [Theory]
        [MemberData(nameof(Weights))]
        public void AMarkAnchor_FollowsTheLocation(int weight)
        {
            var expected = Golden().GetProperty(weight.ToString());
            var face = At(weight);
            var glyphs = Shape(face, "A´");

            // The mark is placed by the base anchor minus its own (0, 600), less the base's advance.
            int advance = face.GetAdvance(glyphs[0].GlyphIndex is int a ? (ushort)a : (ushort)0);
            Assert.Equal(expected.GetProperty("anchorX").GetInt32() - advance, glyphs[1].XOffset, 1);
            Assert.Equal(expected.GetProperty("anchorY").GetInt32() - 600, glyphs[1].YOffset, 1);
        }

        [Fact]
        public void TheDefaultInstance_HasTheDefaultMastersValues()
        {
            Assert.Equal(-50, Shape(Face, "AV")[0].XAdvanceDelta);
            Assert.Equal(-80, Shape(Face, "VA")[0].XAdvanceDelta);
            Assert.Equal(30, Shape(Face, "W")[0].XAdvanceDelta);
        }

        [Fact]
        public void AnInstanceAtTheDefaultLocation_IsTheDefaultTypeface_WithTheSameShaping()
        {
            Assert.Equal(-50, Shape(At(400), "AV")[0].XAdvanceDelta);
        }

        [Fact]
        public void TwoInstances_ShapeDifferently_AndTheShapedRunKeepsItsLocation()
        {
            var thin = Shape(At(100), "AV")[0].XAdvanceDelta;
            var black = Shape(At(900), "AV")[0].XAdvanceDelta;

            Assert.NotEqual(thin, black);
            Assert.Equal(-20, thin, 1);
            Assert.Equal(-110, black, 1);
        }

        [Fact]
        public void AFontWithADamagedVariationStore_StillShapesAtItsDefaults()
        {
            var bytes = File.ReadAllBytes(BundledFonts.VariableLayoutTest);
            int count = (bytes[4] << 8) | bytes[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (Encoding.ASCII.GetString(bytes, record, 4) != "GDEF")
                    continue;

                int offset = (bytes[record + 8] << 24) | (bytes[record + 9] << 16) | (bytes[record + 10] << 8) | bytes[record + 11];
                // The item variation store offset of a GDEF 1.3 header, pointed past the table.
                bytes[offset + 14] = 0x7F;
            }

            var set = new FontSet();
            var family = set.AddData(bytes, new AddOptions { FamilyName = "Damaged-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            var instance = match.Typeface.WithAxes([new AxisSetting("wght", 900)]);

            // The deltas are gone, so the default master's numbers apply; nothing throws.
            Assert.Equal(-50, Shape(instance, "AV")[0].XAdvanceDelta);
        }
    }
}
