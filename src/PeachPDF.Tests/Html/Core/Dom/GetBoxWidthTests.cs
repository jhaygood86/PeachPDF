using PeachPDF.Html.Core.Dom;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Direct coverage for <see cref="CssLayoutEngine.GetBoxWidth(CssBox)"/> - the narrow row-axis
    /// counterpart of <see cref="CssLayoutEngine.GetBoxHeight"/> added for
    /// <see cref="CssLayoutEngineTable"/>'s own row-height enforcement (CSS 2.1 §17.5.3) on a vertical
    /// (<c>writing-mode: vertical-rl</c>/<c>vertical-lr</c>) table, where the row axis is physical width
    /// rather than physical height (issue #1131). Mirrors <see cref="HeightDefinitenessTests"/>'s own
    /// direct-call, post-layout assertion shape.
    /// </summary>
    public class GetBoxWidthTests
    {
        [Fact]
        public async Task AbsoluteLengthWidth_ResolvesDirectly()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='width:100pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(100.0, CssLayoutEngine.GetBoxWidth(box)!.Value, 3);
        }

        [Fact]
        public async Task PercentageWidthAgainstContainingBlock_ResolvesAgainstItsWidth()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><div id='box' style='width:50%'>x</div></div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(200.0, CssLayoutEngine.GetBoxWidth(box)!.Value, 3);
        }

        [Fact]
        public async Task NoExplicitWidthOrMinWidth_ReturnsNull()
        {
            // Mirrors GetBoxHeight's own null-for-auto contract for its own explicit-height branch: a box
            // with neither width nor min-width set has nothing for this narrow resolver to report.
            var (root, _) = await LayoutAsync(Wrap("<div id='box'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.Null(CssLayoutEngine.GetBoxWidth(box));
        }

        [Fact]
        public async Task MinWidthClampWinsOverASmallerExplicitWidth()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='width:50pt;min-width:150pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(150.0, CssLayoutEngine.GetBoxWidth(box)!.Value, 3);
        }

        [Fact]
        public async Task MinWidthAloneWithNoExplicitWidth_Resolves()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='min-width:75pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(75.0, CssLayoutEngine.GetBoxWidth(box)!.Value, 3);
        }

        [Fact]
        public async Task MaxWidthClampWinsOverALargerExplicitWidth()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='box' style='width:150pt;max-width:50pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(50.0, CssLayoutEngine.GetBoxWidth(box)!.Value, 3);
        }

        [Fact]
        public async Task MinWidthWinsOverMaxWidthOnConflict()
        {
            // CSS 2.1 §10.4: min-width wins over max-width on conflict, mirroring the existing async
            // GetBoxWidth(RGraphics, CssBox, double?) overload's own ordering (max applied before min).
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='box' style='width:100pt;max-width:50pt;min-width:150pt'>x</div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(150.0, CssLayoutEngine.GetBoxWidth(box)!.Value, 3);
        }
    }
}
