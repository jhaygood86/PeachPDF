using PeachPDF.Adapters;
using PeachPDF.Html.Core;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Regression tests for <c>CssData.HasFirstLineRules</c> (issue #911): a document-level flag,
/// computed once in <c>CssData.EnsureIndex</c> via <c>CouldMatchAsFirstLineSelector</c>, that lets
/// <c>DomParser.ResolveFirstLineStyle</c> skip its two per-box candidate-gathering passes entirely on
/// documents (the overwhelming majority) that declare no <c>::first-line</c> rule at all. The flag
/// must stay false whenever nothing in the document could ever match via <c>::first-line</c>, and true
/// whenever something could - <c>CouldMatchAsFirstLineSelector</c> is a deliberate superset of
/// <c>MatchesAsFirstLineSelector</c> (same selector-shape switch, minus the per-box conjunct), so it
/// can never wrongly report false when a box would actually have matched.
/// </summary>
public class FirstLineRulesFlagTests
{
    [Fact]
    public async Task NoStylesheetRules_FlagStaysFalse()
    {
        var cssData = await BuildCssData("<p>text</p>");
        Assert.False(cssData.HasFirstLineRules);
    }

    [Fact]
    public async Task OrdinaryRulesWithNoFirstLine_FlagStaysFalse()
    {
        var cssData = await BuildCssData("<style>p { color: red; } .a { color: blue; } #b { color: green; }</style><p class='a' id='b'>text</p>");
        Assert.False(cssData.HasFirstLineRules);
    }

    [Fact]
    public async Task OtherPseudoElement_DoesNotSetFlag()
    {
        // ::before is a different pseudo-element entirely - must not be conflated with ::first-line.
        var cssData = await BuildCssData("<style>p::before { content: 'X'; }</style><p>text</p>");
        Assert.False(cssData.HasFirstLineRules);
    }

    [Fact]
    public async Task SimpleTagFirstLineRule_SetsFlag()
    {
        var cssData = await BuildCssData("<style>p::first-line { color: red; }</style><p>text</p>");
        Assert.True(cssData.HasFirstLineRules);
    }

    [Fact]
    public async Task ClassCompoundFirstLineRule_SetsFlag()
    {
        var cssData = await BuildCssData("<style>.a::first-line { color: red; }</style><p class='a'>text</p>");
        Assert.True(cssData.HasFirstLineRules);
    }

    [Fact]
    public async Task AncestorCombinatorFirstLineRule_SetsFlag()
    {
        var cssData = await BuildCssData("<style>article p::first-line { color: red; }</style><article><p>text</p></article>");
        Assert.True(cssData.HasFirstLineRules);
    }

    [Fact]
    public async Task ListSelectorWithFirstLineAlternative_SetsFlag()
    {
        // Only the second alternative is ::first-line - the flag must still be set (ListSelector's
        // Any() short-circuits true on the first matching alternative).
        var cssData = await BuildCssData("<style>div, p::first-line { color: red; }</style><p>text</p>");
        Assert.True(cssData.HasFirstLineRules);
    }

    [Fact]
    public async Task FirstLineRuleNestedInMediaQuery_SetsFlag()
    {
        // The flag is computed while indexing (which descends into @media/@layer/@supports/@container
        // the same way normal rule gathering does), so a first-line rule nested inside a grouping
        // at-rule must still be found - regardless of whether that media query would ever match.
        var cssData = await BuildCssData("<style>@media print { p::first-line { color: red; } }</style><p>text</p>");
        Assert.True(cssData.HasFirstLineRules);
    }

    private static async Task<CssData> BuildCssData(string body)
    {
        var adapter = new PdfSharpAdapter();
        var container = new HtmlContainerInt(adapter);
        await container.SetHtml($"<!DOCTYPE html><html><head></head><body>{body}</body></html>", null);

        Assert.NotNull(container.CssData);
        return container.CssData!;
    }
}
