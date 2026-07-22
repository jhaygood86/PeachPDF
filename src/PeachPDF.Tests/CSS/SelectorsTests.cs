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

    [Theory]
    // "Whitespace is permitted on either side of the + or - that separates the An and B parts when both
    // are present" (CSS Syntax 3 §6.1), which lists "3n + 1" among its valid examples. The sign then
    // arrives as a standalone delim token rather than as part of a signed <number>.
    [InlineData(":nth-child(10n + 1)", 10, 1)]
    [InlineData(":nth-child(10n - 1)", 10, -1)]
    [InlineData(":nth-child(-2n + 3)", -2, 3)]
    [InlineData(":nth-child( 10n  +  1 )", 10, 1)]
    // The compact form has always worked and must keep working.
    [InlineData(":nth-child(10n+1)", 10, 1)]
    [InlineData(":nth-child(odd)", 2, 1)]
    [InlineData(":nth-last-child(3n + 2)", 3, 2)]
    [InlineData(":nth-of-type(3n + 2)", 3, 2)]
    [InlineData(":nth-last-of-type(3n + 2)", 3, 2)]
    public void NthChildAcceptsAnBWithAndWithoutSpaces(string selector, int step, int offset)
    {
        var sheet = new StylesheetParser().Parse(selector + " { color: red }");

        Assert.Equal(1, sheet.Rules.Length);
        var child = Assert.IsAssignableFrom<ChildSelector>(((StyleRule)sheet.Rules[0]).Selector);
        Assert.Equal(step, child.Step);
        Assert.Equal(offset, child.Offset);
    }

    [Theory]
    // The production requires a <signless-integer> after a standalone sign, so a second sign is invalid.
    // CSS Syntax 3 §6.1 lists "3n + -6" among its invalid examples, alongside "3 n", "+ 2n" and "+ 2".
    [InlineData(":nth-child(3n + -6)")]
    [InlineData(":nth-child(10n + +1)")]
    [InlineData(":nth-child(10n + + 1)")]
    [InlineData(":nth-child(10n + n)")]
    [InlineData(":nth-child(3 n)")]
    [InlineData(":nth-child(+ 2n)")]
    [InlineData(":nth-child(+ 2)")]
    public void NthChildRejectsMalformedAnB(string selector)
    {
        var sheet = new StylesheetParser().Parse(selector + " { color: red }");

        Assert.Equal(0, sheet.Rules.Length);
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
        return GetType().Assembly.GetManifestResourceStream(fullyQualifiedName)!;
    }
}






