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
        [InlineData("<img id='t' src='" + RasterPngFixture.OnePixelDataUri + "' style='width:10pt;height:10pt'>")]
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
        }

        // §2 makes a scroll container monolithic only where its block size is capped: "any elements with
        // overflow set to auto or scroll and any elements with overflow: hidden and a non-auto logical
        // height (and no specified maximum logical height)" is a "may", and an auto-height box has nothing
        // to clip in the block axis. PeachPDF takes the capped case for every overflow value.
        [Theory]
        [InlineData("overflow:hidden", false)]
        [InlineData("overflow:auto", false)]
        [InlineData("overflow:scroll", false)]
        [InlineData("overflow:hidden;height:20pt", true)]
        [InlineData("overflow:auto;height:20pt", true)]
        [InlineData("overflow:scroll;max-height:20pt", true)]
        [InlineData("overflow:hidden;max-height:20pt", true)]
        [InlineData("overflow:visible;height:20pt", false)]
        // A percentage of an indefinite containing block behaves as auto/none (CSS 2.1 §10.5, §10.7).
        [InlineData("overflow:hidden;height:50%", false)]
        [InlineData("overflow:hidden;max-height:50%", false)]
        // In a vertical writing mode the logical height is the physical width.
        [InlineData("overflow:hidden;writing-mode:vertical-rl;height:20pt", false)]
        [InlineData("overflow:hidden;writing-mode:vertical-rl;width:20pt", true)]
        [InlineData("overflow:hidden;writing-mode:vertical-lr;max-width:20pt", true)]
        public async Task ScrollContainer_IsMonolithicOnlyWithACappedBlockSize(string css, bool expected)
        {
            var box = await BoxOf($"<div id='t' style='{css}'>text</div>");

            Assert.Equal(expected, MonolithicContent.IsMonolithic(box));
        }

        // Only a block box in block flow fragments when its height is auto. An inline-block may stay
        // monolithic under §2, and a float is placed at its assigned position, which cannot carry a break
        // into the next fragmentainer. A flex/grid item's height is set by its engine mid-layout, so a
        // block-size test would answer differently before and after the engine commits the item.
        [Theory]
        [InlineData("<div><span id='t' style='display:inline-block;overflow:hidden'>text</span></div>", true)]
        [InlineData("<div style='display:flex'><div id='t' style='overflow:hidden'>text</div></div>", true)]
        [InlineData("<div style='display:grid'><div id='t' style='overflow:auto'>text</div></div>", true)]
        [InlineData("<ul><li id='t' style='overflow:hidden'>text</li></ul>", false)]
        [InlineData("<div><div id='t' style='overflow:hidden;float:left'>text</div></div>", true)]
        [InlineData("<div>x <span style='display:inline-block'><div id='t' style='overflow:hidden'>text</div></span></div>", true)]
        [InlineData("<div><div style='float:left'><div><div id='t' style='overflow:hidden'>text</div></div></div></div>", true)]
        [InlineData("<div style='position:relative'><div><div id='t' style='overflow:hidden'>text</div></div></div>", false)]
        [InlineData("<div><div id='t' style='overflow:hidden;float:bottom'>text</div></div>", true)]
        [InlineData("<div><div style='float:top'><div id='t' style='overflow:hidden'>text</div></div></div>", true)]
        [InlineData("<div style='writing-mode:vertical-rl'><div id='t' style='writing-mode:horizontal-tb;overflow:hidden'>text</div></div>", true)]
        public async Task AutoHeightScrollContainer_StaysMonolithicOutsideBlockFlow(string markup, bool expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup));

            Assert.Equal(expected, MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "t")!));
        }

        // An auto-height scroll container fragments only when everything inside it can carry a break on to
        // the next page, and none of its ancestors is a multi-column container. An absolutely or fixed
        // positioned box, a page float, a multi-column/flex/grid container or a table caption inside it
        // keeps it monolithic, and so does a multi-column container above it. A float or an atomic inline
        // does not: the inline flow that places one lays its content out unbroken, so the clearfix wrapper
        // around a floated menu fragments and the float keeps every line. display:none content generates
        // nothing.
        [Theory]
        [InlineData("<div id='t' style='overflow:hidden'><div style='float:left'>f</div></div>", false)]
        [InlineData("<div id='t' style='overflow:hidden'><div><div style='float:right'>f</div></div></div>", false)]
        [InlineData("<div id='t' style='overflow:hidden'><div style='float:bottom'>f</div>a</div>", true)]
        [InlineData("<div id='t' style='overflow:hidden;position:relative'>a<div style='position:absolute'>b</div></div>", true)]
        [InlineData("<div id='t' style='overflow:hidden'>a<div style='position:fixed'>b</div></div>", true)]
        [InlineData("<div id='t' style='overflow:hidden'>a <span style='display:inline-block'>b</span></div>", false)]
        [InlineData("<div id='t' style='overflow:hidden'>a <table style='display:inline-table'><tr><td>b</td></tr></table></div>", false)]
        [InlineData("<div id='t' style='overflow:hidden'><div style='columns:2'>a</div></div>", true)]
        [InlineData("<div id='t' style='overflow:hidden'><div style='display:flex'>a</div></div>", true)]
        [InlineData("<div id='t' style='overflow:hidden'><div style='display:grid'><p style='break-inside:avoid'>a</p></div></div>", true)]
        [InlineData("<div id='t' style='overflow:hidden'><table><caption>c</caption><tr><td>a</td></tr></table></div>", true)]
        [InlineData("<div style='columns:2'><div id='t' style='overflow:hidden'>a</div></div>", true)]
        [InlineData("<div id='t' style='overflow:hidden'><p>a <b>b</b></p><ul><li>c</li></ul></div>", false)]
        [InlineData("<div id='t' style='overflow:hidden'><table><tr><td>a</td></tr></table></div>", false)]
        [InlineData("<div id='t' style='overflow:hidden'><div style='overflow:auto'><p>a</p></div></div>", false)]
        [InlineData("<div id='t' style='overflow:hidden'><div style='display:none'><div style='float:left'>f</div></div>a</div>", false)]
        public async Task AutoHeightScrollContainer_StaysMonolithicAroundContentThatDropsABreak(string markup, bool expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup));

            Assert.Equal(expected, MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "t")!));
        }

        // The ancestors that carry a break on: a table and its row groups, rows and cells, and a block-level
        // flex or grid container (below the item, which is itself excluded). A scroll container under each
        // fragments, which the allow-list must keep; dropping an entry would make these monolithic.
        [Theory]
        [InlineData("<table><tr><td><div id='t' style='overflow:hidden'>a</div></td></tr></table>")]
        [InlineData("<table><tbody><tr><td><div id='t' style='overflow:hidden'>a</div></td></tr></tbody></table>")]
        [InlineData("<div style='display:flex'><div><div id='t' style='overflow:hidden'>a</div></div></div>")]
        [InlineData("<div style='display:grid'><div><div id='t' style='overflow:hidden'>a</div></div></div>")]
        public async Task AutoHeightScrollContainer_UnderAnAncestorThatCarriesABreak_Fragments(string markup)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup));

            Assert.False(MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "t")!));
        }

        // A vertical box's logical height is its width, whose percentage base is the containing block's
        // width, definite under a horizontal parent. Under a vertical parent block the box is monolithic
        // anyway: that parent lays its children out at assigned positions that cannot carry a break.
        [Theory]
        [InlineData("", true)]
        [InlineData("writing-mode:vertical-rl", true)]
        public async Task VerticalPercentageWidth_CapsTheBlockSizeOnlyAgainstADefiniteWidth(string parentCss, bool expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='{parentCss}'><div id='t' style='overflow:hidden;writing-mode:vertical-rl;width:50%'>text</div></div>"));

            Assert.Equal(expected, MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "t")!));
        }

        // Layout applies a height or max-height only when it is a length, so a keyword is no cap.
        [Theory]
        [InlineData("overflow:hidden;max-height:auto")]
        [InlineData("overflow:hidden;max-height:none")]
        [InlineData("overflow:hidden;height:auto")]
        public async Task KeywordBlockSize_DoesNotCapTheBlockSize(string css)
        {
            var box = await BoxOf($"<div id='t' style='{css}'>text</div>");

            Assert.False(MonolithicContent.IsMonolithic(box));
        }

        // An absolutely positioned box's percentage resolves against its nearest positioned ancestor, not
        // its in-flow containing block, so an auto-height wrapper in between does not make it auto.
        // Layout clamps max-height against the in-flow containing block for every box, abspos included
        // (ApplyHeight), so against the auto-height wrapper a percentage max-height is no cap there.
        [Theory]
        [InlineData("height:50%", true)]
        [InlineData("max-height:50%", false)]
        public async Task AbsolutePercentage_CapsTheBlockSizeAgainstTheBaseLayoutUses(string css, bool expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='position:relative;height:500pt'><div>" +
                $"<div id='t' style='position:absolute;overflow:auto;{css}'>text</div></div></div>"));

            Assert.Equal(expected, MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "t")!));
        }

        // A block size can be fixed without height or max-height: by a preferred aspect ratio (CSS Box
        // Sizing 4 §5), or by both block-axis insets of an absolutely positioned box (CSS 2.1 §10.6.4).
        [Theory]
        [InlineData("overflow:hidden;aspect-ratio:1;width:100pt", true)]
        [InlineData("overflow:hidden;aspect-ratio:auto", false)]
        [InlineData("overflow:auto;position:absolute;top:0;bottom:0", true)]
        [InlineData("overflow:auto;position:absolute;top:0", false)]
        [InlineData("overflow:auto;position:absolute;writing-mode:vertical-rl;left:0;right:0", true)]
        public async Task SizeFixedWithoutAHeight_CapsTheBlockSize(string css, bool expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='position:relative;height:300pt'><div id='t' style='{css}'>text</div></div>"));

            Assert.Equal(expected, MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "t")!));
        }

        // A percentage inside a flex or grid item resolves against a height its engine sets mid-layout, so
        // it keeps the stable answer (capped) rather than one that flips within a pass.
        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid")]
        public async Task PercentageInsideAFlexOrGridItem_CapsTheBlockSize(string containerCss)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='{containerCss}'><div id='item'><div id='t' style='overflow:hidden;height:50%'>text</div></div></div>"));

            var box = LayoutHarness.FindById(root, "t")!;
            Assert.True(MonolithicContent.IsMonolithic(box));

            // The state the item is in before its engine resolves and pins its height, which is what a
            // reader asking earlier in the same pass sees. The answer must not change with it.
            var item = LayoutHarness.FindById(root, "item")!;
            item.Height = "auto";
            item.AlgorithmicDefiniteHeight = null;

            Assert.True(MonolithicContent.IsMonolithic(box));
        }

        [Fact]
        public async Task PercentageHeightOfADefiniteContainingBlock_CapsTheBlockSize()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='height:100pt'><div id='t' style='overflow:hidden;height:50%'>text</div></div>"));

            Assert.True(MonolithicContent.IsMonolithic(LayoutHarness.FindById(root, "t")!));
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
            var box = await BoxOfTag("html { overflow: hidden } body { overflow: auto; max-height: 1000pt }", "body");

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
