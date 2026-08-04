using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// The monolithic classifier, asserted against
    /// <see href="https://www.w3.org/TR/css-break-3/#monolithic">css-break-3 §2</see>'s own set rather than
    /// against the engine's prior behaviour — the same way <c>BreakValuesTests</c> reads §3's value sets.
    /// </summary>
    public class MonolithicContentTests
    {
        // ── replaced elements ─────────────────────────────────────────────────

        [Theory]
        [InlineData("<img id='t' src='data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==' style='width:10pt;height:10pt'>")]
        [InlineData("<svg id='t' width='10' height='10'><rect width='10' height='10'/></svg>")]
        [InlineData("<iframe id='t' style='width:10pt;height:10pt'></iframe>")]
        public async Task ReplacedElement_IsMonolithic(string markup)
        {
            var box = await BoxOf(markup);

            Assert.True(MonolithicContent.IsReplaced(box));
            Assert.True(MonolithicContent.IsMonolithic(box));
        }

        // An <object> is replaced only once its data resource resolves to something renderable, so with
        // nothing to resolve it is an ordinary container - the one case the type test alone gets wrong.
        [Fact]
        public async Task UnresolvedObject_IsNotReplaced()
        {
            var box = await BoxOf("<object id='t' data='nothing-here.bin'>fallback</object>");

            Assert.True(box is CssBoxObject);
            Assert.False(MonolithicContent.IsReplaced(box));
            Assert.False(MonolithicContent.IsMonolithic(box));
        }

        [Fact]
        public async Task OrdinaryBlock_IsNotMonolithic()
        {
            var box = await BoxOf("<div id='t'>text</div>");

            Assert.False(MonolithicContent.IsReplaced(box));
            Assert.False(MonolithicContent.IsMonolithic(box));
        }

        // ── scroll containers ─────────────────────────────────────────────────

        [Theory]
        [InlineData("hidden", true)]
        [InlineData("scroll", true)]
        [InlineData("auto", true)]
        [InlineData("visible", false)]
        // Not in Map.OverflowModes, so it never converts and the box keeps `visible` - which is the answer
        // §2 wants for `clip` anyway, though by accident rather than by design.
        [InlineData("clip", false)]
        public async Task Overflow_DecidesScrollContainer(string overflow, bool expected)
        {
            var box = await BoxOf($"<div id='t' style='overflow:{overflow}'>text</div>");

            Assert.Equal(expected, MonolithicContent.IsScrollContainer(box));
            Assert.Equal(expected, MonolithicContent.IsMonolithic(box));
        }

        // CSS Overflow 3 §3.3: the root's overflow propagates to the viewport, and <body>'s does when the
        // root's is visible, so neither is itself a scroll container. Without this the near-universal
        // `html { overflow: hidden }` idiom would declare an entire document unbreakable.
        [Theory]
        [InlineData("html")]
        [InlineData("body")]
        public async Task ViewportPropagationSource_IsNotAScrollContainer(string tag)
        {
            var box = await BoxOfTag($"{tag} {{ overflow: hidden }}", tag);

            Assert.Equal(Overflow.Hidden, box.Overflow.Value);
            Assert.False(MonolithicContent.IsScrollContainer(box));
            Assert.False(MonolithicContent.IsMonolithic(box));
        }

        // The other half of §3.3, which the theory above cannot see because it never sets both: the body's
        // value propagates only while the root's own is `visible`. Once the root has declared one it took
        // the propagation, and the body is a scroll container in its own right.
        [Fact]
        public async Task Body_UnderARootThatAlreadyDeclaredOverflow_IsAScrollContainer()
        {
            var box = await BoxOfTag("html { overflow: hidden } body { overflow: auto }", "body");

            Assert.True(MonolithicContent.IsScrollContainer(box));
            Assert.True(MonolithicContent.IsMonolithic(box));
        }

        // ...and the companion direction, so the test above is not passing merely because `auto` is set.
        [Fact]
        public async Task Body_UnderAVisibleRoot_PropagatesAndIsNotAScrollContainer()
        {
            var box = await BoxOfTag("html { overflow: visible } body { overflow: auto }", "body");

            Assert.False(MonolithicContent.IsScrollContainer(box));
        }

        // A stray element that happens to be named "body" but is not the root's own child gets no
        // propagation - §3.3 is about the document's body element, not the tag name.
        [Fact]
        public async Task NestedElementNamedBody_IsAnOrdinaryScrollContainer()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div><body id='t' style='overflow:hidden'>text</body></div>"));

            var box = LayoutHarness.FindById(root, "t");

            // The parser may or may not keep such an element; the assertion only means anything if it did.
            if (box is null || box.ParentBox is null || box.ParentBox.HtmlTag?.Name is "html") return;

            Assert.True(MonolithicContent.IsScrollContainer(box));
        }

        // ── the engine constraint, which is a different question ──────────────

        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid")]
        [InlineData("display:table")]
        [InlineData("column-count:2")]
        public async Task EngineThatPaginatesItself_IsNotBySpecMonolithic(string style)
        {
            var box = await BoxOf($"<div id='t' style='{style}'><span>text</span></div>");

            Assert.True(MonolithicContent.PaginatesItsOwnContent(box));

            // The whole point of separating the two: these boxes are suppressed for an implementation
            // reason, and §2 says nothing about them.
            Assert.False(MonolithicContent.IsMonolithic(box));
        }

        [Fact]
        public async Task OrdinaryBlock_DoesNotPaginateItsOwnContent()
        {
            var box = await BoxOf("<div id='t'>text</div>");

            Assert.False(MonolithicContent.PaginatesItsOwnContent(box));
        }

        // ── the fitting question ──────────────────────────────────────────────

        [Theory]
        // Band is 160pt here (200pt page less two 20pt margins).
        [InlineData(100, 0, 0, false)]
        [InlineData(160, 0, 0, true)]
        [InlineData(200, 0, 0, true)]
        // Cloned decorations count towards it: the fragment left behind closes with its own bottom border
        // and padding, and a resumed one re-opens with the top set (§6.2).
        [InlineData(150, 5, 5, true)]
        [InlineData(150, 4, 5, false)]
        public async Task FitsNoFragmentainer_CountsClonedDecorations(
            double height, double clonedStart, double clonedEnd, bool expected)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div>text</div>"), pageHeight: 200, margin: 20);

            Assert.Equal(expected,
                MonolithicContent.FitsNoFragmentainer(height, clonedStart, clonedEnd, container));
        }

        // The companion question, and deliberately not the negation of the one above: "will it fit *there*"
        // is asked of one specific band, where a box exactly as tall as the band plainly does fit. The
        // relocation asks this one, so a band-tall box has somewhere to go.
        [Theory]
        [InlineData(100, 0, 0, 160, true)]
        [InlineData(160, 0, 0, 160, true)]
        [InlineData(161, 0, 0, 160, false)]
        [InlineData(150, 5, 5, 160, true)]
        [InlineData(151, 5, 5, 160, false)]
        public void FitsInBand_TreatsAnExactFitAsFitting(
            double height, double clonedStart, double clonedEnd, double bandHeight, bool expected)
        {
            Assert.Equal(expected, MonolithicContent.FitsInBand(height, clonedStart, clonedEnd, bandHeight));
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> BoxOfTag(string css, string tag)
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>{{css}}</style></head>
                <body><div>text</div></body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);

            return LayoutHarness.Descendants(root).First(b =>
                string.Equals(b.HtmlTag?.Name, tag, StringComparison.OrdinalIgnoreCase));
        }

        private static async Task<CssBox> BoxOf(string markup)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup));
            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            return box!;
        }
    }
}
