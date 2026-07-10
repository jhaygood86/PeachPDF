using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Regression tests for the structural pseudo-class family: bare :first-child/:last-child/
/// :only-child/:first-of-type/:last-of-type/:only-of-type (previously non-functional generic
/// PseudoClassSelectors), :nth-child()/:nth-last-child()/:nth-of-type()/:nth-last-of-type()
/// (previously off-by-one and missing from the matching switch), and :nth-column()/
/// :nth-last-column() (previously missing from the matching switch entirely).
/// </summary>
public class StructuralPseudoClassSelectorTests
{
    // ── :first-child ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstChild_Matches_FirstElement()
    {
        var html = Html(":first-child { background-color: #ff0000; }", "<div><p>first</p><p>second</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    // ── :last-child ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LastChild_Matches_LastElement()
    {
        var html = Html(":last-child { background-color: #ff0000; }", "<div><p>first</p><p>second</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
    }

    // ── :only-child ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyChild_Matches_WhenNoSiblings()
    {
        var html = Html(":only-child { background-color: #ff0000; }", "<div><p>alone</p></div>");
        var box = await FindBoxByTag(html, "p");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task OnlyChild_DoesNotMatch_WhenSiblingsPresent()
    {
        var html = Html(":only-child { background-color: #ff0000; }", "<div><p>first</p><p>second</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.All(boxes, b => Assert.Equal("transparent", b.BackgroundColor));
    }

    // ── :nth-child() — literal, formula, keywords, negative offset ────────────

    [Fact]
    public async Task NthChild_Literal_MatchesOnlyThatPosition()
    {
        var html = Html(":nth-child(2) { background-color: #ff0000; }", "<div><p>1</p><p>2</p><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(3, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
        Assert.Equal("transparent", boxes[2].BackgroundColor);
    }

    [Fact]
    public async Task NthChild_2n_MatchesEvenPositions()
    {
        var html = Html(":nth-child(2n) { background-color: #ff0000; }", "<div><p>1</p><p>2</p><p>3</p><p>4</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
        Assert.Equal("transparent", boxes[2].BackgroundColor);
        Assert.NotEqual("transparent", boxes[3].BackgroundColor);
    }

    [Fact]
    public async Task NthChild_2nPlus1_MatchesOddPositions()
    {
        var html = Html(":nth-child(2n+1) { background-color: #ff0000; }", "<div><p>1</p><p>2</p><p>3</p><p>4</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
        Assert.NotEqual("transparent", boxes[2].BackgroundColor);
        Assert.Equal("transparent", boxes[3].BackgroundColor);
    }

    [Fact]
    public async Task NthChild_Odd_MatchesOddPositions()
    {
        var html = Html(":nth-child(odd) { background-color: #ff0000; }", "<div><p>1</p><p>2</p><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
        Assert.NotEqual("transparent", boxes[2].BackgroundColor);
    }

    [Fact]
    public async Task NthChild_Even_MatchesEvenPositions()
    {
        var html = Html(":nth-child(even) { background-color: #ff0000; }", "<div><p>1</p><p>2</p><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
        Assert.Equal("transparent", boxes[2].BackgroundColor);
    }

    [Fact]
    public async Task NthChild_NegativeStep_MatchesFirstNPositions()
    {
        // "-n+3" matches positions 1, 2, 3 and nothing beyond.
        var html = Html(":nth-child(-n+3) { background-color: #ff0000; }", "<div><p>1</p><p>2</p><p>3</p><p>4</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
        Assert.NotEqual("transparent", boxes[2].BackgroundColor);
        Assert.Equal("transparent", boxes[3].BackgroundColor);
    }

    [Fact]
    public async Task NthLastChild_1_MatchesLastElement()
    {
        var html = Html(":nth-last-child(1) { background-color: #ff0000; }", "<div><p>1</p><p>2</p><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
        Assert.NotEqual("transparent", boxes[2].BackgroundColor);
    }

    // ── :first-of-type / :last-of-type / :only-of-type / :nth-of-type() ──────
    // Mixed-tag siblings prove type-filtering isn't just reusing :nth-child's all-elements scope.

    [Fact]
    public async Task FirstOfType_Matches_FirstOfItsOwnTagOnly()
    {
        var html = Html("p:first-of-type { background-color: #ff0000; }", "<div><span>s</span><p>1</p><p>2</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task LastOfType_Matches_LastOfItsOwnTagOnly()
    {
        var html = Html("p:last-of-type { background-color: #ff0000; }", "<div><p>1</p><p>2</p><span>s</span></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task OnlyOfType_Matches_WhenNoOtherSiblingSharesTag()
    {
        var html = Html("p:only-of-type { background-color: #ff0000; }", "<div><span>s</span><p>1</p></div>");
        var box = await FindBoxByTag(html, "p");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task OnlyOfType_DoesNotMatch_WhenAnotherSameTagSiblingExists()
    {
        var html = Html("p:only-of-type { background-color: #ff0000; }", "<div><p>1</p><p>2</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.All(boxes, b => Assert.Equal("transparent", b.BackgroundColor));
    }

    [Fact]
    public async Task NthOfType_2_MatchesSecondOfItsOwnTagOnly()
    {
        var html = Html("p:nth-of-type(2) { background-color: #ff0000; }", "<div><span>s</span><p>1</p><span>s2</span><p>2</p><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(3, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
        Assert.Equal("transparent", boxes[2].BackgroundColor);
    }

    [Fact]
    public async Task NthLastOfType_1_MatchesLastOfItsOwnTagOnly()
    {
        var html = Html("p:nth-last-of-type(1) { background-color: #ff0000; }", "<div><p>1</p><span>s</span><p>2</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
    }

    // ── Compound forms ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TagPlusNthChild_MatchesOnlyMatchingTagAtThatPosition()
    {
        var html = Html("p:nth-child(2n+1) { background-color: #ff0000; }", "<div><p>1</p><span>2</span><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor); // <p> at position 1 (odd) - matches
        Assert.NotEqual("transparent", boxes[1].BackgroundColor); // <p> at position 3 (odd) - matches
    }

    [Fact]
    public async Task ClassPlusFirstChild_MatchesOnlyWhenBothConditionsHold()
    {
        var html = Html(".item:first-child { background-color: #ff0000; }", "<div><p class='item'>1</p><p class='item'>2</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task ClassPlusFirstChild_DoesNotMatch_WhenFirstChildLacksClass()
    {
        var html = Html(".item:first-child { background-color: #ff0000; }", "<div><p>1</p><p class='item'>2</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.All(boxes, b => Assert.Equal("transparent", b.BackgroundColor));
    }

    // ── :nth-column() / :nth-last-column() ────────────────────────────────────

    [Fact]
    public async Task NthColumn_MatchesAnyOccupiedColumnOfAColspanCell()
    {
        // Row occupies 4 columns total: A=col0, B(colspan=2)=cols1-2, C=col3.
        // nth-column(2) should match B, since one of its occupied columns is position 2.
        var html = Html(
            "td:nth-column(2) { background-color: #ff0000; }",
            "<table><tr><td>A</td><td colspan='2'>B</td><td>C</td></tr></table>");
        var boxes = await FindAllBoxesByTag(html, "td");
        Assert.Equal(3, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor); // A
        Assert.NotEqual("transparent", boxes[1].BackgroundColor); // B
        Assert.Equal("transparent", boxes[2].BackgroundColor); // C
    }

    [Fact]
    public async Task NthLastColumn_1_MatchesLastColumnOnly()
    {
        var html = Html(
            "td:nth-last-column(1) { background-color: #ff0000; }",
            "<table><tr><td>A</td><td colspan='2'>B</td><td>C</td></tr></table>");
        var boxes = await FindAllBoxesByTag(html, "td");
        Assert.Equal(3, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor); // A
        Assert.Equal("transparent", boxes[1].BackgroundColor); // B
        Assert.NotEqual("transparent", boxes[2].BackgroundColor); // C, the last of 4 columns
    }

    // ── Helpers (mirrors SelectorMatchingTests.cs conventions) ────────────────

    private static string Html(string css, string body) =>
        $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

    private async Task<CssBox> FindBoxByTag(string html, string tag)
    {
        var root = await BuildRoot(html);
        var box = DomUtils.GetBoxByTagName(root, tag);
        Assert.NotNull(box);
        return box!;
    }

    private async Task<List<CssBox>> FindAllBoxesByTag(string html, string tag)
    {
        var root = await BuildRoot(html);
        var results = new List<CssBox>();
        CollectByTag(root, tag, results);
        return results;
    }

    private static void CollectByTag(CssBox box, string tag, List<CssBox> results)
    {
        if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
            results.Add(box);
        foreach (var child in box.Boxes)
            CollectByTag(child, tag, results);
    }

    private static async Task<CssBox> BuildRoot(string html)
    {
        var adapter = new PdfSharpAdapter();
        var container = new HtmlContainerInt(adapter);
        await container.SetHtml(html, null);

        var size = new XSize(595, 842);
        container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

        var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
        using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
        await container.PerformLayout(graphics);

        Assert.NotNull(container.Root);
        return container.Root!;
    }
}
