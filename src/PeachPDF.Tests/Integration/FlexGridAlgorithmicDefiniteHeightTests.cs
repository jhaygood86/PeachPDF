using PeachPDF.Html.Core.Dom;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A percentage-height descendant of a flex/grid item whose own height comes from that layout
    /// algorithm (stretch, or a flex item's main size) — not from its own <c>height</c> declaration —
    /// must still resolve, per CSS Sizing 3 §4's definite-size framework
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1167">#1167</see>). Neither
    /// <c>CssLayoutEngineFlex</c> nor <c>CssLayoutEngineGrid</c> ever call <c>ApplyHeight</c>/write the old
    /// <c>IsHeightCalculated</c> flag directly — they only ever reflected a resolved height in
    /// <c>CssBox.Height</c>'s own CSS string transiently (set it, re-lay the item out, revert it) — so
    /// without <see cref="CssBox.AlgorithmicDefiniteHeight"/> durably recording the resolved value, nothing
    /// would survive for a percentage-height descendant to resolve against.
    /// </summary>
    public class FlexGridAlgorithmicDefiniteHeightTests
    {
        [Fact]
        public async Task RowFlexItemStretchedByDefaultAlignItems_PercentageHeightChildResolves()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;height:200pt'>" +
                "<div id='item'><div id='child' style='height:50%'>x</div></div></div>"));

            var item = FindById(root, "item")!;
            var child = FindById(root, "child")!;

            Assert.Equal(200.0, item.ActualBoxSizingHeight, 1);
            Assert.Equal(100.0, child.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task ColumnFlexItemMainSize_PercentageHeightChildResolves()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-direction:column;height:200pt'>" +
                "<div id='item' style='flex:1'><div id='child' style='height:50%'>x</div></div></div>"));

            var item = FindById(root, "item")!;
            var child = FindById(root, "child")!;

            Assert.Equal(200.0, item.ActualBoxSizingHeight, 1);
            Assert.Equal(100.0, child.ActualBoxSizingHeight, 1);
        }

        /// <summary>
        /// A column-direction flex item's used main size is always the flex algorithm's own resolved
        /// result (CSS Flexbox 1 §9.7), even when that differs from the item's own declared <c>height</c>
        /// (used only as the flex-basis input) — <c>flex-grow</c> here grows the item well past its own
        /// declared 20%. <see cref="CssLayoutEngine.ResolveDefiniteHeightValue"/> must check
        /// <see cref="CssBox.AlgorithmicDefiniteHeight"/> before the item's own declared <c>Height</c> to
        /// get this right, matching <see cref="CssLayoutEngine.IsHeightDefinite"/>'s own priority.
        /// </summary>
        [Fact]
        public async Task ColumnFlexItemWithADifferingDeclaredHeightAndFlexGrow_ChildResolvesAgainstTheGrownSize()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-direction:column;height:300pt'>" +
                "<div id='item' style='height:20%;flex-grow:1'><div id='child' style='height:50%'>x</div></div></div>"));

            var item = FindById(root, "item")!;
            var child = FindById(root, "child")!;

            // The lone item grows to fill the whole 300pt container, not the 60pt its own 20% declares.
            Assert.Equal(300.0, item.ActualBoxSizingHeight, 1);
            Assert.Equal(150.0, child.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task GridItemStretchedByDefaultAlignSelf_PercentageHeightChildResolves()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:grid;grid-template-rows:200pt;height:200pt'>" +
                "<div id='item'><div id='child' style='height:50%'>x</div></div></div>"));

            var item = FindById(root, "item")!;
            var child = FindById(root, "child")!;

            Assert.Equal(200.0, item.ActualBoxSizingHeight, 1);
            Assert.Equal(100.0, child.ActualBoxSizingHeight, 1);
        }

        /// <summary>
        /// Direct regression guard for <see cref="CssBox.AlgorithmicDefiniteHeight"/> itself, not just its
        /// downstream effect - a row-flex item with its own explicit <c>height</c> never stretches
        /// (<c>canStretch</c> is false), so the field must stay <see langword="null"/>.
        /// </summary>
        [Fact]
        public async Task RowFlexItemWithItsOwnExplicitHeight_AlgorithmicDefiniteHeightStaysNull()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;height:200pt'><div id='item' style='height:40pt'>x</div></div>"));

            Assert.Null(FindById(root, "item")!.AlgorithmicDefiniteHeight);
        }

        /// <summary>
        /// The other direction: <c>align-items: flex-start</c> (not stretch) leaves the item at its
        /// hypothetical (content) cross size, so it must not be recorded as algorithmically definite.
        /// </summary>
        [Fact]
        public async Task RowFlexItemNotStretched_AlgorithmicDefiniteHeightStaysNull()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;height:200pt;align-items:flex-start'><div id='item'>x</div></div>"));

            Assert.Null(FindById(root, "item")!.AlgorithmicDefiniteHeight);
        }

        /// <summary>
        /// Re-laying the same flex container out (mirroring <c>LayoutHarness.LayoutRepeatedlyAsync</c>'s
        /// own rationale — this engine can revisit a subtree already laid out, e.g. across a resumed
        /// fragmentainer pass) must not leak a stale <see cref="CssBox.AlgorithmicDefiniteHeight"/> from
        /// the first pass into the second — both passes reach the same, correct answer independently.
        /// </summary>
        [Fact]
        public async Task RepeatedLayout_DoesNotLeakAStaleAlgorithmicDefiniteHeight()
        {
            var results = await LayoutRepeatedlyAsync(
                Wrap("<div style='display:flex;height:200pt'>" +
                     "<div id='item'><div id='child' style='height:50%'>x</div></div></div>"),
                passes: 2,
                snapshot: (root, _) => FindById(root, "child")!.ActualBoxSizingHeight);

            Assert.Equal(100.0, results[0], 1);
            Assert.Equal(100.0, results[1], 1);
        }
    }
}
