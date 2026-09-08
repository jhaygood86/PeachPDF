using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
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
