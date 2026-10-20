using PeachBrackets.Css;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.CSS;

/// <summary>
/// The cascade lets an element take the computed style of an earlier sibling with the same tag and attributes only when no rule
/// that could reach it depends on its place among its siblings or on what it contains. <see cref="SelectorSiblingStability"/> is
/// that judgement on one selector, and it is an allowlist: a selector it does not know is not stable.
/// </summary>
public class SelectorSiblingStabilityTests
{
    [Theory]
    [InlineData("td")]
    [InlineData("*")]
    [InlineData(".a")]
    [InlineData("#i")]
    [InlineData("td.a#i")]
    [InlineData("[x]")]
    [InlineData("[x=y]")]
    [InlineData("[x~=y]")]
    [InlineData("[x^=y]")]
    [InlineData("[x$=y]")]
    [InlineData("[x*=y]")]
    [InlineData("[x|=y]")]
    [InlineData("a[href]")]
    [InlineData("td, th")]
    [InlineData("tr td")]
    [InlineData("tr > td")]
    [InlineData(".a .b .c")]
    [InlineData("table.x td.y")]
    [InlineData(":lang(en)")]
    [InlineData(":root")]
    [InlineData(":scope")]
    [InlineData("a:link")]
    [InlineData("a:any-link")]
    [InlineData("a:hover")]
    [InlineData("input:checked")]
    [InlineData("input:disabled")]
    [InlineData(":not(.x)")]
    [InlineData(":not(.x, .y)")]
    [InlineData(":is(.a, .b)")]
    [InlineData(":where(.a)")]
    [InlineData("td:not(.x)")]
    [InlineData("td::before")]
    [InlineData("td::first-letter")]
    // Only the last part of a complex selector is asked about the element; the parts before it are reached by walking up the tree,
    // where every sibling finds the same elements.
    [InlineData("li:first-child a")]
    [InlineData("li:nth-child(2) a")]
    [InlineData("h1 + p span")]
    [InlineData("h1 ~ p > span")]
    [InlineData("div:has(.x) p")]
    [InlineData("tr:empty td")]
    public void ASelectorThatAsksOnlyAboutTheElementAndItsAncestors_IsStable(string selector)
    {
        Assert.True(SelectorSiblingStability.IsStable(Parse(selector)), selector);
    }

    [Theory]
    [InlineData("td:first-child")]
    [InlineData("td:last-child")]
    [InlineData("td:only-child")]
    [InlineData("td:first-of-type")]
    [InlineData("td:last-of-type")]
    [InlineData("td:only-of-type")]
    [InlineData("td:nth-child(2n)")]
    [InlineData("td:nth-last-child(1)")]
    [InlineData("td:nth-of-type(odd)")]
    [InlineData("td:nth-last-of-type(2)")]
    [InlineData("td:nth-child(2n + 1 of .x)")]
    [InlineData(":nth-column(2)")]
    [InlineData(":nth-last-column(1)")]
    [InlineData(":empty")]
    [InlineData("td:empty")]
    [InlineData("textarea:placeholder-shown")]
    [InlineData("tr:has(b)")]
    [InlineData(":has(> b)")]
    [InlineData("h1 + p")]
    [InlineData("h1 ~ p")]
    [InlineData("tr td + td")]
    [InlineData(".a ~ .b")]
    [InlineData("td:not(:first-child)")]
    [InlineData("td:not(:empty)")]
    [InlineData(":is(td:first-child, th)")]
    [InlineData(":where(:last-child)")]
    [InlineData("td, th:first-child")]
    [InlineData("td:first-child, td")]
    [InlineData("div td:nth-child(2)")]
    public void ASelectorThatAsksAboutPositionOrContent_IsNotStable(string selector)
    {
        Assert.False(SelectorSiblingStability.IsStable(Parse(selector)), selector);
    }

    [Theory]
    [InlineData("p::before", 1)]
    [InlineData("p::after", 1)]
    [InlineData("li::marker", 1)]
    [InlineData("p::first-letter", 1)]
    [InlineData("p:before", 1)]
    [InlineData(".a p::before, .b", 1)]
    [InlineData("p::before, p::after", 2)]
    [InlineData(".a .b p::after", 1)]
    [InlineData("p::first-line", 0)]
    [InlineData("p::placeholder", 0)]
    [InlineData("p::selection", 0)]
    [InlineData("p", 0)]
    [InlineData("p:first-child", 0)]
    [InlineData("p ::before", 1)]   // parsed as "p *::before"
    [InlineData("::before", 0)]
    public void OnlyACompoundEndingInABoxCreatingPseudoElement_IsASubject(string selector, int subjects)
    {
        var found = new List<CompoundSelector>();

        SelectorSiblingStability.CollectBoxCreatingSubjects(Parse(selector), found);

        Assert.Equal(subjects, found.Count);
    }

    private static ISelector Parse(string selectorText)
    {
        var sheet = new StylesheetParser().Parse(selectorText + " { color: red }");
        return ((StyleRule)sheet.Rules[0]).Selector;
    }
}
