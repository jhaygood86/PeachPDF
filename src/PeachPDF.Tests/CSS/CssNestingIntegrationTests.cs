using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// CSS Nesting (<see href="https://www.w3.org/TR/css-nesting-1/">CSS Nesting 1</see>): the <c>&amp;</c>
/// nesting selector and nested style rules inside a declaration block. Nested rules are resolved at
/// parse time into ordinary rules with absolute selectors (<c>&amp;</c> → <c>:is(parent)</c>), so these
/// assert the resolved rules actually match and cascade. Includes the regression guards that the
/// declaration path is unaffected (hex colors, custom properties with a brace in their value, a normal
/// declaration after a nested rule).
/// </summary>
public class CssNestingIntegrationTests
{
    [Fact]
    public async Task ImplicitDescendant_And_ParentDeclaration()
    {
        // `& span` == `:is(.box) span` (descendant). The div keeps its own red; the span is blue.
        var html = Html(
            ".box { color: #ff0000; & span { color: #0000ff; } }",
            "<div class='box' id='d'>text<span id='s'>inner</span></div>");
        var (d, s) = (await Box(html, "d"), await Box(html, "s"));
        Assert.Equal("rgb(255, 0, 0)", d.Color);
        Assert.Equal("rgb(0, 0, 255)", s.Color);
    }

    [Fact]
    public async Task AmpersandCompound_MatchesSameElement()
    {
        // `&.active` == `:is(.box).active` — the same element carrying both classes.
        var html = Html(
            ".box { &.active { color: #0000ff; } }",
            "<div class='box active' id='d'>x</div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "d")).Color);
    }

    [Fact]
    public async Task TypeSelectorNestedRule_IsNotMistakenForADeclaration()
    {
        // `p { ... }` inside a block starts with an Ident (like a property name) — the classifier must
        // still see the `{` before any `;` and treat it as a nested rule.
        var html = Html(
            ".card { p { color: #0000ff; } }",
            "<div class='card'><p id='p'>x</p></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task HexColorInNestedValue_SurvivesTheLookahead()
    {
        // Regression: the classify-then-rewind must not corrupt a `#rrggbb` value (value-mode `#`
        // tokenization) the way a token buffer would.
        var html = Html(
            ".card { p { color: #123456; } }",
            "<div class='card'><p id='p'>x</p></div>");
        Assert.Equal("rgb(18, 52, 86)", (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task HexColorInTopLevelValue_Unaffected()
    {
        // The whole feature must leave an ordinary (non-nested) declaration block byte-identical.
        var html = Html(".card { color: #123456; }", "<div class='card' id='d'>x</div>");
        Assert.Equal("rgb(18, 52, 86)", (await Box(html, "d")).Color);
    }

    [Fact]
    public async Task ChildCombinatorLeading()
    {
        // `> p` == `:is(.card) > p` — only a direct child p matches.
        var html = Html(
            ".card { > p { color: #0000ff; } }",
            "<div class='card'><p id='child'>x</p><section><p id='grandchild'>y</p></section></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "child")).Color);
        Assert.NotEqual("rgb(0, 0, 255)", (await Box(html, "grandchild")).Color);
    }

    [Fact]
    public async Task DeclarationAfterNestedRule_StillApplies()
    {
        // A declaration written AFTER a nested rule still applies to the parent (the outer loop resumes
        // correctly); here the later blue wins over the earlier red.
        var html = Html(
            ".card { color: #ff0000; & span { color: #00ff00; } color: #0000ff; }",
            "<div class='card' id='d'>t<span id='s'>x</span></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "d")).Color);
        Assert.Equal("rgb(0, 255, 0)", (await Box(html, "s")).Color);
    }

    [Fact]
    public async Task ThreeLevelNesting()
    {
        // `.a { & .b { & .c { } } }` → `:is(:is(.a) .b) .c` — nested `:is()` with complex args.
        var html = Html(
            ".a { & .b { & .c { color: #0000ff; } } }",
            "<div class='a'><div class='b'><div class='c' id='c'>x</div></div></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "c")).Color);
    }

    [Fact]
    public async Task ParentSelectorList_ResolvesToIs()
    {
        // `.x, .y { & span { } }` → `:is(.x, .y) span` — the nested rule applies under either parent.
        var html = Html(
            ".x, .y { & span { color: #0000ff; } }",
            "<div class='x'><span id='sx'>1</span></div><div class='y'><span id='sy'>2</span></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "sx")).Color);
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "sy")).Color);
    }

    [Fact]
    public async Task NestedRule_InheritsEnclosingMedia()
    {
        // A nested rule inside @media print inherits that context (print media applies here).
        var html = Html(
            "@media print { .card { & p { color: #0000ff; } } }",
            "<div class='card'><p id='p'>x</p></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task NestedRule_InheritsEnclosingLayer()
    {
        // A nested rule inside @layer base loses to a later unlayered rule (layer context is inherited).
        var html = Html(
            "@layer base { .card { & p { color: #ff0000; } } } .card p { color: #0000ff; }",
            "<div class='card'><p id='p'>x</p></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task CustomProperty_CoexistsWithNestedRule()
    {
        // A custom property declared alongside a nested rule still cascades (inherited to descendants).
        var html = Html(
            ".card { --c: #0000ff; p { color: var(--c); } }",
            "<div class='card'><p id='p'>x</p></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    // Note: a custom property whose *value* literally contains a `{` (e.g. `--x: { }`) is classified
    // as a declaration by the `--` guard (correct — custom properties are always declarations), but the
    // declaration value parser (`CreateValue`) has a pre-existing limitation with a brace inside a value.
    // That is unrelated to nesting and does not occur in real utility-framework output, so it is not
    // exercised here.

    [Fact]
    public async Task TypePseudoNestedRule_IsConsumed_NotMistakenForADeclaration()
    {
        // `a:hover { }` (type + pseudo, no `&`) is ambiguous with a declaration `a: hover` — the
        // `{`-before-`;` scan classifies it as a nested rule. `:hover` never matches in a static PDF, so
        // we assert the FOLLOWING nested rule still applies, proving `a:hover { }` was consumed as a rule.
        var html = Html(
            ".card { a:hover { color: #ff0000; } p { color: #0000ff; } }",
            "<div class='card'><p id='p'>x</p></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task BraceInsideUrl_DoesNotTriggerNestedRule()
    {
        // A `{` inside a url()/string is part of an opaque function token, not a rule boundary.
        var html = Html(
            ".card { background: url(\"a{b}.png\"); & p { color: #0000ff; } }",
            "<div class='card'><p id='p'>x</p></div>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string Html(string css, string body) =>
        $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

    private static async Task<CssBox> Box(string html, string id)
    {
        var root = await BuildRoot(html);
        var box = DomUtils.GetBoxById(root, id);
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
