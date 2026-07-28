using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">CSS Text §7.3</see> exempts the
    /// <b>last line of the block</b> from <c>text-align: justify</c>. A line that ends at a fragmentation
    /// break is not that line — the block continues in the next fragmentainer — so it is justified like
    /// any other.
    /// </summary>
    /// <remarks>
    /// The distinction only became observable once a block's flow could stop mid-block: the line the pass
    /// stops on is then the last one the box's <c>LineBoxes</c> list holds, and reading "last line" off the
    /// list alone left every line at a page or column boundary unjustified.
    /// </remarks>
    public class JustifiedLineAtABreakTests
    {
        private const string Style = "text-align:justify;font-size:10pt;line-height:18pt;orphans:1;widows:1";

        /// <summary>
        /// The line a page break falls after is justified: its last word ends at the block's right edge,
        /// as every other justified line's does.
        /// </summary>
        [Fact]
        public async Task TheLineAPageBreakFallsAfter_IsJustified()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document(), pageWidth: 200, pageHeight: 300, margin: 10);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing");

            var block = LayoutHarness.FindById(root, "p")!;

            // The last line the first page kept - the one the break falls after.
            var lastOnFirstPage = block.LineBoxes
                .Last(line => container.SlotStartingAt(line.Words[0].Top) == 0);

            Assert.Equal(block.ClientRight, lastOnFirstPage.Words[^1].Right, 1);
        }

        /// <summary>
        /// The control, and the half that must not regress: the block's real last line is still exempt.
        /// </summary>
        [Fact]
        public async Task TheBlocksOwnLastLine_IsNotJustified()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document(), pageWidth: 200, pageHeight: 300, margin: 10);

            var block = LayoutHarness.FindById(root, "p")!;
            var lastLine = block.LineBoxes[^1];

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);
            Assert.True(lastLine.Words[^1].Right < block.ClientRight - 1,
                $"the block's last line was justified: ends at {lastLine.Words[^1].Right:F1} "
                + $"against a right edge of {block.ClientRight:F1}");
        }

        /// <summary>
        /// A block short enough not to break has exactly one exempt line and no break to confuse it with,
        /// which is what keeps the two tests above from both passing on a fixture that never justifies.
        /// </summary>
        [Fact]
        public async Task ABlockThatDoesNotBreak_JustifiesEveryLineButItsLast()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<p id='p' style='{Style}'>{Words(40)}</p>"),
                pageWidth: 200, pageHeight: 800, margin: 10);

            var block = LayoutHarness.FindById(root, "p")!;

            Assert.True(block.LineBoxes.Count > 2, "fixture must wrap onto several lines");
            Assert.All(block.LineBoxes.SkipLast(1),
                line => Assert.Equal(block.ClientRight, line.Words[^1].Right, 1));
            Assert.True(block.LineBoxes[^1].Words[^1].Right < block.ClientRight - 1);
        }

        private static string Document() =>
            LayoutHarness.Wrap($"<p id='p' style='{Style}'>{Words(244)}</p>");

        private static string Words(int count) =>
            string.Join(" ", Enumerable.Range(0, count).Select(i => $"w{i}"));
    }
}
