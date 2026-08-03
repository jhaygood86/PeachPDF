using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Regression tests for :not(), :is()/:matches(), and :has() - previously parsed successfully but
/// permanently inert (rebuilt into a fake PseudoClassSelector that could only ever match ":link").
/// :is() didn't parse at all before this change.
/// </summary>
public class RelationalPseudoClassSelectorTests : CssConstructionFunctions
{
    // ── :not() ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Not_ExcludesElementsMatchingTheArgument()
    {
        var html = Html(
            "p:not(.exclude) { background-color: #ff0000; }",
            "<div><p class='exclude'>1</p><p>2</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.Equal("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task NotNot_IsRejectedAsInvalid_WholeRuleMatchesNothing()
    {
        // The parser has a pre-existing restriction against nesting :not() inside :not() (IsNested
        // flag) - the whole enclosing selector becomes invalid, so the rule matches nothing at all,
        // even for an element that would satisfy the (nonsensical) double-negation.
        var html = Html(
            "p:not(:not(.foo)) { background-color: #ff0000; }",
            "<div><p class='foo'>1</p></div>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("transparent", box.BackgroundColor);
    }

    // ── :is() / :matches() ────────────────────────────────────────────────────

    [Fact]
    public async Task Is_MatchesEitherBranch()
    {
        var html = Html(
            "p:is(.a, .b) { background-color: #ff0000; }",
            "<div><p class='a'>1</p><p class='b'>2</p><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(3, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
        Assert.Equal("transparent", boxes[2].BackgroundColor);
    }

    [Fact]
    public async Task Matches_LegacyAliasBehavesTheSameAsIs()
    {
        var html = Html(
            "p:matches(.a, .b) { background-color: #ff0000; }",
            "<div><p class='a'>1</p><p>2</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(2, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task Is_SpecificityIsTheStaticMaxOfItsArguments_NotJustTheMatchedBranch()
    {
        // ":is(.a, #b)" must report specificity = max(.a, #b) = one-id, per spec - even though this
        // box only matches via the ".a" branch (never #b). If :is()'s specificity were instead
        // computed dynamically from only the matched branch (one-class), ".a.c" (two classes) would
        // incorrectly outrank it; with the correct static-max (one-id) behavior, :is(...) wins.
        var html = Html(
            ":is(.a, #b) { color: #0000ff; } .a.c { color: #ff0000; }",
            "<div class='a c'>text</div>");
        var box = await FindBoxByTag(html, "div");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    // ── :has() ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Has_MatchesWhenADirectChildSatisfiesTheArgument()
    {
        var html = Html(
            "div:has(.foo) { background-color: #ff0000; }",
            "<div id='yes'><span class='foo'>x</span></div><div id='no'><span>x</span></div>");
        var yes = await FindBoxById(html, "yes");
        var no = await FindBoxById(html, "no");
        Assert.NotEqual("transparent", yes.BackgroundColor);
        Assert.Equal("transparent", no.BackgroundColor);
    }

    [Fact]
    public async Task Has_MatchesWhenADeeplyNestedDescendantSatisfiesTheArgument()
    {
        var html = Html(
            "div:has(.foo) { background-color: #ff0000; }",
            "<div id='outer'><section><article><span class='foo'>deep</span></article></section></div>");
        var outer = await FindBoxById(html, "outer");
        Assert.NotEqual("transparent", outer.BackgroundColor);
    }

    [Fact]
    public async Task Has_CommaSeparatedArgument_MatchesEitherBranch()
    {
        var html = Html(
            "div:has(.a, .b) { background-color: #ff0000; }",
            "<div id='yes'><span class='b'>x</span></div><div id='no'><span class='c'>x</span></div>");
        var yes = await FindBoxById(html, "yes");
        var no = await FindBoxById(html, "no");
        Assert.NotEqual("transparent", yes.BackgroundColor);
        Assert.Equal("transparent", no.BackgroundColor);
    }

    [Fact]
    public async Task Has_ChildCombinator_MatchesOnlyDirectChild()
    {
        // ":has(> .foo)" must NOT behave like plain ":has(.foo)" - a .foo that's only a grandchild
        // (nested one level deeper than a direct child) must not satisfy the child-combinator form.
        var html = Html(
            "div:has(> .foo) { background-color: #ff0000; }",
            "<div id='yes'><span class='foo'>x</span></div><div id='no'><section><span class='foo'>x</span></section></div>");
        var yes = await FindBoxById(html, "yes");
        var no = await FindBoxById(html, "no");
        Assert.NotEqual("transparent", yes.BackgroundColor);
        Assert.Equal("transparent", no.BackgroundColor);
    }

    [Fact]
    public async Task Has_ChildCombinator_WithMultiCompoundArgument_AnchorsOnlyItsFirstCompoundToTheChild()
    {
        // ":has(> .a .b)" means the DIRECT CHILD must match ".a", and ".b" must be a descendant of
        // THAT child - not that the direct child itself must satisfy the whole ".a .b" chain as its
        // own subject (which would require the child to literally match ".b").
        // ".b" is nested two levels below ".a" (not a direct child of it) to exercise the recursive
        // any-depth descendant search for the chain's internal (non-leading) combinator, not just its
        // first level.
        var html = Html(
            "div:has(> .a .b) { background-color: #ff0000; }",
            "<div id='yes'><section class='a'><em><span class='b'>x</span></em></section></div>" +
            "<div id='no'><div><section class='a'><em><span class='b'>x</span></em></section></div></div>" +
            "<div id='no-descendant'><section class='a'><em>no b here</em></section></div>");
        var yes = await FindBoxById(html, "yes");
        var no = await FindBoxById(html, "no");
        var noDescendant = await FindBoxById(html, "no-descendant");
        Assert.NotEqual("transparent", yes.BackgroundColor);
        Assert.Equal("transparent", no.BackgroundColor);
        Assert.Equal("transparent", noDescendant.BackgroundColor);
    }

    [Fact]
    public async Task Has_ChildCombinator_WithUniversalSelector_DoesNotMatchATextOnlyChild()
    {
        // ":has(> *)" must only consider real element children - a child that's pure text (no element
        // child at all) must not satisfy it, even though "*" matches any non-root node in general.
        var html = Html(
            "div:has(> *) { background-color: #ff0000; }",
            "<div id='yes'><span>x</span></div><div id='no'>just text</div>");
        var yes = await FindBoxById(html, "yes");
        var no = await FindBoxById(html, "no");
        Assert.NotEqual("transparent", yes.BackgroundColor);
        Assert.Equal("transparent", no.BackgroundColor);
    }

    [Fact]
    public async Task Has_AdjacentSiblingCombinator_MatchesOnlyImmediateNextSibling()
    {
        var html = Html(
            "div:has(+ .foo) { background-color: #ff0000; }",
            "<div id='yes'></div><span class='foo'>x</span>" +
            "<div id='no'></div><section></section><span class='foo'>x</span>");
        var yes = await FindBoxById(html, "yes");
        var no = await FindBoxById(html, "no");
        Assert.NotEqual("transparent", yes.BackgroundColor);
        Assert.Equal("transparent", no.BackgroundColor);
    }

    [Fact]
    public async Task Has_GeneralSiblingCombinator_MatchesAnyFollowingSibling()
    {
        // Same markup shape as the adjacent-sibling test's "no" case (.foo is a later, not immediate,
        // sibling) - "~" must succeed where "+" fails.
        var html = Html(
            "div:has(~ .foo) { background-color: #ff0000; }",
            "<div id='yes'></div><section></section><span class='foo'>x</span>" +
            "<div id='no'></div>");
        var yes = await FindBoxById(html, "yes");
        var no = await FindBoxById(html, "no");
        Assert.NotEqual("transparent", yes.BackgroundColor);
        Assert.Equal("transparent", no.BackgroundColor);
    }

    [Fact]
    public async Task Has_MixedLeadingCombinatorsInCommaList_MatchesEitherBranch()
    {
        // Each comma-separated alternative tracks its own leading combinator independently.
        var html = Html(
            "div:has(> .a, + .b) { background-color: #ff0000; }",
            "<div id='child'><span class='a'>x</span></div>" +
            "<div id='sibling'></div><span class='b'>x</span>" +
            "<div id='none'><section><span class='a'>x</span></section></div>");
        var child = await FindBoxById(html, "child");
        var sibling = await FindBoxById(html, "sibling");
        var none = await FindBoxById(html, "none");
        Assert.NotEqual("transparent", child.BackgroundColor);
        Assert.NotEqual("transparent", sibling.BackgroundColor);
        Assert.Equal("transparent", none.BackgroundColor);
    }

    [Theory]
    [InlineData("div:has(> .foo)")]
    [InlineData("div:has(+ .foo)")]
    [InlineData("div:has(~ .foo)")]
    [InlineData("div:has(.foo)")]
    public void Has_LeadingCombinator_RoundTripsThroughSelectorText(string selector)
    {
        var sheet = ParseStyleSheet($"{selector} {{ }}");
        Assert.Equal(1, sheet.Rules.Length);
        Assert.Equal(selector, ((StyleRule)sheet.Rules[0]).SelectorText);
    }

    [Fact]
    public void Has_MixedLeadingCombinatorsInCommaList_RoundTripsThroughSelectorText()
    {
        // ListSelector.ToCss doesn't insert a space after its comma (existing, pre-existing behavior -
        // see ListSelector.cs), so this asserts separately from the single-alternative cases above.
        var sheet = ParseStyleSheet("div:has(> .a, + .b) { }");
        Assert.Equal(1, sheet.Rules.Length);
        Assert.Equal("div:has(> .a,+ .b)", ((StyleRule)sheet.Rules[0]).SelectorText);
    }

    // ── :where() ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Where_MatchesEitherBranch_LikeIs()
    {
        var html = Html(
            "p:where(.a, .b) { background-color: #ff0000; }",
            "<div><p class='a'>1</p><p class='b'>2</p><p>3</p></div>");
        var boxes = await FindAllBoxesByTag(html, "p");
        Assert.Equal(3, boxes.Count);
        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.NotEqual("transparent", boxes[1].BackgroundColor);
        Assert.Equal("transparent", boxes[2].BackgroundColor);
    }

    [Fact]
    public async Task Where_ContributesZeroSpecificity_LosesToATypeSelector()
    {
        // ":where(#b)" matches, but per CSS Selectors 4 §16 it contributes ZERO specificity - so a
        // bare type selector "p" (0,0,1) outranks it even though ":where(#b)"'s argument is an id.
        // Contrast Is_SpecificityIsTheStaticMaxOfItsArguments (where ":is(#b)" WOULD win at one-id).
        var html = Html(
            ":where(#b) { color: #0000ff; } p { color: #ff0000; }",
            "<p id='b'>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(255, 0, 0)", box.Color);
    }

    [Fact]
    public async Task Where_StillMatches_WhenUncontested()
    {
        // Proves the zero-specificity rule above lost on specificity, not because :where() failed to
        // match: uncontested, the same ":where(#b)" rule applies its color.
        var html = Html(
            ":where(#b) { color: #0000ff; }",
            "<p id='b'>text</p>");
        var box = await FindBoxByTag(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
    }

    // ── Helpers (mirrors SelectorMatchingTests.cs conventions) ────────────────

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

    private async Task<CssBox> FindBoxById(string html, string id)
    {
        var root = await BuildRoot(html);
        var box = FindById(root, id);
        Assert.NotNull(box);
        return box!;
    }

    private static void CollectByTag(CssBox box, string tag, List<CssBox> results)
    {
        if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
            results.Add(box);
        foreach (var child in box.Boxes)
            CollectByTag(child, tag, results);
    }

    private static CssBox? FindById(CssBox box, string id)
    {
        var val = box.HtmlTag?.TryGetAttribute("id", "");
        if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
            return box;
        foreach (var child in box.Boxes)
        {
            var found = FindById(child, id);
            if (found != null) return found;
        }
        return null;
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
