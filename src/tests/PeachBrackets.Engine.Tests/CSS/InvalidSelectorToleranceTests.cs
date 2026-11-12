using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS;

public class InvalidSelectorToleranceTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    public void InvalidSelectorHandlingUsesSelectorOption(
        bool tolerateInvalidSelectors,
        bool tolerateInvalidValues,
        int expectedStyleRuleCount)
    {
        var parser = new StylesheetParser(
            tolerateInvalidSelectors: tolerateInvalidSelectors,
            tolerateInvalidValues: tolerateInvalidValues);

        var stylesheet = parser.Parse(":bogus-pseudo { color: red; }");

        Assert.Equal(expectedStyleRuleCount, stylesheet.StyleRules.Count());
    }
}
