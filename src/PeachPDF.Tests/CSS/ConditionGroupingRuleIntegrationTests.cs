using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// <c>@supports</c> and <c>@container</c> block handling. PeachPDF has no supports/container-query engine
/// in the render layer, so it cannot evaluate their conditions. Rather than apply rules guarded by a
/// condition it can't test (which would apply a <c>not</c>-guarded fallback and its enhanced counterpart
/// at once), PeachPDF <b>ignores the whole block</b> — the inner rules are never indexed and never apply.
/// A top-level rule for the same element still applies normally, proving it's specifically the wrapped
/// rules that are dropped. Tracked at #283 (<c>@supports</c>) / #284 (<c>@container</c>).
/// </summary>
public class ConditionGroupingRuleIntegrationTests
{
    private const string Blue = "rgb(0, 0, 255)";

    [Fact]
    public async Task Supports_InnerRules_AreIgnored()
    {
        // The top-level rule sets red; the @supports-wrapped rule tries to override to blue but is ignored.
        var html = Html(
            "p { color: #ff0000; } @supports (display: flex) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(255, 0, 0)", box.Color);
    }

    [Fact]
    public async Task SupportsNot_InnerRules_AreIgnored()
    {
        // A `not`-guarded block is dropped just the same — we don't evaluate the condition either way.
        var html = Html(
            "@supports not (display: flex) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.NotEqual(Blue, box.Color);
    }

    [Fact]
    public async Task Container_InnerRules_AreIgnored()
    {
        var html = Html(
            "@container (min-width: 100px) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.NotEqual(Blue, box.Color);
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
