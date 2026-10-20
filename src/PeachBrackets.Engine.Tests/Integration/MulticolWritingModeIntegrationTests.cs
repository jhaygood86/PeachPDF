using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>display: multicol</c> (<c>column-count</c>/<c>column-width</c>) container under a vertical
    /// writing mode falls back to ordinary single-column block flow rather than attempting real column
    /// arrangement - see <c>CssLayoutEngineColumns.Layout</c>'s own remarks on why real vertical-mode
    /// column arrangement is not a narrower, separable half of this problem the way it first looked (the
    /// shared band every column requires would need to become axis-agnostic, rippling into page-level
    /// pagination invariants). Tracked in <c>.claude/accepted-gaps/no-vertical-writing-mode-layout.md</c>
    /// and issue #764.
    /// </summary>
    public class MulticolWritingModeIntegrationTests
    {
        [Fact]
        public async Task VerticalRl_MulticolContainer_FallsBackToSingleColumnBlockFlow()
        {
            // column-count: 3 would ordinarily split these into 3 side-by-side columns for
            // horizontal-tb (see MulticolLayoutIntegrationTests) - under vertical-rl it must not, since
            // column-count/column-width are inert here for now.
            var html = LayoutHarness.Wrap("""
                <div id="mc" style="writing-mode: vertical-rl; column-count: 3; column-gap: 10pt; width: 300pt">
                  <p id="p0">First paragraph.</p>
                  <p id="p1">Second paragraph.</p>
                  <p id="p2">Third paragraph.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p0 = LayoutHarness.FindById(root, "p0");
            var p1 = LayoutHarness.FindById(root, "p1");
            var p2 = LayoutHarness.FindById(root, "p2");
            Assert.NotNull(p0);
            Assert.NotNull(p1);
            Assert.NotNull(p2);

            // A real 3-column split would place these at 3 different X positions. Falling back to
            // single-column block flow instead means every paragraph shares the same X and stacks
            // top-to-bottom in document order - the ordinary <p> block-flow shape, unaffected by
            // column-count.
            Assert.Equal(p0!.Location.X, p1!.Location.X, 1);
            Assert.Equal(p0.Location.X, p2!.Location.X, 1);
            Assert.True(p1.Location.Y > p0.Location.Y, "second paragraph should stack below the first");
            Assert.True(p2.Location.Y > p1.Location.Y, "third paragraph should stack below the second");
        }

        [Fact]
        public async Task VerticalRl_MulticolContainer_PaginatesNormallyAcrossMultiplePages()
        {
            // The single-column block-flow fallback already supports real pagination via the ordinary
            // block-flow machinery - unlike CssLayoutEngineTable's vertical row loop, which genuinely
            // cannot fragment across pages and had to be made monolithic instead, this container needs
            // no equivalent MonolithicContent treatment at all.
            var paragraphs = string.Join("", Enumerable.Range(0, 40)
                .Select(i => $"<p>Paragraph number {i} with some real text content in it.</p>"));
            var html = LayoutHarness.Wrap(
                $"""<div style="writing-mode: vertical-rl; column-count: 3">{paragraphs}</div>""");

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 300);

            Assert.NotNull(container.FragmentTree);
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                $"40 paragraphs on 300pt pages should span multiple pages if pagination isn't broken by the vertical multicol fallback; got {container.FragmentTree.Fragmentainers.Count} page(s).");
        }

        [Fact]
        public async Task HorizontalTb_MulticolContainer_UnaffectedByVerticalFallback()
        {
            // Regression guard: an ordinary horizontal-tb multicol container still splits into real
            // columns - the writing-mode check must not affect it.
            var html = LayoutHarness.Wrap("""
                <div id="mc" style="column-count: 3; column-gap: 10pt; width: 300pt">
                  <p id="p0">First paragraph.</p>
                  <p id="p1">Second paragraph.</p>
                  <p id="p2">Third paragraph.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p0 = LayoutHarness.FindById(root, "p0");
            var p1 = LayoutHarness.FindById(root, "p1");
            Assert.NotNull(p0);
            Assert.NotNull(p1);

            Assert.True(p1!.Location.X > p0!.Location.X, "second paragraph should sit in a column to the right of the first");
        }
    }
}
