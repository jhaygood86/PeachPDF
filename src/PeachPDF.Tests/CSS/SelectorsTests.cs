using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS;

public class SelectorsTests
{
    [Fact]
    public async Task FindAllStyleRulesForAnElement()
    {
        // Arrange
        var sheet = await ParseBootstrapAsync();

        // Act
        var list = sheet.StyleRules
            .Where(r =>
                r.Selector is TypeSelector { Text: "input" }
                || (r.Selector is CompoundSelector selector && selector.First() is TypeSelector { Text: "input" })
            );

        // Assert
        Assert.Equal(6, list.Count());
    }

    [Fact]
    public async Task FindAllStyleRulesElementsWithMoreThanTwoCompoundSelectors()
    {
        // Arrange
        var sheet = await ParseBootstrapAsync();

        // Act
        var list = sheet.StyleRules
            .Where(r => (r.Selector as CompoundSelector)?.Length > 2);

        // Assert
        Assert.Equal(2, list.Count());
    }

    [Fact]
    public async Task FindAllStyleRulesWithCompoundSelector()
    {
        // Arrange
        var sheet = await ParseBootstrapAsync();

        // Act
        var list = sheet.StyleRules
            .Where(r => r.Selector is CompoundSelector selector && selector.Last().Text.StartsWith("["));

        // Assert
        Assert.Equal(7, list.Count());
    }

    private static readonly string[] _standardPseudoElementNames = new[] {
        PseudoElementNames.After,
        PseudoElementNames.Before,
        PseudoElementNames.Content,
        PseudoElementNames.FirstLetter,
        PseudoElementNames.FirstLine,
        PseudoElementNames.Selection
    };

    public static string[] StandardPseudoElementNames
    {
        get
        {
            return _standardPseudoElementNames;
        }
    }

    [Theory]
    [InlineData(false, false, 277)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 277)]
    [InlineData(true, true, 6)]
    public async Task FindAllStandardPseudoElementSelectors(bool allowInvalidSelectors,
                                                            bool nonStandard,
                                                            int expectedCount)
    {
        // Arrange
        var sheet = await ParseBootstrapAsync(allowInvalidSelectors);

        // Act
        var list = sheet.StyleRules
            .Where(r => HasStandardPseudoElementSelector(r.Selector, negate: nonStandard));

        // Assert
        Assert.Equal(expectedCount, list.Count());
    }

    private static bool HasStandardPseudoElementSelector(ISelector selector, bool negate = false)
    {
        if (selector is PseudoElementSelector pes)
            return negate ^ StandardPseudoElementNames.Contains(pes.Name);
        else if (selector is CompoundSelector comp)
            return HasStandardPseudoElementSelector(comp.Last(), negate);
        else if (selector is ListSelector list)
            return list.Any(s => HasStandardPseudoElementSelector(s, negate));
        else if (selector is ComplexSelector complex)
            return HasStandardPseudoElementSelector(complex.Last().Selector, negate);
        return false;
    }

    private async Task<Stylesheet> ParseBootstrapAsync(bool tolerateInvalidSelectors = false)
    {
        await using var stream = GetStream("bootstrap.css");
        var parser = new StylesheetParser(tolerateInvalidSelectors: tolerateInvalidSelectors);
        return await parser.ParseAsync(stream);
    }

    private Stream GetStream(string fileName)
    {
        var fullyQualifiedName = $"{GetType().Namespace}.{fileName}";
        return GetType().Assembly.GetManifestResourceStream(fullyQualifiedName);
    }
}






