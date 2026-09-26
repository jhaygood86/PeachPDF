using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
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
            "<style>@page{size:300pt 200pt;margin:20pt} body{margin:0;font:10pt/12pt " + TestFont + "} p{margin:0}</style>";

        // A bundled font, so the widths that decide where lines wrap and pages break are the same on every
        // machine rather than whatever each one substitutes for a system font.
        private const string TestFont = "LineTopTestSans";

        private static Task<(CssBox Root, HtmlContainerInt Container)> Layout(string html, double pageWidth, double pageHeight) =>
            LayoutHarness.LayoutAsync(html, pageWidth, pageHeight,
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, TestFont));

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

            var (_, container) = await Layout(html, 300, 200);
            var painted = PaintedStrings(container).Where(t => t.Length == 2 && t[0] == 'H' && char.IsDigit(t[1]));

            // Once each: the page the line left claiming it too would draw it twice.
            Assert.Equal(["H1", "H2", "H3"], painted.Order(StringComparer.Ordinal));
        }

        // A padded `vertical-align: top` inline-block across a page foot. Its words are drawn over its top
        // padding, above the line top the flow recorded (a separate, older placement bug), so on the lines near
        // the foot the ink was on one page and the line top on the next: each page rejected the line, and whole
        // lines were drawn on no page. The line top is only trusted while the ink reaches the page it names.
        [Theory]
        [InlineData(30)]
        [InlineData(12)]
        public async Task APaddedTopAlignedInlineBlockAcrossAPageFoot_DrawsEveryWordOnce(int padding)
        {
            var words = string.Join(" ", Enumerable.Range(1, 59).Select(i => $"w{i}"));
            var html = $"<!DOCTYPE html><html><head>{Style}</head><body><p>X<span style='display:inline-block;width:100pt;" +
                       $"vertical-align:top;padding:{padding}pt 6pt 0'>{words}</span>Y</p></body></html>";

            var (_, container) = await Layout(html, 300, 160);
            var painted = PaintedStrings(container).Where(t => t.Length > 1 && t[0] == 'w' && char.IsDigit(t[1])).ToList();

            Assert.Equal(Enumerable.Range(1, 59).Select(i => $"w{i}").Order(), painted.Order());
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

            var (root, _) = await Layout(html, 300, 200);
            var cell = LayoutHarness.FindById(root, "cell")!;
            var line = Assert.Single(cell.LineBoxes);
            var word = Assert.Single(line.Words);

            Assert.True(word.Top > cell.Location.Y + 30, "the fixture must actually move the text down");
            Assert.NotNull(line.FlowTop);
            Assert.InRange(word.Top - line.FlowTop!.Value, -2, 2);
        }

        // Undoing a cell's alignment (TableRowCursor.Retract, which moves the content back by the negative of the
        // distance) has to take the cell's own lines back with its words too, or their recorded top is left where
        // the alignment put it.
        [Fact]
        public async Task UndoingACellsAlignment_TakesItsOwnLineTopBack()
        {
            var html = $"<!DOCTYPE html><html><head>{Style}</head><body><table><tr>"
                       + "<td id='cell' style='vertical-align:bottom;height:100pt'>text</td></tr></table></body></html>";

            var (root, _) = await Layout(html, 300, 200);
            var cell = LayoutHarness.FindById(root, "cell")!;
            var line = Assert.Single(cell.LineBoxes);
            var word = Assert.Single(line.Words);
            var topBefore = word.Top;

            CssLayoutEngine.OffsetCellContent(cell, -40, isVertical: false);

            Assert.Equal(topBefore - 40, word.Top, 3);
            Assert.InRange(word.Top - line.FlowTop!.Value, -2, 2);
        }

        // An inline-block whose text the surrounding line has taken keeps a line box of its own from an earlier
        // sizing layout, still listing that text. Once a translation moved that stale line along with the box,
        // its baseline no longer matched anything, and aligning the inline-block to it threw its text 214pt
        // below its line: drawn on no page. The stale line is not the box's baseline.
        [Theory]
        [InlineData("display:grid;grid-template-columns:repeat(2,1fr);gap:2pt")]
        [InlineData("display:flex")]
        public async Task AnInlineBlockInAnEngineItem_IsDrawnOnceAtItsLine(string containerCss)
        {
            // The review's minimized fuzz document (seed 226), verbatim and in Arial: the text widths decide
            // where the item's line wraps, and neither a simplified copy nor the bundled font (swept over page
            // height and item width) reached the stale line. The font-independent test of the rule is below.
            const string image = "data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==";
            var html = "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/1.2 Arial}</style></head><body>" +
                       $"<div style='{containerCss}'>z226_26 <div style='width:60%;border:1px dotted #333'>" +
                       "<h3 style='line-height:0.6;margin:2pt 0 8pt;padding:3pt;border:1px solid #666;'>" +
                       $"<img style='width:39pt;height:47pt;background:#ccc;vertical-align:middle' src='{image}'></h3>" +
                       "<span style='vertical-align:super;font-size:26pt;'>z226_36 </span> " +
                       "<span id='ib' style='display:inline-block;font-size:26pt;line-height:0;border:1px solid #888'>z226_38</span>" +
                       "</div></div></body></html>";

            var (root, container) = await Layout(html, 240, 150);
            var painted = PaintedStrings(container).Where(t => t is "z226_38");
            var box = LayoutHarness.FindById(root, "ib")!;
            var word = LayoutHarness.Descendants(box).SelectMany(b => b.Words).Single(w => w.Text == "z226_38");

            Assert.Equal(["z226_38"], painted);
            Assert.InRange(word.Top - word.Line!.LineTop, -30, 30);
        }

        // An inline-block with block content, moved down to meet a larger neighbour's baseline: the move
        // translates its subtree, and its own line boxes' recorded tops go with it exactly once. Shifted a
        // second time on top of the translation, its line top ended up the move's distance below its words.
        [Fact]
        public async Task AnInlineBlockMovedToTheBaseline_KeepsItsOwnLineTopWithItsWords()
        {
            var html = $"<!DOCTYPE html><html><head>{Style}</head><body><p id='p'>" +
                       "<span style='font-size:30pt'>Big</span> " +
                       "<span id='ib' style='display:inline-block;border:1px solid'><div>small</div></span></p></body></html>";

            var (root, _) = await Layout(html, 300, 200);
            var box = LayoutHarness.FindById(root, "ib")!;
            var line = LayoutHarness.Descendants(box).SelectMany(b => b.LineBoxes).First(l => l.Words.Any(w => w.Text == "small"));
            var word = line.Words.First(w => w.Text == "small");

            var paragraph = LayoutHarness.FindById(root, "p")!;
            Assert.True(box.Location.Y > paragraph.Location.Y + 5, "the fixture must move the inline-block down");
            Assert.NotNull(line.FlowTop);
            Assert.InRange(word.Top - line.FlowTop!.Value, -2, 2);
        }

        // The rule the fix above rests on, independent of fonts: a line box whose words have all been flowed onto
        // another line no longer gives an inline-block its baseline. The document above triggers it only with
        // Arial's widths; here the stale state is made directly, by pointing the line's words at another line.
        [Fact]
        public async Task ALineWhoseWordsLiveOnAnotherLine_IsNotAnInlineBlocksBaseline()
        {
            var html = $"<!DOCTYPE html><html><head>{Style}</head><body><p id='p'>a " +
                       "<span id='ib' style='display:inline-block'><div>x</div></span></p></body></html>";

            var (root, _) = await Layout(html, 300, 200);
            var box = LayoutHarness.FindById(root, "ib")!;
            var outer = LayoutHarness.FindById(root, "p")!.LineBoxes[0];
            var own = LayoutHarness.Descendants(box).SelectMany(b => b.LineBoxes).Single(l => l.Words.Any(w => w.Text == "x"));

            Assert.Equal(own.BaselineY, CssLayoutEngine.LastOwnLineBaselineOf(box));

            foreach (var word in own.Words) word.Line = outer;

            Assert.Null(CssLayoutEngine.LastOwnLineBaselineOf(box));
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
