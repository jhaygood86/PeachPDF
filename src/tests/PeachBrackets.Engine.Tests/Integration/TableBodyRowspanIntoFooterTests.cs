using System.Threading.Tasks;
using PeachBrackets.Engine.Tests.TestSupport;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A <c>&lt;tbody&gt;</c> cell whose <c>rowspan</c> reaches past the last body row. The HTML table
    /// model (forming a table: ending a row group) keeps a cell inside the row group that holds it, and
    /// Chromium, WebKit and Gecko all do the same: the <c>&lt;tfoot&gt;</c> below is its own group, so its
    /// cells are placed in the grid columns the spanning cell does <b>not</b> occupy in the footer (i.e.
    /// the footer is not shifted by a cell that is not in it) and never overlap the spanning cell.
    /// </summary>
    public class TableBodyRowspanIntoFooterTests
    {
        private const string Cell = "padding:0; border:0; vertical-align:top;";

        private static string Table(string tableStyle) =>
            $"<table style='border-spacing:0; {tableStyle}'>" +
            "<tbody><tr>" +
            $"<td id='a' rowspan='3' style='{Cell} width:40pt; height:10pt;'>a</td>" +
            $"<td id='b' style='{Cell} width:40pt; height:10pt;'>b</td></tr></tbody>" +
            "<tfoot>" +
            $"<tr><td id='f1' style='{Cell} width:40pt; height:10pt;'>f1</td>" +
            $"<td id='g1' style='{Cell} width:40pt; height:10pt;'>g1</td></tr>" +
            $"<tr><td id='f2' style='{Cell} width:40pt; height:10pt;'>f2</td>" +
            $"<td id='g2' style='{Cell} width:40pt; height:10pt;'>g2</td></tr>" +
            "</tfoot></table>";

        [Theory]
        [InlineData("border-collapse:separate;")]
        [InlineData("border-collapse:collapse;")]
        public async Task ABodyCellSpanningPastTheBodyIsClippedToTheBodyAndDoesNotOverlapTheFooter(string tableStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Table(tableStyle)), margin: 0);

            var a = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "a")!;
            var b = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "b")!;
            var f1 = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "f1")!;
            var g1 = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "g1")!;
            var f2 = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "f2")!;
            var g2 = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "g2")!;

            // The footer keeps its own two columns, aligned with the body's two columns.
            Assert.Equal(a.Location.X, f1.Location.X, 0.5);
            Assert.Equal(b.Location.X, g1.Location.X, 0.5);
            Assert.Equal(f1.Location.X, f2.Location.X, 0.5);
            Assert.Equal(g1.Location.X, g2.Location.X, 0.5);

            // The spanning cell ends with the body row; the footer rows are below it, stacked.
            Assert.True(a.ActualBottom <= f1.Location.Y + 0.5,
                $"a ({a.ActualBottom}) must not reach into the footer (starts at {f1.Location.Y})");
            Assert.True(f2.Location.Y >= f1.ActualBottom - 0.5, "footer rows stack");
        }
    }
}
