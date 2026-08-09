using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies the implicit CSS "list-item" counter (CSS2.1 12.5.1 / CSS Lists Level 3) - the same
    /// counter <c>content: counter(list-item)</c> and the default (non-overridden) marker's own
    /// numbering both resolve through (<see cref="CssCounterEngine"/>) - correctly accounts for the
    /// WHATWG HTML presentational hints for &lt;ol start&gt;/&lt;ol reversed&gt;/&lt;li value&gt;, and
    /// applies to any element with a computed <c>Display: list-item</c>, not just &lt;li&gt;.
    /// </summary>
    public class ListItemCounterIntegrationTests
    {
        [Fact]
        public async Task DefaultNumbering_MatchesExplicitCounterOverride()
        {
            // The core "single source of truth" regression guard: an author ::marker override using
            // content: counter(list-item) must always agree with what the default (non-overridden)
            // marker on the same list would have shown.
            var defaultRoot = await BuildAndLayout(Wrap("<ol><li id='a'>a</li><li id='b'>b</li></ol>"));
            var overrideRoot = await BuildAndLayout(Wrap(
                "<style>li::marker { content: counter(list-item) \".\" }</style><ol><li id='a'>a</li><li id='b'>b</li></ol>"));

            var defaultA = (CssBoxMarker)FindById(defaultRoot, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var defaultB = (CssBoxMarker)FindById(defaultRoot, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var overrideA = (CssBoxMarker)FindById(overrideRoot, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var overrideB = (CssBoxMarker)FindById(overrideRoot, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal(defaultA.Text, overrideA.Text);
            Assert.Equal(defaultB.Text, overrideB.Text);
            Assert.Equal("1.", defaultA.Text);
            Assert.Equal("2.", defaultB.Text);
        }

        [Fact]
        public async Task OlStart_OffsetsNumbering()
        {
            var root = await BuildAndLayout(Wrap("<ol start='5'><li id='a'>a</li><li id='b'>b</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("5.", a.Text);
            Assert.Equal("6.", b.Text);
        }

        [Fact]
        public async Task OlReversed_WithoutStart_CountsDownFromChildCount()
        {
            var root = await BuildAndLayout(Wrap("<ol reversed><li id='a'>a</li><li id='b'>b</li><li id='c'>c</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var c = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("3.", a.Text);
            Assert.Equal("2.", b.Text);
            Assert.Equal("1.", c.Text);
        }

        [Fact]
        public async Task OlReversedWithStart_CountsDownFromStart()
        {
            var root = await BuildAndLayout(Wrap("<ol reversed start='10'><li id='a'>a</li><li id='b'>b</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("10.", a.Text);
            Assert.Equal("9.", b.Text);
        }

        [Fact]
        public async Task LiValue_ResetsNumberingAndSubsequentItemsContinue()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol><li id='a'>a</li><li id='b' value='100'>b</li><li id='c'>c</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var c = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("1.", a.Text);
            Assert.Equal("100.", b.Text);
            Assert.Equal("101.", c.Text);
        }

        [Fact]
        public async Task AuthorCounterReset_OverridesOlStartPresentationalHint()
        {
            // Literal author CSS targeting list-item must win over the attribute-derived hint -
            // presentational hints are the lowest possible precedence.
            var root = await BuildAndLayout(Wrap(
                "<style>ol { counter-reset: list-item 41 }</style><ol start='5'><li id='a'>a</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal("42.", a.Text);
        }

        [Fact]
        public async Task DisplayListItem_OnNonLiElement_GetsMarkerAndSequentialNumbering()
        {
            // Marker generation and the implicit list-item counter both key off computed
            // Display: list-item, not off the <li> tag/UA-rule selector match.
            var root = await BuildAndLayout(Wrap(
                "<div style='display: list-item; list-style-type: decimal' id='a'>a</div>" +
                "<div style='display: list-item; list-style-type: decimal' id='b'>b</div>"));

            var divA = FindById(root, "a")!;
            var divB = FindById(root, "b")!;

            var markerA = (CssBoxMarker)divA.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerB = (CssBoxMarker)divB.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("1.", markerA.Text);
            Assert.Equal("2.", markerB.Text);
        }

        [Fact]
        public async Task DisplayListItem_MixedWithRealLiSiblings_ShareOneCounterScope()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol><li id='a'>a</li><div style='display: list-item' id='b'>b</div><li id='c'>c</li></ol>"));

            var markerA = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerB = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerC = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("1.", markerA.Text);
            Assert.Equal("2.", markerB.Text);
            Assert.Equal("3.", markerC.Text);
        }

        [Fact]
        public async Task BeforeContent_CounterDecimalLeadingZero_RendersZeroPaddedNumbers()
        {
            // Exact repro from issue #128: content: counter(x, decimal-leading-zero) used to emit
            // nothing at all (silent no-op). Expected "01 first" / "02 second".
            var root = await BuildAndLayout(Wrap(
                "<style>li::before { content: counter(list-item, decimal-leading-zero) }</style>" +
                "<ol><li id='a'>first</li><li id='b'>second</li></ol>"));

            var beforeA = FindById(root, "a")!.Boxes.Single(b => b.IsBeforePseudoElement);
            var beforeB = FindById(root, "b")!.Boxes.Single(b => b.IsBeforePseudoElement);

            Assert.Equal("01", beforeA.Text);
            Assert.Equal("02", beforeB.Text);
        }

        [Fact]
        public async Task BeforeContent_CounterUnknownStyle_FallsBackToDecimal()
        {
            // CSS Counter Styles Level 3 §2: unknown style renders as decimal, not empty.
            var root = await BuildAndLayout(Wrap(
                "<style>li::before { content: counter(list-item, bogus-style) }</style>" +
                "<ol><li id='a'>first</li></ol>"));

            var beforeA = FindById(root, "a")!.Boxes.Single(b => b.IsBeforePseudoElement);

            Assert.Equal("1", beforeA.Text);
        }

        [Theory]
        // Representative "numeric" (positional digit-substitution) styles - CSS Counter Styles Level 3 §6.1.
        [InlineData("devanagari", 12, "१२.")]
        [InlineData("thai", 12, "๑๒.")]
        [InlineData("cjk-decimal", 12, "一二.")]
        // "fixed" styles (§6.4) - in range, and beyond it (falls back to plain decimal).
        [InlineData("cjk-earthly-branch", 1, "子.")]
        [InlineData("cjk-earthly-branch", 13, "13.")]
        [InlineData("cjk-heavenly-stem", 1, "甲.")]
        [InlineData("cjk-heavenly-stem", 11, "11.")]
        // Ethiopic numeric (§7.2)'s own algorithm.
        [InlineData("ethiopic-numeric", 100, "፻.")]
        // Armenian variants (§6.1) - lower-armenian is new; upper-armenian is an alias of armenian; both
        // need their thousands place (armenian previously silently dropped it above 999).
        [InlineData("lower-armenian", 1, "ա.")]
        [InlineData("upper-armenian", 1, "Ա.")]
        [InlineData("armenian", 1000, "Ռ.")]
        // Hebrew (§6.1) - the 15/16 Tetragrammaton-avoidance override, and the thousands place (also
        // previously silently dropped above 999).
        [InlineData("hebrew", 15, "טו.")]
        [InlineData("hebrew", 16, "טז.")]
        [InlineData("hebrew", 1000, "א׳.")]
        // hiragana-iroha/katakana-iroha (§6.2) - previously silently reused the dictionary-order table.
        [InlineData("hiragana-iroha", 1, "い.")]
        [InlineData("katakana-iroha", 1, "イ.")]
        public async Task Marker_NewOrFixedListStyleType_RendersExpectedGlyph(string listStyleType, int index, string expected)
        {
            var items = string.Concat(Enumerable.Range(1, index).Select(i => $"<li id='i{i}'>x</li>"));
            var root = await BuildAndLayout(Wrap($"<ol style='list-style-type: {listStyleType}'>{items}</ol>"));
            var last = (CssBoxMarker)FindById(root, $"i{index}")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal(expected, last.Text);
        }

        [Fact]
        public async Task Marker_StringListStyleType_RendersLiteralTextWithNoSuffix()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol style='list-style-type: \"-&gt; \"'><li id='a'>a</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("-> ", a.Text);
        }

        [Fact]
        public async Task Marker_StringListStyleType_HandlesNonAsciiLiteral()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol style='list-style-type: \"→ \"'><li id='a'>a</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("→ ", a.Text);
        }

        [Fact]
        public async Task BeforeContent_CounterDisclosureOpenStyle_RendersFixedGlyph()
        {
            // content: counter(name, disclosure-open) must agree with the default marker's own
            // disclosure-open rendering, per the "single source of truth" convention this suite already
            // guards for other styles (see DefaultNumbering_MatchesExplicitCounterOverride above).
            var root = await BuildAndLayout(Wrap(
                "<style>li::before { content: counter(list-item, disclosure-open) }</style>" +
                "<ol><li id='a'>first</li><li id='b'>second</li></ol>"));

            var beforeA = FindById(root, "a")!.Boxes.Single(b => b.IsBeforePseudoElement);
            var beforeB = FindById(root, "b")!.Boxes.Single(b => b.IsBeforePseudoElement);

            Assert.Equal("▾", beforeA.Text);
            Assert.Equal("▾", beforeB.Text);
        }

        [Fact]
        public async Task Marker_DisclosureListStyleTypes_RenderFixedGlyphRegardlessOfIndex()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol style='list-style-type: disclosure-open'><li id='a'>a</li><li id='b'>b</li></ol>" +
                "<ol style='list-style-type: disclosure-closed'><li id='c'>c</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var c = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("▾ ", a.Text);
            Assert.Equal("▾ ", b.Text);
            Assert.Equal("▸ ", c.Text);
        }

        [Fact]
        public async Task Marker_DecimalLeadingZeroListStyle_ZeroPadsSingleDigits()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol style='list-style-type: decimal-leading-zero'><li id='a'>a</li><li id='b'>b</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("01.", a.Text);
            Assert.Equal("02.", b.Text);
        }

        [Fact]
        public async Task CounterSet_OverridesCounterResetOnTheSameElement()
        {
            // CSS Lists 3 §2.3: counter-set applies after counter-reset on the same element, so it must
            // win over that element's own reset - content: counter(x) must reflect the set value (100),
            // not the reset value (0) counter-reset alone would leave it at.
            var root = await BuildAndLayout(Wrap(
                "<style>li::before { content: counter(x) \". \" }</style>"
                + "<ol style='counter-reset: x'><li id='a' style='counter-set: x 100'>a</li></ol>"));

            var beforeA = FindById(root, "a")!.Boxes.Single(b => b.IsBeforePseudoElement);

            Assert.Equal("100. ", beforeA.Text);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<CssBox> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
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

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
