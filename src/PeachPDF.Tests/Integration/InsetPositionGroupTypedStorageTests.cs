using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for the inset/position group (<c>top/left/right/bottom</c>), now
    /// backed by <c>CssProperty&lt;CssKeywordOrValue&lt;AutoKeyword, LengthOrCalc&gt;&gt;</c> - reusing the
    /// same <c>LengthOrCalc</c> value side <c>margin-top/right/bottom/left</c> introduced. Complements
    /// <c>AbsolutePositioningIntegrationTests</c>/<c>Acid2RegressionTests</c>, which exercise these
    /// properties indirectly through their effect on layout geometry rather than asserting the stored
    /// <c>.Value</c> directly.
    /// </summary>
    public class InsetPositionGroupTypedStorageTests
    {
        [Fact]
        public async Task Auto_StoresTheAutoKeyword_CaseInsensitively()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; top:AUTO'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.Top.Value is { IsKeyword: true, Keyword: AutoKeyword.Auto });
        }

        [Fact]
        public async Task LengthValue_StoresParsedLength()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; left:15px'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.Left.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 15f } } });
        }

        [Fact]
        public async Task PercentageValue_StoresParsedPercentage()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; right:10%'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.Right.Value is
            {
                IsValue: true,
                Value: { IsCalc: false, Length: { Type: Length.Unit.Percent, Value: 10f } }
            });
        }

        [Fact]
        public async Task RelativePositioning_BottomOnly_ShiftsUpwards_MatchingLeftTopFallback()
        {
            // CSS 2.1 §9.4.3: "bottom" applies with its sign flipped when "top" is auto. This exact
            // shape (only bottom set) is what a prior fix restored after it was found to be a silent
            // no-op - re-verified here now that the offsets are typed rather than raw strings. Using
            // "pt" (the internal layout unit) directly sidesteps the 1px = 0.75pt conversion entirely.
            var (calcRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; bottom:15pt'>x</div>"));
            var (staticRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t2' style='position:relative;'>x</div>"));

            var box = LayoutHarness.FindById(calcRoot, "t");
            var staticBox = LayoutHarness.FindById(staticRoot, "t2");

            Assert.NotNull(box);
            Assert.NotNull(staticBox);
            Assert.True(box!.Bottom.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 15f } } });
            Assert.Equal(staticBox!.Location.Y - 15, box.Location.Y, 2);
        }

        [Fact]
        public async Task RelativeUnitCalc_EvaluatesAgainstTheBoxsOwnFontSize()
        {
            // font-size:20px resolves to an actual font size of 15pt (1px = 0.75pt), so
            // calc(2em + 10px) = 2*15pt + 10px(=7.5pt) = 37.5pt.
            var (calcRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; font-size:20px; left:calc(2em + 10px)'>x</div>"));
            var (absoluteRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; font-size:20px; left:37.5pt'>x</div>"));

            var calcBox = LayoutHarness.FindById(calcRoot, "t");
            var absoluteBox = LayoutHarness.FindById(absoluteRoot, "t");

            Assert.NotNull(calcBox);
            Assert.NotNull(absoluteBox);
            Assert.True(calcBox!.Left.Value is { IsValue: true, Value: { IsCalc: true } });
            Assert.Equal(absoluteBox!.Location.X, calcBox.Location.X, 2);
        }

        [Fact]
        public async Task AbsolutePositioning_LeftAutoRightSet_FallsBackToRightEdge()
        {
            // CSS 2.1 §10.3.7: left:auto with right set falls back to positioning from the right edge.
            // The expected X is derived from the container's own runtime geometry (ClientRight) rather
            // than a hand-computed absolute pixel value, so the assertion isn't coupled to unrelated
            // px-to-pt conversion or UA-stylesheet default-margin details.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='container' style='position:relative; width:200pt; height:100pt;'>" +
                "<div id='t' style='position:absolute; right:20pt; width:50pt; height:50pt;'></div>" +
                "</div>"));

            var container = LayoutHarness.FindById(root, "container");
            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(container);
            Assert.NotNull(box);
            Assert.True(box!.Left.Value.IsKeyword);
            Assert.True(box.Right.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 20f } } });
            Assert.Equal(container!.ClientRight - box.ActualWidth - 20, box.Location.X, 2);
        }

        [Fact]
        public async Task LogicalInsetBlockStart_ResolvesIntoThePhysicalPropertyTyped()
        {
            // writing-mode: vertical-rl maps block-start to the physical right edge (same mapping the
            // margin group's equivalent test verifies against LogicalPropertyResolver.BlockStart).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; writing-mode:vertical-rl; inset-block-start:12px'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.Right.Value is { IsValue: true, Value: { IsCalc: false, Length: { Value: 12f } } });
        }
    }
}
