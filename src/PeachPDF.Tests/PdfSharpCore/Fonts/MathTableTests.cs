using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Parser coverage for the OpenType <c>MATH</c> table, against the real, unmodified <c>MATH</c>
    /// table of the bundled STIX Two Math font (see <see cref="BundledFonts.Math"/>). Expected values
    /// were extracted independently with fontTools (<c>TTFont('StixTwoMath-Regular.ttf')['MATH']</c>)
    /// rather than derived from this reader itself, so a self-consistent-but-wrong parse can't pass.
    /// </summary>
    public class MathTableTests
    {
        // parenleft's own cmap-mapped glyph (U+0028) - distinct from the *variant* glyph id the
        // MathVariants table uses for its "no stretch needed" entry (933), which is a different glyph
        // in this font's glyph set (an unhinted/differently-organized duplicate, not a naming quirk).
        const ushort ParenLeftGlyphId = 1064;

        static OpenTypeFontface Face(string path)
            => XFontSource.GetOrCreateFrom(File.ReadAllBytes(path)).Fontface;

        [Fact]
        public void HasMathTable_TrueForMathFont_FalseForOrdinaryFont()
        {
            Assert.True(Face(BundledFonts.Math).math?.Table is not null);
            Assert.Null(Face(BundledFonts.Ttf).math);
        }

        [Fact]
        public void Descriptor_ExposesHasMathTableAndMathTable()
        {
            var mathDescriptor = new OpenTypeDescriptor("math-test", "math-test", XFontStyle.Regular,
                Face(BundledFonts.Math), new XPdfFontOptions(PdfFontEncoding.Unicode));
            Assert.True(mathDescriptor.HasMathTable);
            Assert.NotNull(mathDescriptor.MathTable);

            var plainDescriptor = new OpenTypeDescriptor("plain-test", "plain-test", XFontStyle.Regular,
                Face(BundledFonts.Ttf), new XPdfFontOptions(PdfFontEncoding.Unicode));
            Assert.False(plainDescriptor.HasMathTable);
            Assert.Null(plainDescriptor.MathTable);
        }

        [Fact]
        public void MathConstants_MatchesKnownStixTwoMathValues()
        {
            var constants = Face(BundledFonts.Math).math!.Table!.Constants;

            Assert.Equal(70, constants.ScriptPercentScaleDown);
            Assert.Equal(55, constants.ScriptScriptPercentScaleDown);
            Assert.Equal(1325, constants.DelimitedSubFormulaMinHeight);
            Assert.Equal(1800, constants.DisplayOperatorMinHeight);
            Assert.Equal(258, constants.AxisHeight);
            Assert.Equal(480, constants.AccentBaseHeight);
            Assert.Equal(210, constants.SubscriptShiftDown);
            Assert.Equal(360, constants.SuperscriptShiftUp);
            Assert.Equal(585, constants.FractionNumeratorShiftUp);
            Assert.Equal(640, constants.FractionNumeratorDisplayStyleShiftUp);
            Assert.Equal(68, constants.FractionRuleThickness);
            Assert.Equal(68, constants.FractionNumeratorGapMin);
            Assert.Equal(85, constants.RadicalVerticalGap);
            Assert.Equal(68, constants.RadicalRuleThickness);
            Assert.Equal(65, constants.RadicalKernBeforeDegree);
            // Negative - the one field most likely to break under a signed/unsigned read mistake.
            Assert.Equal(-335, constants.RadicalKernAfterDegree);
            Assert.Equal(55, constants.RadicalDegreeBottomRaisePercent);
        }

        /// <summary>
        /// Every remaining MathConstants field this repo's layout engine doesn't itself read yet
        /// (see MathMetrics.cs) - still ground-truth-verified the same way (fontTools,
        /// <c>TTFont('StixTwoMath-Regular.ttf')['MATH'].table.MathConstants</c>), so the full
        /// sequential-read constructor is proven correct field-by-field, not just for the subset a
        /// caller happens to consult today.
        /// </summary>
        [Fact]
        public void MathConstants_RemainingFields_MatchKnownStixTwoMathValues()
        {
            var constants = Face(BundledFonts.Math).math!.Table!.Constants;

            Assert.Equal(1325, constants.DelimitedSubFormulaMinHeight);
            Assert.Equal(1800, constants.DisplayOperatorMinHeight);
            Assert.Equal(150, constants.MathLeading);
            Assert.Equal(656, constants.FlattenedAccentBaseHeight);
            Assert.Equal(160, constants.SubscriptBaselineDropMin);
            Assert.Equal(252, constants.SuperscriptShiftUpCramped);
            Assert.Equal(120, constants.SuperscriptBottomMin);
            Assert.Equal(230, constants.SuperscriptBaselineDropMax);
            Assert.Equal(380, constants.SuperscriptBottomMaxWithSubscript);
            Assert.Equal(40, constants.SpaceAfterScript);
            Assert.Equal(135, constants.UpperLimitGapMin);
            Assert.Equal(300, constants.UpperLimitBaselineRiseMin);
            Assert.Equal(135, constants.LowerLimitGapMin);
            Assert.Equal(670, constants.LowerLimitBaselineDropMin);
            Assert.Equal(470, constants.StackTopShiftUp);
            Assert.Equal(780, constants.StackTopDisplayStyleShiftUp);
            Assert.Equal(385, constants.StackBottomShiftDown);
            Assert.Equal(690, constants.StackBottomDisplayStyleShiftDown);
            Assert.Equal(300, constants.StackDisplayStyleGapMin);
            Assert.Equal(800, constants.StretchStackTopShiftUp);
            Assert.Equal(590, constants.StretchStackBottomShiftDown);
            Assert.Equal(585, constants.FractionDenominatorShiftDown);
            Assert.Equal(640, constants.FractionDenominatorDisplayStyleShiftDown);
            Assert.Equal(150, constants.FractionNumDisplayStyleGapMin);
            Assert.Equal(68, constants.FractionDenominatorGapMin);
            Assert.Equal(150, constants.FractionDenomDisplayStyleGapMin);
            Assert.Equal(350, constants.SkewedFractionHorizontalGap);
            Assert.Equal(68, constants.SkewedFractionVerticalGap);
            Assert.Equal(175, constants.OverbarVerticalGap);
            Assert.Equal(68, constants.OverbarRuleThickness);
            Assert.Equal(68, constants.OverbarExtraAscender);
            Assert.Equal(175, constants.UnderbarVerticalGap);
            Assert.Equal(68, constants.UnderbarRuleThickness);
            Assert.Equal(68, constants.UnderbarExtraDescender);
            Assert.Equal(170, constants.RadicalDisplayStyleVerticalGap);
            Assert.Equal(78, constants.RadicalExtraAscender);
        }

        [Fact]
        public void GlyphInfo_ItalicsCorrection_MatchesKnownValue_ZeroForUncoveredGlyph()
        {
            var glyphInfo = Face(BundledFonts.Math).math!.Table!.GlyphInfo;

            // uni210B (SCRIPT CAPITAL B, U+212C) is glyph 1221 in this font and carries an italics
            // correction of 40 design units.
            Assert.Equal(40, glyphInfo.GetItalicsCorrection(1221));

            // Glyph 0 (.notdef) is never in the coverage table.
            Assert.Equal(0, glyphInfo.GetItalicsCorrection(0));
        }

        [Fact]
        public void GlyphInfo_TopAccentAttachment_MatchesKnownValue_NullForUncoveredGlyph()
        {
            var glyphInfo = Face(BundledFonts.Math).math!.Table!.GlyphInfo;

            // 'A' is glyph 3 in this font, with a top-accent attachment point of 360 design units.
            Assert.Equal(360, glyphInfo.GetTopAccentAttachment(3));

            Assert.Null(glyphInfo.GetTopAccentAttachment(0));
        }

        [Fact]
        public void GlyphInfo_ExtendedShapeCoverage_MatchesKnownGlyphs()
        {
            var glyphInfo = Face(BundledFonts.Math).math!.Table!.GlyphInfo;

            // 'slash' (gid 1060) and the base 'parenleft' (gid 1064) are both extended shapes in this
            // font; glyph 0 (.notdef) is not.
            Assert.True(glyphInfo.IsExtendedShape(1060));
            Assert.True(glyphInfo.IsExtendedShape(ParenLeftGlyphId));
            Assert.False(glyphInfo.IsExtendedShape(0));
        }

        [Fact]
        public void Variants_MinConnectorOverlap_MatchesKnownValue()
        {
            var variants = Face(BundledFonts.Math).math!.Table!.Variants;

            Assert.Equal(100, variants.MinConnectorOverlap);
        }

        [Fact]
        public void Variants_VerticalConstruction_ForParenLeft_MatchesKnownSizeVariantsAndAssembly()
        {
            var variants = Face(BundledFonts.Math).math!.Table!.Variants;

            var construction = variants.GetVerticalConstruction(ParenLeftGlyphId);
            Assert.NotNull(construction);

            // 13 pre-sized variants, smallest (the base glyph itself) to largest, per fontTools'
            // independently-read MathGlyphVariantRecord array.
            Assert.Equal(13, construction!.Variants.Count);
            // The smallest variant is the base glyph itself (gid 1064), with a vertical "advance
            // measurement" (its own height) of 933 design units - a different number from its normal
            // horizontal advance width.
            Assert.Equal(new MathGlyphVariant(ParenLeftGlyphId, 933), construction.Variants[0]);
            Assert.Equal(new MathGlyphVariant(1301, 1187), construction.Variants[1]);
            Assert.Equal(new MathGlyphVariant(1312, 3821), construction.Variants[^1]);

            // A 3-part assembly (top cap, repeatable extender bar, bottom cap) with zero italics
            // correction, matching the real GlyphAssembly bytes.
            Assert.NotNull(construction.Assembly);
            Assert.Equal(0, construction.Assembly!.ItalicsCorrection);
            Assert.Equal(3, construction.Assembly.Parts.Count);
            Assert.Equal(new MathGlyphPart(4862, 0, 250, 1273, IsExtender: false), construction.Assembly.Parts[0]);
            Assert.Equal(new MathGlyphPart(4861, 1000, 1000, 1252, IsExtender: true), construction.Assembly.Parts[1]);
            Assert.Equal(new MathGlyphPart(4860, 250, 0, 1273, IsExtender: false), construction.Assembly.Parts[2]);
        }

        [Fact]
        public void Variants_HorizontalConstruction_ForCombiningCircumflex_MatchesKnownSizeVariants()
        {
            var variants = Face(BundledFonts.Math).math!.Table!.Variants;

            // U+0302 COMBINING CIRCUMFLEX ACCENT, gid 732 - a real horizontally-growing glyph in this
            // font (used to stretch a wide "hat" accent over multiple characters, e.g. mover[accent]).
            var construction = variants.GetHorizontalConstruction(732);
            Assert.NotNull(construction);
            Assert.Equal(6, construction!.Variants.Count);
            Assert.Equal(new MathGlyphVariant(732, 283), construction.Variants[0]);
            Assert.Equal(new MathGlyphVariant(1395, 574), construction.Variants[1]);
            Assert.Equal(new MathGlyphVariant(1396, 1003), construction.Variants[2]);
            Assert.Equal(new MathGlyphVariant(1397, 1496), construction.Variants[3]);
            Assert.Equal(new MathGlyphVariant(1398, 1932), construction.Variants[4]);
            Assert.Equal(new MathGlyphVariant(1399, 2385), construction.Variants[^1]);
        }

        [Fact]
        public void Variants_UncoveredGlyph_ReturnsNullConstruction()
        {
            var variants = Face(BundledFonts.Math).math!.Table!.Variants;

            Assert.Null(variants.GetVerticalConstruction(0));
            Assert.Null(variants.GetHorizontalConstruction(0));
        }
    }
}
