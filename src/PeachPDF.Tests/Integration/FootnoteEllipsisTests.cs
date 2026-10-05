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
    /// fragment of its words carries no overflow clip to take a <c>text-overflow: ellipsis</c> boundary from.
    /// That used to be a <see cref="System.NullReferenceException"/> (#1640); the text now paints untruncated.
    /// </summary>
    public class FootnoteEllipsisTests
    {
        private static async Task<TestRecordingGraphics> PaintNoteAsync(string noteStyle)
        {
            var (_, container) = await LayoutAsync(
                "<style>body{margin:0}div{font:10pt/10pt sans-serif}</style>"
                + "<div>Text<sup style='float:footnote; overflow:hidden; white-space:nowrap; "
                + $"text-overflow:ellipsis; width:60pt; {noteStyle}'>a very long footnote that cannot fit in sixty points</sup> after</div>",
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
            return g;
        }

        [Fact]
        public async Task EllipsisOnAFootnoteBody_PaintsItsTextInsteadOfThrowing()
        {
            var g = await PaintNoteAsync("");

            // Untruncated: every word of the note is drawn and no ellipsis glyph is.
            var drawn = g.Log.OfType<TestRecordingGraphics.DrawStringCall>().Select(d => d.Text).ToList();
            Assert.Contains("footnote", drawn);
            Assert.Contains("points", drawn);
            Assert.DoesNotContain("…", drawn);

            // Nothing clips the body to its 60pt width either, which is why a nowrap note can run past it.
            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.PushClipCall>(), c => c.Rect.Width is > 0 and <= 61);
        }

        [Fact]
        public async Task DecoratedEllipsisFootnoteBody_PaintsWithItsWholeDecoration()
        {
            // The decoration asks for the line's cut; with no clip there is none, so the line is not shortened.
            var g = await PaintNoteAsync("text-decoration:underline");

            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.DoesNotContain("…", g.Log.OfType<TestRecordingGraphics.DrawStringCall>().Select(d => d.Text));
        }
    }
}
