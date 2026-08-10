using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The empirical verification the implementation plan for css-gcpm-3's <c>content: element()</c>
    /// calls for before trusting <see cref="RunningElementLayout.LayoutRunningElementFor"/>'s destructive-
    /// reuse design (re-laying the same shared <see cref="CssBox"/> out repeatedly, once per page,
    /// immediately followed by the caller capturing the result): does a second, narrower re-layout of the
    /// same box actually re-wrap for real, or does it silently keep the first call's line boxes? This is
    /// the test a <c>CssProxyBox</c>-style translate-only implementation (or a caller that forgot to reset
    /// rectangles before repeating) would fail.
    /// </summary>
    public class RunningElementReLayoutIntegrationTests
    {
        private const string LongText =
            "The quick brown fox jumps over the lazy dog while pack my box with five dozen liquor jugs " +
            "and the five boxing wizards jump quickly over the sphinx of black quartz.";

        [Fact]
        public async Task SameRunningBox_ReLaidOutAtDifferentWidths_ProducesDifferentLineWrap()
        {
            var html = Wrap($"<div id='running' style='position:running(heading); font-size:12pt;'>{LongText}</div>");

            int narrowLineCount = 0, wideLineCount = 0;
            double narrowHeight = 0, wideHeight = 0;

            await LayoutAsync(html, after: async (root, container, g) =>
            {
                var runningBox = FindById(root, "running")!;

                var narrowRect = new RRect(50, 50, 80, 500);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, narrowRect, container);
                narrowLineCount = runningBox.LineBoxes.Count;
                narrowHeight = runningBox.ActualBottom - runningBox.Location.Y;

                var wideRect = new RRect(50, 50, 400, 500);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, wideRect, container);
                wideLineCount = runningBox.LineBoxes.Count;
                wideHeight = runningBox.ActualBottom - runningBox.Location.Y;
            });

            // Proof of genuine re-layout, not stale reuse of the first (narrow) call's line boxes: the
            // narrow rect must wrap onto strictly more lines, and stand strictly taller, than the wide one.
            Assert.True(narrowLineCount > wideLineCount,
                $"expected narrow ({narrowLineCount} lines) > wide ({wideLineCount} lines)");
            Assert.True(narrowHeight > wideHeight,
                $"expected narrow height ({narrowHeight}) > wide height ({wideHeight})");
        }

        [Fact]
        public async Task SameRunningBox_ReLaidOutAtDifferentWidths_WidensRunningWidthEachTime()
        {
            var html = Wrap($"<div id='running' style='position:running(heading); font-size:12pt;'>{LongText}</div>");

            double narrowWidth = 0, wideWidth = 0;

            await LayoutAsync(html, after: async (root, container, g) =>
            {
                var runningBox = FindById(root, "running")!;

                var narrowRect = new RRect(50, 50, 80, 500);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, narrowRect, container);
                narrowWidth = runningBox.ActualRight - runningBox.Location.X;

                var wideRect = new RRect(50, 50, 400, 500);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, wideRect, container);
                wideWidth = runningBox.ActualRight - runningBox.Location.X;
            });

            Assert.InRange(narrowWidth, 70, 90);
            Assert.InRange(wideWidth, 390, 410);
        }

        [Fact]
        public async Task RunningBox_RestoresRealParentAfterIsolatedLayout()
        {
            var html = Wrap(@"
                <div id='parent'>
                    <div id='running' style='position:running(heading);'>Heading</div>
                </div>");

            await LayoutAsync(html, after: async (root, container, g) =>
            {
                var parent = FindById(root, "parent")!;
                var runningBox = FindById(root, "running")!;

                var rect = new RRect(50, 50, 200, 100);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, rect, container);

                Assert.Same(parent, runningBox.ParentBox);
                Assert.Contains(runningBox, parent.Boxes);
            });
        }

        [Fact]
        public async Task RunningBox_WithNestedInlineFormatting_ReWrapsChildrenToo()
        {
            // Guards the recursive-reset decision in RunningElementLayout: a nested <span> is itself an
            // inline descendant re-flowed as part of the running box's own line boxes, not a separate
            // block child with its own prologue - this proves the common (inline-content) case re-wraps
            // correctly, which BlockChildReWrapsAtNarrowerWidth below extends to nested block content.
            var html = Wrap(
                "<div id='running' style='position:running(heading); font-size:12pt;'>" +
                $"{LongText} <span style='color:red;'>{LongText}</span></div>");

            int narrowLineCount = 0, wideLineCount = 0;

            await LayoutAsync(html, after: async (root, container, g) =>
            {
                var runningBox = FindById(root, "running")!;

                var narrowRect = new RRect(50, 50, 80, 800);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, narrowRect, container);
                narrowLineCount = runningBox.LineBoxes.Count;

                var wideRect = new RRect(50, 50, 600, 800);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, wideRect, container);
                wideLineCount = runningBox.LineBoxes.Count;
            });

            Assert.True(narrowLineCount > wideLineCount,
                $"expected narrow ({narrowLineCount} lines) > wide ({wideLineCount} lines)");
        }

        [Fact]
        public async Task RunningBox_WithNestedBlockChild_BlockChildReWrapsAtNarrowerWidth()
        {
            // The theoretical gap ResetRectanglesRecursively exists to close: a nested BLOCK child has its
            // own once-per-generation prologue guard, independent of the running box's own. If only the
            // running box's own RectanglesReset ran, this nested child's stale (wide-rect) line boxes
            // would survive into the narrow-rect pass.
            var html = Wrap(
                "<div id='running' style='position:running(heading);'>" +
                $"<div id='nested' style='font-size:12pt;'>{LongText}</div></div>");

            int narrowNestedLines = 0, wideNestedLines = 0;

            await LayoutAsync(html, after: async (root, container, g) =>
            {
                var runningBox = FindById(root, "running")!;

                var wideRect = new RRect(50, 50, 400, 800);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, wideRect, container);
                var nestedAfterWide = Descendants(runningBox).First(b => b.HtmlTag?.TryGetAttribute("id") == "nested");
                wideNestedLines = nestedAfterWide.LineBoxes.Count;

                var narrowRect = new RRect(50, 50, 80, 800);
                await RunningElementLayout.LayoutRunningElementFor(g, runningBox, narrowRect, container);
                var nestedAfterNarrow = Descendants(runningBox).First(b => b.HtmlTag?.TryGetAttribute("id") == "nested");
                narrowNestedLines = nestedAfterNarrow.LineBoxes.Count;
            });

            Assert.True(narrowNestedLines > wideNestedLines,
                $"expected narrow ({narrowNestedLines} lines) > wide ({wideNestedLines} lines) for the nested block child");
        }
    }
}
