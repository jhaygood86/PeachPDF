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

    // ── #237 gap 1: !important layer-order reversal (CSS Cascade 5 §6.4.2) ────────

    [Fact]
    public async Task Important_EarlierLayer_Wins_OverLaterLayer()
    {
        // For NORMAL declarations the later layer wins; among !important declarations the layer order
        // reverses, so the EARLIER layer (base) wins over the later one (utilities).
        var html = Html(
            "@layer base { p { color: #ff0000 !important; } } @layer utilities { p { color: #0000ff !important; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(255, 0, 0)", box.Color);
    }

    [Fact]
    public async Task Important_LayeredRule_Wins_OverUnlayeredImportant()
    {
        // An unlayered !important declaration LOSES to a layered !important one (the mirror of the
        // normal-declaration rule where unlayered wins).
        var html = Html(
            "@layer base { p { color: #0000ff !important; } } p { color: #ff0000 !important; }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task Important_WithinOneLayer_HigherSpecificityStillWins()
    {
        // The layer reversal is about layer order only — within a single layer, specificity still
        // decides among !important declarations (not reversed).
        var html = Html(
            "@layer base { p { color: #ff0000 !important; } p.hi { color: #0000ff !important; } }",
            "<p class='hi'>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    // ── #237 gap 2: nested sub-layers stay contiguous within their parent's band ──

    [Fact]
    public async Task NestedSublayers_StayContiguous_WithinParentBand()
    {
        // a first appears (via a.b) before c, so a's WHOLE subtree {a.b, a.d} ranks below c — even
        // though a.d is declared last of all. Under a flat first-appearance scheme a.d would wrongly
        // outrank c; the correct tree order makes c (the later top-level layer) win.
        var html = Html(
            "@layer a.b { p { color: #ff0000; } } @layer c { p { color: #00ff00; } } @layer a.d { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 255, 0)", box.Color);
    }

    [Fact]
    public async Task NestedSublayer_LaterWithinParent_Wins_OverEarlierSublayer()
    {
        // Within one parent (a), a later-declared sub-layer (a.d) beats an earlier one (a.b).
        var html = Html(
            "@layer a.b { p { color: #ff0000; } } @layer a.d { p { color: #0000ff; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task NestedSublayer_VsParentDirectRules_CharacterizesCurrentOrdering()
    {
        // Characterization (NOT a spec-authority assertion): when a layer has BOTH direct rules and a
        // nested sub-layer, PeachPDF ranks the parent's own direct rules *before* (lower priority than)
        // its nested sub-layers, so the sub-layer wins here. The exact CSS Cascade 5 §6.2 treatment of
        // direct rules interleaved with their own sub-layers is a documented simplification (see the
        // accepted-gap note / tracked follow-up); the unambiguous sibling-contiguity behavior is covered
        // by NestedSublayers_StayContiguous_WithinParentBand above. This pins today's behavior so a
        // future deliberate change to it is visible.
        var html = Html(
            "@layer a { p { color: #ff0000; } @layer b { p { color: #0000ff; } } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    // ── #240: layer-aware revert-layer ───────────────────────────────────────────

    [Fact]
    public async Task RevertLayer_RollsBackToLowerLayer_NotToOrigin()
    {
        // revert-layer in the later layer reveals the earlier layer's value (blue), rather than rolling
        // all the way back to the UA/origin default the way plain `revert` would.
        var html = Html(
            "@layer base { p { color: #0000ff; } } @layer top { p { color: revert-layer; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task Revert_StillRollsBackToOrigin_NotToLowerLayer()
    {
        // Regression guard: plain `revert` is unchanged — it rolls back past the whole author origin, so
        // the earlier layer's blue is NOT revealed (the inherited/initial color applies instead).
        var html = Html(
            "@layer base { p { color: #0000ff; } } @layer top { p { color: revert; } }",
            "<p>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.NotEqual("rgb(0, 0, 255)", box.Color);
    }

    [Fact]
    public async Task RevertLayer_OnCustomProperty_RollsBackToLowerLayer()
    {
        // revert-layer works for custom properties too: the consumer resolves to the lower layer's value.
        var html = Html(
            "@layer base { p { --c: #0000ff; } } @layer top { p { --c: revert-layer; } } p { color: var(--c); }",
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
