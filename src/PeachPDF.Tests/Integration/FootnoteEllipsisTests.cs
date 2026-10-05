using PeachPDF.Html.Core.Paint;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>float: footnote</c> body is painted on its own, by a painter that is not walking the page
    /// (<c>PdfGenerator</c> hands each body to <see cref="FragmentPainter.PaintFragment"/>), so the
    /// fragment of its words has no clipping ancestor to take a <c>text-overflow: ellipsis</c> boundary from
    /// (a fragment's clip is its clipping ancestor's, and the body is a detached root that is its own
    /// truncating block).
    /// </summary>
    public class FootnoteEllipsisTests
    {
        [Fact]
        public async Task EllipsisOnAFootnoteBody_PaintsItsTextInsteadOfThrowing()
        {
            var (_, container) = await LayoutAsync(
                "<style>body{margin:0}div{font:10pt/10pt sans-serif}</style>"
                + "<div>Text<sup style='float:footnote; overflow:hidden; white-space:nowrap; "
                + "text-overflow:ellipsis; width:60pt'>a very long footnote that cannot fit in sixty points</sup> after</div>",
                pageWidth: 400, pageHeight: 400, margin: 20);

            var g = new TestRecordingGraphics();
            var painter = new FragmentPainter(container);
            var bodies = 0;

            foreach (var area in container.FragmentTree!.Fragmentainers.SelectMany(f => f.FootnoteAreas ?? []))
            {
                foreach (var body in area.Bodies)
                {
                    painter.PaintFragment(g, body);
                    bodies++;
                }
            }

            Assert.True(bodies > 0, "the footnote should have produced a body");

            // Untruncated: every word of the note is drawn and no ellipsis glyph is.
            var drawn = g.Log.OfType<TestRecordingGraphics.DrawStringCall>().Select(d => d.Text).ToList();
            Assert.Contains("footnote", drawn);
            Assert.Contains("points", drawn);
            Assert.DoesNotContain("…", drawn);
        }
    }
}
