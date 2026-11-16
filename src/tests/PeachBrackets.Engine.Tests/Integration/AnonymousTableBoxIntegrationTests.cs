using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.PDF.Engine.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Coverage for CSS2.1 §17.2.1 anonymous table-object generation (<c>DomParser.CorrectAnonymousTablesGenerateMissingChildWrappers</c>)
    /// against exactly the shape the real Acid2 test's table line exercises: a `display:table` parent
    /// whose non-row children have mixed display values (already `table-cell`, a nested `display:table`,
    /// and a default block/list-item), which must all land in a single synthesized anonymous
    /// `table-row`, with only the non-cell children individually cell-wrapped. This combination had no
    /// prior coverage - two real bugs survived here: rules 2.1/2.2 only wrapped the single box being
    /// visited instead of grouping the whole consecutive run of non-proper-table-child siblings (so
    /// four rows were created instead of one), and rule 2.3's sibling-grouping predicate was inverted.
    /// </summary>
    public class AnonymousTableBoxIntegrationTests
    {
        [Fact]
        public async Task MixedNonRowChildren_AllGroupIntoOneAnonymousRow_WithOnlyNonCellsWrapped()
        {
            var html = """
                <!DOCTYPE html><html><head></head><body>
                <ul style="display:table">
                  <li class="first" style="display:table-cell"></li>
                  <li class="second" style="display:table"></li>
                  <li class="third" style="display:table-cell"></li>
                  <li class="fourth"></li>
                </ul>
                </body></html>
                """;

            var (root, _) = await BuildAndLayout(html);

            var ul = FindByTag(root, "ul")!;
            Assert.Single(ul.Boxes);

            var row = ul.Boxes[0];
            Assert.Equal(DisplayMode.TableRow, row.Display.Value);
            Assert.Equal(4, row.Boxes.Count);

            var first = FindByClass(root, "first")!;
            var second = FindByClass(root, "second")!;
            var third = FindByClass(root, "third")!;
            var fourth = FindByClass(root, "fourth")!;

            // Already-cell children stay direct children of the row (not re-wrapped).
            Assert.Same(row, first.ParentBox);
            Assert.Same(row, third.ParentBox);

            // Non-cell children each get their own anonymous table-cell wrapper as a direct row child.
            Assert.NotSame(row, second.ParentBox);
            Assert.Equal(DisplayMode.TableCell, second.ParentBox!.Display.Value);
            Assert.Same(row, second.ParentBox!.ParentBox);
            Assert.Single(second.ParentBox!.Boxes);

            Assert.NotSame(row, fourth.ParentBox);
            Assert.Equal(DisplayMode.TableCell, fourth.ParentBox!.Display.Value);
            Assert.Same(row, fourth.ParentBox!.ParentBox);
            Assert.Single(fourth.ParentBox!.Boxes);

            // The nested display:table on "second" establishes its own table, unaffected by the
            // anonymous-cell wrapper around it.
            Assert.Equal(DisplayMode.Table, second.Display.Value);

            // Row order must match document order.
            Assert.Equal(
                new[] { first, second.ParentBox, third, fourth.ParentBox },
                row.Boxes);
        }

        [Fact]
        public async Task RowGroupWithNonRowChildren_GroupsConsecutiveSiblingsIntoOneAnonymousRow()
        {
            // Rule 2.2: a row-group box's non-`table-row` children must group into one anonymous
            // `table-row`, stopping at the first already-`table-row` sibling - the same
            // consecutive-sibling-grouping shape as rule 2.1, but for a row-group parent instead of a
            // table parent (had no coverage; only rule 2.1/2.3's identically-shaped fix was tested).
            var html = """
                <!DOCTYPE html><html><head></head><body>
                <div style="display:table">
                  <div class="rowgroup" style="display:table-row-group">
                    <span class="a"></span>
                    <span class="b"></span>
                    <div class="realrow" style="display:table-row"></div>
                  </div>
                </div>
                </body></html>
                """;

            var root = await BuildOnly(html);

            var rowGroup = FindByClass(root, "rowgroup")!;
            Assert.Equal(2, rowGroup.Boxes.Count);

            // The anonymous row rule 2.2 wraps "a"/"b" in immediately cascades into rule 2.3 as well
            // (the new row's own children, "a"/"b", still aren't table-cells) - so the row ends up with
            // a single anonymous table-cell child wrapping both (via a further anonymous block box, the
            // usual inline-content-needs-a-block-wrapper mechanism, unrelated to the table rules under
            // test here), not "a"/"b" as its direct children.
            var anonRow = rowGroup.Boxes[0];
            Assert.Equal(DisplayMode.TableRow, anonRow.Display.Value);
            Assert.Single(anonRow.Boxes);

            var anonCell = anonRow.Boxes[0];
            Assert.Equal(DisplayMode.TableCell, anonCell.Display.Value);

            var a = FindByClass(root, "a")!;
            var b = FindByClass(root, "b")!;
            var realRow = FindByClass(root, "realrow")!;

            Assert.Same(anonCell, a.ParentBox!.ParentBox);
            Assert.Same(anonCell, b.ParentBox!.ParentBox);

            // Document order preserved: the anonymous row takes "a"'s original position, ahead of the
            // real table-row that follows it.
            Assert.Same(rowGroup, realRow.ParentBox);
            Assert.Equal(new[] { anonRow, realRow }, rowGroup.Boxes);
        }

        [Fact]
        public async Task MisparentedTableCell_WrappedInAnonymousRowThenTable_AtItsOriginalDocumentPosition()
        {
            // Two rules fire in sequence (CorrectAnonymousTablesGenerateMissingParents):
            //   3.1 — a `table-cell` whose parent isn't a `table-row` is wrapped in an anonymous
            //         `table-row` (both cells group into one, at cell1's original index);
            //   3.2 — that anonymous `table-row`, now a proper table child under a non-table parent
            //         (the body), is itself misparented and must be wrapped in an anonymous `table`.
            // Both wrappers are positioned via SetBeforeBox at the run's original index among its
            // siblings - not appended at the end, which would silently reorder it after later,
            // unrelated siblings. No `display:table` ancestor here deliberately - rule 3.1 fires purely
            // off "table-cell whose parent isn't table-row", regardless of any table context.
            var html = """
                <!DOCTYPE html><html><head></head><body>
                  <div class="before"></div>
                  <span class="cell1" style="display:table-cell"></span>
                  <span class="cell2" style="display:table-cell"></span>
                  <div class="after"></div>
                </body></html>
                """;

            var root = await BuildOnly(html);

            var body = FindByTag(root, "body")!;
            var before = FindByClass(root, "before")!;
            var after = FindByClass(root, "after")!;
            var cell1 = FindByClass(root, "cell1")!;
            var cell2 = FindByClass(root, "cell2")!;

            // 3.1: both cells share one anonymous table-row.
            var anonRow = cell1.ParentBox!;
            Assert.Equal(DisplayMode.TableRow, anonRow.Display.Value);
            Assert.Same(anonRow, cell2.ParentBox);

            // 3.2: the anonymous row is wrapped in an anonymous table (the table formatting context the
            // stray cells require per CSS2.1 §17.2.1) — without it the row would render outside any table.
            var anonTable = anonRow.ParentBox!;
            Assert.Null(anonTable.HtmlTag);
            Assert.Equal(DisplayMode.Table, anonTable.Display.Value);

            // The synthesized table must sit between "before" and "after" in document order, not after
            // "after" (which is what a naive append-at-the-end would produce).
            Assert.Equal(new[] { before, anonTable, after }, body.Boxes);
        }

        [Fact]
        public async Task MisparentedRowFollowedByRealRowGroup_JoinsTheSameAnonymousTable()
        {
            // CSS2.1 §17.2.1 rule 3.1 wraps the stray cell in an anonymous table-row; rule 3.2 then
            // must see that row and the following, already-proper, row group as *consecutive proper
            // table children* and wrap both into one anonymous table. Running 3.1 and 3.2 interleaved,
            // per child, evaluated 3.2 on the cell before 3.1 had wrapped it into a row - so the row
            // group (whose own turn came later in the same single pass) built its own, separate,
            // anonymous table instead of joining the first one.
            var html = """
                <!DOCTYPE html><html><head></head><body>
                  <span class="cell" style="display:table-cell"></span>
                  <div class="group" style="display:table-row-group">
                    <div class="row" style="display:table-row"></div>
                  </div>
                </body></html>
                """;

            var root = await BuildOnly(html);

            var body = FindByTag(root, "body")!;
            var cell = FindByClass(root, "cell")!;
            var group = FindByClass(root, "group")!;

            var anonRow = cell.ParentBox!;
            Assert.Equal(DisplayMode.TableRow, anonRow.Display.Value);

            var anonTable = anonRow.ParentBox!;
            Assert.Equal(DisplayMode.Table, anonTable.Display.Value);

            // The row group must be a child of the very same anonymous table as the anonymous row,
            // not of a second, separately-synthesized one.
            Assert.Same(anonTable, group.ParentBox);
            Assert.Single(body.Boxes);
            Assert.Equal(new[] { anonRow, group }, anonTable.Boxes);
        }

        [Fact]
        public async Task AnonymousTableStructureBoxes_InheritStyleFromTheirParent()
        {
            // CSS2.1 §6.2: every box, anonymous ones included, inherits the inheritable properties of
            // its parent rather than keeping the initial values - a bare `new CssBox(parent, null)`
            // (as opposed to `CssBox.CreateBox`) left the anonymous row/cell/table at the CSS initial
            // values instead, e.g. painting in the UA default color/font regardless of what the
            // misparented content around it actually specified.
            var html = """
                <!DOCTYPE html><html><head></head><body>
                  <div style="color: rgb(1, 2, 3); font-size: 20pt">
                    <span class="cell" style="display:table-cell"></span>
                  </div>
                </body></html>
                """;

            var root = await BuildOnly(html);
            var cell = FindByClass(root, "cell")!;

            var anonRow = cell.ParentBox!;
            Assert.Equal(DisplayMode.TableRow, anonRow.Display.Value);
            Assert.Equal("rgb(1, 2, 3)", anonRow.Color);

            var anonTable = anonRow.ParentBox!;
            Assert.Equal(DisplayMode.Table, anonTable.Display.Value);
            Assert.Equal("rgb(1, 2, 3)", anonTable.Color);
            Assert.Equal(20.0, anonTable.ActualFont.Size, 3);
        }

        [Fact]
        public async Task WhitespaceBetweenTwoMisparentedCells_IsRemoved_ButWhitespaceNextToOrdinaryInlineContentSurvives()
        {
            // CSS2.1 §17.2.1 rule 1.4: a whitespace-only anonymous inline box is only removed when
            // *both* its immediate siblings are internal table boxes (or 'table-caption') - the
            // pre-existing heuristic instead kept whitespace only when both neighbors were "IsInline",
            // which is false for an unwrapped 'table-cell' span, so it wrongly dropped the space
            // between ordinary text and a misparented cell that is about to become its own anonymous
            // table sibling, while still (by the same wrong test, coincidentally) removing the space
            // rule 1.4 actually requires removed, between the two cells. "wrap" is a block (not
            // inline) parent, so CSS2.1 §9.2.1.1 wraps the ordinary inline runs either side of the
            // resulting block-level anonymous table in their own anonymous block boxes - the kept
            // whitespace rides along as the last/first child of those wrappers, rather than staying a
            // direct child of "wrap" itself.
            var html = """
                <!DOCTYPE html><html><head></head><body>
                  <div class="wrap">
                    <span class="a">a</span>
                    <span class="b" style="display:table-cell">b</span>
                    <span class="c" style="display:table-cell">c</span>
                    <span class="d">d</span>
                  </div>
                </body></html>
                """;

            var root = await BuildOnly(html);

            var wrap = FindByClass(root, "wrap")!;
            var a = FindByClass(root, "a")!;
            var d = FindByClass(root, "d")!;

            // "a" is followed by a real whitespace-only text box, a sibling within its own anonymous
            // block wrapper - it must survive, not be stripped, since "a" isn't an internal table box.
            var aBlock = a.ParentBox!;
            var afterA = aBlock.Boxes[aBlock.Boxes.IndexOf(a) + 1];
            Assert.True(string.IsNullOrWhiteSpace(afterA.Text));

            // Likewise before "d", in its own anonymous block wrapper.
            var dBlock = d.ParentBox!;
            var beforeD = dBlock.Boxes[dBlock.Boxes.IndexOf(d) - 1];
            Assert.True(string.IsNullOrWhiteSpace(beforeD.Text));

            // The anonymous table wrapping "b"/"c" (between the two anonymous block wrappers) has
            // exactly two cells as its row's children - no stray whitespace-only cell in between them
            // (rule 1.4 removed that whitespace box before rule 2.3 could wrap it into one of its own).
            var anonTable = wrap.Boxes[wrap.Boxes.IndexOf(aBlock) + 1];
            Assert.Equal(DisplayMode.Table, anonTable.Display.Value);
            var row = anonTable.Boxes[0];
            Assert.Equal(2, row.Boxes.Count);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // The anonymous-table-box correction pass (DomParser.CorrectAnonymousTables) runs during
        // SetHtml, before layout - these two tests only assert on the resulting box tree shape, so
        // building without a full PerformLayout avoids exercising the (separate, unrelated) table
        // layout engine against synthetic structures it wasn't otherwise being exercised against.
        private static async Task<CssBox> BuildOnly(string html)
        {
            var renderContext = new HtmlRenderContext();
            renderContext.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var renderContext = new HtmlRenderContext();
            renderContext.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, System.StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }

        private static CssBox? FindByClass(CssBox box, string className)
        {
            var val = box.HtmlTag?.TryGetAttribute("class", "");
            if (val != null && val.Split(' ').Contains(className))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByClass(child, className);
                if (found != null) return found;
            }
            return null;
        }
    }
}
