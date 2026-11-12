using PeachPDF.Rendering;
using PeachDrawing.PDF;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A block-level grid container that continues across pages of different widths follows
    /// <see href="https://www.w3.org/TR/css-break-3/#varying-size-boxes">css-break-3 section 5.1</see>: each fragment recalculates its sizes and
    /// positions using its own fragmentainer's size, so the container's frame and the tracks and items on each page track the page they are on.
    /// Asserted on the fragment tree (the layout/paint contract), as for flex.
    /// </summary>
    /// <remarks>
    /// Every fixture is a 612pt-wide sheet with <c>@page { margin: 20pt 50pt }</c> and no left margin on the first page, so page 0's content is
    /// 562pt wide and every later page's is 512pt wide.
    /// </remarks>
    public class GridPerPageReflowIntegrationTests
    {
        private const string Head = """
            <!DOCTYPE html><html><head><style>
            @page { margin: 20pt 50pt; }
            @page :first { margin-left: 0; }
            body { margin: 0; font: 12pt Arial; }
            .i { border: 1px solid #333; }
            p { margin: 0; }
            """;

        private const double FirstPageMeasure = 562;
        private const double LaterPageMeasure = 512;
        private const double Tolerance = 0.6;

        private static async Task<HtmlContainerInt> BuildAsync(string html, double sheetHeight = 300)
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            container.PageSize = new Size(
                612 - container.MarginLeft - container.MarginRight,
                sheetHeight - container.MarginTop - container.MarginBottom);
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new Size(container.PageSize.Width, 0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, new Size(container.PageSize.Width, container.PageSize.Height));
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static BoxFragment? FragmentOf(BoxFragment root, CssBox target)
        {
            if (ReferenceEquals(root.Box, target)) return root;

            foreach (var child in root.Children)
            {
                if (FragmentOf(child, target) is { } found) return found;
            }

            return null;
        }

        private static CssBox ById(HtmlContainerInt container, string id)
        {
            static CssBox? Find(CssBox box, string id)
            {
                if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, StringComparison.OrdinalIgnoreCase))
                    return box;

                foreach (var child in box.Boxes)
                {
                    if (Find(child, id) is { } found) return found;
                }

                return null;
            }

            return Find(container.Root!, id) ?? throw new InvalidOperationException($"no element #{id}");
        }

        /// <summary>The border box <paramref name="id"/> has on each page it has a fragment on, keyed by page.</summary>
        private static Dictionary<int, Rect> FramesOf(HtmlContainerInt container, string id)
        {
            var box = ById(container, id);
            var frames = new Dictionary<int, Rect>();
            var tree = container.FragmentTree!;

            for (var page = 0; page < tree.Fragmentainers.Count; page++)
            {
                if (FragmentOf(tree.Fragmentainers[page].Root, box) is { } fragment)
                    frames[page] = fragment.WholeBoxRect;
            }

            return frames;
        }

        private static string Describe(Dictionary<int, Rect> frames) =>
            string.Join(" ", frames.OrderBy(f => f.Key).Select(f => $"p{f.Key}:x={f.Value.X:0.#},w={f.Value.Width:0.#}"));

        private static string Cells(int count) =>
            string.Concat(Enumerable.Range(0, count).Select(n => $"<div id=\"c{n}\" class=\"i\" style=\"height: 120pt\">cell {n}</div>"));

        [Fact]
        public async Task Container_TracksTheMeasureOfEachPageItContinuesOnto()
        {
            var container = await BuildAsync(Head + $$"""
                #g { display: grid; grid-template-columns: 1fr 1fr 1fr; }
                </style></head><body><div id="g">{{Cells(12)}}</div></body></html>
                """);

            var frames = FramesOf(container, "g");

            Assert.True(frames.Count >= 2, "the fixture must span at least two pages");
            Assert.Equal(FirstPageMeasure, frames[0].Width, Tolerance);

            foreach (var (page, frame) in frames.Where(f => f.Key > 0))
                Assert.True(Math.Abs(frame.Width - LaterPageMeasure) < Tolerance, $"page {page}: {frame.Width}");
        }

        [Fact]
        public async Task Rows_ThatLandOnALaterPageAreSizedToThatPagesColumns()
        {
            var container = await BuildAsync(Head + $$"""
                #g { display: grid; grid-template-columns: 1fr 1fr 1fr; }
                </style></head><body><div id="g">{{Cells(12)}}</div></body></html>
                """);

            // Rows of three: the first two rows fit page 0 (562 / 3 wide), the last two land on page 1 (512 / 3 wide).
            Assert.Equal(FirstPageMeasure / 3, FramesOf(container, "c0")[0].Width, Tolerance);
            Assert.Equal(FirstPageMeasure / 3, FramesOf(container, "c3")[0].Width, Tolerance);

            var landed = FramesOf(container, "c9");
            Assert.Single(landed);
            Assert.Equal(LaterPageMeasure / 3, landed.Values.Single().Width, Tolerance);
        }

        [Fact]
        public async Task FixedColumns_AreTheSameOnEveryPage()
        {
            var container = await BuildAsync(Head + $$"""
                #g { display: grid; grid-template-columns: 100pt 100pt 100pt; }
                </style></head><body><div id="g">{{Cells(12)}}</div></body></html>
                """);

            foreach (var id in new[] { "c0", "c9" })
                foreach (var frame in FramesOf(container, id).Values)
                    Assert.Equal(100, frame.Width, Tolerance);
        }

        [Fact]
        public async Task AGridOnASinglePage_IsUnchanged()
        {
            var container = await BuildAsync(Head + $$"""
                #g { display: grid; grid-template-columns: 1fr 1fr 1fr; }
                </style></head><body><div id="g">{{Cells(3)}}</div></body></html>
                """);

            Assert.Equal(FirstPageMeasure / 3, FramesOf(container, "c0")[0].Width, Tolerance);
            Assert.Equal(FirstPageMeasure, FramesOf(container, "g")[0].Width, Tolerance);
        }

        [Fact]
        public async Task ItemsThatStraddlePages_LoseAndRepeatNoWords()
        {
            string Marked(int cell, int count) => string.Join(' ', Enumerable.Range(0, count).Select(n => $"w{cell}_{n}"));
            var html = Head + $$"""
                @page { size: 612pt 300pt; }
                #g { display: grid; grid-template-columns: 1fr 1fr; }
                </style></head><body><div id="g"><div class="i">{{Marked(1, 400)}}</div><div class="i">{{Marked(2, 400)}}</div><div class="i">{{Marked(3, 400)}}</div></div></body></html>
                """;

            var (painted, pages) = await PeachBrackets.Engine.Tests.TestSupport.PaintedWords.LayOutAndPaintAsync(html);

            Assert.True(pages >= 3, $"the fixture spans {pages} pages");
            Assert.Equal(PeachBrackets.Engine.Tests.TestSupport.PaintedWords.InMarkup(html), painted);
        }
    }
}
