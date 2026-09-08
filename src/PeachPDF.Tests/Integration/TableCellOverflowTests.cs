using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The UA stylesheet set <c>overflow: hidden</c> on <c>td, th</c>. The HTML Standard's own
    /// rendering section (§15.3.8, Tables) sets only <c>display: table-cell</c>, <c>padding: 1px</c>
    /// and — on <c>th</c> — <c>font-weight: bold</c>. It does not set <c>overflow</c> at all, and no
    /// browser does either, so content too wide for a cell overflows and is painted.
    /// <para>
    /// It is invisible under automatic table layout, because the column simply widens to fit. Under
    /// <c>table-layout: fixed</c> the column cannot widen, and the overflow was silently cut instead.
    /// </para>
    /// </summary>
    public class TableCellOverflowTests
    {
        [Fact]
        public async Task ACellsComputedOverflowIsVisible()
        {
            var (root, _) = await LayoutAsync(Wrap("<table><tr><td id='c'>x</td><th id='h'>y</th></tr></table>"));

            Assert.Equal(Overflow.Visible, FindById(root, "c")!.Overflow.Value);
            Assert.Equal(Overflow.Visible, FindById(root, "h")!.Overflow.Value);
        }

        [Fact]
        public async Task ACellDoesNotPushAClipOfItsOwn()
        {
            // The paint-level statement of the same thing, per this repo's convention for a change
            // that alters what reaches the graphics layer: a cell with no author overflow must push
            // no clip at all. `overflow: hidden` from the UA sheet pushed one on every cell.
            var (root, container) = await LayoutAsync(Wrap(
                "<table style='table-layout:fixed; width:190pt'><tr>"
                + "<td id='c' style='width:40pt'>WIDECONTENTHERE</td><td style='width:150pt'>next</td>"
                + "</tr></table>"));

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, FindById(root, "c")!, recording);

            Assert.Empty(recording.PushedClips);
        }

        [Fact]
        public async Task AnAuthorDeclaredOverflowHiddenStillClips()
        {
            // The contrast case: removing the UA rule must not remove the property's own effect.
            var (root, container) = await LayoutAsync(Wrap(
                "<table style='table-layout:fixed; width:190pt'><tr>"
                + "<td id='c' style='width:40pt; overflow:hidden'>WIDECONTENTHERE</td><td style='width:150pt'>next</td>"
                + "</tr></table>"));

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, FindById(root, "c")!, recording);

            Assert.NotEmpty(recording.PushedClips);
        }
    }
}
