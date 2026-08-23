using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Direct coverage for <see cref="CssBox.GetContainerRelativeUnitBasis"/>/
    /// <see cref="CssBox.GetViewportUnitBasis"/>'s writing-mode-aware inline/block axis split (issue #795),
    /// and <see cref="CssBox.GetContainerRelativeUnitBasis"/>'s definite-height fast path
    /// (<see cref="CssBox.ResolveDefiniteHeightPt"/>, issue #805). Geometry is set directly via
    /// <see cref="CssBox.Location"/>/<see cref="CssBox.Size"/> rather than through a real
    /// <see cref="HtmlContainerInt.PerformLayout"/> pass, so these tests are independent of this engine's
    /// own layout-ordering.
    /// </summary>
    public class ContainerAndViewportUnitBasisTests
    {
        [Fact]
        public void GetContainerRelativeUnitBasis_HorizontalContainer_InlineIsWidthAndBlockIsHeight()
        {
            var container = new CssBox(null, null)
            {
                HtmlContainer = new HtmlContainerInt(new PdfSharpAdapter()),
                Location = new RPoint(0, 0),
                Size = new RSize(400, 200),
                ContainerType = CssProperty<ContainerType>.FromValue("size", ContainerType.Size),
                WritingMode = CssProperty<WritingMode>.FromValue("horizontal-tb", WritingMode.HorizontalTb)
            };
            var child = new CssBox(container, null);

            var (widthPt, heightPt, inlineSizePt, blockSizePt) = child.GetContainerRelativeUnitBasis();

            Assert.Equal(400d, widthPt);
            Assert.Equal(200d, heightPt);
            Assert.Equal(400d, inlineSizePt);  // inline == width under horizontal-tb
            Assert.Equal(200d, blockSizePt);   // block == height under horizontal-tb
        }

        [Fact]
        public void GetContainerRelativeUnitBasis_VerticalContainer_InlineIsHeightAndBlockIsWidth()
        {
            var container = new CssBox(null, null)
            {
                HtmlContainer = new HtmlContainerInt(new PdfSharpAdapter()),
                Location = new RPoint(0, 0),
                Size = new RSize(400, 200),
                ContainerType = CssProperty<ContainerType>.FromValue("size", ContainerType.Size),
                WritingMode = CssProperty<WritingMode>.FromValue("vertical-rl", WritingMode.VerticalRl)
            };
            var child = new CssBox(container, null);

            var (widthPt, heightPt, inlineSizePt, blockSizePt) = child.GetContainerRelativeUnitBasis();

            // cqw/cqh (widthPt/heightPt) stay physical, unaffected by the container's own writing-mode.
            Assert.Equal(400d, widthPt);
            Assert.Equal(200d, heightPt);
            // cqi/cqb (inlineSizePt/blockSizePt) rotate onto the orthogonal physical axis under
            // vertical-rl - the exact swap #795 was filed for.
            Assert.Equal(200d, inlineSizePt); // inline == physical height under vertical-rl/vertical-lr
            Assert.Equal(400d, blockSizePt);  // block == physical width under vertical-rl/vertical-lr
        }

        [Fact]
        public void GetContainerRelativeUnitBasis_VerticalInlineSizeOnlyContainer_BlockAxisStaysNull()
        {
            // An inline-size container tracks only its own inline axis - for a vertical container that's
            // the physical height, so heightPt/blockSizePt (both physical-width-driven here) must stay
            // null, while widthPt/inlineSizePt (both physical-height-driven here) remain available.
            var container = new CssBox(null, null)
            {
                HtmlContainer = new HtmlContainerInt(new PdfSharpAdapter()),
                Location = new RPoint(0, 0),
                Size = new RSize(400, 200),
                ContainerType = CssProperty<ContainerType>.FromValue("inline-size", ContainerType.InlineSize),
                WritingMode = CssProperty<WritingMode>.FromValue("vertical-rl", WritingMode.VerticalRl)
            };
            var child = new CssBox(container, null);

            var (widthPt, heightPt, inlineSizePt, blockSizePt) = child.GetContainerRelativeUnitBasis();

            Assert.Equal(400d, widthPt);
            Assert.Null(heightPt);
            Assert.Equal(200d, inlineSizePt);
            Assert.Null(blockSizePt);
        }

        [Fact]
        public void GetContainerRelativeUnitBasis_ContainerHasADefiniteHeightButNoLiveGeometryYet_StillResolvesTheRealHeight()
        {
            // Regression test for issue #805: Size left at its construction-time default (0,0) simulates
            // a container whose own ClientBottom hasn't been settled by CssLayoutEngine.ApplyHeight yet
            // (which, for a definite height, only happens in that container's own layout epilogue - after
            // a descendant like this test's `child` may already have asked for this basis). A definite
            // `height` never depends on content, so ResolveDefiniteHeightPt must resolve the real 200px
            // (150pt) directly from the Height string, not the live (still-zero) ClientBottom - ClientTop.
            var container = new CssBox(null, null)
            {
                HtmlContainer = new HtmlContainerInt(new PdfSharpAdapter()),
                Location = new RPoint(0, 0),
                Size = new RSize(400, 0), // width settled; height NOT yet settled (still its default)
                Height = "200px",
                ContainerType = CssProperty<ContainerType>.FromValue("size", ContainerType.Size),
                WritingMode = CssProperty<WritingMode>.FromValue("horizontal-tb", WritingMode.HorizontalTb)
            };
            var child = new CssBox(container, null);

            var (_, heightPt, _, blockSizePt) = child.GetContainerRelativeUnitBasis();

            Assert.Equal(150d, heightPt);
            Assert.Equal(150d, blockSizePt);
        }

        [Fact]
        public void GetContainerRelativeUnitBasis_ContainerHasAMinHeightTallerThanItsExplicitHeight_ClampsUpward()
        {
            // ResolveDefiniteHeightPt must apply the same min-height clamp CssLayoutEngine.GetBoxHeight
            // itself applies - a container declaring `height: 100px; min-height: 300px;` actually lays out
            // at 300px (225pt), so cqh/cqb must report that clamped value, not the unclamped 100px (75pt).
            var container = new CssBox(null, null)
            {
                HtmlContainer = new HtmlContainerInt(new PdfSharpAdapter()),
                Location = new RPoint(0, 0),
                Size = new RSize(400, 0),
                Height = "100px",
                MinHeight = "300px",
                ContainerType = CssProperty<ContainerType>.FromValue("size", ContainerType.Size),
                WritingMode = CssProperty<WritingMode>.FromValue("horizontal-tb", WritingMode.HorizontalTb)
            };
            var child = new CssBox(container, null);

            var (_, heightPt, _, blockSizePt) = child.GetContainerRelativeUnitBasis();

            Assert.Equal(225d, heightPt);
            Assert.Equal(225d, blockSizePt);
        }

        [Fact]
        public void GetContainerRelativeUnitBasis_ContainerHasAMaxHeightShorterThanItsExplicitHeight_ClampsDownward()
        {
            // Mirrors CssLayoutEngine.ApplyHeight's own max-height clamp (applied after GetBoxHeight
            // returns, unlike min-height, which GetBoxHeight already applies internally) - a container
            // declaring `height: 300px; max-height: 100px;` actually lays out at 100px (75pt).
            var container = new CssBox(null, null)
            {
                HtmlContainer = new HtmlContainerInt(new PdfSharpAdapter()),
                Location = new RPoint(0, 0),
                Size = new RSize(400, 0),
                Height = "300px",
                MaxHeight = "100px",
                ContainerType = CssProperty<ContainerType>.FromValue("size", ContainerType.Size),
                WritingMode = CssProperty<WritingMode>.FromValue("horizontal-tb", WritingMode.HorizontalTb)
            };
            var child = new CssBox(container, null);

            var (_, heightPt, _, blockSizePt) = child.GetContainerRelativeUnitBasis();

            Assert.Equal(75d, heightPt);
            Assert.Equal(75d, blockSizePt);
        }

        [Fact]
        public void GetContainerRelativeUnitBasis_NoEligibleAncestor_ReturnsAllNull()
        {
            var lone = new CssBox(null, null);

            var (widthPt, heightPt, inlineSizePt, blockSizePt) = lone.GetContainerRelativeUnitBasis();

            Assert.Null(widthPt);
            Assert.Null(heightPt);
            Assert.Null(inlineSizePt);
            Assert.Null(blockSizePt);
        }

        [Fact]
        public void GetViewportUnitBasis_HorizontalRoot_InlineIsWidthAndBlockIsHeight()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter)
            {
                PageSize = new RSize(800, 600),
                RootWritingMode = WritingMode.HorizontalTb
            };
            var box = new CssBox(null, null) { HtmlContainer = container };

            var (viewportWidthPt, viewportHeightPt, viewportInlinePt, viewportBlockPt) = box.GetViewportUnitBasis();

            Assert.Equal(800d, viewportWidthPt);
            Assert.Equal(600d, viewportHeightPt);
            Assert.Equal(800d, viewportInlinePt);
            Assert.Equal(600d, viewportBlockPt);
        }

        [Fact]
        public void GetViewportUnitBasis_VerticalRoot_InlineIsHeightAndBlockIsWidth()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter)
            {
                PageSize = new RSize(800, 600),
                RootWritingMode = WritingMode.VerticalRl
            };
            var box = new CssBox(null, null) { HtmlContainer = container };

            var (viewportWidthPt, viewportHeightPt, viewportInlinePt, viewportBlockPt) = box.GetViewportUnitBasis();

            // vw/vh (viewportWidthPt/viewportHeightPt) stay physical, unaffected by the root's writing-mode.
            Assert.Equal(800d, viewportWidthPt);
            Assert.Equal(600d, viewportHeightPt);
            // vi/vb (viewportInlinePt/viewportBlockPt) rotate onto the orthogonal physical axis under a
            // vertical-rl/vertical-lr root - the exact swap #795 was filed for.
            Assert.Equal(600d, viewportInlinePt);
            Assert.Equal(800d, viewportBlockPt);
        }

        [Fact]
        public void GetViewportUnitBasis_NoHtmlContainer_ReturnsAllNull()
        {
            var box = new CssBox(null, null);

            var (viewportWidthPt, viewportHeightPt, viewportInlinePt, viewportBlockPt) = box.GetViewportUnitBasis();

            Assert.Null(viewportWidthPt);
            Assert.Null(viewportHeightPt);
            Assert.Null(viewportInlinePt);
            Assert.Null(viewportBlockPt);
        }
    }
}
