using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §10.3.5: a floating box's <c>auto</c> width is shrink-to-fit,
    /// <c>min(max-content, max(min-content, available))</c> — not stretch-to-containing-block.
    /// <para>
    /// Given the stretch width a float fills its containing block, which leaves <c>float: right</c>
    /// nowhere to go: it lands at the left as a full-width bar and the content that belongs beside it
    /// is pushed onto its own line. Float PLACEMENT was never wrong — a float with a DECLARED width
    /// lands on a browser's x to the point — which is what makes this hard to see.
    /// </para>
    /// <para>
    /// Chrome 152 on a 300pt container: an auto-width <c>float: right</c> badge sits 257.9pt in, and
    /// text after an auto-width <c>float: left</c> badge sits beside it on the same line.
    /// </para>
    /// </summary>
    public class FloatAutoWidthTests
    {
        [Fact]
        public async Task AnAutoWidthFloatRightSitsAtTheRightEdge()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:300pt'><div id='f' style='float:right; border:1px solid black'>BADGE</div></div>"));

            var badge = FindById(root, "f")!;
            var width = badge.ActualRight - badge.Location.X;
            var offsetInContainer = badge.Location.X - FindById(root, "f")!.ContainingBlock.Location.X;

            Assert.True(width < 100, $"a shrink-to-fit badge must not fill its 300pt container, was {width:F1}pt wide");
            Assert.True(offsetInContainer > 180,
                $"float:right must reach the right of a 300pt container (Chrome: 257.9pt in), was {offsetInContainer:F1}pt");
        }

        [Fact]
        public async Task ContentSitsBesideAnAutoWidthFloatLeft()
        {
            // The consequence a reader actually notices: a full-width float leaves no room beside it,
            // so the text that belongs next to the badge drops onto its own line.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:300pt'><div style='float:left; border:1px solid black'>BADGE</div>"
                + "<span id='t'>beside</span></div>"));

            var badgeWords = Descendants(root).SelectMany(b => b.Words).Where(w => w.Text == "BADGE").ToList();
            var beside = Descendants(FindById(root, "t")!).SelectMany(b => b.Words).First();

            Assert.NotEmpty(badgeWords);

            // Same line, not the same baseline — the badge's 1px border insets its own text by
            // 0.75pt, so an exact comparison would be asserting the border away.
            Assert.True(System.Math.Abs(badgeWords[0].Top - beside.Top) < 3,
                $"the text must sit on the float's line: badge at y={badgeWords[0].Top}, text at y={beside.Top}");
            Assert.True(beside.Left > badgeWords[0].Left,
                "the text must sit to the RIGHT of the float, not under it");
        }

        [Fact]
        public async Task ADeclaredFloatWidthIsUnchanged()
        {
            // The contrast case: only the auto measurement was wrong, so a declared width must land
            // exactly where it always did.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:300pt'><div id='f' style='float:right; width:60px'>BADGE</div></div>"));

            var badge = FindById(root, "f")!;
            Assert.Equal(45.0, badge.ActualRight - badge.Location.X, 1);   // 60px at 96 CSS dpi
        }

        [Fact]
        public async Task AnAutoWidthFloatIsNeverWiderThanItsContainingBlock()
        {
            // max(min-content, available) is bounded above by max-content, so a float whose content
            // exceeds the container is clamped to the container rather than overflowing.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:120pt'><div id='f' style='float:left'>"
                + "a considerably longer run of text than the container can hold</div></div>"));

            var f = FindById(root, "f")!;
            Assert.True(f.ActualRight - f.Location.X <= 120.5,
                $"a float must not exceed its containing block, was {f.ActualRight - f.Location.X:F1}pt");
        }
    }
}
