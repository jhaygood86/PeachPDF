using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An <c>inline-block</c>'s height is that of its content - the line boxes it generates, each at least
    /// the <c>line-height</c> it inherits (CSS 2.1 §10.6.6, §10.8.1) - so its background and border cover
    /// the whole line, not just the glyphs' content area (issue #1694). All assertions are in terms of
    /// <c>line-height</c>, so they do not depend on which font resolves.
    /// </summary>
    public class InlineBlockLineBoxHeightTests
    {
        private static Rect PaintedRectOf(CssBox box) => Assert.Single(box.Rectangles).Value;

        [Fact]
        public async Task AFlowedInlineBlockIsAsTallAsTheLineHeightItInherits()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='line' style='width:400pt; line-height:42pt; font-size:14pt'>" +
                "<span id='box' style='display:inline-block'>Label</span> after</div>"));

            var rect = PaintedRectOf(FindById(root, "box")!);

            Assert.Equal(42.0, rect.Height, 1);
        }

        [Fact]
        public async Task TheBoxCoversTheLineFromItsTopEdge()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='line' style='width:400pt; line-height:42pt; font-size:14pt'>" +
                "<span id='box' style='display:inline-block'>Label</span> after</div>"));

            var line = FindById(root, "line")!;
            var rect = PaintedRectOf(FindById(root, "box")!);

            Assert.Equal(line.Location.Y, rect.Top, 1);
            Assert.Equal(line.Location.Y + 42.0, rect.Bottom, 1);
        }

        [Fact]
        public async Task PaddingAndBorderAreAddedOutsideTheLineHeight()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt; line-height:42pt; font-size:14pt'>" +
                "<span id='box' style='display:inline-block; padding:2pt; border:1pt solid'>Label</span></div>"));

            Assert.Equal(42.0 + 2 * 3, PaintedRectOf(FindById(root, "box")!).Height, 1);
        }

        [Fact]
        public async Task APlainInlineStaysAtItsContentArea()
        {
            // CSS 2.1 §10.8.1: an inline box's own height is its content area, whatever the line-height.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt; line-height:42pt; font-size:14pt'>" +
                "<span id='box' style='background:red'>Label</span> after</div>"));

            Assert.True(PaintedRectOf(FindById(root, "box")!).Height < 30.0);
        }

        [Fact]
        public async Task AnInlineBlockWithItsOwnLineHeightUsesIt()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt; line-height:42pt; font-size:14pt'>" +
                "<span id='box' style='display:inline-block; line-height:30pt'>Label</span></div>"));

            Assert.Equal(30.0, PaintedRectOf(FindById(root, "box")!).Height, 1);
        }
    }
}
