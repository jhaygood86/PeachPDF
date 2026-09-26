using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An absolutely positioned box across page boundaries: drawn on the page its offsets place it on even
    /// when that page is already emitted, laid out in one piece so its own break cannot end the pass, and
    /// never displacing the in-flow content after it (CSS 2.1 §9.3.1).
    /// </summary>
    /// <remarks>
    /// The fixtures use a 300pt-wide, 200pt page with 20pt margins, so page <c>k</c>'s band is
    /// <c>[20 + 160k, 180 + 160k)</c>, and 10pt/12pt text.
    /// </remarks>
    public class AbsoluteBoxAcrossPagesIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        // The absolutely positioned box itself is laid out unbroken: every one of its lines is placed, on
        // the page its slice falls in, rather than the lines after its first page boundary being lost.
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

        // An absolutely positioned box placed on an emitted page re-opens every page its content reaches, not
        // only the pages its border box covers: overflowing text past a short box's height was lost.
        [Fact]
        public async Task AbsoluteBoxOnAnEmittedPage_DrawsItsOverflowingContent()
        {
            var placed = await WordFragments(
                $"{Lines("P", 40)}<div style='position:absolute;top:0;left:150pt;width:100pt;height:30pt'>{Lines("W", 25)}</div>");

            Assert.Equal(Enumerable.Range(1, 25).Select(i => $"W{i}"), placed.Select(w => w.Text).Distinct().Order(WordNumber.Instance));
        }

        // An absolutely positioned box that is or holds a multi-column container keeps the breaking path too.
        // Laid out unbroken, its columns lost the fragmentainer and W18–W20 with it.
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

        // Such a box keeps the breaking path, so a break inside it ends the pass, and the next pass resumes
        // inside it on page 2. The content after it, placed at its parent's top on page 1, landed on the page
        // that pass had already emitted and was lost; re-opening that page drew a short block but sliced a long
        // one across the margin and lost a following multi-column block's first page. As on main, the
        // parent's absolutely positioned first child is treated as preceding the content after it, which is
        // laid out below it and paginated normally, including when that first child is a plain absolute box
        // and the multi-column one follows it.
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

        // A plain absolute box as the first child, then the multi-column one: main returned the plain first
        // child as the previous sibling, and the content after both was laid out below it. Checking only
        // whether the first child held columns lost that content and the paragraph after the block again.
        [Fact]
        public async Task ContentAfterAPlainThenAMultiColumnAbsoluteBox_IsDrawnInsideAPageBand()
        {
            // Through the PdfGenerator pipeline with an @page rule, as the review measured it. The content goes
            // below the plain first child, so X1 reaches page 2 for it to be drawn there, as on main.
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

        // In-flow content after a tall absolutely positioned box, first in its block or after other content.
        // The box's break used to end the pass, and the paragraphs after it, which it does not displace (CSS
        // 2.1 §9.3.1), were placed back on the page the break left and drawn on no page. Laid out unbroken,
        // the box is sliced across the pages and every paragraph is drawn once, inside a page band.
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
            string body, string prefix, double pageWidth = 595, double pageHeight = PageHeight, double margin = Margin)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>" +
                       $"</head><body>{body}</body></html>";
            var (_, container) = await LayoutHarness.LayoutAsync(
                html, pageWidth: pageWidth, pageHeight: pageHeight, margin: margin);

            // Painted, not merely present in the fragment tree: a fragmented scroll container in a column
            // kept every word in its fragments but clipped the first column's to nothing at paint time.
            var painted = new List<string>();
            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, recording, page);
                painted.AddRange(VisiblyDrawnStrings(recording.Log));
            }

            return painted
                .Where(t => t.StartsWith(prefix, StringComparison.Ordinal) && t.Length > prefix.Length
                            && char.IsDigit(t[prefix.Length]))
                .Distinct()
                .ToList();
        }

        // Every string drawn with some part of it inside all the rectangle clips in force when it was drawn.
        // A path clip is popped like a rectangle one, so it holds a place on the stack but tests nothing.
        private static IEnumerable<string> VisiblyDrawnStrings(IEnumerable<PaintOp> log)
        {
            var clips = new Stack<PeachPDF.Html.Adapters.Entities.RRect?>();
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
