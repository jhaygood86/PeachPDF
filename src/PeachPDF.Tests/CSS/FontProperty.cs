namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssFontPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssFontFamilyMultipleWithIdentifiersLegal()
        {
            var snippet = "font-family: Gill Sans Extrabold, sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("Gill Sans Extrabold, sans-serif", concrete.Value);
        }

        [Fact]
        public void CssFontFamilyInitialLegal()
        {
            var snippet = "font-family: initial ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("initial", concrete.Value);
        }

        [Fact]
        public void CssFontFamilyMultipleDiverseLegal()
        {
            var snippet = "font-family: Courier, \"Lucida Console\", monospace ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("Courier, \"Lucida Console\", monospace", concrete.Value);
        }

        [Fact]
        public void CssFontFamilyMultipleStringLegal()
        {
            var snippet = "font-family: \"Goudy Bookletter 1911\", sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("\"Goudy Bookletter 1911\", sans-serif", concrete.Value);
        }

        [Fact]
        public void CssFontFamilyMultipleNumberIllegal()
        {
            var snippet = "font-family: Goudy Bookletter 1911, sans-serif  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontFamilyMultipleFractionIllegal()
        {
            var snippet = "font-family: Red/Black, sans-serif  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontFamilyMultipleStringMixedWithIdentifierIllegal()
        {
            var snippet = "font-family: \"Lucida\" Grande, sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontFamilyMultipleExclamationMarkIllegal()
        {
            var snippet = "font-family: Ahem!, sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontFamilyMultipleAtIllegal()
        {
            var snippet = "font-family: test@foo, sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontFamilyHashIllegal()
        {
            var snippet = "font-family: #POUND ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontFamilyDashIllegal()
        {
            var snippet = "font-family: Hawaii 5-0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-family", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontFamilyProperty>(property);
            var concrete = (FontFamilyProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontVariantNormalUppercaseLegal()
        {
            var snippet = "font-variant : NORMAL";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-variant", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontVariantProperty>(property);
            var concrete = (FontVariantProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void CssFontVariantSmallCapsLegal()
        {
            var snippet = "font-variant : small-caps ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-variant", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontVariantProperty>(property);
            var concrete = (FontVariantProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("small-caps", concrete.Value);
        }

        [Fact]
        public void CssFontVariantSmallCapsIllegal()
        {
            var snippet = "font-variant : smallCaps ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-variant", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontVariantProperty>(property);
            var concrete = (FontVariantProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontStyleItalicLegal()
        {
            var snippet = "font-style : italic";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontStyleProperty>(property);
            var concrete = (FontStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("italic", concrete.Value);
        }

        [Fact]
        public void CssFontStyleObliqueLegal()
        {
            var snippet = "font-style : oblique ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontStyleProperty>(property);
            var concrete = (FontStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("oblique", concrete.Value);
        }

        [Fact]
        public void CssFontStyleNormalImportantLegal()
        {
            var snippet = "font-style : normal !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-style", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<FontStyleProperty>(property);
            var concrete = (FontStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void CssFontSizeAbsoluteImportantXxSmallLegal()
        {
            var snippet = "font-size : xx-small !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("xx-small", concrete.Value);
        }

        [Fact]
        public void CssFontSizeAbsoluteMediumUppercaseLegal()
        {
            var snippet = "font-size : medium";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("medium", concrete.Value);
        }

        [Fact]
        public void CssFontSizeAbsoluteLargeImportantLegal()
        {
            var snippet = "font-size : large !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("large", concrete.Value);
        }

        [Fact]
        public void CssFontSizeRelativeLargerLegal()
        {
            var snippet = "font-size : larger ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("larger", concrete.Value);
        }

        [Fact]
        public void CssFontSizeRelativeLargestIllegal()
        {
            var snippet = "font-size : largest ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontSizePercentLegal()
        {
            var snippet = "font-size : 120% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("120%", concrete.Value);
        }

        [Fact]
        public void CssFontSizeZeroLegal()
        {
            var snippet = "font-size : 0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void CssFontSizeLengthLegal()
        {
            var snippet = "font-size : 3.5em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3.5em", concrete.Value);
        }

        [Fact]
        public void CssFontSizeNumberIllegal()
        {
            var snippet = "font-size : 120.3 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeProperty>(property);
            var concrete = (FontSizeProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontWeightPercentllegal()
        {
            var snippet = "font-weight : 100% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-weight", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontWeightProperty>(property);
            var concrete = (FontWeightProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontWeightBolderLegalImportant()
        {
            var snippet = "font-weight : bolder !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-weight", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<FontWeightProperty>(property);
            var concrete = (FontWeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("bolder", concrete.Value);
        }

        [Fact]
        public void CssFontWeightBoldLegal()
        {
            var snippet = "font-weight : bold";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-weight", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontWeightProperty>(property);
            var concrete = (FontWeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("bold", concrete.Value);
        }

        [Fact]
        public void CssFontWeight400Legal()
        {
            var snippet = "font-weight : 400 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-weight", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontWeightProperty>(property);
            var concrete = (FontWeightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("400", concrete.Value);
        }

        [Fact]
        public void CssFontStretchNormalUppercaseImportantLegal()
        {
            var snippet = "font-stretch : NORMAL !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-stretch", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<FontStretchProperty>(property);
            var concrete = (FontStretchProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void CssFontStretchExtraCondensedLegal()
        {
            var snippet = "font-stretch : extra-condensed ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-stretch", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontStretchProperty>(property);
            var concrete = (FontStretchProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("extra-condensed", concrete.Value);
        }

        [Fact]
        public void CssFontStretchSemiExpandedSpaceBetweenIllegal()
        {
            var snippet = "font-stretch : semi expanded ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-stretch", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontStretchProperty>(property);
            var concrete = (FontStretchProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontShorthandWithFractionLegal()
        {
            var snippet = "font : 12px/14px sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("12px / 14px sans-serif", concrete.Value);
        }

        [Fact]
        public void CssFontShorthandPercentLegal()
        {
            var snippet = "font : 80% sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("80% sans-serif", concrete.Value);
        }

        [Fact]
        public void CssFontShorthandBoldItalicLargeLegal()
        {
            var snippet = "font : bold italic large serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("italic bold large serif", concrete.Value);
        }

        [Fact]
        public void CssFontShorthandPredefinedLegal()
        {
            var snippet = "font : status-bar ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("status-bar", concrete.Value);
        }

        [Fact]
        public void CssFontShorthandSizeAndFontListLegal()
        {
            var snippet = "font : 15px arial,sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15px arial, sans-serif", concrete.Value);
        }

        [Fact]
        public void CssFontShorthandStyleWeightSizeLineHeightAndFontListLegal()
        {
            var snippet = "font : italic bold 12px/30px Georgia, serif";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("italic bold 12px / 30px Georgia, serif", concrete.Value);
        }

        [Fact]
        public void CssLetterSpacingLengthPxLegal()
        {
            var snippet = "letter-spacing: 3px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("letter-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<LetterSpacingProperty>(property);
            var concrete = (LetterSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3px", concrete.Value);
        }

        [Fact]
        public void CssLetterSpacingLengthFloatPxLegal()
        {
            var snippet = "letter-spacing: .3px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("letter-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<LetterSpacingProperty>(property);
            var concrete = (LetterSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.3px", concrete.Value);
        }

        [Fact]
        public void CssLetterSpacingLengthFloatEmLegal()
        {
            var snippet = "letter-spacing: 0.3em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("letter-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<LetterSpacingProperty>(property);
            var concrete = (LetterSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.3em", concrete.Value);
        }

        [Fact]
        public void CssLetterSpacingNormalLegal()
        {
            var snippet = "letter-spacing: normal ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("letter-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<LetterSpacingProperty>(property);
            var concrete = (LetterSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void CssFontSizeAdjustNoneLegal()
        {
            var snippet = "font-size-adjust : NONE";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size-adjust", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeAdjustProperty>(property);
            var concrete = (FontSizeAdjustProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssFontSizeAdjustNumberLegal()
        {
            var snippet = "font-size-adjust : 0.5";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size-adjust", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeAdjustProperty>(property);
            var concrete = (FontSizeAdjustProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.5", concrete.Value);
        }

        [Fact]
        public void CssFontSizeAdjustLengthIllegal()
        {
            var snippet = "font-size-adjust : 1.1em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font-size-adjust", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontSizeAdjustProperty>(property);
            var concrete = (FontSizeAdjustProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssFontSizeHeightFamilyLegal()
        {
            var snippet = "font: 12pt/14pt sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("12pt / 14pt sans-serif", concrete.Value);
        }

        [Fact]
        public void CssFontSizeFamilyLegal()
        {
            var snippet = "font: 80% sans-serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("80% sans-serif", concrete.Value);
        }

        [Fact]
        public void CssFontSizeHeightMultipleFamiliesLegal()
        {
            var snippet = "font: x-large/110% 'New Century Schoolbook', serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("x-large / 110% \"New Century Schoolbook\", serif", concrete.Value);
        }

        [Fact]
        public void CssFontWeightVariantSizeFamiliesLegal()
        {
            var snippet = "font: bold italic large Palatino, serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("italic bold large Palatino, serif", concrete.Value);
        }

        [Fact]
        public void CssFontStyleVariantSizeHeightFamilyLegal()
        {
            var snippet = "font: normal small-caps 120%/120% Fantasy ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("normal small-caps 120% / 120% fantasy", concrete.Value);
        }

        [Fact]
        public void CssFontStyleVariantSizeFamiliesLegal()
        {
            var snippet = "font: condensed oblique 12pt \"Helvetica Neue\", serif ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("oblique condensed 12pt \"Helvetica Neue\", serif", concrete.Value);
        }

        [Fact]
        public void CssFontSystemFamilyLegal()
        {
            var snippet = "font: status-bar ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("status-bar", concrete.Value);
        }

        [Fact]
        public void CssFontInitialAll()
        {
            var snippet = "font: initial ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("initial", concrete.Value);
        }

        [Fact]
        public void CssFontInheritAll()
        {
            var snippet = "font: inherit ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("inherit", concrete.Value);
        }

        [Fact]
        public void CssFontUnsetAll()
        {
            var snippet = "font: unset ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("unset", concrete.Value);
        }

        [Fact]
        public void CssFontFaceWithThreeRulesShouldSerializeCorrectly()
        {
            var snippet = @"@font-face {
        font-family: FrutigerLTStd;
            src: url(""https://example.com/FrutigerLTStd-Light.otf"") format(""opentype"");
           font-weight: bold;
    }";
            var rule = ParseRule(snippet);
            Assert.Equal(RuleType.FontFace, rule.Type);
            Assert.Equal("@font-face { font-family: FrutigerLTStd; src: url(\"https://example.com/FrutigerLTStd-Light.otf\") format(\"opentype\"); font-weight: bold }", rule.ToCss());
        }

        [Fact]
        public void CssFontFaceWithTwoRulesShouldSerializeCorrectly()
        {
            var snippet = @"@font-face {
        font-family: FrutigerLTStd;
            src: url(""https://example.com/FrutigerLTStd-Light.otf"") format(""opentype"");
    }";
            var rule = ParseRule(snippet);
            Assert.Equal(RuleType.FontFace, rule.Type);
            Assert.Equal("@font-face { font-family: FrutigerLTStd; src: url(\"https://example.com/FrutigerLTStd-Light.otf\") format(\"opentype\") }", rule.ToCss());
        }

        [Fact]
        public void CssFontFaceWithOneRuleShouldSerializeCorrectly()
        {
            var snippet = @"@font-face {
        font-family: FrutigerLTStd;
    }";
            var rule = ParseRule(snippet);
            Assert.Equal(RuleType.FontFace, rule.Type);
            Assert.Equal("@font-face { font-family: FrutigerLTStd }", rule.ToCss());
        }

        [Fact]
        public void CssFontStyleWeightSizeHeightFamiliesLegal()
        {
            var snippet = "font: italic bold 12px/30px Georgia, serif";
            var property = ParseDeclaration(snippet);
            Assert.Equal("font", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<FontProperty>(property);
            var concrete = (FontProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("italic bold 12px / 30px Georgia, serif", concrete.Value);
        }
    }
}














