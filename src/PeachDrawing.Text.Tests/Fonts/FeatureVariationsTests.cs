using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The <c>FeatureVariations</c> of <c>GSUB</c>: at a region of a variable font's design space a feature uses other lookups, which here swap
    /// A for A.heavy from weight 600 and B for B.heavy from weight 800.
    /// </summary>
    public class FeatureVariationsTests
    {
        private const int A = 1, B = 2, AHeavy = 3, BHeavy = 4;

        private static Typeface Load(byte[]? damaged = null)
        {
            var set = new FontSet();
            var options = new AddOptions { FamilyName = "Feature-" + Guid.NewGuid().ToString("N") };
            var family = damaged is null ? set.AddFile(BundledFonts.VariableFeatureTest, options) : set.AddData(damaged, options);
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static readonly Typeface Face = Load();

        private static int[] Glyphs(Typeface face, string text) =>
            Shaper.Shape(face, text, ShapeSettings.Default).Glyphs.Select(g => g.GlyphIndex).ToArray();

        [Theory]
        [InlineData(400, A, B)]        // the default: no record applies
        [InlineData(100, A, B)]
        [InlineData(500, A, B)]        // below the first region
        [InlineData(650, AHeavy, B)]   // the wide region: A only
        [InlineData(799, AHeavy, B)]
        [InlineData(850, AHeavy, BHeavy)]   // the narrow region, which is first: both
        [InlineData(900, AHeavy, BHeavy)]
        public void AFeatureSubstitution_AppliesAtTheRegionsOfTheDesignSpace(int weight, int expectedA, int expectedB)
        {
            var instance = Face.WithAxes([new AxisSetting("wght", weight)]);

            Assert.Equal([expectedA, expectedB], Glyphs(instance, "AB"));
        }

        [Fact]
        public void TheAdvancesFollowTheSubstitutedGlyphs()
        {
            var instance = Face.WithAxes([new AxisSetting("wght", 900)]);
            var run = Shaper.Shape(instance, "AB", ShapeSettings.Default);

            Assert.Equal(700, instance.GetAdvance((ushort)run.Glyphs[0].GlyphIndex));
            Assert.Equal(900, instance.GetAdvance((ushort)run.Glyphs[1].GlyphIndex));
            Assert.Equal(1600, run.Advance);
        }

        [Fact]
        public void TheDefaultTypeface_IsNotAffected_AndInstancesDoNotShareAnswers()
        {
            var heavy = Face.WithAxes([new AxisSetting("wght", 900)]);
            var light = Face.WithAxes([new AxisSetting("wght", 200)]);

            // Shaped in an order that would expose a cache keyed by the table alone.
            Assert.Equal([AHeavy, BHeavy], Glyphs(heavy, "AB"));
            Assert.Equal([A, B], Glyphs(light, "AB"));
            Assert.Equal([A, B], Glyphs(Face, "AB"));
            Assert.Equal([AHeavy, BHeavy], Glyphs(heavy, "AB"));
        }

        [Fact]
        public void ManyLocations_KeepWorking_AfterTheViewCacheIsDropped()
        {
            for (int weight = 100; weight <= 900; weight += 8)
            {
                var instance = Face.WithAxes([new AxisSetting("wght", weight)]);
                int expected = weight >= 601 ? AHeavy : A;
                Assert.Equal(expected, Glyphs(instance, "A")[0]);
            }
        }

        [Fact]
        public void AFontWithADamagedFeatureVariationsTable_ShapesAtItsDefaults()
        {
            var bytes = File.ReadAllBytes(BundledFonts.VariableFeatureTest);
            int count = (bytes[4] << 8) | bytes[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (Encoding.ASCII.GetString(bytes, record, 4) != "GSUB")
                    continue;

                int offset = (bytes[record + 8] << 24) | (bytes[record + 9] << 16) | (bytes[record + 10] << 8) | bytes[record + 11];
                // featureVariationsOffset (after version and the three list offsets), pointed far past the table.
                bytes[offset + 10] = 0x7F;
            }

            var instance = Load(bytes).WithAxes([new AxisSetting("wght", 900)]);

            Assert.Equal([A, B], Glyphs(instance, "AB"));
        }

        [Fact]
        public void AnyOneDamagedByteInTheGsub_NeverMakesShapingThrow()
        {
            var original = File.ReadAllBytes(BundledFonts.VariableFeatureTest);
            int count = (original[4] << 8) | original[5];
            int gsubOffset = 0, gsubLength = 0;
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (Encoding.ASCII.GetString(original, record, 4) == "GSUB")
                {
                    gsubOffset = (original[record + 8] << 24) | (original[record + 9] << 16) | (original[record + 10] << 8) | original[record + 11];
                    gsubLength = (original[record + 12] << 24) | (original[record + 13] << 16) | (original[record + 14] << 8) | original[record + 15];
                }
            }

            for (int at = gsubOffset; at < gsubOffset + gsubLength; at++)
            {
                foreach (byte value in new byte[] { 0x00, 0xFF, 0x80 })
                {
                    var font = (byte[])original.Clone();
                    font[at] = value;
                    Typeface loaded;
                    try
                    {
                        loaded = Load(font);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;       // a font too damaged to parse is refused when it is matched, which is allowed
                    }

                    Shaper.Shape(loaded.WithAxes([new AxisSetting("wght", 700)]), "AB", ShapeSettings.Default);
                }
            }
        }

        // ---- the table reader on its own -------------------------------------------------------------------------------------------

        private static byte[] Table(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        private static byte[] U16(params int[] values) => values.SelectMany(v => new[] { (byte)(v >> 8), (byte)v }).ToArray();

        private static byte[] U32(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

        [Fact]
        public void ARecordWithNoConditionSet_AppliesEverywhere_AndANonFormat1ConditionNever()
        {
            // Two records: the first has an unknown-format condition (never met), the second has no condition set (always).
            var conditionSet = Table(U16(1), U32(6), U16(9, 0, 0, 0));     // one condition, at offset 6, format 9
            var substitution = Table(U16(1, 0), U16(1), U16(0), U32(12), U16(0, 1), U16(7));   // feature 0 -> lookups [7]
            int header = 8 + 2 * 8;
            var body = Table(conditionSet, substitution);
            var table = Table(U16(1, 0), U32(2),
                U32((uint)header), U32((uint)(header + conditionSet.Length)),
                U32(0), U32((uint)(header + conditionSet.Length)),
                body);

            var parsed = FeatureVariationsTable.TryParse(table, 0);

            Assert.NotNull(parsed);
            Assert.Equal([7], parsed!.SubstitutionsAt([0.5])![0]);
        }

        [Fact]
        public void MalformedTables_AreRejected()
        {
            Assert.Null(FeatureVariationsTable.TryParse(Table(U16(2, 0), U32(1)), 0));
            Assert.Null(FeatureVariationsTable.TryParse(Table(U16(1, 0), U32(0)), 0));
            Assert.Null(FeatureVariationsTable.TryParse(Table(U16(1, 0), U32(0x7FFFFFFF)), 0));
            Assert.Null(FeatureVariationsTable.TryParse([1, 0], 0));
        }
    }
}
