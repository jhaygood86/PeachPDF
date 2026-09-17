using PeachPDF.Html.Core.Dom;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Direct coverage for <see cref="CssLayoutEngine.IsHeightDefinite"/>/
    /// <see cref="CssLayoutEngine.ResolveDefiniteHeightValue"/> — the deterministic, recursive replacement
    /// for the old <c>CssBox.IsHeightCalculated</c> cached field (issue #1167). Asserted directly against
    /// these methods, post-layout, as a regression guard independent of any one downstream consumer (the
    /// inline-block percentage-height tests in <c>InlineBlockDeclaredHeightGeometryTests</c> exercise the
    /// same logic indirectly, through painted geometry).
    /// </summary>
    public class HeightDefinitenessTests
    {
        [Fact]
        public async Task AbsoluteLengthHeight_IsDefinite()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='height:100pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Equal(100.0, CssLayoutEngine.ResolveDefiniteHeightValue(box)!.Value, 3);
        }

        [Fact]
        public async Task PercentageHeightAgainstADefiniteAncestor_IsDefinite()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='height:400pt'><div id='box' style='height:50%'>x</div></div>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Equal(200.0, CssLayoutEngine.ResolveDefiniteHeightValue(box)!.Value, 3);
        }

        [Fact]
        public async Task PercentageHeightChainResolvesThroughMultipleLevels()
        {
            // A percentage height's own basis can itself be a percentage against a further ancestor -
            // CSS Sizing 3 §4's definiteness is recursive over the whole chain, not just one level.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='height:400pt'><div id='middle' style='height:50%'>" +
                "<div id='box' style='height:50%'>x</div></div></div>"));
            var middle = FindById(root, "middle")!;
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(middle));
            Assert.Equal(200.0, CssLayoutEngine.ResolveDefiniteHeightValue(middle)!.Value, 3);

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Equal(100.0, CssLayoutEngine.ResolveDefiniteHeightValue(box)!.Value, 3);
        }

        [Fact]
        public async Task PercentageHeightAgainstAnAutoHeightAncestor_IsIndefinite()
        {
            var (root, _) = await LayoutAsync(Wrap("<div><div id='box' style='height:50%'>x</div></div>"));
            var box = FindById(root, "box")!;

            Assert.False(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Null(CssLayoutEngine.ResolveDefiniteHeightValue(box));
        }

        [Fact]
        public async Task AspectRatioDerivedHeight_IsDefinite()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='width:100pt;aspect-ratio:2/1'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Equal(50.0, CssLayoutEngine.ResolveDefiniteHeightValue(box)!.Value, 3);
        }

        [Fact]
        public async Task MinHeightClampWinsOverAShorterExplicitHeight_ReflectsInTheResolvedValue()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='height:50pt;min-height:150pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Equal(150.0, CssLayoutEngine.ResolveDefiniteHeightValue(box)!.Value, 3);
        }

        [Fact]
        public async Task MaxHeightClampWinsOverATallerExplicitHeight_ReflectsInTheResolvedValue()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='height:150pt;max-height:50pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Equal(50.0, CssLayoutEngine.ResolveDefiniteHeightValue(box)!.Value, 3);
        }

        /// <summary>
        /// CSS 2.1 §17.5.3: a table cell's own explicit height can be stretched taller by its row, so the
        /// value resolver defers to the live, bottom-up <c>CssBox.Size.Height</c> for this one box kind -
        /// even though the cell's own declared height is still definite by §10.5.
        /// </summary>
        [Fact]
        public async Task TableCell_IsDefiniteButTheValueDefersToLiveSize()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<table><tr><td id='box' style='height:20pt'>a</td></tr></table>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Null(CssLayoutEngine.ResolveDefiniteHeightValue(box));
        }

        /// <summary>
        /// CSS 2.1 §10.6.7: an independent-formatting-context box whose height comes from a preferred
        /// aspect-ratio (not its own <c>height</c> declaration) can still be grown by a contained float, so
        /// the value resolver defers to the live size for this case too - unlike an ordinary block with an
        /// explicit <c>height</c>, which §10.6.3 exempts from float-containment growth entirely.
        /// </summary>
        [Fact]
        public async Task AspectRatioHeightOnAFloatContainingBox_IsDefiniteButTheValueDefersToLiveSize()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='box' style='overflow:hidden;width:50pt;aspect-ratio:1/1'>" +
                "<div style='float:left;height:200pt;width:10pt'></div></div>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Null(CssLayoutEngine.ResolveDefiniteHeightValue(box));
        }

        /// <summary>
        /// An ordinary independent-formatting-context box (e.g. <c>overflow:hidden</c>) with its own
        /// explicit <c>height</c> is NOT subject to the table-cell/aspect-ratio carve-outs above - §10.6.3
        /// makes a genuinely declared height the used height regardless of content, floats included, so
        /// this is the common case the fix closes rather than a residual.
        /// </summary>
        [Fact]
        public async Task ExplicitHeightOnAFloatContainingBox_ResolvesDirectlyDespiteEstablishingAnIndependentFormattingContext()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='box' style='overflow:hidden;height:40pt'>" +
                "<div style='float:left;height:200pt;width:10pt'></div></div>"));
            var box = FindById(root, "box")!;

            Assert.True(CssLayoutEngine.IsHeightDefinite(box));
            Assert.Equal(40.0, CssLayoutEngine.ResolveDefiniteHeightValue(box)!.Value, 3);
        }
    }
}
