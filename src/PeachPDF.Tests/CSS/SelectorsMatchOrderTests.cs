using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// <see cref="Selectors.MatchOrder"/> is a cached, cost-sorted view over a compound/list selector's
/// members, kept separate from their own source order (which <c>ToCss</c>/<c>Text</c>, <c>Specificity</c>,
/// and the "a trailing PseudoElementSelector is structurally last" convention all still rely on
/// unchanged). These tests build selectors by hand in a deliberately expensive-first order and assert
/// <c>MatchOrder</c> re-ranks them cheapest first - correctness of the ranking itself, independent of the
/// allocation behavior asserted in <c>CssDataSelectorMatchAllocationTests</c>.
/// </summary>
public class SelectorsMatchOrderTests
{
    [Fact]
    public void CompoundSelector_RanksCheapSelectorBeforeExpensiveOne_RegardlessOfAddOrder()
    {
        var expensive = ParseSelector(":has(.x)");
        var cheap = ParseSelector("#id");

        var compound = new CompoundSelector();
        compound.Add(expensive);
        compound.Add(cheap);

        var order = compound.MatchOrder;

        Assert.Equal(2, order.Length);
        Assert.Same(cheap, compound[order[0]]);
        Assert.Same(expensive, compound[order[1]]);

        // Source order (what ToCss/Text/Last() see) must be untouched by building MatchOrder.
        Assert.Same(expensive, compound[0]);
        Assert.Same(cheap, compound[1]);
    }

    [Fact]
    public void ListSelector_RanksCheapAlternativeBeforeExpensiveOne_RegardlessOfAddOrder()
    {
        var expensive = ParseSelector(":has(.x)");
        var cheap = ParseSelector(".cls");

        var list = new ListSelector();
        list.Add(expensive);
        list.Add(cheap);

        var order = list.MatchOrder;

        Assert.Equal(2, order.Length);
        Assert.Same(cheap, list[order[0]]);
        Assert.Same(expensive, list[order[1]]);

        Assert.Same(expensive, list[0]);
        Assert.Same(cheap, list[1]);
    }

    [Fact]
    public void MatchOrder_IsStableAcrossRepeatedAccess()
    {
        var compound = new CompoundSelector();
        compound.Add(ParseSelector(":has(.x)"));
        compound.Add(ParseSelector("#id"));
        compound.Add(ParseSelector(".cls"));

        var first = compound.MatchOrder;
        var second = compound.MatchOrder;

        Assert.Same(first, second);
    }

    private static ISelector ParseSelector(string selectorText)
    {
        var sheet = new StylesheetParser().Parse(selectorText + " { color: red }");
        return ((StyleRule)sheet.Rules[0]).Selector;
    }
}
