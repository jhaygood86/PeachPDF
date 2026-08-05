using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for the margin group (<c>margin-top/right/bottom/left</c>), now
    /// backed by <c>CssProperty&lt;CssKeywordOrValue&lt;AutoKeyword, LengthOrCalc&gt;&gt;</c> - reusing the
    /// same <c>LengthOrCalc</c> value side <c>column-width</c> introduced. Complements
    /// <c>AutoMarginWidthIntegrationTests</c>/<c>LogicalPropertiesWritingModeIntegrationTests</c>, which
    /// exercise these properties indirectly through their effect on layout geometry rather than asserting
    /// the stored <c>.Value</c> directly.
    /// </summary>
    public class MarginGroupTypedStorageTests
    {
        [Fact]
        public async Task Auto_StoresTheAutoKeyword_CaseInsensitively()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='margin-top:AUTO'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.MarginTop.Value is { IsKeyword: true, Keyword: AutoKeyword.Auto });
        }

        [Fact]
        public async Task LengthValue_StoresParsedLength()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='margin-left:15px'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.MarginLeft.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 15f } } });
        }

        [Fact]
        public async Task PercentageValue_StoresParsedPercentage()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='margin-right:10%'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.MarginRight.Value is
            {
                IsValue: true,
                Value: { IsCalc: false, Length: { Type: Length.Unit.Percent, Value: 10f } }
            });
        }

        [Fact]
        public async Task RelativeUnitCalc_EvaluatesAgainstTheBoxsOwnFontSize()
        {
            // font-size:20px resolves to an actual font size of 15pt (1px = 0.75pt), so
            // calc(2em + 10px) = 2*15pt + 10px(=7.5pt) = 37.5pt.
            var (calcRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20px; margin-bottom:calc(2em + 10px)'></div>"));
            var (absoluteRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='font-size:20px; margin-bottom:37.5pt'></div>"));

            var calcBox = LayoutHarness.FindById(calcRoot, "t");
            var absoluteBox = LayoutHarness.FindById(absoluteRoot, "t");

            Assert.NotNull(calcBox);
            Assert.NotNull(absoluteBox);
            Assert.True(calcBox!.MarginBottom.Value is { IsValue: true, Value: { IsCalc: true } });
            Assert.Equal(absoluteBox!.ActualMarginBottom, calcBox.ActualMarginBottom, 2);
        }

        [Fact]
        public async Task HspaceAttribute_SetsLeftAndRightMarginsTyped()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<img id='t' src='x.png' hspace='10' />"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.MarginLeft.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 10f } } });
            Assert.True(box.MarginRight.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 10f } } });
        }

        [Fact]
        public async Task VspaceAttribute_SetsTopAndBottomMarginsTyped()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<img id='t' src='x.png' vspace='5' />"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.MarginTop.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 5f } } });
            Assert.True(box.MarginBottom.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 5f } } });
        }

        [Fact]
        public async Task LogicalMarginBlockStart_ResolvesIntoThePhysicalPropertyTyped()
        {
            // writing-mode: vertical-rl maps block-start to the physical right edge (CLAUDE.md's own
            // worked example for this resolution).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='writing-mode:vertical-rl; margin-block-start:12px'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.MarginRight.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 12f } } });
        }
    }
}
