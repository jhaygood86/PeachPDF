using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// <c>@supports</c> and <c>@container</c> inner-rule application. Both parse into grouping rules, but
/// their contents were previously never indexed (only <c>@media</c> was recursed), so any rule wrapped
/// in them was silently dropped. PeachPDF can't evaluate the condition, so — matching the existing
/// <c>@media</c>-feature convention — the condition is treated as met and the inner rules apply.
/// </summary>
public class ConditionGroupingRuleIntegrationTests
{
    [Fact]
    public async Task Supports_InnerRules_AreApplied()
    {
        var html = Html(
            "@supports (display: flex) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task Supports_InnerRules_Apply_EvenWhenConditionIsUnverifiable()
    {
        // The condition queries a feature PeachPDF doesn't implement (grid); it is treated as met and
        // the inner rule still applies — the documented "condition treated as met" behavior.
        var html = Html(
            "@supports (display: grid) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task Container_InnerRules_AreApplied()
    {
        var html = Html(
            "@container (min-width: 100px) { p { color: #0000ff; } }",
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
