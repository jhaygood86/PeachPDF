using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// <see cref="SelectorMatchCost"/> ranks selector kinds cheapest-to-evaluate first so
/// <c>CssData.DoesSelectorMatch</c>'s indexed <c>All</c>/<c>Any</c> loops (see
/// <see cref="Selectors.MatchOrder"/>) can short-circuit on a cheap check before an expensive one ever
/// runs. These tests pin down the ordering itself, independent of the allocation behavior asserted in
/// <c>CssDataSelectorMatchAllocationTests</c>.
/// </summary>
public class SelectorMatchCostTests
{
    [Theory]
    [InlineData("#id")]
    [InlineData(".cls")]
    [InlineData("div")]
    [InlineData("*")]
    public void Tier0Selectors_AreCheapestOfEverything(string selectorText)
    {
        var tier0 = ParseSelector(selectorText);
        var tier1 = ParseSelector("[attr]");
        var tier2 = ParseSelector(":root");
        var tier3 = ParseSelector(":only-child");
        var tier4 = ParseSelector(":lang(en)");
        var tier5 = ParseSelector(":not(.x)");
        var tier6 = ParseSelector(":has(.x)");

        var cost = SelectorMatchCost.Of(tier0);
        Assert.True(cost < SelectorMatchCost.Of(tier1));
        Assert.True(cost < SelectorMatchCost.Of(tier2));
        Assert.True(cost < SelectorMatchCost.Of(tier3));
        Assert.True(cost < SelectorMatchCost.Of(tier4));
        Assert.True(cost < SelectorMatchCost.Of(tier5));
        Assert.True(cost < SelectorMatchCost.Of(tier6));
    }

    [Theory]
    [InlineData("[attr]")]
    [InlineData("[attr=val]")]
    [InlineData("[attr~=val]")]
    [InlineData("[attr^=val]")]
    [InlineData("[attr$=val]")]
    [InlineData("[attr*=val]")]
    [InlineData("[attr|=val]")]
    public void AttributeSelectors_CostMoreThanTier0ButLessThanPseudoClass(string selectorText)
    {
        var attr = ParseSelector(selectorText);
        var tier0 = ParseSelector("div");
        var pseudoClass = ParseSelector(":root");

        var cost = SelectorMatchCost.Of(attr);
        Assert.True(cost > SelectorMatchCost.Of(tier0));
        Assert.True(cost < SelectorMatchCost.Of(pseudoClass));
    }

    [Fact]
    public void PseudoClassSelector_CostsLessThanStructuralChildSelectors()
    {
        var root = ParseSelector(":root");
        var onlyChild = ParseSelector(":only-child");

        Assert.True(SelectorMatchCost.Of(root) < SelectorMatchCost.Of(onlyChild));
    }

    [Theory]
    [InlineData(":nth-child(2)")]
    [InlineData(":first-child")]
    [InlineData(":last-child")]
    [InlineData(":first-of-type")]
    [InlineData(":last-of-type")]
    [InlineData(":only-child")]
    [InlineData(":only-of-type")]
    public void StructuralSelectors_CostLessThanLangAndMoreThanPseudoClass(string selectorText)
    {
        var structural = ParseSelector(selectorText);
        var pseudoClass = ParseSelector(":root");
        var lang = ParseSelector(":lang(en)");

        var cost = SelectorMatchCost.Of(structural);
        Assert.True(cost > SelectorMatchCost.Of(pseudoClass));
        Assert.True(cost < SelectorMatchCost.Of(lang));
    }

    [Fact]
    public void LangSelector_CostsLessThanNotAndIs()
    {
        var lang = ParseSelector(":lang(en)");
        var not = ParseSelector(":not(.x)");
        var matches = ParseSelector(":is(.x)");

        Assert.True(SelectorMatchCost.Of(lang) < SelectorMatchCost.Of(not));
        Assert.True(SelectorMatchCost.Of(lang) < SelectorMatchCost.Of(matches));
    }

    [Fact]
    public void HasSelector_IsTheMostExpensiveKind()
    {
        var has = ParseSelector(":has(.x)");
        var not = ParseSelector(":not(.x)");
        var matches = ParseSelector(":is(.x)");
        var lang = ParseSelector(":lang(en)");

        var cost = SelectorMatchCost.Of(has);
        Assert.True(cost > SelectorMatchCost.Of(not));
        Assert.True(cost > SelectorMatchCost.Of(matches));
        Assert.True(cost > SelectorMatchCost.Of(lang));
    }

    private static ISelector ParseSelector(string selectorText)
    {
        var sheet = new StylesheetParser().Parse(selectorText + " { color: red }");
        return ((StyleRule)sheet.Rules[0]).Selector;
    }
}
