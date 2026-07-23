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
        [InlineData("")]
        [InlineData("0")]           // line 0 is invalid
        [InlineData("span 0")]      // span must be >= 1
        [InlineData("span")]        // span needs a count
        [InlineData("span auto")]
        [InlineData("1.5")]         // not an integer
        [InlineData("[name]")]      // named lines out of scope
        [InlineData("banana")]
        public void Invalid_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }
    }
}
