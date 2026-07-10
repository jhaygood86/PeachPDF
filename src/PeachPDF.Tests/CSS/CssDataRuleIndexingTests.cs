using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Regression tests for <c>CssData</c>'s tag/class/id rule index (added to avoid linearly
/// scanning every stylesheet rule against every box during cascade). These render real HTML/CSS
/// and assert on the resulting box tree, so they exercise <c>GetUserAgentStyleRules</c>/
/// <c>GetAuthorStyleRules</c> exactly as the cascade does - the index only narrows candidates,
/// <c>DoesSelectorMatch</c> is still the source of truth, but a bucketing bug would show up here
/// as a rule silently failing to apply (or applying somewhere it shouldn't).
/// </summary>
public class CssDataRuleIndexingTests
{
    // ── Id selector (its own bucket) ──────────────────────────────────────────

    [Fact]
    public async Task IdSelector_Matches_ElementWithThatId()
    {
        var html = Html("#target { background-color: #ff0000; }", "<div id='target'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task IdSelector_DoesNotMatch_DifferentId()
    {
        var html = Html("#target { background-color: #ff0000; }", "<div id='other'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── Multi-class compound selector (bucketed by one of its classes) ───────

    [Fact]
    public async Task MultiClassCompound_Matches_WhenElementHasBothClasses()
    {
        var html = Html(".a.b { background-color: #ff0000; }", "<div class='a b'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task MultiClassCompound_DoesNotMatch_WhenOnlyOneClassPresent()
    {
        var html = Html(".a.b { background-color: #ff0000; }", "<div class='a'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task ElementWithNoClassAttribute_DoesNotMatchClassSelector()
    {
        // Regression for the class bucket lookup: a box with no "class" attribute at all must
        // not blow up or accidentally match, it should just never be a class-bucket candidate.
        var html = Html(".a { background-color: #ff0000; }", "<div>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── List (comma) selector spanning multiple buckets ───────────────────────

    [Fact]
    public async Task ListSelector_TagAlternative_MatchesViaTagBucket()
    {
        var html = Html("div, .foo { background-color: #ff0000; }", "<div>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task ListSelector_ClassAlternative_MatchesViaClassBucket()
    {
        var html = Html("span, .foo { background-color: #ff0000; }", "<div class='foo'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task ListSelector_MatchingBothAlternatives_AppliesOnceWithoutCorruption()
    {
        // "div, .foo" on a <div class="foo"> is reachable through both the tag bucket AND the
        // class bucket - regression for the dedup in CssData.GetStyleRulesByOrigin, which must
        // yield the rule exactly once so cascade application isn't run twice for it.
        var html = Html("div, .foo { background-color: #ff0000; color: #00ff00; }", "<div class='foo'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(255, 0, 0)", box.BackgroundColor);
        Assert.Equal("rgb(0, 255, 0)", box.Color);
    }

    // ── Universal selector ────────────────────────────────────────────────────

    [Fact]
    public async Task UniversalSelector_MatchesAnyElement()
    {
        var html = Html("* { background-color: #ff0000; }", "<span>text</span>");
        var box = await FindBoxByTag(html, "span");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    // ── Pseudo-element compound (falls back to the unindexed/universal bucket) ─

    [Fact]
    public async Task PseudoElement_CombinedWithClassSelector_StillGeneratesContent()
    {
        var html = Html(".label::before { content: 'X: '; }", "<span class='label'>text</span>");
        var root = await BuildRoot(html);
        var span = DomUtils.GetBoxByTagName(root, "span");
        Assert.NotNull(span);

        var beforeBox = span!.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);
        Assert.NotNull(beforeBox);
        Assert.Equal("X: ", beforeBox!.Text);
    }

    // ── :nth-child(1) compound (falls back to the unindexed/universal bucket) ──

    [Fact]
    public async Task NthChildCompound_MatchesExactlyOneChild()
    {
        // Note: bare ":first-child" parses as a generic PseudoClassSelector in this engine's
        // parser (SelectorConstructor only maps the "nth-child(...)" function form to
        // FirstChildSelector), and DoesSelectorMatch(PseudoClassSelector,...) only recognizes
        // ":link" - so plain ":first-child" never matches anything here, a pre-existing gap
        // unrelated to this change. ":nth-child(1)" does produce a real FirstChildSelector, which
        // is what CollectIndexKeys routes to the universal fallback bucket (rather than indexing
        // by tag/class/id) - "*" for the other compound member sidesteps a separate, pre-existing
        // quirk where the non-nth-child member of a compound is checked against the parent box
        // rather than the candidate box itself (AllSelector matches unconditionally either way).
        //
        // DoesSelectorMatch(FirstChildSelector,...) compares a 0-based child index against the
        // literal (1-based, per CSS) offset parsed from "nth-child(N)", so "nth-child(1)" actually
        // matches the *second* element child here, not the first - this test asserts that actual
        // (pre-existing) behaviour rather than CSS-spec-correct ":first-child" semantics, since
        // what matters for this regression suite is only that the rule is reachable at all.
        var html = Html(
            "*:nth-child(1) { background-color: #ff0000; }",
            "<div><p>first</p><p>second</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");

        Assert.Equal(2, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
    }

    // ── Media query rules (kept as an unindexed linear scan) ──────────────────

    [Fact]
    public async Task MediaQueryRule_ForPrintMedia_StillApplies()
    {
        // PeachPDF always cascades against the "print" media type (see DomParser.GenerateCssTree).
        var html = Html("@media print { div { background-color: #ff0000; } }", "<div>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.NotEqual("transparent", box.BackgroundColor);
    }

    [Fact]
    public async Task MediaQueryRule_ForScreenMedia_DoesNotApplyToPrintOutput()
    {
        var html = Html("@media screen { div { background-color: #ff0000; } }", "<div>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── Specificity/cascade order across differently-bucketed rules ──────────

    [Fact]
    public async Task ClassSelector_BeatsTagSelector_RegardlessOfBucket()
    {
        // Standard CSS specificity: a class selector (0,1,0) outranks a type selector (0,0,1),
        // even though the two rules are now looked up via completely different index buckets.
        var html = Html("div { color: #0000ff; } .highlight { color: #ff0000; }", "<div class='highlight'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(255, 0, 0)", box.Color);
    }

    [Fact]
    public async Task IdSelector_BeatsClassSelector_RegardlessOfBucket()
    {
        var html = Html("#target { color: #ff0000; } .highlight { color: #0000ff; }", "<div id='target' class='highlight'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(255, 0, 0)", box.Color);
    }

    [Fact]
    public async Task AuthorTagRule_OverridesUserAgentDefault()
    {
        // UA stylesheet sets h1's font-size; an author tag-selector rule (a different bucket
        // lookup than the UA rule's own tag bucket, but the same bucket *kind*) must still win.
        var html = Html("h1 { font-size: 10px; }", "<h1>heading</h1>");
        var box = await FindBoxByTag(html, "h1");
        Assert.Equal("10px", box.FontSize);
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
