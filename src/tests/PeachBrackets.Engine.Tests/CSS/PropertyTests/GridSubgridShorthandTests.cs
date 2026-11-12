using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS.PropertyTests
{
    /// <summary>
    /// <c>subgrid</c> is a value of the <c>grid-template-rows</c>/<c>-columns</c> longhands, so the <c>grid</c> and <c>grid-template</c> shorthands
    /// accept it wherever those longhands go (CSS Grid 2 section 9): <c>grid: subgrid / subgrid</c>. The areas form takes an
    /// explicit track list after the slash, so <c>subgrid</c> is not valid there.
    /// </summary>
    public class GridSubgridShorthandTests
    {
        private static StyleDeclaration Declared(string declaration)
        {
            var stylesheet = new StylesheetParser().Parse("html { " + declaration + " }");
            return ((StyleRule)stylesheet.StyleRules.First()).Style;
        }

        [Theory]
        [InlineData("grid-template: subgrid / subgrid")]
        [InlineData("grid: subgrid / subgrid")]
        public void TheShorthandsAcceptSubgridOnBothAxes(string declaration)
        {
            var style = Declared(declaration);

            Assert.Equal("subgrid", style.GetPropertyValue(PropertyNames.GridTemplateRows));
            Assert.Equal("subgrid", style.GetPropertyValue(PropertyNames.GridTemplateColumns));
        }

        [Fact]
        public void ALineNameListFollowsSubgridInTheShorthand()
        {
            var style = Declared("grid-template: subgrid [a b] [c] / subgrid [x]");

            Assert.Contains("subgrid", style.GetPropertyValue(PropertyNames.GridTemplateRows));
            Assert.Contains("[a b]", style.GetPropertyValue(PropertyNames.GridTemplateRows));
            Assert.Contains("[x]", style.GetPropertyValue(PropertyNames.GridTemplateColumns));
        }

        [Fact]
        public void SubgridOnOneAxisAndTracksOnTheOtherIsAccepted()
        {
            var style = Declared("grid-template: 1fr 2fr / subgrid");

            Assert.Equal("subgrid", style.GetPropertyValue(PropertyNames.GridTemplateColumns));
            Assert.Contains("1fr", style.GetPropertyValue(PropertyNames.GridTemplateRows));
        }

        [Fact]
        public void SubgridIsNotValidAfterTheAreasForm()
        {
            var style = Declared("grid-template: \"a b\" 50px / subgrid");

            Assert.True(string.IsNullOrEmpty(style.GetPropertyValue(PropertyNames.GridTemplateColumns)) || style.GetPropertyValue(PropertyNames.GridTemplateColumns) == "none",
                $"the declaration is dropped, columns were '{style.GetPropertyValue(PropertyNames.GridTemplateColumns)}'");
        }

        [Fact]
        public void SubgridIsAcceptedInTheAutoFlowFormsTemplateSide()
        {
            var style = Declared("grid: subgrid / auto-flow");

            Assert.Equal("subgrid", style.GetPropertyValue(PropertyNames.GridTemplateRows));
            Assert.Contains("column", style.GetPropertyValue(PropertyNames.GridAutoFlow));
        }
    }
}
