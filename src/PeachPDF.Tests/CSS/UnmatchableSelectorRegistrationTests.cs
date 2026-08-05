using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Selectors PeachPDF recognizes but can never match (see <c>UnmatchableSelectors</c>). The point is not
/// that they select nothing - an unregistered name selected nothing too - but that they no longer
/// invalidate the <em>selector list</em> they appear in (CSS Selectors 3 §4), which is what discarded
/// Tailwind v4's entire <c>:root, :host { … }</c> design-token block.
/// </summary>
public class UnmatchableSelectorRegistrationTests
{
    // ── The reported shape: an unmatchable half must not take the matchable half down ──────────

    [Fact]
    public async Task RootHostList_AppliesItsRootHalf()
    {
        // The exact shape Tailwind v4 emits at the top of every stylesheet.
        var html = Html(
            ":root, :host { color: #0000ff; }",
            "<p id='p'>text</p>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task RootHostList_DesignTokens_ReachTheirVarConsumers()
    {
        // …and the consequence that actually mattered: the custom properties declared in that block are
        // no longer guaranteed-invalid at every var() reference in the document.
        var html = Html(
            ":root, :host { --brand: #0000ff; --gap: 8pt; } p { color: var(--brand); margin-top: var(--gap); }",
            "<p id='p'>text</p>");
        var box = await Box(html, "p");
        Assert.Equal("rgb(0, 0, 255)", box.Color);
        Assert.Equal("8pt", box.MarginTop.ToString());
    }

    [Theory]
    // Ident-form pseudo-classes.
    [InlineData(":host")]
    [InlineData(":defined")]
    [InlineData(":modal")]
    [InlineData(":fullscreen")]
    [InlineData(":picture-in-picture")]
    [InlineData(":autofill")]
    [InlineData(":user-valid")]
    [InlineData(":user-invalid")]
    [InlineData(":popover-open")]
    [InlineData(":open")]
    [InlineData(":target-within")]
    [InlineData(":local-link")]
    [InlineData(":current")]
    [InlineData(":past")]
    [InlineData(":future")]
    [InlineData(":playing")]
    [InlineData(":paused")]
    [InlineData(":seeking")]
    [InlineData(":buffering")]
    [InlineData(":stalled")]
    [InlineData(":muted")]
    [InlineData(":volume-locked")]
    // Functional pseudo-classes.
    [InlineData(":host(.theme)")]
    [InlineData(":host-context(.theme)")]
    [InlineData(":state(--loaded)")]
    // Ident-form pseudo-elements.
    [InlineData("::placeholder")]
    [InlineData("::backdrop")]
    [InlineData("::file-selector-button")]
    [InlineData("::details-content")]
    [InlineData("::target-text")]
    [InlineData("::spelling-error")]
    [InlineData("::grammar-error")]
    [InlineData("::cue")]
    [InlineData("::cue-region")]
    [InlineData("::view-transition")]
    // Functional pseudo-elements.
    [InlineData("::part(label)")]
    [InlineData("::slotted(span)")]
    [InlineData("::highlight(search)")]
    [InlineData("::view-transition-group(root)")]
    [InlineData("::view-transition-image-pair(root)")]
    [InlineData("::view-transition-old(root)")]
    [InlineData("::view-transition-new(root)")]
    // Vendor extensions, accepted wholesale by the leading-hyphen rule rather than enumerated.
    [InlineData(":-moz-focusring")]
    [InlineData(":-moz-ui-invalid")]
    [InlineData(":-webkit-autofill")]
    [InlineData(":-moz-locale-dir(rtl)")]
    [InlineData("::-webkit-input-placeholder")]
    [InlineData("::-moz-placeholder")]
    [InlineData("::-webkit-file-upload-button")]
    [InlineData("::-moz-focus-inner")]
    [InlineData("::-webkit-inner-spin-button")]
    [InlineData("::-webkit-scrollbar")]
    [InlineData("::-webkit-datetime-edit-year-field")]
    // A legacy single-colon vendor pseudo-element arrives through the pseudo-CLASS path.
    [InlineData(":-ms-input-placeholder")]
    public async Task SharingAListWithAnUnmatchableSelector_DoesNotDropTheRule(string unmatchable)
    {
        var html = Html(
            $"p, {unmatchable} {{ color: #0000ff; }}",
            "<p id='p'>text</p>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Theory]
    [InlineData(":host")]
    [InlineData(":host(.theme)")]
    [InlineData(":defined")]
    [InlineData(":-moz-focusring")]
    [InlineData("::placeholder")]
    [InlineData("::backdrop")]
    [InlineData("::part(label)")]
    [InlineData("::-webkit-file-upload-button")]
    public async Task AnUnmatchableSelector_SelectsNothing(string unmatchable)
    {
        // The other half of the contract: registering the name must not make it match something. A
        // selector list of only unmatchable selectors leaves every element untouched.
        var html = Html(
            $"{unmatchable} {{ color: #0000ff; }}",
            "<p id='p'>text</p>");
        Assert.NotEqual("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task AnUnmatchablePseudoElement_GeneratesNoBox()
    {
        // ::before/::after/::marker/::first-letter synthesize a real child box when matched. A
        // registered-but-unmatchable pseudo-element must not reach that synthesis at all.
        var html = Html(
            "p::placeholder { content: 'X'; color: #0000ff; }",
            "<p id='p'>text</p>");
        var box = await Box(html, "p");
        Assert.DoesNotContain(box.Boxes, b => b.IsPseudoElement);
    }

    [Fact]
    public async Task AGenuinelyUnknownPseudoClass_StillInvalidatesTheList()
    {
        // Deliberately NOT a blanket "accept anything unknown": a typo invalidates the whole list, as it
        // does in a browser. Registration is an allow-list plus the vendor-extension rule.
        var html = Html(
            "p, :bogus-pseudo { color: #0000ff; }",
            "<p id='p'>text</p>");
        Assert.NotEqual("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Theory]
    [InlineData("::bogus-pseudo")]
    // A functional form whose name is not a recognized functional pseudo-element, and a `::` followed by
    // something that is neither an ident nor a function at all.
    [InlineData("::bogus-pseudo(x)")]
    [InlineData("::.klass")]
    public async Task AGenuinelyUnknownPseudoElement_StillInvalidatesTheList(string unknown)
    {
        var html = Html(
            $"p, {unknown} {{ color: #0000ff; }}",
            "<p id='p'>text</p>");
        Assert.NotEqual("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    // ── `:-webkit-any()` / `:-moz-any()` are `:is()`, not merely unmatchable ────────────────────

    [Fact]
    public async Task WebkitAny_BehavesAsIs()
    {
        // The legacy vendor spellings of :is() have its exact semantics, so they get the real matcher
        // rather than the never-matching vendor path.
        var html = Html(
            "p:-webkit-any(.a, .b) { color: #0000ff; }",
            "<p class='a' id='a'>1</p><p class='b' id='b'>2</p><p id='c'>3</p>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "a")).Color);
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "b")).Color);
        Assert.NotEqual("rgb(0, 0, 255)", (await Box(html, "c")).Color);
    }

    [Fact]
    public async Task MozAny_BehavesAsIs()
    {
        var html = Html(
            "p:-moz-any(.a, .b) { color: #0000ff; }",
            "<p class='a' id='a'>1</p><p id='c'>2</p>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "a")).Color);
        Assert.NotEqual("rgb(0, 0, 255)", (await Box(html, "c")).Color);
    }

    // ── Case-insensitivity (CSS Syntax 3 §3.3) — the lexer does not normalize an ident's case ──

    [Theory]
    [InlineData(":HOVER")]
    [InlineData(":Host")]
    [InlineData("::PLACEHOLDER")]
    [InlineData("::BEFORE")]
    public async Task AnUppercasedPseudoName_IsRecognized_AndDoesNotDropTheRule(string spelling)
    {
        var html = Html(
            $"p, {spelling} {{ color: #0000ff; }}",
            "<p id='p'>text</p>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    // ── Parse-level: the selector survives with its authored text ──────────────────────────────

    [Theory]
    [InlineData(":host", ":host")]
    [InlineData(":host(.theme)", ":host(.theme)")]
    [InlineData(":state(--loaded)", ":state(--loaded)")]
    [InlineData("::placeholder", "::placeholder")]
    [InlineData("::part(label)", "::part(label)")]
    [InlineData("::slotted(span)", "::slotted(span)")]
    [InlineData("::-webkit-file-upload-button", "::-webkit-file-upload-button")]
    public void SelectorText_RoundTrips(string authored, string expected)
    {
        var sheet = $"{authored} {{ color: red; }}".ToCssStylesheet();
        var rule = Assert.Single(sheet.StyleRules);
        Assert.Equal(expected, rule.SelectorText);
    }

    [Fact]
    public void FunctionalPseudoElement_KeepsItsArgument_SoTwoOfThemAreDistinct()
    {
        // The argument is part of a functional pseudo-element's identity; folding it away would make
        // ::part(a) and ::part(b) the same selector.
        var sheet = "::part(a) { color: red; } ::part(b) { color: blue; }".ToCssStylesheet();
        var rules = sheet.StyleRules.ToList();
        Assert.Equal(2, rules.Count);
        Assert.Equal("::part(a)", rules[0].SelectorText);
        Assert.Equal("::part(b)", rules[1].SelectorText);
    }

    // ── #405: a nested construct inside a rule whose own selector did not parse ─────────────────

    [Fact]
    public void NestedAtRule_InsideARuleWithAnUnparsedSelector_DoesNotThrow()
    {
        // CreateStyle pushes its rule onto the node stack before filling declarations, so a rule whose
        // selector failed to parse is the "parent" while its block is consumed. Resolving a nested
        // construct against it used to dereference the null selector.
        var sheet = """
                    ::bogus-pseudo {
                      color: red;
                      @supports (color: color-mix(in lab, red, red)) {
                        color: blue;
                      }
                    }
                    p { color: #00ff00; }
                    """.ToCssStylesheet();

        // The undroppable part: parsing completes and the rule AFTER the malformed one survives.
        Assert.Contains(sheet.StyleRules, r => r.SelectorText == "p");
    }

    [Fact]
    public async Task TailwindPreflightShape_ParsesAndTheSurroundingRulesApply()
    {
        // The verbatim construct Tailwind v4's preflight emits unconditionally - a nested @supports
        // inside a ::placeholder rule, itself inside @layer + @supports.
        var html = Html(
            """
            @layer base {
              @supports (not (-webkit-appearance: -apple-pay-button)) or (contain-intrinsic-size: 1px) {
                ::placeholder {
                  color: currentcolor;
                  @supports (color: color-mix(in lab, red, red)) {
                    color: color-mix(in oklab, currentcolor 50%, transparent);
                  }
                }
              }
            }
            :root, :host { --brand: #0000ff; }
            p { color: var(--brand); }
            """,
            "<p id='p'>text</p>");
        Assert.Equal("rgb(0, 0, 255)", (await Box(html, "p")).Color);
    }

    [Fact]
    public void ANestedStyleRule_UnderAnUnparsedParent_IsConsumedAndDiscarded()
    {
        // Same shape as above with an ordinary nested rule rather than a nested at-rule. The nested
        // rule has no parent selector to resolve against, so it is consumed (keeping the parser in
        // sync) and dropped - never scoped to `:is(*)`, which would apply it to the whole document.
        var sheet = """
                    ::bogus-pseudo {
                      .inner { color: #ff0000; }
                    }
                    p { color: #00ff00; }
                    """.ToCssStylesheet();

        var rule = Assert.Single(sheet.StyleRules);
        Assert.Equal("p", rule.SelectorText);
    }

    [Fact]
    public void SelectorText_OfARuleWhoseSelectorDidNotParse_IsEmptyRatherThanThrowing()
    {
        // A failed selector leaves the rule with no Selector child at all (the setter removes it).
        // StyleRule.ToCss reads SelectorText unconditionally, so the getter has to survive that state.
        var rule = new StyleRule(new StylesheetParser()) { Selector = null };

        Assert.Null(rule.Selector);
        Assert.Equal(string.Empty, rule.SelectorText);
        Assert.NotNull(rule.ToCss());
    }

    // ── Helpers (mirrors CssNestingIntegrationTests.cs conventions) ─────────────────────────────

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
