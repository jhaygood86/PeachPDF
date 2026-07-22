using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Cascade-layer (<c>@layer</c>) support, per CSS Cascade 5. Before this, an <c>@layer</c> block was
/// an unknown at-rule whose contents were skipped wholesale — so a utility-framework stylesheet that
/// wraps everything in layers rendered almost nothing. These assert the layer rules are both applied
/// and correctly ordered (later layer wins; unlayered beats layered; specificity breaks ties within a
/// layer; a leading order-declaring statement establishes precedence regardless of block order).
/// </summary>
public class CascadeLayerIntegrationTests
{
    [Fact]
    public async Task LayerBlock_Rules_AreApplied_NotDropped()
    {
        var html = Html(
            "@layer base { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task LaterLayer_Wins_OverEarlierLayer_AtEqualSpecificity()
    {
        // base is declared before components; per CSS Cascade 5 the later layer wins, even though both
        // rules have identical (type-selector) specificity.
        var html = Html(
            "@layer base { p { color: #ff0000; } } @layer components { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task LaterLayer_Wins_EvenAgainstHigherSpecificityInEarlierLayer()
    {
        // The earlier layer's rule has higher specificity (id), but layer precedence sorts ahead of
        // specificity: the later layer's low-specificity rule still wins.
        var html = Html(
            "@layer a { #x { color: #ff0000; } } @layer b { p { color: #0000ff; } }",
            "<p id='x'>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task UnlayeredRule_Wins_OverAnyLayeredRule()
    {
        // An unlayered normal declaration outranks a layered one regardless of specificity.
        var html = Html(
            "@layer utilities { p#x { color: #ff0000; } } p { color: #0000ff; }",
            "<p id='x'>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task StatementDeclaresOrder_RegardlessOfBlockOrder()
    {
        // The leading "@layer base, utilities;" statement fixes the order (utilities after base), so
        // utilities wins even though its block is written BEFORE base's block in source. This is the
        // exact pattern utility frameworks emit ("@layer theme, base, components, utilities;" first).
        var html = Html(
            "@layer base, utilities; @layer utilities { p { color: #0000ff; } } @layer base { p { color: #ff0000; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task WithinOneLayer_SpecificityStillBreaksTies()
    {
        var html = Html(
            "@layer base { p { color: #ff0000; } p.hi { color: #0000ff; } }",
            "<p class='hi'>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task DottedLayerName_IsParsedAndApplied()
    {
        // A nested/dotted layer name (`@layer framework.utilities`) parses and its rules apply.
        var html = Html(
            "@layer framework.utilities { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task AnonymousLayer_Rules_AreApplied_AndBeatenByUnlayered()
    {
        // An anonymous @layer { } is its own distinct layer; its rules apply but lose to an unlayered rule.
        var html = Html(
            "@layer { p { color: #ff0000; } } p { color: #0000ff; }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

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
