using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Sizing 3 gives <c>min-height</c> the initial value <c>auto</c> (it resolves to 0 for a block, and to the
    /// content size for a flex item in the main axis - CSS Flexbox 1 section 4.5). The registry used to declare
    /// <c>0</c>, which is a different computed value even where it lays out the same.
    /// </summary>
    public class MinHeightInitialValueTests
    {
        private static double HeightOf(CssBox box) => box.ActualBottom - box.Location.Y;

        [Fact]
        public async Task TheComputedInitialValueIsAuto()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='d'>x</div>"));

            Assert.Equal("auto", FindById(root, "d")!.MinHeight);
        }

        [Fact]
        public async Task AnExplicitZeroStillCompilesToZero()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='d' style='min-height:0'>x</div>"));

            Assert.Equal("0", FindById(root, "d")!.MinHeight);
        }

        [Fact]
        public async Task AutoMinHeightLaysOutAPaddedBlockTheSameAsAZeroOne()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='auto' style='padding:10pt;border:2pt solid;font-size:10pt'>x</div>" +
                "<div id='zero' style='padding:10pt;border:2pt solid;font-size:10pt;min-height:0'>x</div>"));

            Assert.Equal(HeightOf(FindById(root, "zero")!), HeightOf(FindById(root, "auto")!), 2);
        }

        [Fact]
        public async Task AColumnFlexItemLaysOutTheSameAsWithAnExplicitZeroMinimum()
        {
            // Behaviour pin: the initial value change must not move a flex item, whichever way it resolves.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-direction:column;height:10pt;font-size:10pt'>" +
                "<div id='auto'>one<br>two<br>three</div></div>" +
                "<div style='display:flex;flex-direction:column;height:10pt;font-size:10pt'>" +
                "<div id='zero' style='min-height:0'>one<br>two<br>three</div></div>"));

            Assert.Equal(HeightOf(FindById(root, "zero")!), HeightOf(FindById(root, "auto")!), 2);
        }
    }
}
