using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A cell's declared <c>width</c> is its CONTENT width under <c>box-sizing: content-box</c> (the
    /// default), so its padding and border sit outside it. <c>_columnWidths</c> holds OUTER widths —
    /// <c>GetColumnMinWidths</c> fills the same array from <c>cell.GetMinimumWidth()</c>, which
    /// already includes them — so a bare declared width made every explicitly sized column narrower
    /// than a browser's by exactly its padding plus border.
    /// <para>
    /// Reference measurements are Chrome 152, rendered headless and read with <c>pdftotext -bbox</c>
    /// on the same three shapes. Asserted as relationships between them rather than as absolute
    /// points, since the two engines disagree about font metrics and border-collapse rounding.
    /// </para>
    /// </summary>
    public class TableDeclaredCellWidthTests
    {
        // Chrome 152, first column's width in points: 84.75 padded, 77.25 bare, 76.5 border-box.
        private const string Padded =
            "<td id='c' style='width:100px; padding:3px 5px; border:1px solid black'>x</td>";
        private const string Bare =
            "<td id='c' style='width:100px'>x</td>";
        private const string BorderBox =
            "<td id='c' style='width:100px; padding:3px 5px; border:1px solid black; box-sizing:border-box'>x</td>";

        [Fact]
        public async Task ADeclaredWidthIsTheContentWidth_SoPaddingAndBorderWidenTheColumn()
        {
            var padded = await ColumnWidthAsync(Padded);
            var bare = await ColumnWidthAsync(Bare);

            // 10px of horizontal padding is 7.5pt; Chrome puts the same two shapes 7.5pt apart.
            Assert.True(padded > bare + 6,
                $"a padded cell's column must be wider than an unpadded one declaring the same width: {padded:F2} vs {bare:F2}");
        }

        [Fact]
        public async Task BorderBoxDoesNotWidenTheColumn()
        {
            // The contrast case, and the reason this reads box-sizing rather than always adding:
            // under border-box the declared width already covers padding and border.
            var borderBox = await ColumnWidthAsync(BorderBox);
            var bare = await ColumnWidthAsync(Bare);

            Assert.Equal(bare, borderBox, 1);
        }

        [Fact]
        public async Task AnUnpaddedCellIsUnaffected()
        {
            // 100px is 75pt at 96 CSS dpi. Without padding or border there is nothing to add, so the
            // column is the declared width and this change is a no-op for it.
            var bare = await ColumnWidthAsync(Bare);

            Assert.Equal(75.0, bare, 1);
        }

        private static async Task<double> ColumnWidthAsync(string firstCell)
        {
            var (root, _) = await LayoutAsync(Wrap(
                $"<table style='border-collapse:collapse'><tr>{firstCell}<td>auto</td></tr></table>"));

            var cell = FindById(root, "c")!;
            return cell.ActualRight - cell.Location.X;
        }
    }
}
