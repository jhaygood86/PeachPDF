using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
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
            Assert.Equal(CssConstants.TableRow, row.Display);
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
            Assert.Equal(CssConstants.TableCell, second.ParentBox!.Display);
            Assert.Same(row, second.ParentBox!.ParentBox);
            Assert.Single(second.ParentBox!.Boxes);

            Assert.NotSame(row, fourth.ParentBox);
            Assert.Equal(CssConstants.TableCell, fourth.ParentBox!.Display);
            Assert.Same(row, fourth.ParentBox!.ParentBox);
            Assert.Single(fourth.ParentBox!.Boxes);

            // The nested display:table on "second" establishes its own table, unaffected by the
            // anonymous-cell wrapper around it.
            Assert.Equal(CssConstants.Table, second.Display);

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
            Assert.Equal(CssConstants.TableRow, anonRow.Display);
            Assert.Single(anonRow.Boxes);

            var anonCell = anonRow.Boxes[0];
            Assert.Equal(CssConstants.TableCell, anonCell.Display);

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
            Assert.Equal(CssConstants.TableRow, anonRow.Display);
            Assert.Same(anonRow, cell2.ParentBox);

            // 3.2: the anonymous row is wrapped in an anonymous table (the table formatting context the
            // stray cells require per CSS2.1 §17.2.1) — without it the row would render outside any table.
            var anonTable = anonRow.ParentBox!;
            Assert.Null(anonTable.HtmlTag);
            Assert.Equal(CssConstants.Table, anonTable.Display);

            // The synthesized table must sit between "before" and "after" in document order, not after
            // "after" (which is what a naive append-at-the-end would produce).
            Assert.Equal(new[] { before, anonTable, after }, body.Boxes);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // The anonymous-table-box correction pass (DomParser.CorrectAnonymousTables) runs during
        // SetHtml, before layout - these two tests only assert on the resulting box tree shape, so
        // building without a full PerformLayout avoids exercising the (separate, unrelated) table
        // layout engine against synthetic structures it wasn't otherwise being exercised against.
        private static async Task<CssBox> BuildOnly(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
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
