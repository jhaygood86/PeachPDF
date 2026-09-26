using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Whether a line is claimed by the page its line box is on, rather than by where its ink starts, and
    /// whether a line box's recorded top (<c>CssLineBox.FlowTop</c>) moves with its words when a mover
    /// translates them, since the fragment emitter reads it to make that decision.
    /// </summary>
    public class LineTopFollowsItsWordsTests
    {
        private const string Style =
            "<style>@page{size:300pt 200pt;margin:20pt} body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>";

        // A heading in 13pt type on the body's 12pt line has a negative half-leading, so its ink rises
        // above its line box (CSS 2.1 §10.8.1). A break moved the line to the next page's top, its ink
        // started above that page's band, the page it left claimed it, and that page never reached the
        // heading: it was drawn on no page.
        [Fact]
        public async Task AHeadingWhoseInkRisesAboveItsLine_MovedToThePageTop_IsDrawnOnce()
        {
            // The card's border and padding are what put the second heading's line exactly on the boundary.
            const string cardCss = "border:1px solid #666;padding:4pt;";
            const string greek = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi "
                                 + "omicron pi rho sigma tau upsilon";
            string Card(int n) =>
                $"<div style='{cardCss}margin:0 0 8pt'>"
                + $"<h2 style='font-size:13pt;margin:10pt 0 4pt'>H{n}</h2>"
                + $"<p style='margin:0 0 6pt'>{greek}</p><p style='margin:0 0 6pt'>second para of card {n}</p></div>";

            var body = "<p style='margin:0 0 6pt'>fill0</p>" + Card(1) + Card(2) + Card(3);
            var html = $"<!DOCTYPE html><html><head>{Style}</head><body>{body}</body></html>";

            var (_, container) = await LayoutHarness.LayoutAsync(html, 300, 200);
            var painted = PaintedStrings(container).Where(t => t.Length == 2 && t[0] == 'H' && char.IsDigit(t[1]));

            // Once each: the page the line left claiming it too would draw it twice.
            Assert.Equal(["H1", "H2", "H3"], painted.Order(StringComparer.Ordinal));
        }

        // A bottom-aligned cell with its text directly in it owns the line that text is on, but the
        // alignment moves the cell's children, not the cell. The line's recorded top has to move with its
        // words, since the fragment emitter reads it to decide which page the line is on.
        [Theory]
        [InlineData("bottom")]
        [InlineData("middle")]
        public async Task AnAlignedCellsOwnLine_KeepsItsLineTopWithItsWords(string align)
        {
            var html = $"<!DOCTYPE html><html><head>{Style}</head><body><table><tr>"
                       + $"<td id='cell' style='vertical-align:{align};height:100pt'>text</td></tr></table></body></html>";

            var (root, _) = await LayoutHarness.LayoutAsync(html, 300, 200);
            var cell = LayoutHarness.FindById(root, "cell")!;
            var line = Assert.Single(cell.LineBoxes);
            var word = Assert.Single(line.Words);

            Assert.True(word.Top > cell.Location.Y + 30, "the fixture must actually move the text down");
            Assert.NotNull(line.FlowTop);
            Assert.InRange(word.Top - line.FlowTop!.Value, -2, 2);
        }

        // Every string drawn, on every page, with some part of it inside all the rectangle clips in force
        // when it was drawn. A path clip is popped like a rectangle one, so it holds a place on the stack
        // but tests nothing.
        private static List<string> PaintedStrings(HtmlContainerInt container)
        {
            var painted = new List<string>();

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, recording, page);

                var clips = new Stack<RRect?>();
                foreach (var op in recording.Log)
                {
                    switch (op.Kind)
                    {
                        case PaintOpKind.PushClip:
                            clips.Push(op.Bounds);
                            break;
                        case PaintOpKind.PushClipPath:
                            clips.Push(null);
                            break;
                        case PaintOpKind.PopClip when clips.Count > 0:
                            clips.Pop();
                            break;
                        case PaintOpKind.DrawString when op.Text is { } text
                                                        && clips.All(c => c is not { } clip || clip.IntersectsWith(op.Bounds)):
                            painted.Add(text);
                            break;
                    }
                }
            }

            return painted;
        }
    }
}
