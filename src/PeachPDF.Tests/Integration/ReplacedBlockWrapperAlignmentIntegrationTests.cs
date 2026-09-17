using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>display: block</c> <c>&lt;img&gt;</c>/inline <c>&lt;svg&gt;</c> is wrapped in a synthetic
    /// anonymous block (<see cref="PeachPDF.Html.Core.Dom.CssBox.IsReplacedBlockWrapper"/>, see
    /// <see cref="ReplacedBlockWrapperLayoutIntegrationTests"/>'s own remarks) purely so this engine can
    /// size it as a single atomic inline "word". That wrapper's synthetic line must not be treated as a
    /// real inline formatting context an ancestor's <c>text-align</c>/<c>text-align-last</c> can reach
    /// into - <see href="https://www.w3.org/TR/css-text-3/#text-align-property">css-text-3 §6.1</see>
    /// scopes both to a block's inline-level content only. Only the wrapped element's own
    /// <c>margin-left</c>/<c>margin-right: auto</c> (<see href="https://www.w3.org/TR/CSS21/visudet.html#blockwidth">CSS 2.1 §10.3.3</see>,
    /// via <see href="https://www.w3.org/TR/CSS21/visudet.html#block-replaced-width">§10.3.4</see> for a
    /// replaced element) may move it (issue #1176).
    /// </summary>
    public class ReplacedBlockWrapperAlignmentIntegrationTests
    {
        [Fact]
        public async Task BlockImage_InsideATextAlignCenterContainer_IsNotCentered()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='width:400pt;text-align:center'>"
                + "<img id='img' style='display:block;width:160pt;height:160pt' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            // The image is Display:Inline-forced by DomParser.CorrectReplacedElementBoxes (it is the
            // synthetic wrapper's single word), so its painted position is its own word's Left, not a
            // Location a block box never assigns it - see FirstWord's own remarks.
            Assert.Equal(wrap.Location.X, img.FirstWord.Left, 3);
        }

        [Fact]
        public async Task BlockImage_InsideATextAlignRightContainer_IsNotShiftedRight()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='width:400pt;text-align:right'>"
                + "<img id='img' style='display:block;width:160pt;height:160pt' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(wrap.Location.X, img.FirstWord.Left, 3);
        }

        [Fact]
        public async Task BlockImage_WithAutoHorizontalMargins_IsStillCentered()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='width:400pt'>"
                + "<img id='img' style='display:block;width:160pt;height:160pt;margin-left:auto;margin-right:auto' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(wrap.Location.X + (400 - 160) / 2.0, img.FirstWord.Left, 3);
        }

        [Fact]
        public async Task BlockImage_WithAutoMarginsAndMaxWidth_CentersUsingTheClampedWidth()
        {
            // ResolveAutoHorizontalMargin's IsReplacedBlockWrapper branch reads the declared `width`
            // directly (this box never goes through GetBoxWidth's own block-width resolution), so it has
            // to apply max-width/min-width itself (CSS 2.1 §10.4) rather than centering around the
            // unclamped declared width - mirroring the auto-width branch's own clamp a few lines below it.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='width:400pt'>"
                + "<img id='img' style='display:block;width:300pt;max-width:100pt;height:100pt;"
                + "margin-left:auto;margin-right:auto' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(wrap.Location.X + (400 - 100) / 2.0, img.FirstWord.Left, 3);
        }

        [Fact]
        public async Task BlockImage_WithAutoMarginsAndMinWidth_CentersUsingTheWidenedWidth()
        {
            // The same clamp's min-width side (CSS 2.1 §10.4: min wins over max) - a declared width
            // narrower than min-width must widen before the centering split, not center around the
            // narrower declared width.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='width:400pt'>"
                + "<img id='img' style='display:block;width:50pt;min-width:100pt;height:100pt;"
                + "margin-left:auto;margin-right:auto' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(wrap.Location.X + (400 - 100) / 2.0, img.FirstWord.Left, 3);
        }

        [Fact]
        public async Task InlineImage_InsideATextAlignCenterContainer_IsStillCentered()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='width:400pt;text-align:center'>"
                + "<img id='img' style='width:20pt;height:20pt' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.False(img.IsReplacedBlockWrapper);
            Assert.False(img.ParentBox!.IsReplacedBlockWrapper);
            Assert.Equal(wrap.Location.X + (400 - 20) / 2.0, img.FirstWord.Left, 3);
        }

        [Fact]
        public async Task BlockImage_InVerticalWritingModeWithTextAlignCenter_IsNotCentered()
        {
            // The vertical counterpart (CssLayoutEngine.ApplyVerticalTextAlignment) of the horizontal
            // cases above - its natural placement isn't already flush to the column's own inline-start
            // edge the way horizontal's is (see that method's own remarks), so it needs its own coverage
            // of the same text-align exemption, flushing to the column's physical-top edge instead.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='writing-mode:vertical-rl;width:100pt;height:400pt;text-align:center'>"
                + "<img id='img' style='display:block;width:20pt;height:160pt' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(wrap.ClientTop, img.FirstWord.Top, 3);
        }

        [Fact]
        public async Task BlockImage_InVerticalRtlWritingModeWithTextAlignCenter_IsNotCentered()
        {
            // direction:rtl flips a vertical column's own inline-start edge to physical-bottom
            // (WritingModeFrame.InlineStartIsBottom) - the guard's toBottom argument has to track that
            // flip rather than assuming LTR's physical-top, or an RTL column would flush to the wrong edge.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='writing-mode:vertical-rl;direction:rtl;width:100pt;height:400pt;text-align:center'>"
                + "<img id='img' style='display:block;width:20pt;height:160pt' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(wrap.ClientBottom, img.FirstWord.Bottom, 3);
        }
    }
}
