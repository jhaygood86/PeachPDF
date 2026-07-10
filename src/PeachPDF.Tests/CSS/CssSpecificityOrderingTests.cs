using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Regression tests for CssData's cascade ordering: matched rules are now applied in
/// (specificity ascending, true document order) rather than the old pure "whatever order rules
/// were enumerated in" behavior, which didn't respect CSS specificity at all (a low-specificity
/// rule declared later used to incorrectly beat a high-specificity rule declared earlier), and
/// which additionally didn't preserve true source order across the plain-rule/@media boundary.
/// </summary>
public class CssSpecificityOrderingTests
{
    [Fact]
    public async Task HigherSpecificityRule_WinsEvenWhenDeclaredEarlier()
    {
        // #el (id, highest specificity) is declared FIRST; div (type, lowest specificity) is
        // declared SECOND. Under the old "pure source order" behavior, div (later) would
        // incorrectly win. Specificity must decide this, not declaration order.
        var html = Html("#el { color: #0000ff; } div { color: #ff0000; }", "<div id='el'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task EqualSpecificity_SameOrigin_StillResolvesByLastDeclared()
    {
        var html = Html("div { color: #ff0000; } div { color: #0000ff; }", "<div>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task MediaRuleDeclaredEarlier_LosesToEqualSpecificityPlainRuleDeclaredLater()
    {
        // The @media print block (containing a "div" rule) appears FIRST in the source; a plain
        // "div" rule appears SECOND. Both have identical (type-only) specificity, so true source
        // order must decide - the old two-pass "all plain rules, then all media rules" enumeration
        // didn't preserve this (it would treat the media rule as if it came after the plain rule,
        // regardless of true position), so this would previously have picked the wrong winner.
        var html = Html(
            "@media print { div { color: #0000ff; } } div { color: #ff0000; }",
            "<div>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(255, 0, 0)", box.Color);
    }

    [Fact]
    public async Task CommaListRule_UsesOnlyTheMatchedBranchsSpecificity_NotASum()
    {
        // The box matches ".a" (one class) but NOT "#b" (an id) in the list selector ".a, #b".
        // The old buggy ListSelector.Specificity summed ALL alternatives (class + id), which would
        // make this rule outrank ".a.c" (two classes) purely because of the unmatched "#b" branch's
        // id specificity leaking in. The correct behavior only credits the branch that actually
        // matched (".a" = one class), so ".a.c" (two classes) must win.
        var html = Html(
            ".a, #b { color: #0000ff; } .a.c { color: #ff0000; }",
            "<div class='a c'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(255, 0, 0)", box.Color);
    }

    // ── Helpers (mirrors SelectorMatchingTests.cs conventions) ────────────────

    private static string Html(string css, string body) =>
        $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

    private async Task<CssBox> FindBoxByTag(string html, string tag)
    {
        var root = await BuildRoot(html);
        var box = PeachPDF.Html.Core.Utils.DomUtils.GetBoxByTagName(root, tag);
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
