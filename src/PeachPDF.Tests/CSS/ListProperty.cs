using PeachPDF.CSS;
namespace PeachPDF.Tests.CSS
{
    using Xunit;

    public class CssListPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssListStylePositionOutsideLegal()
        {
            var snippet = "list-style-position: outside ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStylePositionProperty>(property);
            var concrete = (ListStylePositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("outside", concrete.Value);
        }

        [Fact]
        public void CssListStylePositionOutsideIllegal()
        {
            var snippet = "list-style-position: out-side ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStylePositionProperty>(property);
            var concrete = (ListStylePositionProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssListStylePositionNoneIllegal()
        {
            var snippet = "list-style-position: none ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStylePositionProperty>(property);
            var concrete = (ListStylePositionProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssListStylePositionInsideLegal()
        {
            var snippet = "list-style-position: insiDe ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStylePositionProperty>(property);
            var concrete = (ListStylePositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("inside", concrete.Value);
        }

        [Fact]
        public void CssListStyleImageNoneLegal()
        {
            var snippet = "list-style-image: none ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleImageProperty>(property);
            var concrete = (ListStyleImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssListStyleImageUrlLegal()
        {
            var snippet = "list-style-image: url(http://www.example.com/images/list.png)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleImageProperty>(property);
            var concrete = (ListStyleImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"http://www.example.com/images/list.png\")", concrete.Value);
        }

        [Fact]
        public void CssListStyleTypeDiscLegal()
        {
            var snippet = "list-style-type: disc ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-type", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleTypeProperty>(property);
            var concrete = (ListStyleTypeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("disc", concrete.Value);
        }

        [Fact]
        public void CssListStyleTypeLowerAlphaLegal()
        {
            var snippet = "list-style-type: lower-ALPHA ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-type", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleTypeProperty>(property);
            var concrete = (ListStyleTypeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("lower-alpha", concrete.Value);
        }

        [Fact]
        public void CssListStyleTypeGeorgianLegal()
        {
            var snippet = "list-style-type: georgian ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-type", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleTypeProperty>(property);
            var concrete = (ListStyleTypeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("georgian", concrete.Value);
        }

        [Fact]
        public void CssListStyleTypeDecimalLeadingZeroLegal()
        {
            var snippet = "list-style-type: decimal-leading-zerO ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-type", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleTypeProperty>(property);
            var concrete = (ListStyleTypeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("decimal-leading-zero", concrete.Value);
        }

        [Theory]
        // Pre-existing values that were documented as supported but - until Converters.ListStyleConverter
        // (Map.ListStyles) was completed alongside the new values below - actually failed to parse at
        // this CSS-OM layer, since an unrecognized keyword makes the whole declaration invalid; a
        // `<style>`/inline-style declaration for one of these silently fell back to the property's
        // cascaded/initial value instead of ever reaching CssBoxMarker.
        [InlineData("hebrew", "hebrew")]
        [InlineData("hiragana", "hiragana")]
        [InlineData("hiragana-iroha", "hiragana-iroha")]
        [InlineData("katakana", "katakana")]
        [InlineData("katakana-iroha", "katakana-iroha")]
        // Numeric (positional digit-substitution) styles - CSS Counter Styles Level 3 §6.1.
        [InlineData("arabic-indic", "arabic-indic")]
        [InlineData("Bengali", "bengali")]
        [InlineData("cambodian", "cambodian")]
        [InlineData("khmer", "khmer")]
        [InlineData("cjk-decimal", "cjk-decimal")]
        [InlineData("devanagari", "devanagari")]
        [InlineData("gujarati", "gujarati")]
        [InlineData("gurmukhi", "gurmukhi")]
        [InlineData("kannada", "kannada")]
        [InlineData("lao", "lao")]
        [InlineData("malayalam", "malayalam")]
        [InlineData("mongolian", "mongolian")]
        [InlineData("myanmar", "myanmar")]
        [InlineData("oriya", "oriya")]
        [InlineData("persian", "persian")]
        [InlineData("tamil", "tamil")]
        [InlineData("telugu", "telugu")]
        [InlineData("thai", "thai")]
        [InlineData("tibetan", "tibetan")]
        // Fixed styles - §6.4.
        [InlineData("cjk-earthly-branch", "cjk-earthly-branch")]
        [InlineData("cjk-heavenly-stem", "cjk-heavenly-stem")]
        // Ethiopic numeric - §7.2.
        [InlineData("ethiopic-numeric", "ethiopic-numeric")]
        // Armenian variants - §6.1.
        [InlineData("lower-armenian", "lower-armenian")]
        [InlineData("UPPER-ARMENIAN", "upper-armenian")]
        // Symbolic (fixed-glyph, uncounted) styles - §6.3.
        [InlineData("disclosure-open", "disclosure-open")]
        [InlineData("disclosure-closed", "disclosure-closed")]
        public void CssListStyleTypeNewKeywordLegal(string input, string expected)
        {
            var snippet = $"list-style-type: {input} ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-type", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleTypeProperty>(property);
            var concrete = (ListStyleTypeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal(expected, concrete.Value);
        }

        [Fact]
        public void CssListStyleTypeStringLegal()
        {
            var snippet = "list-style-type: \"-> \" ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-type", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleTypeProperty>(property);
            var concrete = (ListStyleTypeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("\"-> \"", concrete.Value);
        }

        [Fact]
        public void CssListStyleTypeNumberIllegal()
        {
            var snippet = "list-style-type: number ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style-type", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleTypeProperty>(property);
            var concrete = (ListStyleTypeProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssListStyleCircleLegal()
        {
            var snippet = "list-style: circle ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleProperty>(property);
            var concrete = (ListStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("circle", concrete.Value);
        }

        [Fact]
        public void CssListStyleNone()
        {
            var snippet = "list-style: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleProperty>(property);
            var concrete = (ListStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssListStyleSquareInsideLegal()
        {
            var snippet = "list-style: square inside ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleProperty>(property);
            var concrete = (ListStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("square inside", concrete.Value);
        }

        [Fact]
        public void CssListStyleSquareImageInsideLegal()
        {
            var snippet = "list-style: square url('image.png') inside ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleProperty>(property);
            var concrete = (ListStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("square inside url(\"image.png\")", concrete.Value);
        }

        [Fact]
        public void CssCounterResetLegal()
        {
            var snippet = "counter-reset: chapter section 1 page;";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-reset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterResetProperty>(property);
            var concrete = (CounterResetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("chapter section 1 page", concrete.Value);
        }

        [Fact]
        public void CssCounterResetSingleLegal()
        {
            var snippet = "counter-reset: counter-name";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-reset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterResetProperty>(property);
            var concrete = (CounterResetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("counter-name", concrete.Value);
        }

        [Fact]
        public void CssCounterResetNoneLegal()
        {
            var snippet = "counter-reset: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-reset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterResetProperty>(property);
            var concrete = (CounterResetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssCounterResetNumberIllegal()
        {
            var snippet = "counter-reset: 3";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-reset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterResetProperty>(property);
            var concrete = (CounterResetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssCounterResetNegativeLegal()
        {
            var snippet = "counter-reset  :  counter-name   -1";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-reset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterResetProperty>(property);
            var concrete = (CounterResetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("counter-name -1", concrete.Value);
        }

        [Fact]
        public void CssCounterResetTwoCountersExplicitLegal()
        {
            var snippet = "counter-reset  :  counter1   1   counter2   4  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-reset", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterResetProperty>(property);
            var concrete = (CounterResetProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("counter1 1 counter2 4", concrete.Value);
        }

        [Fact]
        public void CssCounterIncrementNoneLegal()
        {
            var snippet = "counter-increment: none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-increment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterIncrementProperty>(property);
            var concrete = (CounterIncrementProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssCounterIncrementLegal()
        {
            var snippet = "counter-increment: chapter section 2 page";
            var property = ParseDeclaration(snippet);
            Assert.Equal("counter-increment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<CounterIncrementProperty>(property);
            var concrete = (CounterIncrementProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("chapter section 2 page", concrete.Value);
        }

        [Fact]
        public void CssListStyleNoneSquareLegal()
        {
            var snippet = "list-style: none square";
            var property = ParseDeclaration(snippet);
            Assert.Equal("list-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ListStyleProperty>(property);
            var concrete = (ListStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("square none", concrete.Value);  // Canonical order: type, image, position
        }
    }
}