using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.PDF.Engine.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// <c>CssData.DoesSelectorMatch(CompoundSelector, ICssDomNode?)</c> now evaluates a compound selector's
/// members in <see cref="Selectors.MatchOrder"/> (cheapest first) rather than source order - this is the
/// first place this codebase intentionally evaluates selectors out of source order, so it's worth a
/// direct end-to-end assertion that a compound selector's AND result is unaffected by which conjunct
/// happens to be cheap, rather than relying only on the broader suite's implicit coverage. Every case
/// below pairs a cheap selector (<c>#id</c>) with the single most expensive kind (<c>:has()</c>), since
/// that's the pair with the largest gap in <see cref="SelectorMatchCost"/> and therefore the one most
/// likely to actually reorder.
/// </summary>
public class SelectorMatchOrderIndependenceTests
{
    [Fact]
    public async Task CheapSelectorFails_ExpensiveSelectorWouldPass_CompoundStillDoesNotMatch()
    {
        var html = Html(
            "#wrong-id:has(.child) { background-color: #ff0000; }",
            "<div id='real' class='parent'><span class='child'></span></div>");

        var box = await FindBoxByTag(html, "div");

        Assert.Equal("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task CheapSelectorPasses_ExpensiveSelectorFails_CompoundStillDoesNotMatch()
    {
        var html = Html(
            "#real:has(.missing) { background-color: #ff0000; }",
            "<div id='real' class='parent'><span class='child'></span></div>");

        var box = await FindBoxByTag(html, "div");

        Assert.Equal("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task BothCheapAndExpensiveSelectorPass_CompoundMatches()
    {
        var html = Html(
            "#real:has(.child) { background-color: #ff0000; }",
            "<div id='real' class='parent'><span class='child'></span></div>");

        var box = await FindBoxByTag(html, "div");

        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    private static string Html(string css, string body) =>
        $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

    private async Task<CssBox> FindBoxByTag(string html, string tag)
    {
        var root = await BuildRoot(html);
        var box = DomUtils.GetBoxByTagName(root, tag);
        Assert.NotNull(box);
        return box!;
    }

    private static async Task<CssBox> BuildRoot(string html)
    {
        var renderContext = new HtmlRenderContext();
        var container = new HtmlContainerInt(renderContext);
        await container.SetHtml(html, null);

        var size = new Size(595, 842);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);

        return container.Root!;
    }
}
