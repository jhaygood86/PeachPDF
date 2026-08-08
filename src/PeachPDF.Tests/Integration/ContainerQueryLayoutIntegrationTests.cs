using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for <c>@container</c> size-query evaluation (issue #610). Each positive-match
    /// case necessarily exercises <see cref="HtmlContainerInt.PerformLayout"/>'s convergence loop: pass 1
    /// always starts with no container-size data (every <c>@container</c> condition false, matching the
    /// pre-fix behavior), so a case that expects the gated rule to apply can only pass if the loop's
    /// second pass - now armed with the first pass's resolved container size - actually re-cascades and
    /// re-layouts. Mirrors <see cref="MediaQueryLayoutIntegrationTests"/>'s harness idiom (bare
    /// <c>HtmlContainerInt</c> + <c>SetHtml</c>/<c>PerformLayout</c>, no <c>PdfGenerator</c>).
    /// <c>style()</c> queries are covered separately once implemented (issue #610's follow-on).
    /// </summary>
    public class ContainerQueryLayoutIntegrationTests
    {
        private const string Blue = "rgb(0, 0, 255)";
        private const string Red = "rgb(255, 0, 0)";

        // ── container-type establishing (or not) a size query container ─────────

        [Theory]
        [InlineData("normal", false)]      // normal establishes no SIZE query container
        [InlineData("size", true)]
        [InlineData("inline-size", true)]
        public async Task ContainerType_GatesWhetherASizeQueryContainerIsEstablished(string containerType, bool shouldApply)
        {
            var html = Html(
                $"#box {{ container-type: {containerType}; width: 300px; }} " +
                "p { color: #ff0000; } " +
                "@container (min-width: 100px) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(shouldApply ? Blue : Red, box.Color);
        }

        // ── the container shorthand (name / type) establishes the same container ─

        [Fact]
        public async Task ContainerShorthand_EstablishesTheSameQueryContainerAsTheLonghands()
        {
            var html = Html(
                "#box { container: sidebar / inline-size; width: 300px; } " +
                "p { color: #ff0000; } " +
                "@container sidebar (min-width: 100px) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        // ── cqw/cqh/cqi/cqb/cqmin/cqmax resolve against the nearest ancestor query container ─
        // (independent of @container rules entirely - a single top-down layout pass, no convergence
        // loop needed, since the container's own size is already known by the time its descendant's
        // width is resolved - see CssValueParser.ParseLength's box-aware overload.)

        [Theory]
        [InlineData("width: 50cqw", 150d)]   // 50% of the 300px (225pt) container inline size
        [InlineData("width: 50cqi", 150d)]
        public async Task ContainerRelativeUnit_ResolvesAgainstTheNearestAncestorContainer(string declaration, double expectedPx)
        {
            var html = Html(
                "#box { container-type: inline-size; width: 300px; } " +
                $"#target {{ {declaration}; height: 10px; }}",
                "<div id='box'><div id='target'></div></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            // ActualBoxSizingWidth is in points; 0.75pt per px (Length.PointsPerPx).
            Assert.Equal(expectedPx * 0.75, target!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task ContainerRelativeUnit_WithNoAncestorContainer_FallsBackToViewportSize()
        {
            // No ancestor container-type at all - falls back to the small-viewport unit (svw) per CSS
            // Containment 3 §6.2/issue #615, i.e. 50% of the harness's 800pt page width, not 0.
            var html = Html("#target { width: 50cqw; height: 10px; }", "<div id='target'></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(400d, target!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task ContainerRelativeUnit_ResolvesInsideCalc()
        {
            var html = Html(
                "#box { container-type: inline-size; width: 300px; } " +
                "#target { width: calc(50cqw + 10px); height: 10px; }",
                "<div id='box'><div id='target'></div></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            // 50% of the 300px (225pt) container inline size (112.5pt) + 10px (7.5pt) = 120pt.
            Assert.Equal(120d, target!.ActualBoxSizingWidth, 1);
        }

        // font-size threads the same container basis width/calc() do (DerivedStyle.ActualFont), but
        // for a box with text content it never actually resolves against it - word-splitting reads
        // ActualFont during cascade, before any layout pass has determined the ancestor container's
        // size, and the resolved font is cached forever from that first (pre-layout) read. See
        // .claude/accepted-gaps/font-size-container-relative-units-resolve-to-zero-for-text-content.md.
        [Fact]
        public async Task ContainerRelativeUnit_ForFontSize_ResolvesToZero_ForTextContent()
        {
            var html = Html(
                "#box { container-type: inline-size; width: 300px; } " +
                "#target { font-size: 10cqw; }",
                "<div id='box'><p id='target'>text</p></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(0d, target!.ActualFont.Size, 1);
        }

        // ── width / min-width / max-width / inline-size against an inline-size container ─

        [Theory]
        [InlineData("(min-width: 200px)", true)]
        [InlineData("(min-width: 400px)", false)]
        [InlineData("(max-width: 400px)", true)]
        [InlineData("(max-width: 200px)", false)]
        [InlineData("(width >= 300px)", true)]
        [InlineData("(inline-size >= 300px)", true)]
        [InlineData("(inline-size >= 400px)", false)]
        public async Task SizeFeature_MatchesAgainstTheInlineSizeContainersOwnWidth(string condition, bool shouldApply)
        {
            var html = Html(
                "#box { container-type: inline-size; width: 300px; } " +
                "p { color: #ff0000; } " +
                "@container " + condition + " { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(shouldApply ? Blue : Red, box.Color);
        }

        // ── height / block-size / aspect-ratio / orientation against a `size` container ─

        [Theory]
        [InlineData("(min-height: 100px)", true)]
        [InlineData("(min-height: 300px)", false)]
        [InlineData("(block-size >= 200px)", true)]
        [InlineData("(orientation: landscape)", true)]
        [InlineData("(orientation: portrait)", false)]
        [InlineData("(min-aspect-ratio: 1/1)", true)]
        [InlineData("(min-aspect-ratio: 3/1)", false)]
        public async Task SizeFeature_MatchesAgainstTheSizeContainersOwnHeight(string condition, bool shouldApply)
        {
            var html = Html(
                "#box { container-type: size; width: 400px; height: 200px; } " +
                "p { color: #ff0000; } " +
                "@container " + condition + " { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(shouldApply ? Blue : Red, box.Color);
        }

        // The key axis-restriction regression test: an `inline-size` container tracks only the inline
        // axis, so a block-axis condition must never match against it, however tall it actually is.
        [Theory]
        [InlineData("(min-height: 10px)")]
        [InlineData("(block-size >= 10px)")]
        [InlineData("(orientation: landscape)")]
        [InlineData("(min-aspect-ratio: 1/100)")]
        public async Task BlockAxisFeature_NeverMatchesAgainstAnInlineSizeOnlyContainer(string condition)
        {
            var html = Html(
                "#box { container-type: inline-size; width: 400px; height: 200px; } " +
                "p { color: #ff0000; } " +
                "@container " + condition + " { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // ── named containers: nearest match wins, a non-matching name never applies ─

        [Fact]
        public async Task NamedQuery_MatchesTheNearestAncestorDeclaringThatName()
        {
            var html = Html(
                "#outer { container-type: inline-size; container-name: outer; width: 500px; } " +
                "#inner { container-type: inline-size; container-name: inner; width: 150px; } " +
                "p { color: #ff0000; } " +
                "@container inner (min-width: 100px) { p { color: #0000ff; } }",
                "<div id='outer'><div id='inner'><p>text</p></div></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        [Fact]
        public async Task NamedQuery_SkipsANearerAncestorThatDoesNotDeclareTheQueriedName()
        {
            // #inner (150px) doesn't satisfy 200px on its own, and the queried name "outer" must skip
            // straight past #inner (which doesn't carry that name) to #outer (500px, satisfies 200px) -
            // not fall back to whichever ancestor is nearest regardless of name.
            var html = Html(
                "#outer { container-type: inline-size; container-name: outer; width: 500px; } " +
                "#inner { container-type: inline-size; container-name: inner; width: 150px; } " +
                "p { color: #ff0000; } " +
                "@container outer (min-width: 200px) { p { color: #0000ff; } }",
                "<div id='outer'><div id='inner'><p>text</p></div></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        [Fact]
        public async Task NamedQuery_DoesNotApply_WhenNoAncestorDeclaresThatName()
        {
            var html = Html(
                "#box { container-type: inline-size; container-name: sidebar; width: 500px; } " +
                "p { color: #ff0000; } " +
                "@container nomatch (min-width: 100px) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // ── container-name: none | <custom-ident>+ - `none` is only valid alone ─────

        [Fact]
        public async Task ContainerName_NoneCombinedWithOtherIdentifiers_IsRejected()
        {
            // "none outer" is not valid container-name syntax (CSS Containment 3 §2) - the whole
            // declaration must be dropped, leaving #box's container-name at its initial "none"
            // (unnamed), so a query for the name "outer" still never matches it.
            var html = Html(
                "#box { container-type: inline-size; container-name: none outer; width: 500px; } " +
                "p { color: #ff0000; } " +
                "@container outer (min-width: 100px) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // ── no eligible ancestor at all → CSS Containment 3 §7.2's "unknown", which is falsy ─

        [Fact]
        public async Task Query_DoesNotApply_WhenNoAncestorEstablishesAQueryContainer()
        {
            var html = Html(
                "p { color: #ff0000; } @container (min-width: 1px) { p { color: #0000ff; } }",
                "<p>text</p>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // ── style() queries (CSS Containment 3 §7.3) ─────────────────────────────

        [Fact]
        public async Task StyleQuery_Matches_WhenTheNearestAncestorsCustomPropertyEqualsTheQueriedValue()
        {
            var html = Html(
                "p { color: #ff0000; } @container style(--theme: dark) { p { color: #0000ff; } }",
                "<div id='box' style='--theme: dark;'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        [Fact]
        public async Task StyleQuery_DoesNotMatch_WhenTheCustomPropertyHasADifferentValue()
        {
            var html = Html(
                "p { color: #ff0000; } @container style(--theme: dark) { p { color: #0000ff; } }",
                "<div id='box' style='--theme: light;'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // The key eligibility-divergence regression test vs. size queries: container-type: normal (the
        // property's own initial value) does NOT disqualify an element as a style-query container, only
        // as a size-query one (BlockAxisFeature_NeverMatchesAgainstAnInlineSizeOnlyContainer's converse).
        [Fact]
        public async Task StyleQuery_Matches_AgainstAContainerTypeNormalAncestor()
        {
            var html = Html(
                "#box { container-type: normal; --theme: dark; } " +
                "p { color: #ff0000; } " +
                "@container style(--theme: dark) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        [Fact]
        public async Task StyleQuery_MatchesAStandardProperty_NotOnlyCustomProperties()
        {
            var html = Html(
                "#box { display: block; } " +
                "p { color: #ff0000; } " +
                "@container style(display: block) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        [Theory]
        [InlineData("(--a: 1) and (--b: 2)", "1", "2", true)]
        [InlineData("(--a: 1) and (--b: 2)", "1", "9", false)]
        [InlineData("(--a: 1) or (--b: 2)", "9", "2", true)]
        [InlineData("(--a: 1) or (--b: 2)", "9", "9", false)]
        [InlineData("not (--a: 9)", "1", "2", true)]
        [InlineData("not (--a: 1)", "1", "2", false)]
        public async Task StyleQuery_AndOrNotCombinators_EvaluateEndToEnd(string query, string aValue, string bValue, bool shouldApply)
        {
            var html = Html(
                $"#box {{ --a: {aValue}; --b: {bValue}; }} " +
                "p { color: #ff0000; } " +
                "@container style(" + query + ") { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(shouldApply ? Blue : Red, box.Color);
        }

        [Fact]
        public async Task StyleQuery_ThreeOperandAndChain_EvaluatesEndToEnd()
        {
            var html = Html(
                "#box { --a: 1; --b: 2; --c: 3; } " +
                "p { color: #ff0000; } " +
                "@container style((--a: 1) and (--b: 2) and (--c: 3)) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        [Fact]
        public async Task StyleQuery_ThreeOperandAndChain_DoesNotApply_WhenTheThirdOperandFails()
        {
            var html = Html(
                "#box { --a: 1; --b: 2; --c: 9; } " +
                "p { color: #ff0000; } " +
                "@container style((--a: 1) and (--b: 2) and (--c: 3)) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        [Fact]
        public async Task StyleQuery_UnrecognizedLeafToken_NeverMatches()
        {
            // A malformed style() argument (starts with neither an ident, "not", nor "(") - the parser's
            // fallback null-return path, exercised end-to-end rather than just at the parser-unit level.
            var html = Html(
                "p { color: #ff0000; } @container style(123) { p { color: #0000ff; } }",
                "<p>text</p>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        [Fact]
        public async Task StyleQuery_NamedQuery_MatchesTheNearestAncestorDeclaringThatName()
        {
            var html = Html(
                "#outer { container-name: outer; --theme: dark; } " +
                "#inner { container-name: inner; --theme: light; } " +
                "p { color: #ff0000; } " +
                "@container inner style(--theme: light) { p { color: #0000ff; } }",
                "<div id='outer'><div id='inner'><p>text</p></div></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        [Fact]
        public async Task StyleQuery_DoesNotApply_WhenNoAncestorDeclaresTheQueriedName()
        {
            var html = Html(
                "#box { container-name: sidebar; --theme: dark; } " +
                "p { color: #ff0000; } " +
                "@container nomatch style(--theme: dark) { p { color: #0000ff; } }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // The nested/chained convergence test: an outer size @container block sets a custom property on
        // an inner element, whose own @container style(...) condition queries that same property. Pass 1
        // (no size data) leaves the custom property unset, so the style query is false and the paragraph
        // stays red - only a full re-cascade pass, armed with #sizebox's real resolved width, both sets
        // the custom property AND re-evaluates the style query against it in the same pass, turning the
        // paragraph blue. This is the one test that actually exercises the full-rebuild-per-pass design
        // doing real cross-container work, not just a single-pass evaluation.
        [Fact]
        public async Task NestedContainerQueries_SizeGatedCustomPropertyFeedsAStyleQuery_ConvergesToTheCorrectAnswer()
        {
            var html = Html(
                "#sizebox { container-type: inline-size; width: 300px; } " +
                "p { color: #ff0000; } " +
                "@container (min-width: 200px) { #stylebox { --theme: dark; } } " +
                "@container style(--theme: dark) { p { color: #0000ff; } }",
                "<div id='sizebox'><div id='stylebox'><p>text</p></div></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Blue, box.Color);
        }

        // ── the convergence loop must be a genuine no-op when nothing gates on it ─

        [Fact]
        public async Task DocumentWithASizeContainerButNoContainerRule_RendersUnaffected()
        {
            // HasSizeContainers is true here (there IS a size container), so PerformLayout's loop does
            // run its extra pass(es) - but with no @container rule anywhere, nothing should change.
            var html = Html(
                "#box { container-type: inline-size; width: 300px; } p { color: #ff0000; }",
                "<div id='box'><p>text</p></div>");

            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // ── prior behavior for a document with neither a size container nor a rule ─

        [Fact]
        public async Task DocumentWithNoContainerFeatureAtAll_RendersUnaffected()
        {
            var html = Html("p { color: #ff0000; }", "<p>text</p>");
            var box = await FindByTag(html, "p");
            Assert.Equal(Red, box.Color);
        }

        // ─── Harness ──────────────────────────────────────────────────────────

        private static string Html(string css, string body) =>
            $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

        private static async Task<CssBox> FindByTag(string html, string tag)
        {
            var root = await BuildRoot(html);
            var box = DomUtils.GetBoxByTagName(root, tag);
            Assert.NotNull(box);
            return box!;
        }

        private static async Task<CssBox> BuildRoot(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);

            var size = new XSize(800, 600);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            await container.SetHtml(html, null);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }
    }
}
