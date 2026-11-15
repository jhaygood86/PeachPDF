namespace PeachBrackets.Engine.Tests.CSS
{
    using PeachBrackets.Css;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// The grid-lanes properties of CSS Grid Layout Module Level 3: the <c>grid-lanes</c> shorthand, <c>grid-lanes-direction</c>, <c>grid-lanes-pack</c>
    /// and the two-keyword <c>display</c> syntax that names a lanes container.
    /// </summary>
    public class GridLanesPropertyTests : CssConstructionFunctions
    {
        private static StyleDeclaration Style(string declaration) =>
            (StyleDeclaration)ParseStyleSheet($"div {{ {declaration} }}").Rules.OfType<StyleRule>().Single().Style;

        [Fact]
        public void GridLanes_ATrackListIsTheColumns_AndTheDirectionResets()
        {
            var style = Style("grid-lanes: 100px 1fr;");
            Assert.Equal("100px 1fr", style.GetPropertyValue("grid-template-columns"));
            Assert.Equal("normal", style.GetPropertyValue("grid-lanes-direction"));
        }

        [Fact]
        public void GridLanes_RowDirection_MakesTheTrackListTheRows()
        {
            var style = Style("grid-lanes: row 50px 50px;");
            Assert.Equal("50px 50px", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("row", style.GetPropertyValue("grid-lanes-direction"));
        }

        [Theory]
        [InlineData("grid-lanes: column fill-reverse 1fr 1fr;", "column fill-reverse")]
        [InlineData("grid-lanes: 1fr 1fr track-reverse;", "column track-reverse")]
        [InlineData("grid-lanes: row fill-reverse track-reverse 40px;", "row fill-reverse track-reverse")]
        public void GridLanes_ReverseWords_AreCollectedIntoTheDirection(string declaration, string direction)
        {
            Assert.Equal(direction, Style(declaration).GetPropertyValue("grid-lanes-direction"));
        }

        [Fact]
        public void GridLanes_None_ResetsTheTemplatesToNone()
        {
            var style = Style("grid-lanes: none;");
            Assert.Equal("none", style.GetPropertyValue("grid-template-columns"));
            Assert.Equal("none", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("none", style.GetPropertyValue("grid-template-areas"));
            Assert.Equal("column", style.GetPropertyValue("grid-lanes-direction"));
        }

        [Fact]
        public void GridLanes_AreaStrings_SetTheAreas()
        {
            var style = Style("grid-lanes: \"a b\" 1fr 1fr;");
            Assert.Equal("\"a b\"", style.GetPropertyValue("grid-template-areas"));
            Assert.Equal("1fr 1fr", style.GetPropertyValue("grid-template-columns"));
        }

        [Theory]
        [InlineData("grid-lanes: row column 1fr;")]
        [InlineData("grid-lanes: row row 1fr;")]
        [InlineData("grid-lanes: column fill-reverse fill-reverse 1fr;")]
        [InlineData("grid-lanes: column track-reverse track-reverse 1fr;")]
        [InlineData("grid-lanes: normal fill-reverse 1fr;")]
        [InlineData("grid-lanes: row;")]
        [InlineData("grid-lanes: \"a\" row 1fr extra-ident;")]
        [InlineData("grid-lanes: \"a b\" \"a\" 1fr;")]
        public void GridLanes_InvalidShorthands_AreDropped(string declaration)
        {
            var style = Style(declaration);
            Assert.Equal("", style.GetPropertyValue("grid-lanes-direction"));
        }

        [Theory]
        [InlineData("grid-lanes-direction: normal")]
        [InlineData("grid-lanes-direction: row")]
        [InlineData("grid-lanes-direction: column")]
        [InlineData("grid-lanes-direction: row fill-reverse")]
        [InlineData("grid-lanes-direction: column track-reverse fill-reverse")]
        public void GridLanesDirection_AcceptsItsGrammar(string declaration)
        {
            Assert.Equal(declaration.Substring("grid-lanes-direction: ".Length), Style(declaration).GetPropertyValue("grid-lanes-direction"));
        }

        [Theory]
        [InlineData("grid-lanes-direction: fill-reverse")]
        [InlineData("grid-lanes-direction: row fill-reverse fill-reverse")]
        [InlineData("grid-lanes-direction: row track-reverse track-reverse")]
        [InlineData("grid-lanes-direction: row sideways")]
        [InlineData("grid-lanes-direction: row column")]
        [InlineData("grid-lanes-direction: 10px")]
        [InlineData("grid-lanes-direction: row fill-reverse track-reverse extra")]
        [InlineData("grid-lanes-direction: normal fill-reverse")]
        public void GridLanesDirection_RejectsWhatItsGrammarDoesNot(string declaration)
        {
            Assert.Equal("", Style(declaration).GetPropertyValue("grid-lanes-direction"));
        }

        [Theory]
        [InlineData("normal", "normal")]
        [InlineData("dense", "dense")]
        [InlineData("sparse", "")]
        [InlineData("dense normal", "")]
        [InlineData("10px", "")]
        public void GridLanesPack_AcceptsNormalAndDenseOnly(string value, string expected)
        {
            Assert.Equal(expected, Style($"grid-lanes-pack: {value}").GetPropertyValue("grid-lanes-pack"));
        }

        [Theory]
        [InlineData("inline grid-lanes", "inline-grid-lanes")]
        [InlineData("grid-lanes block", "grid-lanes")]
        [InlineData("block grid-lanes", "grid-lanes")]
        [InlineData("block flex", "flex")]
        [InlineData("inline flex", "inline-flex")]
        [InlineData("inline flow-root", "inline-block")]
        [InlineData("block flow-root", "flow-root")]
        [InlineData("inline grid", "inline-grid")]
        [InlineData("block table", "table")]
        [InlineData("inline table", "inline-table")]
        [InlineData("block flow", "block")]
        [InlineData("inline flow", "inline")]
        public void Display_TwoKeywordForms_ReadAsTheirSingleKeyword(string value, string expected)
        {
            Assert.Equal(expected, Style($"display: {value}").GetPropertyValue("display"));
        }

        [Theory]
        [InlineData("display: inline bogus")]
        [InlineData("display: bogus block")]
        [InlineData("display: block block")]
        [InlineData("display: inline 10px")]
        public void Display_InvalidTwoKeywordForms_AreDropped(string declaration)
        {
            Assert.Equal("", Style(declaration).GetPropertyValue("display"));
        }
    }
}
