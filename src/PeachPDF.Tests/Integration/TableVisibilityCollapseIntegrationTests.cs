using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 <see href="https://www.w3.org/TR/CSS21/tables.html#dynamic-effects">§17.6.1</see>:
    /// <c>visibility: collapse</c> on a table row/row-group/column/column-group removes it from the
    /// table's geometry entirely - as if it had <c>display: none</c> - so the rows/columns after it
    /// shift up/left to fill the gap. Distinct from <c>visibility: hidden</c>, which reserves the
    /// element's layout space and only omits painting it.
    ///
    /// Each test compares a table containing a collapsed row/column against a "control" table built
    /// without it at all, so the assertions do not depend on hard-coded border-spacing/padding
    /// arithmetic - a collapsed row/column must lay out exactly as if it were never there.
    /// </summary>
    public class TableVisibilityCollapseIntegrationTests
    {
        [Fact]
        public async Task CollapsedRow_DirectTableChild_TakesNoSpace()
        {
            // <tr> as a direct child of <table> (no row group) - CssLayoutEngineTable.AssignBoxKinds'
            // CssConstants.TableRow branch.
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tr><td class="a" style="height:20px">A</td></tr>
                  <tr style="visibility:collapse"><td class="b" style="height:20px">B</td></tr>
                  <tr><td class="c" style="height:20px">C</td></tr>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tr><td class="a" style="height:20px">A</td></tr>
                  <tr><td class="c" style="height:20px">C</td></tr>
                </table>
                </body></html>
                """);

            AssertSameLocation(FindByClass(control, "c")!, FindByClass(experiment, "c")!);
        }

        [Fact]
        public async Task CollapsedRow_InsideRowGroup_TakesNoSpace()
        {
            // A <tr> inside a <tbody> - the CssConstants.TableRowGroup branch.
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tbody>
                    <tr><td class="a" style="height:20px">A</td></tr>
                    <tr style="visibility:collapse"><td class="b" style="height:20px">B</td></tr>
                    <tr><td class="c" style="height:20px">C</td></tr>
                  </tbody>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tbody>
                    <tr><td class="a" style="height:20px">A</td></tr>
                    <tr><td class="c" style="height:20px">C</td></tr>
                  </tbody>
                </table>
                </body></html>
                """);

            AssertSameLocation(FindByClass(control, "c")!, FindByClass(experiment, "c")!);
        }

        [Fact]
        public async Task CollapsedRowGroup_CollapsesEveryRowInsideIt()
        {
            // visibility is an inherited property, so visibility:collapse on the <tbody> itself must
            // collapse every row inside it without the row declaring anything - AssignBoxKinds never
            // has to walk up to the group.
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tr><td class="a" style="height:20px">A</td></tr>
                  <tbody style="visibility:collapse">
                    <tr><td style="height:20px">B1</td></tr>
                    <tr><td style="height:20px">B2</td></tr>
                  </tbody>
                  <tr><td class="c" style="height:20px">C</td></tr>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tr><td class="a" style="height:20px">A</td></tr>
                  <tr><td class="c" style="height:20px">C</td></tr>
                </table>
                </body></html>
                """);

            AssertSameLocation(FindByClass(control, "c")!, FindByClass(experiment, "c")!);
        }

        [Fact]
        public async Task HiddenRow_StillReservesSpace_UnlikeCollapse()
        {
            // Guard distinguishing the two values: visibility:hidden must keep reserving the row's
            // space (only paint is skipped), so the row after it must NOT land where it would if the
            // hidden row had been collapsed instead.
            var (hidden, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tr><td class="a" style="height:20px">A</td></tr>
                  <tr style="visibility:hidden"><td style="height:20px">B</td></tr>
                  <tr><td class="c" style="height:20px">C</td></tr>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <tr><td class="a" style="height:20px">A</td></tr>
                  <tr><td class="c" style="height:20px">C</td></tr>
                </table>
                </body></html>
                """);

            var hiddenC = FindByClass(hidden, "c")!;
            var controlC = FindByClass(control, "c")!;
            Assert.True(hiddenC.Location.Y > controlC.Location.Y,
                $"a visibility:hidden row must still reserve its space, but hiddenC.Y={hiddenC.Location.Y} controlC.Y={controlC.Location.Y}");
        }

        [Fact]
        public async Task CollapsedRow_InsideDetachedHeader_TakesNoSpace()
        {
            // A <thead> is detached from the row-loop and measured separately
            // (DetachAndMeasureRepeatedRowGroups) - a collapsed row inside it has its own skip there.
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <thead>
                    <tr><td style="height:20px">H1</td></tr>
                    <tr style="visibility:collapse"><td style="height:20px">H2</td></tr>
                  </thead>
                  <tr><td class="body" style="height:20px">Body</td></tr>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0">
                  <thead>
                    <tr><td style="height:20px">H1</td></tr>
                  </thead>
                  <tr><td class="body" style="height:20px">Body</td></tr>
                </table>
                </body></html>
                """);

            AssertSameLocation(FindByClass(control, "body")!, FindByClass(experiment, "body")!);
        }

        [Fact]
        public async Task CollapsedRow_InsideDetachedFooter_TakesNoSpace()
        {
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table class="t" style="border-spacing:0">
                  <tr><td style="height:20px">Body</td></tr>
                  <tfoot>
                    <tr><td style="height:20px">F1</td></tr>
                    <tr style="visibility:collapse"><td style="height:20px">F2</td></tr>
                  </tfoot>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table class="t" style="border-spacing:0">
                  <tr><td style="height:20px">Body</td></tr>
                  <tfoot>
                    <tr><td style="height:20px">F1</td></tr>
                  </tfoot>
                </table>
                </body></html>
                """);

            Assert.Equal(FindByClass(control, "t")!.ActualBottom, FindByClass(experiment, "t")!.ActualBottom, 3);
        }

        [Fact]
        public async Task CollapsedColumn_TakesNoWidth()
        {
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table class="t" style="border-spacing:0; width:300px">
                  <colgroup>
                    <col style="width:100px">
                    <col style="width:100px; visibility:collapse">
                    <col style="width:100px">
                  </colgroup>
                  <tr>
                    <td class="a">A</td>
                    <td class="b">B</td>
                    <td class="c">C</td>
                  </tr>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table class="t" style="border-spacing:0; width:200px">
                  <colgroup>
                    <col style="width:100px">
                    <col style="width:100px">
                  </colgroup>
                  <tr>
                    <td class="a">A</td>
                    <td class="c">C</td>
                  </tr>
                </table>
                </body></html>
                """);

            AssertSameLocation(FindByClass(control, "c")!, FindByClass(experiment, "c")!);

            var experimentTable = FindByClass(experiment, "t")!;
            var controlTable = FindByClass(control, "t")!;
            Assert.Equal(controlTable.ActualRight, experimentTable.ActualRight, 3);
        }

        [Fact]
        public async Task CollapsedColumn_WithBorderSpacing_LeavesNoResidualGap()
        {
            // Regression guard: a collapsed column must not leave one border-spacing unit behind
            // (its own two boundary gaps must merge into the single gap a genuinely-absent column
            // would leave), which only shows once border-spacing is non-zero - the default UA
            // stylesheet sets `border-spacing: 2px` on every table, so this is the common case.
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table class="t" style="border-spacing:5px; width:300px">
                  <colgroup>
                    <col style="width:100px">
                    <col style="width:100px; visibility:collapse">
                    <col style="width:100px">
                  </colgroup>
                  <tr>
                    <td class="a">A</td>
                    <td>B</td>
                    <td class="c">C</td>
                  </tr>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table class="t" style="border-spacing:5px; width:205px">
                  <colgroup>
                    <col style="width:100px">
                    <col style="width:100px">
                  </colgroup>
                  <tr>
                    <td class="a">A</td>
                    <td class="c">C</td>
                  </tr>
                </table>
                </body></html>
                """);

            AssertSameLocation(FindByClass(control, "c")!, FindByClass(experiment, "c")!);
            Assert.Equal(FindByClass(control, "t")!.ActualRight, FindByClass(experiment, "t")!.ActualRight, 3);
        }

        [Fact]
        public async Task CollapsedColumnGroup_CollapsesEveryColumnInsideIt()
        {
            var (experiment, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0; width:200px">
                  <colgroup>
                    <col style="width:100px">
                  </colgroup>
                  <colgroup style="visibility:collapse">
                    <col style="width:50px">
                    <col style="width:50px">
                  </colgroup>
                  <tr>
                    <td class="a">A</td>
                    <td>B1</td>
                    <td>B2</td>
                  </tr>
                </table>
                </body></html>
                """);

            var (control, _) = await BuildAndLayout("""
                <!DOCTYPE html><html><body>
                <table style="border-spacing:0; width:100px">
                  <colgroup>
                    <col style="width:100px">
                  </colgroup>
                  <tr>
                    <td class="a">A</td>
                  </tr>
                </table>
                </body></html>
                """);

            var experimentA = FindByClass(experiment, "a")!;
            var controlA = FindByClass(control, "a")!;
            Assert.Equal(controlA.ActualWidth, experimentA.ActualWidth, 3);
        }

        private static void AssertSameLocation(CssBox expected, CssBox actual)
        {
            Assert.Equal(expected.Location.X, actual.Location.X, 3);
            Assert.Equal(expected.Location.Y, actual.Location.Y, 3);
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
