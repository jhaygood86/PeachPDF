using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A line of content layout keeps whole and slices where it crosses a page boundary (css-break-3 §4.4:
    /// "the UA may also fragment the contents of monolithic elements by slicing the element's graphical
    /// representation") is drawn by every page it overlaps, each page showing its own part. Drawn only by the
    /// page its top is on, the part past that page's foot was clipped away and the rest was drawn nowhere.
    /// </summary>
    public class SlicedMonolithLinesTests
    {
        // A capped, scroll container taller than any page: laid out whole and sliced across the pages.
        private static string CappedContainer(int lines, double padding, string extra = "") =>
            "<div style='overflow:hidden;max-height:100000pt;padding-top:" + padding.ToString(CultureInfo.InvariantCulture)
            + "pt;line-height:16pt;font:10pt/16pt Arial;" + extra + "'>"
            + string.Concat(Enumerable.Range(1, lines).Select(i => $"<p style='margin:0 0 6pt'>L{i}</p>")) + "</div>";

        private static string Document(string body, double pageHeight) =>
            "<!DOCTYPE html><html><head><style>@page{size:300pt " + pageHeight.ToString(CultureInfo.InvariantCulture)
            + "pt;margin:20pt} body{margin:0}</style></head><body>" + body + "</body></html>";

        // Padding and page height move the lines across the boundary, so the sweep puts a line on it at
        // some step for each of them.
        [Theory]
        [InlineData(0)]
        [InlineData(7.5)]
        public async Task ACappedScrollContainerTallerThanAPage_ShowsEveryLineInFullAcrossItsPages(double padding)
        {
            List<string> failures = [];

            for (var height = 100.0; height <= 160.0; height += 1.0)
            {
                var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                    Document(CappedContainer(30, padding), height), new PdfGenerateConfig { PageSize = PageSize.Letter });

                var shown = VisibleFractions(container);

                foreach (var line in Enumerable.Range(1, 30).Select(i => $"L{i}"))
                {
                    var fraction = shown.GetValueOrDefault(line);
                    if (fraction < 0.95) failures.Add($"{height}pt: {line} shows {fraction:0.00} of its height");
                }
            }

            Assert.Empty(failures);
        }

        // Two parts of one line are never the whole line twice over: each page shows its own part only.
        [Fact]
        public async Task ASlicedLine_IsNotShownMoreThanOnceInFull()
        {
            List<string> failures = [];

            for (var height = 100.0; height <= 160.0; height += 1.0)
            {
                var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                    Document(CappedContainer(30, 0), height), new PdfGenerateConfig { PageSize = PageSize.Letter });

                foreach (var (line, fraction) in VisibleFractions(container))
                {
                    if (fraction > 1.05) failures.Add($"{height}pt: {line} shows {fraction:0.00} of its height");
                }
            }

            Assert.Empty(failures);
        }

        // Content that layout already moves whole is drawn on one page only: the slice rule does not apply to it.
        [Fact]
        public async Task AnOrdinaryParagraphAtAPageFoot_IsStillDrawnByOnePage()
        {
            var body = string.Concat(Enumerable.Range(1, 40).Select(i => $"<p style='margin:0 0 6pt'>L{i}</p>"));
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(body, 130), new PdfGenerateConfig { PageSize = PageSize.Letter });

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);
            Assert.All(PaintedPerPage(container).SelectMany(p => p).GroupBy(t => t), g => Assert.Single(g));
        }

        // #1439's document: a padded, top-aligned inline-block taller than a page. Its text starts below the
        // padding, and every one of its words is still shown on some page.
        [Fact]
        public async Task ATallPaddedTopAlignedInlineBlock_ShowsEveryWord()
        {
            var words = string.Concat(Enumerable.Range(1, 59).Select(i => $"w{i}x "));
            var html = "<!DOCTYPE html><html><head><style>@page{size:300pt 160pt;margin:20pt} body{margin:0;font:10pt/12pt Arial} p{margin:0}</style></head><body>"
                + "<p>X<span style='display:inline-block;width:100pt;vertical-align:top;padding:30pt 6pt 0'>" + words + "</span>Y</p></body></html>";

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig { PageSize = PageSize.Letter });

            var shown = VisibleFractions(container);
            var missing = Enumerable.Range(1, 59).Select(i => $"w{i}x").Where(w => shown.GetValueOrDefault(w) < 0.95).ToList();

            Assert.Empty(missing);
        }

        // #1371: a scroll container inside a multi-column container is kept whole, and where the column
        // container lands on the page decides whether its first line sits on a boundary. Every word must
        // show in full at every position.
        [Fact]
        public async Task AScrollContainerInAColumnContainer_ShowsEveryWordWhereverItLands()
        {
            List<string> failures = [];

            for (var filler = 0; filler <= 20; filler++)
            {
                var html = "<!DOCTYPE html><html><head><style>@page{size:300pt 200pt;margin:20pt} body{margin:0;font:10pt/12pt Arial} p{margin:0}</style></head><body>"
                    + string.Concat(Enumerable.Range(1, filler).Select(i => $"<p>f{i}</p>"))
                    + "<div style='columns:2'><div style='overflow:auto'>w250 w251 w252 w253 w254 w255<div>"
                    + string.Concat(Enumerable.Repeat("<br>", 12)) + "</div></div></div></body></html>";

                var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig { PageSize = PageSize.Letter });
                var shown = VisibleFractions(container);

                foreach (var word in Enumerable.Range(250, 6).Select(i => $"w{i}"))
                {
                    var fraction = shown.GetValueOrDefault(word);
                    if (Math.Abs(fraction - 1) > 0.05) failures.Add($"{filler} fillers: {word} shows {fraction:0.00}");
                }
            }

            Assert.Empty(failures);
        }

        // The visible part of each string, summed over every page that draws it, as a fraction of its height.
        internal static Dictionary<string, double> VisibleFractions(HtmlContainerInt container)
        {
            var shown = new Dictionary<string, double>();

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, recording, page);

                var clips = new Stack<Rect?>();
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
                        case PaintOpKind.DrawString when op.Text is { } text && op.Bounds.Height > 0:
                            var top = op.Bounds.Top;
                            var bottom = op.Bounds.Bottom;
                            foreach (var clip in clips.OfType<Rect>())
                            {
                                top = Math.Max(top, clip.Top);
                                bottom = Math.Min(bottom, clip.Bottom);
                            }

                            shown[text] = shown.GetValueOrDefault(text) + Math.Max(0, bottom - top) / op.Bounds.Height;
                            break;
                    }
                }
            }

            return shown;
        }

        private static List<List<string>> PaintedPerPage(HtmlContainerInt container)
        {
            List<List<string>> pages = [];

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, recording, page);
                pages.Add(recording.Log.Where(o => o.Kind == PaintOpKind.DrawString && o.Text is not null)
                    .Select(o => o.Text!).ToList());
            }

            return pages;
        }
    }
}
