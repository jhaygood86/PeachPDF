namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using Xunit;

    /// <summary>Tests for the shared <see cref="GridLineGrammar"/> — the grid <c>&lt;grid-line&gt;</c> value
    /// (<c>auto | &lt;integer&gt; | span &lt;integer&gt;</c>).</summary>
    public class GridLineGrammarTests : CssConstructionFunctions
    {
        private static GridLine Parse(string value) =>
            GridLineGrammar.TryParse(CssValueParser.GetCssTokens(value));

        [Fact]
        public void Auto_Parses()
        {
            var line = Parse("auto");
            Assert.NotNull(line);
            Assert.True(line.IsAuto);
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("3", 3)]
        [InlineData("-1", -1)]
        public void IntegerLine_Parses(string value, int expected)
        {
            var line = Parse(value);
            Assert.NotNull(line);
            Assert.False(line.IsAuto);
            Assert.False(line.IsSpan);
            Assert.Equal(expected, line.Value);
        }

        [Theory]
        [InlineData("span 1", 1)]
        [InlineData("span 3", 3)]
        public void Span_Parses(string value, int expected)
        {
            var line = Parse(value);
            Assert.NotNull(line);
            Assert.True(line.IsSpan);
            Assert.Equal(expected, line.Value);
        }

        [Theory]
        [InlineData("sidebar")]
        [InlineData("main-start")]
        public void NamedLine_Parses(string value)
        {
            var line = Parse(value);
            Assert.NotNull(line);
            Assert.False(line.IsAuto);
            Assert.False(line.IsSpan);
            Assert.Equal(value, line.Name);
            Assert.Equal(1, line.Value);
        }

        [Theory]
        [InlineData("col 2", "col", 2)]
        [InlineData("2 col", "col", 2)]
        [InlineData("col -1", "col", -1)]
        public void NamedNthLine_Parses(string value, string name, int nth)
        {
            var line = Parse(value);
            Assert.NotNull(line);
            Assert.Equal(name, line.Name);
            Assert.Equal(nth, line.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]           // line 0 is invalid
        [InlineData("span 0")]      // span must be >= 1
        [InlineData("span")]        // span needs a count
        [InlineData("span auto")]
        [InlineData("1.5")]         // not an integer
        [InlineData("[name]")]      // bracketed line names belong in a track list, not a <grid-line>
        [InlineData("none")]        // a CSS-wide/reserved keyword is not a custom-ident line name
        [InlineData("initial")]
        [InlineData("span foo")]    // span <custom-ident> is out of v1 scope
        public void Invalid_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }
    }
}
