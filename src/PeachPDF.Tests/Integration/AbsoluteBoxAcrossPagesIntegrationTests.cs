using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An absolutely positioned box across page boundaries: drawn on the page its offsets place it on even
    /// when that page is already emitted, broken between its lines as its own run of fragmentainer passes so
    /// that its break cannot end the pass of the block around it, and never displacing the in-flow content
    /// after it (CSS 2.1 §9.3.1).
    /// </summary>
    /// <remarks>
    /// The fixtures use a 300pt-wide, 200pt page with 20pt margins, so page <c>k</c>'s band is
    /// <c>[20 + 160k, 180 + 160k)</c>, and 10pt/12pt text.
    /// </remarks>
    public class AbsoluteBoxAcrossPagesIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        // The absolutely positioned box itself breaks between its lines, as a browser prints it: every one of its
        // lines is placed once, inside a page band, and a page that runs out of room continues on the next.
        [Fact]
        public async Task TallAbsoluteBox_PlacesEveryOneOfItsOwnLines()
        {
            var lines = string.Join("<br>", Enumerable.Range(1, 37).Select(i => $"W{i}"));
            var placed = await WordsPlaced(
                "<p>P1</p><p>P2</p><p>P3</p>" +
                $"<div style='position:absolute;top:0;right:0;width:60pt'>{lines}</div>" +
                string.Concat(Enumerable.Range(1, 20).Select(i => $"<p>Q{i}</p>")), "W", pageWidth: 300);

            Assert.Equal(Enumerable.Range(1, 37).Select(i => $"W{i}"), placed.Order(WordNumber.Instance));
        }

        // A box taller than the page, on a document with almost nothing else, adds the pages it needs and breaks
        // between lines: no line is cut by a page edge and the lines fall 12, 13, 13 and 2 to a page, which is
        // what a browser prints for the same document (13 lines fit a 160pt band; the box starts 5pt down page 1).
        [Fact]
        public async Task TallAbsoluteBox_AddsPagesAndBreaksBetweenItsLines()
        {
            var placed = await WordFragments(
                $"<p>Flow</p><div style='position:absolute;top:5pt;right:0;width:100pt'>{Lines("W", 40)}</div>");

            AssertEachDrawnOnceInsideABand(placed, 40);
            Assert.Equal([12, 13, 13, 2], placed.GroupBy(w => w.Page).OrderBy(g => g.Key).Select(g => g.Count()));
        }

        // A box whose last line or lines cross the page foot breaks between its lines. Its position comes from its
        // offsets, so the widows correction that lays a block out again from the next page's top cannot apply to
        // it: it put the box back where it was, with its last line across the foot and no fragment on the next
        // page, and the line was drawn on no page. Its widows are relaxed instead, so a 4-line box splits 3 and 1.
        [Theory]
        [InlineData(3, 130)]
        [InlineData(3, 134)]
        [InlineData(4, 117)]
        [InlineData(4, 120)]
        [InlineData(5, 117)]
        public async Task AbsoluteBoxWhoseLastLinesCrossThePageFoot_BreaksBetweenItsLines(int lineCount, int top)
        {
            var placed = await WordFragments(
                $"<p>P1</p><div style='position:absolute;top:{top}pt;left:0;width:59pt'>{Lines("W", lineCount)}</div>");

            AssertEachDrawnOnceInsideABand(placed, lineCount);
            Assert.Equal(2, placed.Select(w => w.Page).Distinct().Count());
        }

        // An absolutely positioned box placed on an emitted page re-opens every page its content reaches, not
        // only the pages its border box covers: overflowing text past a short box's height was lost.
        [Fact]
        public async Task AbsoluteBoxOnAnEmittedPage_DrawsItsOverflowingContent()
        {
            var placed = await WordFragments(
                $"{Lines("P", 40)}<div style='position:absolute;top:0;left:150pt;width:100pt;height:30pt'>{Lines("W", 25)}</div>");

            Assert.Equal(Enumerable.Range(1, 25).Select(i => $"W{i}"), placed.Select(w => w.Text).Distinct().Order(WordNumber.Instance));
        }

        // An absolutely positioned box that is or holds a multi-column container runs its columns inside its own
        // passes like any other box: every word is drawn once, inside a page band, and none is lost to the column
        // break (the last lines of 20 paragraphs in two columns were once).
        [Theory]
        [InlineData("<p>X1</p><div style='position:absolute;top:120pt;width:200pt;columns:2'>{0}</div>")]
        [InlineData("<p>X1</p><div style='position:absolute;top:120pt;width:200pt'><div style='columns:2'>{0}</div></div>")]
        [InlineData("<div style='position:relative'><p>X1</p><div style='position:absolute;top:120pt;width:200pt;columns:2'>{0}</div></div>")]
        public async Task AbsoluteBoxThatIsOrHoldsAMultiColumnContainer_PlacesEveryWordInsideAPageBand(string shape)
        {
            var paragraphs = string.Concat(Enumerable.Range(1, 20).Select(i => $"<p>W{i}</p>"));
            var placed = await WordFragments(string.Format(shape, paragraphs));

            AssertEachDrawnOnceInsideABand(placed, 20);
        }

        // The content after such a box, which it does not displace (CSS 2.1 §9.3.1), starts at its parent's top and
        // is paginated normally: the box's break does not end the pass that places it, so nothing after the box is
        // put back on a page that pass has left, whether the box is the block's first child or comes later.
        [Theory]
        [InlineData("<p>B1</p><div>{0}<p>W9</p></div>", 9)]
        [InlineData("<p>B1</p><div>{0}<p>W9</p><p>W10</p><p>W11</p><p>W12</p><p>W13</p><p>W14</p><p>W15</p><p>W16</p><p>W17</p><p>W18</p></div>", 18)]
        [InlineData("<p>B1</p><div>{0}<div style='columns:2'>{1}</div></div>", 34)]
        [InlineData("<p>B1</p><div>{0}<div>{2}</div></div>", 21)]
        public async Task ContentAfterAnAbsoluteMultiColumnBox_IsDrawnInsideAPageBand(string shape, int count)
        {
            var box = "<div style='position:absolute;top:120pt;left:180pt;width:80pt;columns:2'>" +
                      string.Concat(Enumerable.Range(1, 8).Select(i => $"<p>W{i}</p>")) + "</div>";
            var many = string.Concat(Enumerable.Range(9, 26).Select(i => $"<p>W{i}</p>"));
            var thirteen = string.Concat(Enumerable.Range(9, 13).Select(i => $"<p>W{i}</p>"));
            var placed = await WordFragments(string.Format(shape, box, many, thirteen));

            AssertEachDrawnOnceInsideABand(placed, count);
        }

        // A plain absolute box as the first child, then the multi-column one: neither displaces the content after
        // them, and none of it is lost.
        [Fact]
        public async Task ContentAfterAPlainThenAMultiColumnAbsoluteBox_IsDrawnInsideAPageBand()
        {
            // Through the PdfGenerator pipeline with an @page rule. The @page rule sets the 300×200pt page the band
            // constants describe; the config's size is only its default.
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>@page{size:300pt 200pt;margin:20pt} " +
                "body{margin:0;font:10pt/12pt Arial} p{margin:0}</style></head><body>" +
                "<p>BEFORE</p><div><div style='position:absolute;top:80pt;left:200pt;height:80pt'>X1</div>" +
                "<div style='position:absolute;top:120pt;width:200pt;columns:2'>" +
                string.Concat(Enumerable.Range(1, 8).Select(i => $"<p>W{i}</p>")) + "</div>" +
                "<p>AFTER</p></div><p>END</p></body></html>",
                new PdfGenerateConfig { PageSize = PageSize.Letter });

            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root).SelectMany(f => f.Words))
                .Where(w => w.Word.Text is "AFTER" or "END")
                .ToList();

            Assert.Equal(["AFTER", "END"], placed.Select(w => w.Word.Text!).Order());
            Assert.All(placed, w => Assert.True(
                w.Rect.Top >= Margin - 0.01 && w.Rect.Bottom <= PageHeight - Margin + 0.01,
                $"{w.Word.Text} lies outside its page band ({w.Rect.Top:F2}-{w.Rect.Bottom:F2})"));
        }

        // An absolute multi-column box placed on an earlier page than the content around it moves nothing:
        // the paragraphs before and after it stay on the second page, inside an overflow: hidden wrapper.
        [Fact]
        public async Task AbsoluteMultiColumnBoxOnAnEarlierPage_LeavesTheContentAroundItInPlace()
        {
            var placed = await WordFragments(
                "<p style='break-after:page'>B1</p><div style='overflow:hidden'><p>W1</p>" +
                "<div style='position:absolute;top:0;left:150pt;width:100pt;columns:2'><p>X1</p><p>X2</p></div>" +
                "<p>W2</p></div>");

            AssertEachDrawnOnceInsideABand(placed, 2);
            Assert.All(placed, w => Assert.Equal(1, w.Page));
        }

        // A box centred between top and bottom by auto margins (CSS 2.1 §10.6.4) is placed twice: provisionally
        // in its own epilogue, and finally by its containing block's, once that block's height is known. The
        // final position can be on a page already emitted, and it was drawn on no page.
        [Theory]
        [InlineData("")]
        [InlineData("position:relative")]
        public async Task AnAbsoluteBoxCentredByItsContainingBlock_IsDrawnWhereItIsFinallyPlaced(string wrapperCss)
        {
            var paragraphs = string.Concat(Enumerable.Range(1, 60).Select(i => $"<p>P{i}</p>"));
            var placed = await WordsPlaced(
                $"<div style='{wrapperCss}'>{paragraphs}" +
                "<div style='position:absolute;top:0;bottom:0;height:20pt;margin:auto 0;left:150pt'>Z1</div></div>",
                "Z", pageWidth: 300, distinct: false);

            // Once: not also at the provisional position its own epilogue gave it.
            Assert.Equal(["Z1"], placed);
        }

        // In-flow content after a tall absolutely positioned box, first in its block or after other content.
        // The box's break used to end the pass, and the paragraphs after it, which it does not displace (CSS
        // 2.1 §9.3.1), were placed back on the page the break left and drawn on no page. The box now breaks in
        // passes of its own, and every paragraph is drawn once, inside a page band.
        [Theory]
        [InlineData("")]
        [InlineData("<p>P0</p>")]
        public async Task ContentAfterATallAbsoluteBox_IsDrawnInsideAPageBand(string before)
        {
            var filler = string.Join("<br>", Enumerable.Range(1, 30).Select(i => $"F{i}"));
            var paragraphs = string.Concat(Enumerable.Range(1, 10).Select(i => $"<p>W{i}</p>"));
            var placed = await WordFragments(
                $"{before}<div style='position:absolute;top:5pt;left:0;width:120pt'>{filler}</div>{paragraphs}");

            AssertEachDrawnOnceInsideABand(placed, 10);
        }

        private static string Lines(string prefix, int count) =>
            string.Join("<br>", Enumerable.Range(1, count).Select(i => $"{prefix}{i}"));

        private static async Task<List<(int Page, string Text, double Top, double Bottom)>> WordFragments(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>" +
                       $"</head><body>{body}</body></html>";
            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: PageHeight, margin: Margin);

            return container.FragmentTree!.Fragmentainers
                .SelectMany((page, index) => Flatten(page.Root).SelectMany(f => f.Words)
                    .Select(w => (index, w.Word.Text ?? "", w.Rect.Top, w.Rect.Bottom)))
                .Where(w => w.Item2.Length > 1 && w.Item2[0] == 'W' && char.IsDigit(w.Item2[1]))
                .ToList();
        }

        private static void AssertEachDrawnOnceInsideABand(List<(int Page, string Text, double Top, double Bottom)> placed, int count)
        {
            Assert.Equal(Enumerable.Range(1, count).Select(i => $"W{i}"), placed.Select(w => w.Text).Order(WordNumber.Instance));
            Assert.All(placed, w => Assert.True(
                w.Top >= Margin - 0.01 && w.Bottom <= PageHeight - Margin + 0.01,
                $"{w.Text} lies outside its page band ({w.Top:F2}-{w.Bottom:F2})"));
        }

        private static async Task<List<string>> WordsPlaced(
            string body, string prefix, double pageWidth = 595, double pageHeight = PageHeight, double margin = Margin,
            bool distinct = true)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>" +
                       $"</head><body>{body}</body></html>";
            var (_, container) = await LayoutHarness.LayoutAsync(
                html, pageWidth: pageWidth, pageHeight: pageHeight, margin: margin);

            // Painted, not merely present in the fragment tree: a word can be in a fragment that paint clips to
            // nothing, so only strings drawn inside every clip in force count.
            var painted = new List<string>();
            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, recording, page);
                painted.AddRange(VisiblyDrawnStrings(recording.Log));
            }

            var matching = painted
                .Where(t => t.StartsWith(prefix, StringComparison.Ordinal) && t.Length > prefix.Length
                            && char.IsDigit(t[prefix.Length]));

            return (distinct ? matching.Distinct() : matching).ToList();
        }

        // Every string drawn with some part of it inside all the rectangle clips in force when it was drawn.
        // A path clip is popped like a rectangle one, so it holds a place on the stack but tests nothing.
        private static IEnumerable<string> VisiblyDrawnStrings(IEnumerable<PaintOp> log)
        {
            var clips = new Stack<PeachDrawing.Core.Rect?>();
            foreach (var op in log)
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
                        yield return text;
                        break;
                }
            }
        }

        private sealed class WordNumber : IComparer<string>
        {
            public static readonly WordNumber Instance = new();

            public int Compare(string? x, string? y) =>
                int.Parse(x.AsSpan(1)).CompareTo(int.Parse(y.AsSpan(1)));
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
