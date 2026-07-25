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
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                  {{tag}} { overflow: hidden }
                </style></head><body><div>text</div></body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var box = LayoutHarness.Descendants(root).First(b =>
                string.Equals(b.HtmlTag?.Name, tag, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(CssConstants.Hidden, box.Overflow);
            Assert.False(MonolithicContent.IsScrollContainer(box));
            Assert.False(MonolithicContent.IsMonolithic(box));
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

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> BoxOf(string markup)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup));
            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            return box!;
        }
    }
}
