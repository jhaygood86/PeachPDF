using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #473: <c>DomParser.IsAtomicInlineLevel</c> used to name only <c>inline-flex</c> as an atomic
    /// inline-level box with a layout path of its own, so <c>CorrectBlockInsideInline</c> incorrectly
    /// hoisted/split a block-level descendant out of an <c>inline-block</c>/<c>inline-table</c>/
    /// <c>inline-grid</c> box (CSS Display 3 §2.3). Closing that alone would have made things worse (the
    /// generic recursive <c>CssLayoutEngine.FlowBox</c> path places nothing at all for a block-level
    /// child) - <c>CssLayoutEngine.FlowAtomicBlockContentChild</c> is the new atomic-placement branch that
    /// makes closing it actually render real content. This is also what makes issue #18's real-world
    /// document - a <c>&lt;table style="display:inline-block"&gt;</c> used to keep a narrow info block from
    /// stretching full-width - render its rows at all.
    /// </summary>
    public class AtomicInlineLevelBlockContentIntegrationTests
    {
        [Fact]
        public async Task InlineBlockTable_ThreeByThreeRows_AllRowsRenderAtDistinctIncreasingPositions()
        {
            // The issue #18/#473 shape: a table reset to display:inline-block (a common trick to keep its
            // width to its content rather than stretching to fill the containing block) holding several
            // rows of label/separator/value cells.
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <div style="border:1px solid grey; width:400pt;">
                <table style="display:inline-block; width:90%">
                <tr><td>PDF</td><td>:</td><td>TestPrint</td></tr>
                <tr><td>Number</td><td>:</td><td>OneTwoThree</td></tr>
                <tr><td>Date</td><td>:</td><td>TwentySix</td></tr>
                </table>
                </div>
                </body></html>
                """);

            var words = CollectWords(root);
            Assert.Contains("PDF", words);
            Assert.Contains("Number", words);
            Assert.Contains("Date", words);
            Assert.Contains("TestPrint", words);
            Assert.Contains("OneTwoThree", words);
            Assert.Contains("TwentySix", words);

            var pdfWord = FindWord(root, "PDF");
            var numberWord = FindWord(root, "Number");
            var dateWord = FindWord(root, "Date");

            Assert.True(numberWord.Top > pdfWord.Top,
                $"row 2 (Number, top={numberWord.Top}) must sit below row 1 (PDF, top={pdfWord.Top}), not collapse onto it");
            Assert.True(dateWord.Top > numberWord.Top,
                $"row 3 (Date, top={dateWord.Top}) must sit below row 2 (Number, top={numberWord.Top})");
        }

        [Theory]
        [InlineData("inline-block")]
        [InlineData("inline-grid")]
        public async Task AtomicInlineLevelBox_WithBlockLevelChildren_LaysBothInsideRealContainerHeight(string display)
        {
            // The accepted-gap doc's own measurement case: a fixed-width atomic inline-level box holding a
            // 10pt and a 40pt block child. Before this fix, the first child was hoisted out (display:
            // inline-flex) or nothing was placed at all (the other three) - either way the container's own
            // height did not reflect its real content.
            var (root, _) = await BuildAndLayout($"""
                <!DOCTYPE html><html><body>
                <div id="outer" style="display:{display}; width:200pt; grid-template-columns: 1fr 1fr;">
                <div id="a" style="width:10pt;height:10pt;">A</div>
                <div id="b" style="width:40pt;height:10pt;">B</div>
                </div>
                </body></html>
                """);

            var outer = FindById(root, "outer")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.True(outer.ActualBottom - outer.Location.Y > 0,
                $"'{display}' container height must reflect its real content, not stay at 0 (Location.Y={outer.Location.Y}, ActualBottom={outer.ActualBottom})");
            Assert.True(a.ActualBottom <= outer.ActualBottom + 0.5,
                $"item A (bottom={a.ActualBottom}) must be laid out inside the '{display}' container (bottom={outer.ActualBottom}), not hoisted out above it");
            Assert.True(b.ActualBottom <= outer.ActualBottom + 0.5,
                $"item B (bottom={b.ActualBottom}) must be laid out inside the '{display}' container (bottom={outer.ActualBottom}), not hoisted out above it");
            Assert.Contains("A", CollectWords(root));
            Assert.Contains("B", CollectWords(root));
        }

        [Fact]
        public async Task InlineGrid_InlineWithinAParagraph_BothItemsRenderSideBySide()
        {
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <p>before <span style="display:inline-grid; grid-template-columns: 1fr 1fr; width:120pt;">
                <div id="a">A</div><div id="b">B</div>
                </span> after</p>
                </body></html>
                """);

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Contains("before", CollectWords(root));
            Assert.Contains("after", CollectWords(root));
            Assert.True(b.Location.X > a.Location.X,
                $"grid items should lay out left-to-right in a 2-column track, but a.X={a.Location.X} b.X={b.Location.X}");
            Assert.Equal(a.Location.Y, b.Location.Y, 3);
        }

        [Fact]
        public async Task InlineBlock_WithOnlyInlineContent_StillFlowsOnTheSameLineAsItsSiblings()
        {
            // Regression guard: the ordinary, already-working inline-block case (its own content is purely
            // inline - text/inline elements) must keep using the recursive FlowBox path, unaffected by the
            // new atomic branch.
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <p>before <span style="display:inline-block;border:1px solid black;">hello <b>world</b></span> after</p>
                </body></html>
                """);

            var beforeWord = FindWord(root, "before");
            var helloWord = FindWord(root, "hello");
            var worldWord = FindWord(root, "world");
            var afterWord = FindWord(root, "after");

            Assert.True(System.Math.Abs(helloWord.Top - beforeWord.Top) < 1,
                "an inline-block with only inline content must stay on the same line as its siblings");
            Assert.True(beforeWord.Left < helloWord.Left);
            Assert.True(helloWord.Left < worldWord.Left);
            Assert.True(worldWord.Left < afterWord.Left);
        }

        [Fact]
        public async Task InlineBlockWithBlockThenText_GivesTrailingInlineRunItsOwnAnonymousBlock()
        {
            // The outer block's only child is itself inline-level. Both DOM-normalization passes used to
            // stop at that all-inline boundary, so they never normalized the inline-block's independent
            // formatting context: the title block rendered, while the bare trailing text was dispatched
            // as a block child, received zero width/no line boxes, and disappeared.
            var (root, container) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <div id="card" style="display:inline-block;width:120pt;padding:10pt">
                  <b id="title" style="display:block">Title</b>Description text must render below the title.
                </div>
                </body></html>
                """);

            var card = FindById(root, "card")!;
            var title = FindById(root, "title")!;
            var description = FindWord(root, "Description");
            var anonymousRun = Assert.Single(card.Boxes, box => box.IsInlineRunWrapper);

            Assert.Contains(description, anonymousRun.Boxes.SelectMany(box => box.Words));
            Assert.True(description.Top >= title.ActualBottom,
                $"description top ({description.Top}) must follow the title block ({title.ActualBottom})");

            var graphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, graphics);
            Assert.Contains(graphics.DrawStringCalls, call => call.Text == "Description");
        }

        [Fact]
        public async Task FixedWidthAtomicInlineBlocks_UseContentBoxWidthAndWrapAsWholeBoxes()
        {
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body style="margin:0">
                <div id="row" style="width:220pt"><span id="a" style="display:inline-block;width:80pt;padding:10pt;margin:0 5pt 12pt 0;vertical-align:top"><b style="display:block">A</b>body</span><span id="b" style="display:inline-block;width:80pt;padding:10pt;margin:0 5pt 12pt 0;vertical-align:top"><b style="display:block">B</b>body</span><span id="c" style="display:inline-block;width:80pt;padding:10pt;margin:0 5pt 12pt 0;vertical-align:top"><b style="display:block">C</b>body</span></div>
                </body></html>
                """);

            var row = FindById(root, "row")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;

            Assert.Equal(100, a.ActualBoxSizingWidth, 3);
            Assert.Equal(100, b.ActualBoxSizingWidth, 3);
            Assert.Equal(100, c.ActualBoxSizingWidth, 3);
            Assert.Equal(a.Location.Y, b.Location.Y, 3);
            Assert.True(c.Location.Y > a.Location.Y,
                $"third 105pt margin box must wrap below the first two (a.Y={a.Location.Y}, c.Y={c.Location.Y})");
            Assert.Equal(a.Location.X, c.Location.X, 3);
            Assert.True(c.Location.Y >= a.ActualBottom + a.ActualMarginBottom - 0.01,
                $"wrapped row must start below the previous row's bottom margin (a.Bottom={a.ActualBottom}, margin={a.ActualMarginBottom}, c.Y={c.Location.Y})");
            Assert.True(c.Location.X + c.ActualBoxSizingWidth + c.ActualMarginRight <= row.ClientRight + 0.01,
                "wrapped atomic inline must stay inside the containing block");
        }

        [Fact]
        public async Task AtomicInlineWrapAtLineClamp_StopsBeforeLayingOutTheHiddenBox()
        {
            var (root, container) = await BuildAndLayout("""
                <!DOCTYPE html><html><body style="margin:0">
                <div id="row" style="width:140pt;line-clamp:1">visible words <span id="card" style="display:inline-block;width:100pt"><b style="display:block">HiddenCardTitle</b></span></div>
                </body></html>
                """);

            var row = FindById(root, "row")!;
            var card = FindById(root, "card")!;

            Assert.Single(row.LineBoxes);
            Assert.Empty(card.LineBoxes);

            var graphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, graphics);
            Assert.DoesNotContain(graphics.DrawStringCalls, call => call.Text.Contains("HiddenCardTitle"));
        }

        [Fact]
        public async Task InlineBlockWithAutoWidth_RespectsItsOwnMinWidthFloor()
        {
            // CSS2.1 §10.3.9/§10.4: shrink-to-fit's own result is still floored by min-width. A one-cell
            // table's natural content width is far under 150pt - and a block-level descendant (the table's
            // own row) is what routes this box through FlowAtomicBlockContentChild's shrink-to-fit path at
            // all (see HasBlockLevelDescendant).
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table id="outer" style="display:inline-block; min-width:150pt; border:1px solid black;">
                <tr><td>A</td></tr>
                </table>
                </body></html>
                """);

            var outer = FindById(root, "outer")!;
            var width = outer.ActualRight - outer.Location.X;

            Assert.True(width >= 150, $"min-width:150pt must floor the shrink-to-fit width, but got {width}pt");
        }

        [Fact]
        public async Task InlineBlockTable_WithStringSet_RegistersItsNamedString()
        {
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table id="tbl" style="display:inline-block; string-set: label 'Info Block';">
                <tr><td>CELL</td></tr>
                </table>
                </body></html>
                """);

            var outerTable = FindById(root, "tbl")!;

            Assert.True(outerTable.NamedStrings.ContainsKey("label"),
                "string-set on an atomic inline-level box must still register its named string when routed through the new atomic-placement path");
            Assert.Equal("Info Block", outerTable.NamedStrings["label"].Value);
        }

        [Fact]
        public async Task InlineBlockTable_SingleCell_SurvivesAndIsWrappedInAnonymousInlineTable()
        {
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="display:inline-block"><tr><td>CELL</td></tr></table>
                </body></html>
                """);

            Assert.Contains("CELL", CollectWords(root));
            Assert.Contains(EnumerateBoxes(root),
                b => b.HtmlTag is null && b.Display.Value is DisplayMode.InlineTable);
        }

        [Fact]
        public async Task InlineBlockTable_CellPercentageWidth_ResolvesAgainstTheTableNotTheOuterAncestor()
        {
            // Issue #18: CssBox.ContainingBlock used to walk PAST a non-IsBlock atomic inline-level box
            // (inline-block/inline-table aren't IsBlock) straight to the next real block ancestor, so a
            // percentage width on a cell inside a <table style="display:inline-block"> resolved against
            // that wider ancestor instead of the table's own (narrower) width - visibly overflowing the
            // table's own border. A 500pt outer div with a 40%-wide table and a 50%-wide cell should give
            // the cell ~100pt (50% of 200pt), not ~250pt (50% of the 500pt div).
            var (root, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <div style="width:500pt;">
                <table id="tbl" style="display:inline-block; width:40%">
                <tr><td id="cell" style="width:50%">A</td><td>B</td></tr>
                </table>
                </div>
                </body></html>
                """);

            var cell = FindById(root, "cell")!;
            var cellWidth = cell.ActualRight - cell.Location.X;

            Assert.True(cellWidth < 150,
                $"the cell's 50% width must resolve against the table's own ~200pt width (giving ~100pt), not the 500pt outer div (which would give ~250pt) - got {cellWidth}pt");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static IEnumerable<CssBox> EnumerateBoxes(CssBox box)
        {
            yield return box;
            foreach (var child in box.Boxes)
                foreach (var descendant in EnumerateBoxes(child))
                    yield return descendant;
        }

        private static List<string?> CollectWords(CssBox box) =>
            EnumerateBoxes(box).SelectMany(b => b.Words.Select(w => w.Text)).ToList();

        private static CssRect FindWord(CssBox box, string text)
        {
            foreach (var b in EnumerateBoxes(box))
            {
                var word = b.Words.FirstOrDefault(w => w.Text == text);
                if (word is not null) return word;
            }

            throw new Xunit.Sdk.XunitException($"No word '{text}' found in the box tree.");
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}
