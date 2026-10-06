using PeachDrawing.Core;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>float: footnote</c> body is painted on its own, by a painter that is not walking the page
    /// (<c>PdfGenerator</c> hands each body to <see cref="FragmentPainter.PaintFragment"/>), and its fragments are
    /// built by <c>MarginBoxContentFragmentBuilder</c>. That builder used to give every fragment a null clip, so
    /// <c>overflow: hidden</c> clipped nothing and <c>text-overflow: ellipsis</c> had no edge to truncate at -
    /// first failing the render (#1640), then painting the full text (#1641).
    /// </summary>
    public class FootnoteEllipsisTests
    {
        private const string Note = "a very long footnote that cannot fit in sixty points";

        private static async Task<(TestRecordingGraphics G, BoxFragment Body)> PaintNoteAsync(string noteStyle)
        {
            var (_, container) = await LayoutAsync(
                "<style>body{margin:0}div{font:10pt/10pt sans-serif}</style>"
                + "<div>Text<sup style='float:footnote; overflow:hidden; white-space:nowrap; "
                + $"width:60pt; {noteStyle}'>{Note}</sup> after</div>",
                pageWidth: 400, pageHeight: 400, margin: 20);

            var g = new TestRecordingGraphics();
            var painter = new FragmentPainter(container);
            var bodies = new List<BoxFragment>();

            foreach (var area in container.FragmentTree!.Fragmentainers.SelectMany(f => f.FootnoteAreas ?? []))
            {
                foreach (var body in area.Bodies)
                {
                    painter.PaintDetached(g, body);
                    bodies.Add(body);
                }
            }

            Assert.NotEmpty(bodies);
            return (g, bodies[0]);
        }

        private static List<string> Drawn(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawStringCall>().Select(d => d.Text).ToList();

        [Fact]
        public async Task EllipsisOnAFootnoteBody_TruncatesAtTheBodysPaddingEdge()
        {
            var (g, body) = await PaintNoteAsync("text-overflow:ellipsis");

            var drawn = Drawn(g);
            Assert.Contains("very", drawn);
            Assert.DoesNotContain("footnote", drawn);
            Assert.DoesNotContain("points", drawn);

            var ellipsis = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawStringCall>(), d => d.Text == "…");
            Assert.True(ellipsis.PaintPoint.X >= body.Rect.Left && ellipsis.PaintPoint.X + ellipsis.Size.Width <= body.Rect.Right + 0.5,
                $"the ellipsis ({ellipsis.PaintPoint.X}..{ellipsis.PaintPoint.X + ellipsis.Size.Width}) should sit inside the body ({body.Rect.Left}..{body.Rect.Right})");
        }

        [Fact]
        public async Task OverflowHiddenOnAFootnoteBody_ClipsItsContentToTheBodysPaddingEdge()
        {
            // No text-overflow: the content is still cut at the body's width instead of running past it.
            var (g, body) = await PaintNoteAsync("");

            var clips = g.Log.OfType<TestRecordingGraphics.PushClipCall>().Where(c => c.Rect.Width is > 0 and <= 61).ToList();
            Assert.NotEmpty(clips);
            Assert.All(clips, c =>
            {
                Assert.Equal(body.Rect.Left, c.Rect.Left, 1);
                Assert.Equal(body.Rect.Width, c.Rect.Width, 1);
            });
            Assert.DoesNotContain("…", Drawn(g));
            Assert.DoesNotContain("points", Drawn(g)); // wholly outside the clip, so not painted at all
        }

        [Fact]
        public async Task OverflowHiddenOnAFootnoteBody_InsetsTheClipByItsBorder()
        {
            var (g, body) = await PaintNoteAsync("border:3pt solid black");

            var clips = g.Log.OfType<TestRecordingGraphics.PushClipCall>().Where(c => c.Rect.Left > body.Rect.Left).ToList();
            Assert.NotEmpty(clips);
            Assert.All(clips, c =>
            {
                Assert.Equal(body.Rect.Left + 3, c.Rect.Left, 1);
                Assert.Equal(body.Rect.Right - 3, c.Rect.Right, 1);
            });
        }

        [Fact]
        public async Task UnderlineOnATruncatedFootnoteBody_EndsWhereTheEllipsisBegins()
        {
            // The decoration asks the same geometry for its cut, so it needs the same body edge.
            var (g, _) = await PaintNoteAsync("text-overflow:ellipsis; text-decoration:underline");

            var ellipsis = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawStringCall>(), d => d.Text == "…");
            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(ellipsis.PaintPoint.X, line.X2, 1);
        }
    }
}
