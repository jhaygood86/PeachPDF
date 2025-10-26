namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class TextPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void WordSpacingZeroLengthLegal()
        {
            var snippet = "word-spacing: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WordSpacingProperty>(property);
            var concrete = (WordSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void WordSpacingLengthFloatRemLegal()
        {
            var snippet = "word-spacing: .3rem ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WordSpacingProperty>(property);
            var concrete = (WordSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.3rem", concrete.Value);
        }

        [Fact]
        public void WordSpacingLengthFloatEmLegal()
        {
            var snippet = "word-spacing: 0.3em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WordSpacingProperty>(property);
            var concrete = (WordSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.3em", concrete.Value);
        }

        [Fact]
        public void WordSpacingNormalLegal()
        {
            var snippet = "word-spacing: normal ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WordSpacingProperty>(property);
            var concrete = (WordSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void TextShadowLegalInsetAtLast()
        {
            var snippet = "text-shadow: 0 0 2px black inset";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-shadow", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextShadowProperty>(property);
            var concrete = (TextShadowProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("inset 0 0 2px rgb(0, 0, 0)", concrete.Value);
        }

        [Fact]
        public void TextShadowLegalColorInFront()
        {
            var snippet = "text-shadow: rgba(255,255,255,0.5) 0px 3px 3px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-shadow", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextShadowProperty>(property);
            var concrete = (TextShadowProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("0 3px 3px rgba(255, 255, 255, 0.5)", concrete.Value);
        }

        [Fact]
        public void TextShadowLegalMultipleMultilines()
        {
            var snippet = @"text-shadow: 0px 3px 0px #b2a98f,
             0px 14px 10px rgba(0,0,0,0.15),
             0px 24px 2px rgba(0,0,0,0.1),
             0px 34px 30px rgba(0,0,0,0.1)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-shadow", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextShadowProperty>(property);
            var concrete = (TextShadowProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("0 3px 0 rgb(178, 169, 143), 0 14px 10px rgba(0, 0, 0, 0.15), 0 24px 2px rgba(0, 0, 0, 0.1), 0 34px 30px rgba(0, 0, 0, 0.1)", concrete.Value);
        }

        [Fact]
        public void TextShadowLegalMultipleInline()
        {
            var snippet = "text-shadow: 4px 3px 0px #fff, 9px 8px 0px rgba(0,0,0,0.15)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-shadow", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextShadowProperty>(property);
            var concrete = (TextShadowProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("4px 3px 0 rgb(255, 255, 255), 9px 8px 0 rgba(0, 0, 0, 0.15)", concrete.Value);
        }

        [Fact]
        public void TextShadowLegalColorRgbaLast()
        {
            var snippet = "text-shadow: 2px 4px 3px rgba(0,0,0,0.3)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-shadow", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextShadowProperty>(property);
            var concrete = (TextShadowProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("2px 4px 3px rgba(0, 0, 0, 0.3)", concrete.Value);
        }

        [Fact]
        public void TextAlignLegalJustify()
        {
            var snippet = "text-align:justify";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignProperty>(property);
            var concrete = (TextAlignProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("justify", concrete.Value);
        }

        [Fact]
        public void TextIndentLegalLength()
        {
            var snippet = "text-indent:3em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-indent", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextIndentProperty>(property);
            var concrete = (TextIndentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("3em", concrete.Value);
        }

        [Fact]
        public void TextIndentLegalZero()
        {
            var snippet = "text-indent:0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-indent", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextIndentProperty>(property);
            var concrete = (TextIndentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void TextIndentLegalPercent()
        {
            var snippet = "text-indent:10%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-indent", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextIndentProperty>(property);
            var concrete = (TextIndentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.Equal("10%", concrete.Value);
        }

        [Fact]
        public void TextIndentIllegalNone()
        {
            var snippet = "text-indent:none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-indent", property.Name);
            Assert.False(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TextIndentProperty>(property);
            var concrete = (TextIndentProperty)property;
        }

        [Fact]
        public void TextDecorationIllegal()
        {
            var snippet = "text-decoration: line-pass";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration", property.Name);
            Assert.False(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationProperty>(property);
            var concrete = (TextDecorationProperty)property;
        }

        [Fact]
        public void TextDecorationLegalLineThrough()
        {
            var snippet = "text-decoration: line-Through";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationProperty>(property);
            var concrete = (TextDecorationProperty)property;
            Assert.Equal("line-through", concrete.Value);
        }

        [Fact]
        public void TextDecorationLegalUnderlineOverline()
        {
            var snippet = "text-decoration:  underline  overline";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration", property.Name);
            Assert.IsType<TextDecorationProperty>(property);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            var concrete = (TextDecorationProperty)property;
            Assert.Equal("underline overline", concrete.Value);
        }

        [Fact]
        public void TextDecorationColorLegalHex()
        {
            var snippet = "text-decoration-color: #F00";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration-color", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationColorProperty>(property);
            var concrete = (TextDecorationColorProperty)property;
            Assert.Equal("rgb(255, 0, 0)", concrete.Value);
        }

        [Fact]
        public void TextDecorationColorLegalRed()
        {
            var snippet = "text-decoration-color: red";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration-color", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationColorProperty>(property);
            var concrete = (TextDecorationColorProperty)property;
            Assert.Equal("rgb(255, 0, 0)", concrete.Value);
        }

        [Fact]
        public void TextDecorationLineIllegalInteger()
        {
            var snippet = "text-decoration-line: 5";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration-line", property.Name);
            Assert.False(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationLineProperty>(property);
            var concrete = (TextDecorationLineProperty)property;
        }

        [Fact]
        public void TextDecorationLineLegalNone()
        {
            var snippet = "text-decoration-line: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration-line", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationLineProperty>(property);
            var concrete = (TextDecorationLineProperty)property;
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void TextDecorationLineLegalOverlineUnderlineLineThrough()
        {
            var snippet = "text-decoration-line: overline    underline line-through  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration-line", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationLineProperty>(property);
            var concrete = (TextDecorationLineProperty)property;
            Assert.Equal("overline underline line-through", concrete.Value);
        }

        [Fact]
        public void TextDecorationStyleLegalWavyUppercase()
        {
            var snippet = "text-decoration-style: WAVY ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration-style", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationStyleProperty>(property);
            var concrete = (TextDecorationStyleProperty)property;
            Assert.Equal("wavy", concrete.Value);
        }

        [Fact]
        public void TextDecorationStyleIllegalMultiple()
        {
            var snippet = "text-decoration-style: wavy dotted";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-decoration-style", property.Name);
            Assert.False(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextDecorationStyleProperty>(property);
            var concrete = (TextDecorationStyleProperty)property;
        }

        [Fact]
        public void TextDecorationExpansionAndRecombination()
        {
            var snippet = ".centered {text-decoration:underline;}";
            var expected = ".centered { text-decoration: underline }";
            var result = ParseRule(snippet);
            var actual = result.Text;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void WordBreakNormalLegal()
        {
            var snippet = "word-break : normal";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-break", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<WordBreakProperty>(property);
            var concrete = (WordBreakProperty)property;
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void WordBreakBreakAllLegal()
        {
            var snippet = "word-break : break-all";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-break", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<WordBreakProperty>(property);
            var concrete = (WordBreakProperty)property;
            Assert.Equal("break-all", concrete.Value);
        }

        [Fact]
        public void WordBreakKeepAllLegal()
        {
            var snippet = "word-break : keep-all";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-break", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<WordBreakProperty>(property);
            var concrete = (WordBreakProperty)property;
            Assert.Equal("keep-all", concrete.Value);
        }

        [Fact]
        public void WordBreakNoneIllegal()
        {
            var snippet = "word-break : none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("word-break", property.Name);
            Assert.False(property.HasValue);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<WordBreakProperty>(property);
        }

        [Fact]
        public void TextAlignLastAutoLegal()
        {
            var snippet = "text-align-last: auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void TextAlignLastStartLegal()
        {
            var snippet = "text-align-last: start";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("start", concrete.Value);
        }

        [Fact]
        public void TextAlignLastEndLegal()
        {
            var snippet = "text-align-last: end";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("end", concrete.Value);
        }

        [Fact]
        public void TextAlignLastRightLegal()
        {
            var snippet = "text-align-last: right";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("right", concrete.Value);
        }

        [Fact]
        public void TextAlignLastLeftLegal()
        {
            var snippet = "text-align-last: left";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("left", concrete.Value);
        }

        [Fact]
        public void TextAlignLastCenterLegal()
        {
            var snippet = "text-align-last: center";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("center", concrete.Value);
        }

        [Fact]
        public void TextAlignLastJustifyLegal()
        {
            var snippet = "text-align-last: justify";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("justify", concrete.Value);
        }

        [Fact]
        public void TextAlignLastNoneIllegal()
        {
            var snippet = "text-align-last: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-align-last", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAlignLastProperty>(property);
            var concrete = (TextAlignLastProperty)property;
            Assert.False(property.HasValue);
        }

        [Fact]
        public void TextAnchorStartLegal()
        {
            var snippet = "text-anchor: start";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-anchor", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAnchorProperty>(property);
            var concrete = (TextAnchorProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("start", concrete.Value);
        }

        [Fact]
        public void TextAnchorMiddleLegal()
        {
            var snippet = "text-anchor: middle";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-anchor", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAnchorProperty>(property);
            var concrete = (TextAnchorProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("middle", concrete.Value);
        }

        [Fact]
        public void TextAnchorEndLegal()
        {
            var snippet = "text-anchor: end";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-anchor", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAnchorProperty>(property);
            var concrete = (TextAnchorProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("end", concrete.Value);
        }

        [Fact]
        public void TextAnchorNoneIllegal()
        {
            var snippet = "text-anchor: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-anchor", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextAnchorProperty>(property);
            var concrete = (TextAnchorProperty)property;
            Assert.False(property.HasValue);
        }

        [Fact]
        public void TextJustifyAutoLegal()
        {
            var snippet = "text-justify: auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void TextJustifyDistributeLegal()
        {
            var snippet = "text-justify: distribute";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("distribute", concrete.Value);
        }

        [Fact]
        public void TextJustifyDistributeAllLinesLegal()
        {
            var snippet = "text-justify: distribute-all-lines";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("distribute-all-lines", concrete.Value);
        }

        [Fact]
        public void TextJustifyDistributeCenterLastLegal()
        {
            var snippet = "text-justify: distribute-center-last";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("distribute-center-last", concrete.Value);
        }

        [Fact]
        public void TextJustifyInterClusterLegal()
        {
            var snippet = "text-justify: inter-cluster";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("inter-cluster", concrete.Value);
        }

        [Fact]
        public void TextJustifyInterIdeographLegal()
        {
            var snippet = "text-justify: inter-ideograph";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("inter-ideograph", concrete.Value);
        }

        [Fact]
        public void TextJustifyInterWordLegal()
        {
            var snippet = "text-justify: inter-word";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("inter-word", concrete.Value);
        }

        [Fact]
        public void TextJustifyKashidaLegal()
        {
            var snippet = "text-justify: kashida";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("kashida", concrete.Value);
        }

        [Fact]
        public void TextJustifyNewspaperLegal()
        {
            var snippet = "text-justify: newspaper";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("newspaper", concrete.Value);
        }

        [Fact]
        public void TextJustifyNoneIllegal()
        {
            var snippet = "text-justify: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("text-justify", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<TextJustifyProperty>(property);
            var concrete = (TextJustifyProperty)property;
            Assert.False(property.HasValue);
        }

        [Fact]
        public void OverflowWrapNormalLegal()
        {
            var snippet = "overflow-wrap: normal";
            var property = ParseDeclaration(snippet);
            Assert.Equal("overflow-wrap", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<OverflowWrapProperty>(property);
            var concrete = (OverflowWrapProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void OverflowWrapAlternateNameNormalLegal()
        {
            var snippet = "word-wrap: normal";
            var property = ParseDeclaration(snippet);
            Assert.Equal("overflow-wrap", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<OverflowWrapProperty>(property);
            var concrete = (OverflowWrapProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("normal", concrete.Value);
        }

        [Fact]
        public void OverflowWrapBreakWordLegal()
        {
            var snippet = "overflow-wrap: break-word";
            var property = ParseDeclaration(snippet);
            Assert.Equal("overflow-wrap", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<OverflowWrapProperty>(property);
            var concrete = (OverflowWrapProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("break-word", concrete.Value);
        }

        [Fact]
        public void OverflowWrapAlternateNameBreakWordLegal()
        {
            var snippet = "word-wrap: break-word";
            var property = ParseDeclaration(snippet);
            Assert.Equal("overflow-wrap", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<OverflowWrapProperty>(property);
            var concrete = (OverflowWrapProperty)property;
            Assert.True(property.HasValue);
            Assert.Equal("break-word", concrete.Value);
        }

        [Fact]
        public void OverflowWrapNoneIllegal()
        {
            var snippet = "overflow-wrap: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("overflow-wrap", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<OverflowWrapProperty>(property);
            var concrete = (OverflowWrapProperty)property;
            Assert.False(property.HasValue);
        }

        [Fact]
        public void OverflowWrapAlternateNameNoneIllegal()
        {
            var snippet = "word-wrap: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("overflow-wrap", property.Name);
            Assert.False(property.IsInherited);
            Assert.False(property.IsImportant);
            Assert.IsType<OverflowWrapProperty>(property);
            var concrete = (OverflowWrapProperty)property;
            Assert.False(property.HasValue);
        }
    }
}








