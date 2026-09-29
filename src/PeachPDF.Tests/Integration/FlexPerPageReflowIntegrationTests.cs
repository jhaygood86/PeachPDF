using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A block-level flex container that continues across pages of different widths follows
    /// <see href="https://www.w3.org/TR/css-break-3/#varying-size-boxes">css-break-3 §5.1</see>: each
    /// fragment recalculates its sizes and positions using its own fragmentainer's size. The container's
    /// frame, and the sizes of the items on each page - whether their line starts there or continues
    /// there from an earlier page - track the page they are on. Asserted on the fragment tree (the
    /// layout/paint contract) rather than on PDF content-stream substrings.
    /// </summary>
    /// <remarks>
    /// Every fixture is a 612pt-wide sheet with <c>@page { margin: 20pt 50pt }</c> and no left margin on
    /// the first page, so page 0's content is 562pt wide (its left edge is at 0) and every later page's is
    /// 512pt wide. The frames asserted were compared against Prince, which lays these out the same way;
    /// Chromium keeps every item at its first page's width, and lets the items overflow the narrower
    /// container.
    /// </remarks>
    public class FlexPerPageReflowIntegrationTests
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

        private static string Words(int count, string prefix) =>
            string.Join(' ', Enumerable.Range(0, count).Select(i => $"{prefix}{i}"));

        private static async Task<HtmlContainerInt> BuildAsync(string html, double sheetHeight = 300)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new Size(
                612 - container.MarginLeft - container.MarginRight,
                sheetHeight - container.MarginTop - container.MarginBottom);
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new Size(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
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

        private static int WordsIn(BoxFragment fragment) => fragment.Words.Count + fragment.Children.Sum(WordsIn);

        // ─── the container's own frame ────────────────────────────────────────────

        [Fact]
        public async Task Container_TracksTheMeasureOfEachPageItContinuesOnto()
        {
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; } .i { flex: 1 1 0; }
                </style></head><body><div id="f"><div class="i">{{Words(600, "a")}}</div><div class="i">{{Words(600, "b")}}</div></div></body></html>
                """);

            var frames = FramesOf(container, "f");

            Assert.True(frames.Count >= 3, "the fixture must span at least three pages");
            Assert.Equal(FirstPageMeasure, frames[0].Width, Tolerance);

            foreach (var (page, frame) in frames.Where(f => f.Key > 0))
                Assert.True(Math.Abs(frame.Width - LaterPageMeasure) < Tolerance, $"page {page}: {frame.Width}");
        }

        [Fact]
        public async Task Container_LinesPushedOntoLaterPagesGetThatPagesFrame()
        {
            var items = string.Concat(Enumerable.Range(0, 9).Select(n =>
                $"<div class=\"i\" style=\"height: 150pt; flex: 1 0 170pt; break-inside: avoid\">g{n}</div>"));
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; flex-wrap: wrap; }
                </style></head><body><div id="f">{{items}}</div></body></html>
                """);

            var frames = FramesOf(container, "f");

            Assert.True(frames.Count >= 3);
            Assert.Equal(FirstPageMeasure, frames[0].Width, Tolerance);
            Assert.All(frames.Where(f => f.Key > 0), f => Assert.Equal(LaterPageMeasure, f.Value.Width, Tolerance));
        }

        [Theory]
        [InlineData("width: 400pt")]
        [InlineData("display: inline-flex")]
        public async Task Container_WithAnExplicitOrShrinkWrappedSize_KeepsOnePageInvariantFrame(string style)
        {
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; {{style}}; } .i { flex: 0 0 auto; width: 100pt; }
                </style></head><body><div id="f"><div class="i">{{Words(400, "a")}}</div></div></body></html>
                """);

            var frames = FramesOf(container, "f");

            Assert.True(frames.Count >= 2);
            Assert.All(frames, f => Assert.Equal(frames[0].Width, f.Value.Width, Tolerance));
        }

        [Fact]
        public async Task Container_InADocumentWithNoPerPageMeasure_IsUntouched()
        {
            var container = await BuildAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 20pt 50pt; }
                body { margin: 0; font: 12pt Arial; }
                #f { display: flex; } .i { flex: 1 1 0; }
                </style></head><body><div id="f"><div class="i" id="a">{{Words(300, "a")}}</div><div class="i" id="b">{{Words(300, "b")}}</div></div></body></html>
                """);

            foreach (var id in new[] { "f", "a", "b" })
            {
                var frames = FramesOf(container, id);

                Assert.True(frames.Count >= 2);
                Assert.All(frames, f => Assert.Equal(frames[0].Width, f.Value.Width, Tolerance));
                Assert.All(frames, f => Assert.Equal(frames[0].X, f.Value.X, Tolerance));
            }
        }

        // ─── items of a line that starts on a page of another measure ─────────────

        [Fact]
        public async Task WrappedRow_LinesOnLaterPagesAreCollectedAndSizedAgainstThatPagesMeasure()
        {
            var items = string.Concat(Enumerable.Range(0, 9).Select(n =>
                $"<div class=\"i\" id=\"g{n}\" style=\"height: 150pt; flex: 1 0 170pt; break-inside: avoid\">g{n}</div>"));
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; flex-wrap: wrap; }
                </style></head><body><div id="f">{{items}}</div></body></html>
                """);

            // Page 0 is 562 wide: three items of 170pt fit a line. A later page is 512 wide: only two do,
            // and they grow to fill it.
            for (var n = 0; n < 3; n++)
                Assert.Equal(FirstPageMeasure / 3, FramesOf(container, $"g{n}")[0].Width, Tolerance);

            var laterItems = Enumerable.Range(3, 6).Select(n => FramesOf(container, $"g{n}").Single().Value).ToList();

            Assert.All(laterItems, frame => Assert.Equal(LaterPageMeasure / 2, frame.Width, Tolerance));

            for (var i = 0; i < laterItems.Count; i += 2)
                Assert.Equal(laterItems[i].Width + laterItems[i + 1].Width, LaterPageMeasure, Tolerance);
        }

        [Fact]
        public async Task WrapReverse_LineLandingOnALaterPageIsSizedAgainstThatPagesMeasure()
        {
            var items = string.Concat(Enumerable.Range(0, 7).Select(n =>
                $"<div class=\"i\" id=\"g{n}\" style=\"height: 120pt; flex: 1 0 170pt; break-inside: avoid\">g{n}</div>"));
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; flex-wrap: wrap-reverse; }
                </style></head><body><div id="f">{{items}}</div></body></html>
                """);

            // wrap-reverse puts the first line last: g0 and g1 land on the second page, which is 512 wide,
            // so the two of them share it.
            var g0 = FramesOf(container, "g0").Single(f => f.Key > 0).Value;
            var g1 = FramesOf(container, "g1").Single(f => f.Key > 0).Value;

            Assert.Equal(LaterPageMeasure / 2, g0.Width, Tolerance);
            Assert.Equal(LaterPageMeasure / 2, g1.Width, Tolerance);
            Assert.Equal(g0.Right, g1.Left, Tolerance);
        }

        // ─── items of a line that continues onto a page of another measure ────────

        [Fact]
        public async Task StraddlingRow_ItemsAreResizedToEachPagesMeasure_AndKeepTheirEarlierFrames()
        {
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; } .i { flex: 1 1 0; }
                </style></head><body><div id="f"><div class="i" id="a">{{Words(300, "a")}}</div><div class="i" id="b">{{Words(300, "b")}}</div><div class="i" id="c">{{Words(150, "c")}}</div></div></body></html>
                """);

            var a = FramesOf(container, "a");
            var b = FramesOf(container, "b");
            var c = FramesOf(container, "c");

            Assert.True(a.Count >= 3);

            // The first page: three equal items filling 562pt.
            Assert.Equal(FirstPageMeasure / 3, a[0].Width, Tolerance);
            Assert.Equal(FirstPageMeasure / 3, b[0].Width, Tolerance);
            Assert.Equal(FirstPageMeasure / 3, c[0].Width, Tolerance);
            Assert.Equal(a[0].Right, b[0].Left, Tolerance);

            // Every later page: the same three items, refitted to 512pt, side by side with no overflow.
            foreach (var page in a.Keys.Where(page => page > 0))
            {
                Assert.Equal(LaterPageMeasure / 3, a[page].Width, Tolerance);
                Assert.Equal(LaterPageMeasure / 3, b[page].Width, Tolerance);
                Assert.Equal(LaterPageMeasure / 3, c[page].Width, Tolerance);
                Assert.Equal(a[page].Right, b[page].Left, Tolerance);
                Assert.Equal(b[page].Right, c[page].Left, Tolerance);
                Assert.Equal(a[1].Left, a[page].Left, Tolerance);
            }

            // The container's frame and its items' agree on every page.
            var f = FramesOf(container, "f");

            foreach (var page in a.Keys)
                Assert.Equal(f[page].Right, c[page].Right, Tolerance);
        }

        [Fact]
        public async Task StraddlingRow_EveryWordIsDrawnOnce_AndTheContentThatFollowsIsBelowTheItems()
        {
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; } .i { flex: 1 1 0; }
                </style></head><body><div id="f"><div class="i" id="a">{{Words(300, "a")}}</div><div class="i" id="b">{{Words(300, "b")}}</div><div class="i" id="c">{{Words(150, "c")}}</div></div><p id="after">after</p></body></html>
                """);

            var tree = container.FragmentTree!;
            var total = tree.Fragmentainers.Sum(fragmentainer => WordsIn(fragmentainer.Root));

            Assert.Equal(300 + 300 + 150 + 1, total);

            var f = ById(container, "f");
            var after = ById(container, "after");

            Assert.True(after.Location.Y >= f.ActualBottom - 0.5, $"{after.Location.Y} < {f.ActualBottom}");

            foreach (var id in new[] { "a", "b", "c" })
                Assert.True(ById(container, id).ActualBottom <= f.ActualBottom + 0.5);
        }

        [Fact]
        public async Task StraddlingRow_BlocksInsideAnItemFollowItsFrameOnEachPage()
        {
            string Item(string id) =>
                $"<div class=\"i\" id=\"{id}\"><p id=\"{id}a\">{Words(200, "a")}</p><p id=\"{id}b\">{Words(200, "b")}</p></div>";

            var container = await BuildAsync(Head + $$"""
                #f { display: flex; } .i { flex: 1 1 0; } p { margin: 2pt; }
                </style></head><body><div id="f">{{Item("x")}}{{Item("y")}}{{Item("z")}}</div></body></html>
                """);

            var xa = FramesOf(container, "xa");
            var xb = FramesOf(container, "xb");
            var x = FramesOf(container, "x");

            Assert.True(x.Count >= 3);

            // Each block is as wide as its item's content box on every page it is drawn on: the item's
            // width less its border and the block's own margins.
            foreach (var frames in new[] { xa, xb })
            {
                foreach (var (page, frame) in frames)
                {
                    Assert.Equal(x[page].Width - 2 * 1 - 2 * 2, frame.Width, Tolerance);
                    Assert.Equal(x[page].Left + 1 + 2, frame.Left, Tolerance);
                }
            }
        }

        [Fact]
        public async Task DefiniteHeightWrappedRow_ItemsNoPassResumesAreStatedForTheLaterPage()
        {
            var items = string.Concat(Enumerable.Range(0, 9).Select(n =>
                $"<div class=\"i\" id=\"g{n}\" style=\"height: 100pt; flex: 1 0 170pt\">g{n}</div>"));
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; flex-wrap: wrap; height: 700pt; }
                </style></head><body><div id="f">{{items}}</div></body></html>
                """);

            // Nothing in these items has a line box to break at, so no pass ever resumes them: they are
            // drawn once and cut by the page edge, and their frame on a later page is the line as that page
            // would size it. Three of them fit a 512pt page only by overflowing it (the basis is 170pt and
            // the items cannot shrink), so each keeps its basis instead of the first page's 187pt.
            var widths = Enumerable.Range(0, 9)
                .SelectMany(n => FramesOf(container, $"g{n}").Where(f => f.Key > 0).Select(f => f.Value.Width))
                .ToList();

            Assert.NotEmpty(widths);
            Assert.All(widths, width => Assert.True(width < FirstPageMeasure / 3 - 5, $"{width} was sized for the first page"));
        }

        // ─── the machinery ────────────────────────────────────────────────────────

        [Fact]
        public async Task PinnedItemSize_IsRemembered_AndCanBePutBackToWhatTheAuthorWrote()
        {
            var container = await BuildAsync(Head + $$"""
                #f { display: flex; } .i { flex: 1 1 0; }
                </style></head><body><div id="f"><div class="i" id="a" style="width: 100pt">{{Words(20, "a")}}</div></div></body></html>
                """);

            var item = ById(container, "a");

            // The commit pass pinned the item's size to what layout decided.
            Assert.True(item.ItemContentSizeEverPinned);
            Assert.Equal("100pt", item.WidthBeforeItemPin);

            item.Width = "37.0000pt";
            ItemContentCommit.UnpinIfPinned(item);

            Assert.Equal("100pt", item.Width);
        }

        [Fact]
        public async Task UnpinIfPinned_LeavesAnItemNoCommitEverPinnedAlone()
        {
            var container = await BuildAsync(Head + """
                </style></head><body><div id="a" style="width: 12pt">x</div></body></html>
                """);

            var box = ById(container, "a");

            ItemContentCommit.UnpinIfPinned(box);

            Assert.Equal("12pt", box.Width);
        }
    }
}
