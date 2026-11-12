using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS Containment 2 §3.3: with style containment, the counters its subtree increments or sets and the quote nesting inside it are scoped to the subtree. What
/// follows the box sees the counter and the quote depth it had before the box, and inside it the quote nesting starts again at 0.
/// </summary>
public class StyleContainmentTests
{
    private const string Css =
        ".n { counter-increment: c; margin: 0 } .n::before { content: counter(c) '.' } .r::before { content: counter(c) '|' } .r { margin: 0 }" +
        ".q { display: block; margin: 0; quotes: '<' '>' '[' ']' } .q::before { content: open-quote } .q::after { content: close-quote }";

    private static async Task<List<string>> BeforeTextsAsync(string body)
    {
        var html = $"<!DOCTYPE html><html><head><style>{Css}</style></head><body style='margin:0'>{body}</body></html>";
        var (root, _) = await LayoutHarness.LayoutAsync(html);

        return [.. LayoutHarness.Descendants(root)
            .Where(b => b.IsBeforePseudoElement)
            .Select(b => string.Concat(LayoutHarness.Descendants(b).Distinct().SelectMany(x => x.Words).Select(w => w.Text)).Trim())];
    }

    private static int[] Numbers(IEnumerable<string> texts) =>
        [.. texts.SelectMany(t => System.Text.RegularExpressions.Regex.Matches(t, "[0-9]+").Select(m => int.Parse(m.Value)))];

    [Theory]
    // No containment: one counter all the way through.
    [InlineData("", "1.|2.|3.|4.")]
    [InlineData("contain:layout paint", "1.|2.|3.|4.")]
    // The two inside count from a fresh counter, and what follows continues the outer one.
    [InlineData("contain:style", "1.|1.|2.|2.")]
    [InlineData("contain:content", "1.|1.|2.|2.")]
    [InlineData("contain:strict", "1.|1.|2.|2.")]
    [InlineData("content-visibility:auto", "1.|1.|2.|2.")]
    public async Task StyleContainmentScopesTheCountersItsSubtreeIncrements(string style, string expected)
    {
        var texts = await BeforeTextsAsync(
            $"<div style='counter-reset:c'><p class='n'>a</p><div style='{style}'><p class='n'>b</p><p class='n'>c</p></div><p class='n'>d</p></div>");

        Assert.Equal(expected, string.Join("|", texts));
    }

    [Fact]
    public async Task TheContainedBoxsOwnIncrementIsScopedToItsSubtreeToo()
    {
        var texts = await BeforeTextsAsync(
            "<div style='counter-reset:c'><p class='n'>a</p><div class='n' style='contain:style'><p class='r'>inside</p></div><p class='n'>d</p></div>");

        // The box's own n::before reads 1 (its increment is inside), the child reads that 1, and the paragraph after continues the outer counter: 2.
        Assert.Equal([1, 1, 1, 2], Numbers(texts));
    }

    [Fact]
    public async Task ACounterThatNothingInsideIncrementsIsTheOuterOneInside()
    {
        var texts = await BeforeTextsAsync(
            "<div style='counter-reset:c'><p class='n'>a</p><div style='contain:style'><p class='r'>read</p></div><p class='n'>d</p></div>");

        Assert.Equal([1, 1, 2], Numbers(texts));
    }

    [Fact]
    public async Task NestedStyleContainmentScopesEachLevel()
    {
        var texts = await BeforeTextsAsync(
            "<div style='counter-reset:c'><p class='n'>a</p><div style='contain:style'><p class='n'>b</p><div style='contain:style'><p class='n'>c</p></div><p class='n'>d</p></div><p class='n'>e</p></div>");

        // a=1; inside the outer box 1 (b), the inner box starts again (c=1), d=2 continues the outer box's counter, and e=2 continues the document's.
        Assert.Equal("1.|1.|1.|2.|2.", string.Join("|", texts));
    }

    [Theory]
    [InlineData("", "<|[|<")]
    [InlineData("contain:style", "<|<|<")]
    public async Task StyleContainmentStartsTheQuoteNestingAgainInsideItAndRestoresItAfter(string style, string expected)
    {
        var texts = await BeforeTextsAsync(
            $"<div class='q'>x<div style='{style}'><div class='q'>y</div></div></div><div class='q'>z</div>");

        // Outer quote: depth 0 '<'. Inner: depth 1 '[' normally, but depth 0 again inside a contained box. After the first outer quote closes, the next is back at depth 0.
        Assert.Equal(expected, string.Join("|", texts));
    }

    [Fact]
    public async Task WhatAStyleContainedBoxOpensAndClosesDoesNotChangeTheDepthAfterIt()
    {
        var texts = await BeforeTextsAsync(
            "<div class='q'>x<div style='contain:style'><div class='q'>y<div class='q'>deep</div></div></div><div class='q'>after</div></div>");

        // The outer quote is at depth 0 and the box after the contained one is at depth 1, as if the contained box had not been there.
        Assert.Equal("<|<|[|[", string.Join("|", texts));
    }
}
