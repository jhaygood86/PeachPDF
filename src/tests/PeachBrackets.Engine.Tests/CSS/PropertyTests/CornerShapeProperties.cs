namespace PeachBrackets.Engine.Tests.CSS.PropertyTests
{
    using PeachBrackets.Css;
    using Xunit;

    /// <summary>The CSS-OM side of CSS Borders 4's corner-shape family: what is accepted and how a value is normalized.</summary>
    public class CornerShapePropertyTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("round", "round")]
        [InlineData("BEVEL", "bevel")]
        [InlineData("superellipse(2)", "superellipse(2)")]
        [InlineData("superellipse( -1.5 )", "superellipse(-1.5)")]
        [InlineData("superellipse(infinity)", "superellipse(infinity)")]
        [InlineData("superellipse(-infinity)", "superellipse(-infinity)")]
        public void ALonghandAcceptsEveryShapeValue(string value, string expected)
        {
            var style = ParseDeclarations("corner-top-left-shape: " + value);
            Assert.Equal(expected, style.GetPropertyValue("corner-top-left-shape"));
        }

        [Theory]
        [InlineData("wobbly")]
        [InlineData("round bevel")]
        [InlineData("superellipse()")]
        [InlineData("superellipse(1 2)")]
        [InlineData("superellipse(bevel)")]
        [InlineData("10px")]
        public void ALonghandRejectsAnythingElse(string value)
        {
            var style = ParseDeclarations("corner-top-left-shape: " + value);
            Assert.Equal("", style.GetPropertyValue("corner-top-left-shape"));
        }

        [Theory]
        [InlineData("corner-shape: bevel scoop", "corner-top-left-shape", "bevel", "corner-top-right-shape", "scoop")]
        [InlineData("corner-top-shape: notch", "corner-top-left-shape", "notch", "corner-top-right-shape", "notch")]
        [InlineData("corner-bottom-shape: notch square", "corner-bottom-left-shape", "notch", "corner-bottom-right-shape", "square")]
        [InlineData("corner-block-end-shape: squircle", "corner-end-start-shape", "squircle", "corner-end-end-shape", "squircle")]
        [InlineData("corner-inline-start-shape: bevel scoop", "corner-start-start-shape", "bevel", "corner-end-start-shape", "scoop")]
        [InlineData("border-left-radius: 4px 8px", "border-top-left-radius", "4px 4px", "border-bottom-left-radius", "8px 8px")]
        [InlineData("border-inline-end-radius: 4px", "border-start-end-radius", "4px 4px", "border-end-end-radius", "4px 4px")]
        [InlineData("corner-top: scoop 4px", "corner-top-left-shape", "scoop", "border-top-right-radius", "4px 4px")]
        [InlineData("corner-inline-end: 4px bevel", "corner-start-end-shape", "bevel", "border-end-end-radius", "4px 4px")]
        [InlineData("corner-start-end: bevel 3px", "corner-start-end-shape", "bevel", "border-start-end-radius", "3px")]
        [InlineData("corner: notch 5px / 7px", "corner-bottom-right-shape", "notch", "border-bottom-left-radius", "5px 7px")]
        public void AShorthandExpandsToTheLonghandsItNames(string declaration, string first, string firstValue, string second, string secondValue)
        {
            var style = ParseDeclarations(declaration);

            Assert.Equal(firstValue, style.GetPropertyValue(first));
            Assert.Equal(secondValue, style.GetPropertyValue(second));
        }

        [Theory]
        [InlineData("corner-shape: bevel scoop squircle notch square")]
        [InlineData("corner-top-shape: bevel scoop notch")]
        [InlineData("border-top-radius: 1px 2px 3px")]
        [InlineData("corner-top: bevel bevel bevel")]
        [InlineData("corner-top-left: bevel bevel")]
        [InlineData("corner: wobbly")]
        public void AShorthandWithTooManyOrUnknownValuesIsInvalid(string declaration)
        {
            var style = ParseDeclarations(declaration);
            Assert.Equal(0, style.Length);
        }
    }
}
