using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

public class SelectorMatchingTests
{
    // ── Attribute: starts-with [attr^=value] ─────────────────────────────────

    [Fact]
    public async Task AttrBegins_Matches_WhenValueStartsWith()
    {
        var html = Html("[class^='btn'] { background-color: #ff0000; }", "<span class='btn-primary'>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task AttrBegins_DoesNotMatch_WhenValueDoesNotStartWith()
    {
        var html = Html("[class^='btn'] { background-color: #ff0000; }", "<span class='input-btn'>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── Attribute: ends-with [attr$=value] ───────────────────────────────────

    [Fact]
    public async Task AttrEnds_Matches_WhenValueEndsWith()
    {
        var html = Html("[lang$='-US'] { background-color: #ff0000; }", "<span lang='en-US'>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task AttrEnds_DoesNotMatch_WhenValueDoesNotEndWith()
    {
        var html = Html("[lang$='-US'] { background-color: #ff0000; }", "<span lang='US-en'>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── Attribute: hyphen-prefix [attr|=value] ───────────────────────────────

    [Fact]
    public async Task AttrHyphen_Matches_ExactValue()
    {
        var html = Html("[lang|='en'] { background-color: #ff0000; }", "<span lang='en'>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task AttrHyphen_Matches_HyphenPrefixed()
    {
        var html = Html("[lang|='en'] { background-color: #ff0000; }", "<span lang='en-US'>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task AttrHyphen_DoesNotMatch_SubstringOnly()
    {
        var html = Html("[lang|='en'] { background-color: #ff0000; }", "<span lang='english'>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── Combinator: adjacent sibling (div + p) ───────────────────────────────

    [Fact]
    public async Task AdjacentSibling_Matches_ImmediatelyFollowingSibling()
    {
        var html = Html("div + p { background-color: #ff0000; }", "<div>d</div><p id='t'>first</p>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
    }

    [Fact]
    public async Task AdjacentSibling_DoesNotMatch_SecondSibling()
    {
        var html = Html("div + p { background-color: #ff0000; }", "<div>d</div><p>first</p><p>second</p>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task AdjacentSibling_DoesNotMatch_PrecedingSibling()
    {
        var html = Html("div + p { background-color: #ff0000; }", "<p>before</p><div>d</div>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── Combinator: general sibling (div ~ p) ────────────────────────────────

    [Fact]
    public async Task GeneralSibling_Matches_AllFollowingSiblings()
    {
        var html = Html("div ~ p { background-color: #ff0000; }", "<div>d</div><p>first</p><p>second</p>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task GeneralSibling_DoesNotMatch_PrecedingSibling()
    {
        var html = Html("div ~ p { background-color: #ff0000; }", "<p>before</p><div>d</div>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── Regression: existing combinators still work ──────────────────────────

    [Fact]
    public async Task ChildCombinator_StillMatches_DirectChild()
    {
        var html = Html("div > p { background-color: #ff0000; }", "<div><p>direct</p><section><p>nested</p></section></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task DescendantCombinator_StillMatches_AnyDescendant()
    {
        var html = Html("div p { background-color: #ff0000; }", "<div><section><p>deep</p></section></div><p>outside</p>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
