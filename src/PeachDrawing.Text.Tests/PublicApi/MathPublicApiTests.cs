using PeachDrawing.Text;
using PeachDrawing.Text.OpenType;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.PublicApi
{
    /// <summary>
    /// The <c>MATH</c> table as a consumer outside the assembly reads it, through <see cref="Typeface.MathData"/>. The expected
    /// values were read from the bundled STIX Two Math font with fontTools, so they do not come from the reader under test.
    /// </summary>
    public class MathPublicApiTests
    {
        private static Typeface Face(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Math-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        [Fact]
        public void MathData_IsPresentOnlyOnAFontMadeForMathematics()
        {
            var math = Face(BundledFonts.Math);
            Assert.True(math.HasMathData);
            Assert.NotNull(math.MathData);

            var plain = Face(BundledFonts.Ttf);
            Assert.False(plain.HasMathData);
            Assert.Null(plain.MathData);
        }

        [Fact]
        public void Constants_AreTheFontsOwnValues()
        {
            var constants = Face(BundledFonts.Math).MathData!.Constants;

            Assert.Equal(70, constants.ScriptPercentScaleDown);
            Assert.Equal(55, constants.ScriptScriptPercentScaleDown);
            Assert.Equal(258, constants.AxisHeight);
            Assert.Equal(68, constants.FractionRuleThickness);
            Assert.Equal(-335, constants.RadicalKernAfterDegree);
        }

        [Fact]
        public void GlyphInfo_AnswersForGlyphsTheFontCoversAndZeroForOthers()
        {
            var math = Face(BundledFonts.Math);
            var info = math.MathData!.GlyphInfo;

            // The italics correction of a glyph the font has no entry for is 0, never an exception.
            Assert.Equal(0, info.GetItalicsCorrection(ushort.MaxValue));
            Assert.False(info.IsExtendedShape(ushort.MaxValue));
            Assert.Null(info.GetTopAccentAttachment(ushort.MaxValue));

            // At least one of the Latin letters has an italics correction: the math italic capitals are covered.
            var found = false;
            for (var cp = 0x1D434; cp <= 0x1D44D && !found; cp++)
            {
                if (math.TryMapRune(new Rune(cp), out var glyph) && info.GetItalicsCorrection(glyph) != 0) found = true;
            }
            Assert.True(found);
        }

        [Fact]
        public void Variants_GiveTheStretchyGlyphsTheirSizesAndAssemblies()
        {
            var math = Face(BundledFonts.Math);
            var variants = math.MathData!.Variants;
            Assert.True(variants.MinConnectorOverlap > 0);

            Assert.True(math.TryMapRune(new Rune('('), out var paren));
            var vertical = variants.GetVerticalConstruction(paren);
            Assert.NotNull(vertical);
            Assert.NotEmpty(vertical.Variants);

            // Variants run from the smallest to the largest.
            for (var i = 1; i < vertical.Variants.Count; i++)
            {
                Assert.True(vertical.Variants[i].AdvanceMeasurement >= vertical.Variants[i - 1].AdvanceMeasurement);
            }

            // A parenthesis has no horizontal construction, and a glyph outside the table has neither.
            Assert.Null(variants.GetHorizontalConstruction(paren));
            Assert.Null(variants.GetVerticalConstruction(ushort.MaxValue));

            // Some vertical construction in the font carries an assembly of parts.
            MathGlyphAssembly? assembly = null;
            for (ushort glyph = 0; glyph < 6000 && assembly is null; glyph++)
            {
                assembly = variants.GetVerticalConstruction(glyph)?.Assembly;
            }
            Assert.NotNull(assembly);
            Assert.NotEmpty(assembly.Parts);
            Assert.Contains(assembly.Parts, part => part.IsExtender);
            Assert.All(assembly.Parts, part => Assert.True(part.FullAdvance > 0));
        }

        [Fact]
        public void GlyphInfo_ReportsTheGlyphsTheFontCoversInEachTable()
        {
            var info = Face(BundledFonts.Math).MathData!.GlyphInfo;

            ushort? withAccent = null, extended = null;
            for (ushort glyph = 0; glyph < 6000; glyph++)
            {
                if (withAccent is null && info.GetTopAccentAttachment(glyph) is not null) withAccent = glyph;
                if (extended is null && info.IsExtendedShape(glyph)) extended = glyph;
            }

            Assert.NotNull(withAccent);
            Assert.NotNull(extended);
            Assert.True(info.GetTopAccentAttachment(withAccent.Value) > 0);
        }

        [Fact]
        public void Variants_GrowHorizontallyToo_AndKeepTheirAssemblyOrder()
        {
            var variants = Face(BundledFonts.Math).MathData!.Variants;

            MathGlyphConstruction? horizontal = null;
            MathGlyphAssembly? assembly = null;
            for (ushort glyph = 0; glyph < 6000 && (horizontal is null || assembly is null); glyph++)
            {
                horizontal ??= variants.GetHorizontalConstruction(glyph);
                assembly ??= variants.GetHorizontalConstruction(glyph)?.Assembly ?? variants.GetVerticalConstruction(glyph)?.Assembly;
            }

            Assert.NotNull(horizontal);
            Assert.NotEmpty(horizontal.Variants);
            Assert.NotNull(assembly);
            Assert.True(assembly.ItalicsCorrection >= 0);
            Assert.True(assembly.Parts.Count >= 2);
        }

        [Fact]
        public void TheReadOnlyListsCannotBeWrittenThroughACast()
        {
            var variants = Face(BundledFonts.Math).MathData!.Variants;
            var construction = variants.GetVerticalConstruction(GlyphOf('('));
            Assert.NotNull(construction);

            Assert.False(construction.Variants is MathGlyphVariant[]);
            Assert.True(((System.Collections.IList)construction.Variants).IsReadOnly);

            MathGlyphAssembly? assembly = null;
            for (ushort glyph = 0; glyph < 6000 && assembly is null; glyph++)
            {
                assembly = variants.GetVerticalConstruction(glyph)?.Assembly;
            }

            Assert.NotNull(assembly);
            Assert.False(assembly.Parts is MathGlyphPart[]);
            Assert.True(((System.Collections.IList)assembly.Parts).IsReadOnly);
        }

        private static ushort GlyphOf(char c)
        {
            Assert.True(Face(BundledFonts.Math).TryMapRune(new Rune(c), out var glyph));
            return glyph;
        }

        [Fact]
        public void AnAssembly_CanBeDescribedByACaller()
        {
            var parts = new[]
            {
                new MathGlyphPart(7, 0, 100, 500, IsExtender: false),
                new MathGlyphPart(8, 100, 100, 250, IsExtender: true),
            };

            var assembly = new MathGlyphAssembly(12.5, parts);

            Assert.Equal(12.5, assembly.ItalicsCorrection);
            Assert.Equal(parts, assembly.Parts);
            Assert.Throws<ArgumentNullException>(() => new MathGlyphAssembly(0, null!));
        }

        [Fact]
        public void MathData_IsOneTableForAsLongAsTheFaceLives()
        {
            var math = Face(BundledFonts.Math);
            Assert.Same(math.MathData, math.MathData);
        }
    }
}
