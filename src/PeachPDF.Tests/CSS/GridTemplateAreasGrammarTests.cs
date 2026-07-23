namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using Xunit;

    /// <summary>Tests for the shared <see cref="GridTemplateAreasGrammar"/> — the
    /// <c>grid-template-areas</c> value (a rectangular grid of named cells).</summary>
    public class GridTemplateAreasGrammarTests : CssConstructionFunctions
    {
        private static GridAreas Parse(string value) =>
            GridTemplateAreasGrammar.TryParse(CssValueParser.GetCssTokens(value));

        [Fact]
        public void RectangularGrid_ParsesWithAreaBounds()
        {
            var areas = Parse("\"header header header\" \"nav main main\" \"footer footer footer\"");
            Assert.NotNull(areas);
            Assert.Equal(3, areas.RowCount);
            Assert.Equal(3, areas.ColCount);
            Assert.Equal((0, 0, 0, 2), areas.Areas["header"]);   // whole top row
            Assert.Equal((1, 0, 1, 0), areas.Areas["nav"]);      // row 1, col 0
            Assert.Equal((1, 1, 1, 2), areas.Areas["main"]);     // row 1, cols 1-2
            Assert.Equal((2, 0, 2, 2), areas.Areas["footer"]);
        }

        [Fact]
        public void DotCells_AreEmpty_AndNotAreas()
        {
            var areas = Parse("\"a . b\" \". . .\"");
            Assert.NotNull(areas);
            Assert.Equal(2, areas.RowCount);
            Assert.Equal(3, areas.ColCount);
            Assert.True(areas.Areas.ContainsKey("a"));
            Assert.True(areas.Areas.ContainsKey("b"));
            Assert.Null(areas.Cells[0, 1]);
            Assert.Null(areas.Cells[1, 0]);
        }

        [Fact]
        public void TripleDot_IsAlsoAnEmptyCell()
        {
            var areas = Parse("\"a ... b\"");
            Assert.NotNull(areas);
            Assert.Null(areas.Cells[0, 1]);
        }

        [Theory]
        [InlineData("\"a a\" \"a a a\"")]        // ragged rows (2 vs 3 columns)
        [InlineData("\"a b\" \"b a\"")]          // 'a' is not a rectangle (diagonal)
        [InlineData("\"a a\" \"a .\"")]          // 'a' bounding box includes an empty cell → not filled
        [InlineData("\"a b a\"")]                // 'a' occupies cols 0 and 2 with b between → not rectangular
        [InlineData("100px")]                    // not a string list
        [InlineData("\"\"")]                     // an empty string row
        [InlineData("")]
        public void Invalid_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }
    }
}
