using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Svg;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// <c>@supports</c> and <c>@container</c> block handling. <c>@supports</c>'s condition is now genuinely
/// evaluated (<see cref="CssData.IndexRules"/>/<see cref="CssData.FlattenRules"/> resolve
/// <c>IConditionFunction.Check()</c> once, statically, since the condition takes no arguments and can't
/// vary per query) against the actual set of HTML/SVG properties PeachPDF's renderer implements — see
/// <see cref="PeachPDF.CSS.DeclarationCondition"/> and CLAUDE.md's "CSS/SVG property registry generator"
/// section. <c>@container</c> still has no evaluatable condition (truthiness depends on the nearest
/// matched element's own container size, which isn't known at parse time the way <c>@supports</c>'s is),
/// so it's still ignored entirely — the inner rules are never indexed. Tracked at #284.
/// </summary>
public class ConditionGroupingRuleIntegrationTests
{
    private const string Blue = "rgb(0, 0, 255)";
    private const string Red = "rgb(255, 0, 0)";

    [Fact]
    public async Task Supports_InnerRules_Apply_WhenConditionIsSupported()
    {
        var html = Html(
            "p { color: #ff0000; } @supports (display: flex) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal(Blue, box.Color);
    }

    [Fact]
    public async Task Supports_InnerRules_DoNotApply_WhenConditionIsUnsupported()
    {
        // animation-name parses fine in the CSS-OM but PeachPDF never renders animations - the
        // condition must evaluate false, not merely "unknown so ignored."
        var html = Html(
            "p { color: #ff0000; } @supports (animation-name: spin) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal(Red, box.Color);
    }

    [Fact]
    public async Task SupportsNot_Fallback_Applies_WhenFeatureIsUnsupported()
    {
        var html = Html(
            "@supports not (animation-name: spin) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal(Blue, box.Color);
    }

    [Fact]
    public async Task SupportsNot_Fallback_DoesNotApply_WhenFeatureIsSupported()
    {
        var html = Html(
            "@supports not (display: flex) { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.NotEqual(Blue, box.Color);
    }

    // The exact scenario the pre-evaluation stopgap (#283) was written to avoid: an enhanced block and
    // its not-guarded fallback wrapping the same declaration, both present in one stylesheet. Real
    // evaluation must let exactly one of them win, never both and never neither.
    [Fact]
    public async Task SupportsEnhancedBlockAndNotGuardedFallback_ExactlyOneApplies()
    {
        var html = Html(
            "@supports (display: flex) { p { color: #0000ff; } } " +
            "@supports not (display: flex) { p { color: #00ff00; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal(Blue, box.Color);
        Assert.NotEqual("rgb(0, 255, 0)", box.Color);
    }

    [Fact]
    public async Task Supports_AndOrCombination_EvaluatesEndToEnd()
    {
        // ((unsupported) or (unsupported)) and (supported) - the AND must short-circuit false even
        // though `transform` alone is genuinely supported.
        var html = Html(
            "p { color: #ff0000; } " +
            "@supports ((transition-property: color) or (animation-name: spin)) and (transform: rotate(10deg)) " +
            "{ p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal(Red, box.Color);
    }

    [Fact]
    public async Task Supports_SvgOnlyProperty_AppliesToAnInlineSvgElement()
    {
        // "fill" isn't an HTML property (CssPropertyRegistry has no entry for it) - this reaches
        // DeclarationCondition.Check()'s SvgPropertyRegistry fallback, and must apply on a real inline
        // <svg> element sharing the host document's one CssData, not just at the Check()-unit level.
        var html = Html(
            "@supports (fill: red) { circle { fill: #00ff00; } }",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"><circle cx=\"5\" cy=\"5\" r=\"5\"/></svg>");
        var root = await BuildRoot(html);
        var svgBox = Assert.IsType<CssBoxSvg>(DomUtils.GetBoxByTagName(root, "svg"));

        var document = svgBox.Document;
        Assert.NotNull(document);
        var circle = Assert.IsType<SvgCircleElement>(document!.Children[0]);
        Assert.Equal(RColor.FromArgb(0x00, 0xff, 0x00), circle.Fill.Color);
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
