using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// #237 gap 3: an <c>@font-face</c>/<c>@property</c>/<c>@page</c> rule nested inside an <c>@layer</c>
/// block (or an <c>@media</c>/<c>@supports</c> inside a layer) must still be collected. Before this,
/// the three collectors only scanned top-level rules, so wrapping them in a layer — the exact shape a
/// utility framework emits — silently dropped them. <c>CssData.EnumerateRulesRecursive</c> now descends
/// the grouping at-rules, and the collectors use it.
/// </summary>
public class CascadeLayerAtRuleCollectionTests
{
    // ── The shared recursive enumerator surfaces every nested at-rule ────────────

    [Fact]
    public async Task EnumerateRulesRecursive_FindsAtRulesNestedInLayers()
    {
        const string css =
            "@layer base { @font-face { font-family: L; src: url(a.woff2); } " +
            "  @property --x { syntax: \"<color>\"; initial-value: red; inherits: false; } " +
            "  @page { size: 20mm 30mm; } }" +
            "@layer wrap { @media print { @page { margin: 5mm; } } }";

        var cssData = await CssData.Parse(new PdfSharpAdapter(), css, combineWithDefault: false);
        var all = cssData.EnumerateRulesRecursive().ToList();

        Assert.Single(all.OfType<IFontFaceRule>());
        Assert.Single(all.OfType<IPropertyRule>());
        Assert.Equal(2, all.OfType<IPageRule>().Count()); // one directly in a layer, one in @media in a layer
    }

    // ── @property inside a layer is registered (its var() resolves) ──────────────

    [Fact]
    public async Task PropertyInsideLayer_IsRegistered_AndItsInitialValueResolves()
    {
        // --swatch is defined ONLY via @property (inside a layer); with no author declaration setting it,
        // var(--swatch) must resolve to the registered initial-value — proving the @property was collected.
        var html = Html(
            "@layer tokens { @property --swatch { syntax: \"<color>\"; initial-value: #0000ff; inherits: false; } }" +
            " p { color: var(--swatch); }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    // ── @page inside a layer applies its base page geometry ──────────────────────

    [Fact]
    public async Task PageInsideLayer_AppliesPageSize()
    {
        var html = Html("@layer print { @page { size: 20mm 40mm; } }", "<p>text</p>");
        var container = await BuildContainer(html);
        Assert.NotNull(container.CssPageSize);
        Assert.Equal(20 * 72.0 / 25.4, container.CssPageSize!.Value.Width, 3);
        Assert.Equal(40 * 72.0 / 25.4, container.CssPageSize!.Value.Height, 3);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string Html(string css, string body) =>
        $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

    private static async Task<HtmlContainerInt> BuildContainer(string html)
    {
        var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(adapter);

        var size = new XSize(595, 842);
        container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
        await container.SetHtml(html, null);

        var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
        using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
        await container.PerformLayout(graphics);
        return container;
    }

    private static async Task<CssBox> FindBoxByTag(string html, string tag)
    {
        var container = await BuildContainer(html);
        var box = DomUtils.GetBoxByTagName(container.Root!, tag);
        Assert.NotNull(box);
        return box!;
    }
}
