using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;

namespace PeachPDF.Tests.Html.Core.Dom
{
    public class CssLayoutEngineTableTests
    {
      private readonly ITestOutputHelper _output;

        public CssLayoutEngineTableTests(ITestOutputHelper output)
        {
    _output = output;
        }

  #region Basic Table Layout Tests

        [Fact]
     public async Task TableLayout_CalculatesCorrectDimensions()
        {
     // Arrange
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { width: 100%; border-collapse: collapse; }
    td { border: 1px solid black; padding: 8px; }
    </style>
</head>
<body>
    <table>
        <tr><td>Cell 1</td><td>Cell 2</td></tr>
        <tr><td>Cell 3</td><td>Cell 4</td></tr>
    </table>
</body>
</html>";

 var (rootBox, container) = await BuildCssBoxTree(html);

            // Act
            var table = FindTableBox(rootBox);

 // Assert
Assert.NotNull(table);
 Assert.True(table.ActualRight > table.Location.X, "Table should have width");
  Assert.True(table.ActualBottom > table.Location.Y, "Table should have height");
  _output.WriteLine($"Table dimensions: {table.ActualRight - table.Location.X} x {table.ActualBottom - table.Location.Y}");
    }

        [Fact]
        public async Task TableLayout_WithColspan_CalculatesCorrectWidth()
        {
       // Arrange
   var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
 table { width: 100%; border-collapse: collapse; }
  td { border: 1px solid black; padding: 8px; }
  </style>
</head>
<body>
    <table>
  <tbody>
        <tr><td colspan='2'>Wide Cell</td><td>Normal</td></tr>
        <tr><td>Cell 1</td><td>Cell 2</td><td>Cell 3</td></tr>
  </tbody>
 </table>
</body>
</html>";

   var (rootBox, container) = await BuildCssBoxTree(html);

  // Act
var table = FindTableBox(rootBox);
      Assert.NotNull(table);

   // Find the tbody which contains the rows
  var tbody = table.Boxes.FirstOrDefault(b => b.Display == CssConstants.TableRowGroup);
          if (tbody == null)
      {
          // If no explicit tbody, rows might be direct children
     tbody = table.Boxes.FirstOrDefault(b => b.Boxes.Any(child => child.Display == CssConstants.TableRow));
      }

   Assert.NotNull(tbody);
  Assert.True(tbody.Boxes.Count >= 1, "Should have at least one row");
 
var firstRow = tbody.Boxes[0];

     // Assert
  Assert.True(firstRow.Boxes.Count >= 2, "First row should have at least 2 cells");
   var firstCell = firstRow.Boxes[0];
   var lastCell = firstRow.Boxes[firstRow.Boxes.Count - 1];

 Assert.True(firstCell.ActualRight - firstCell.Location.X > lastCell.ActualRight - lastCell.Location.X,
     "Colspan cell should be wider than single cell");
   _output.WriteLine($"Colspan cell width: {firstCell.ActualRight - firstCell.Location.X}");
        _output.WriteLine($"Normal cell width: {lastCell.ActualRight - lastCell.Location.X}");
        }

      [Fact]
    public async Task TableLayout_WithRowspan_CalculatesCorrectHeight()
{
            // Arrange
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: collapse; }
        td { border: 1px solid black; padding: 8px; }
    </style>
</head>
<body>
    <table>
        <tr><td rowspan='2'>Tall Cell</td><td>Cell 2</td></tr>
        <tr><td>Cell 3</td></tr>
      <tr><td>Cell 4</td><td>Cell 5</td></tr>
    </table>
</body>
</html>";

   var (rootBox, container) = await BuildCssBoxTree(html);

            // Act
            var table = FindTableBox(rootBox);
   Assert.NotNull(table);

  var firstRow = table.Boxes[0].Boxes[0];
    var tallCell = firstRow.Boxes[0];

// Assert
var tallCellHeight = tallCell.ActualBottom - tallCell.Location.Y;
            Assert.True(tallCellHeight > 0, "Rowspan cell should have height");
            _output.WriteLine($"Rowspan cell height: {tallCellHeight}");
        }

      #endregion

        #region Header/Footer Layout Tests

  [Fact]
 public async Task TableLayout_WithHeader_LayoutsHeaderFirst()
     {
    // Arrange
 var html = @"
<!DOCTYPE html>
<html>
<head>
  <style>
        @page { size: A4; margin: 20mm; }
  table { width: 100%; border-collapse: collapse; }
th, td { border: 1px solid black; padding: 8px; }
   thead { display: table-header-group; }
    </style>
</head>
<body>
    <table>
   <thead>
  <tr><th>Header 1</th><th>Header 2</th></tr>
      </thead>
  <tbody>
  <tr><td>Cell 1</td><td>Cell 2</td></tr>
  <tr><td>Cell 3</td><td>Cell 4</td></tr>
        </tbody>
    </table>
</body>
</html>";

 var (rootBox, container) = await BuildCssBoxTree(html, pageHeight: 800); // Large page to avoid multiple proxies

   // Act
       var table = FindTableBox(rootBox);
   Assert.NotNull(table);

 // Find a header proxy (there should be one at the top)
  var headerProxies = table.Boxes.OfType<CssProxyBox>()
   .Where(p => p.Display == CssConstants.TableHeaderGroup)
  .ToList();

 // Assert
  Assert.True(headerProxies.Count > 0, "Should have at least one header proxy");
  var firstProxy = headerProxies.OrderBy(p => p.Location.Y).First();
 _output.WriteLine($"First header proxy type: {firstProxy.GetType().Name}, Display: {firstProxy.Display}, Location.Y: {firstProxy.Location.Y}");
    Assert.Equal(CssConstants.TableHeaderGroup, firstProxy.Display);
   }

        [Fact]
        public async Task TableLayout_WithFooter_LayoutsFooterLast()
        {
        // Arrange
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { width: 100%; border-collapse: collapse; }
        th, td { border: 1px solid black; padding: 8px; }
      tfoot { display: table-footer-group; }
  </style>
</head>
<body>
    <table>
        <tbody>
            <tr><td>Cell 1</td><td>Cell 2</td></tr>
    </tbody>
        <tfoot>
         <tr><td>Footer 1</td><td>Footer 2</td></tr>
   </tfoot>
  </table>
</body>
</html>";

     var (rootBox, container) = await BuildCssBoxTree(html);

          // Act
    var table = FindTableBox(rootBox);
          Assert.NotNull(table);

     // Footer might be a proxy, so check all boxes
      var hasFooter = table.Boxes.Any(b => b.Display == CssConstants.TableFooterGroup);

    // Assert
     Assert.True(hasFooter, "Table should have footer");
            _output.WriteLine($"Table has {table.Boxes.Count} children including footer");
        }

      [Fact]
        public async Task TableLayout_RemovesHeaderFromTreeWhenRepeating()
        {
 // Arrange
       var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page { size: A4; margin: 20mm; }
  table { width: 100%; border-collapse: collapse; }
        th, td { border: 1px solid black; padding: 8px; }
        thead { display: table-header-group; }
    </style>
</head>
<body>
    <table>
        <thead>
            <tr><th>Header</th></tr>
        </thead>
        <tbody>
    " + string.Join("", Enumerable.Range(1, 20).Select(i => $"<tr><td>Row {i}</td></tr>")) + @"
   </tbody>
    </table>
</body>
</html>";

var (rootBox, container) = await BuildCssBoxTree(html, pageHeight: 400);

            // Act
            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

        // Check that thead is not in direct children (it's replaced by proxies)
      var directTheadChildren = table.Boxes.Where(b => 
    b.Display == CssConstants.TableHeaderGroup && b is not CssProxyBox).ToList();

   var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();

      // Assert
            Assert.Empty(directTheadChildren);
            Assert.NotEmpty(proxyBoxes);
            _output.WriteLine($"Direct thead children: {directTheadChildren.Count}");
        _output.WriteLine($"Proxy boxes: {proxyBoxes.Count}");
   }

    #endregion

        #region Page Break Tests

        [Fact]
        public async Task TableLayout_DetectsPageBreaksCorrectly()
    {
  // Arrange
  var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page { size: A4; margin: 20mm; }
   table { width: 100%; border-collapse: collapse; }
        th, td { border: 1px solid black; padding: 10px; }
   thead { display: table-header-group; }
    </style>
</head>
<body>
    <table>
      <thead>
      <tr><th>Header</th></tr>
        </thead>
        <tbody>
    " + string.Join("", Enumerable.Range(1, 30).Select(i => $"<tr><td>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

var pageHeight = 300.0;
            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            // Act
        var table = FindTableBox(rootBox);
            Assert.NotNull(table);

   var tableHeight = table.ActualBottom - table.Location.Y;
  var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();

            // Assert
   Assert.True(tableHeight > pageHeight, $"Table height ({tableHeight}) should exceed page height ({pageHeight})");
          Assert.True(proxyBoxes.Count >= 2, $"Should have at least 2 header proxies for multi-page table, found {proxyBoxes.Count}");

            _output.WriteLine($"Table height: {tableHeight}");
            _output.WriteLine($"Page height: {pageHeight}");
         _output.WriteLine($"Pages spanned: ~{Math.Ceiling(tableHeight / pageHeight)}");
     _output.WriteLine($"Header proxies: {proxyBoxes.Count}");
        }

        [Fact]
        public async Task TableLayout_PositionsHeadersAtCorrectPageStarts()
  {
    // Arrange
  var html = @"
<!DOCTYPE html>
<html>
<head>
 <style>
 @page { size: A4; margin: 20mm; }
  table { width: 100%; border-collapse: collapse; }
 th, td { border: 1px solid black; padding: 10px; }
        thead { display: table-header-group; }
   </style>
</head>
<body>
    <table>
 <thead>
<tr><th>Header</th></tr>
     </thead>
   <tbody>
    " + string.Join("", Enumerable.Range(1, 10).Select(i => $"<tr><td>Row {i}</td></tr>")) + @"
   </tbody>
 </table>
</body>
</html>";

 var pageHeight = 200.0;  // Very short pages to force multiple page breaks
var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

   // Act
  var table = FindTableBox(rootBox);
         Assert.NotNull(table);

   var headerProxies = table.Boxes.OfType<CssProxyBox>()
   .Where(p => p.Display == CssConstants.TableHeaderGroup)
        .OrderBy(p => p.Location.Y)
   .ToList();

   // Assert
  _output.WriteLine($"Total header proxies: {headerProxies.Count}");
      _output.WriteLine($"Total table boxes: {table.Boxes.Count}");
  
    Assert.True(headerProxies.Count >= 1, "Should have at least one header proxy");

  // Log all header positions
  for (int i = 0; i < headerProxies.Count; i++)
   {
       _output.WriteLine($"Header {i}: Location.Y={headerProxies[i].Location.Y}, ActualBottom={headerProxies[i].ActualBottom}");
  }

  // First header should be near the start
 var firstHeaderY = headerProxies[0].Location.Y;
         Assert.True(firstHeaderY < 100, $"First header should be near top, but is at Y={firstHeaderY}");

 // If there are multiple proxies, verify they have DIFFERENT Y positions
     if (headerProxies.Count > 1)
     {
 // Check that not all proxies are at the same position
   var uniquePositions = headerProxies.Select(p => p.Location.Y).Distinct().Count();
 Assert.True(uniquePositions > 1, 
      $"Multiple header proxies should be at different Y positions, but all {headerProxies.Count} are at Y={firstHeaderY}");
    }
   }

        #endregion

        #region Column Width Tests

        [Fact]
        public async Task TableLayout_DistributesWidthEqually_WhenNoWidthsSpecified()
  {
  // Arrange
   var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { width: 600px; border-collapse: collapse; }
td { border: 1px solid black; padding: 8px; }
 </style>
</head>
<body>
    <table>
 <tbody>
      <tr><td>Cell 1</td><td>Cell 2</td><td>Cell 3</td></tr>
 </tbody>
</table>
</body>
</html>";

 var (rootBox, container) = await BuildCssBoxTree(html);

      // Act
  var table = FindTableBox(rootBox);
  Assert.NotNull(table);

 // Find tbody which contains the row
   var tbody = table.Boxes.FirstOrDefault(b => b.Display == CssConstants.TableRowGroup);
      if (tbody == null)
     {
         // If no explicit tbody, look for a box that has rows as children
   tbody = table.Boxes.FirstOrDefault(b => b.Boxes.Any(child => child.Display == CssConstants.TableRow));
    }

Assert.NotNull(tbody);
   Assert.True(tbody.Boxes.Count > 0, "Tbody should have rows");

 var row = tbody.Boxes[0];
       var cells = row.Boxes;

       // Assert
Assert.Equal(3, cells.Count);

   var widths = cells.Select(c => c.ActualRight - c.Location.X).ToList();
  _output.WriteLine($"Cell widths: {string.Join(", ", widths)}");

  // Widths should be approximately equal (allowing for borders/padding)
  var avgWidth = widths.Average();
     foreach (var width in widths)
 {
         Assert.True(Math.Abs(width - avgWidth) < 5, 
  $"Cell width {width} should be close to average {avgWidth}");
      }
   }

        [Fact]
     public async Task TableLayout_RespectsSpecifiedColumnWidths()
{
   // Arrange
   // Four (not three) unstyled-width columns: an even auto-layout split of 600px across 4 columns
   // lands near 150px each, comfortably below the >=180 threshold below - so this can only pass if
   // ":first-child" actually forced the first cell to ~200px, not by coincidentally landing in the
   // same range as an inert rule's auto-layout split (which a 3-column table's ~200px/column average
   // could not distinguish from a working rule).
var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
 table { width: 600px; border-collapse: collapse; }
    td { border: 1px solid black; padding: 8px; }
      td:first-child { width: 200px; }
    </style>
</head>
<body>
    <table>
      <tbody>
        <tr><td>Wide Cell</td><td>Auto</td><td>Auto</td><td>Auto</td></tr>
   </tbody>
</table>
</body>
</html>";

    var (rootBox, container) = await BuildCssBoxTree(html);

     // Act
    var table = FindTableBox(rootBox);
 Assert.NotNull(table);

  // Find tbody which contains the row
   var tbody = table.Boxes.FirstOrDefault(b => b.Display == CssConstants.TableRowGroup);
  if (tbody == null)
 {
 // If no explicit tbody, look for a box that has rows as children
   tbody = table.Boxes.FirstOrDefault(b => b.Boxes.Any(child => child.Display == CssConstants.TableRow));
  }

Assert.NotNull(tbody);
   Assert.True(tbody.Boxes.Count > 0, "Tbody should have rows");

   var row = tbody.Boxes[0];
     var firstCell = row.Boxes[0];
    var firstCellWidth = firstCell.ActualRight - firstCell.Location.X;
    var secondCell = row.Boxes[1];
    var secondCellWidth = secondCell.ActualRight - secondCell.Location.X;

        // Assert
    _output.WriteLine($"First cell width: {firstCellWidth}, second cell width: {secondCellWidth}");
     Assert.True(firstCellWidth >= 180, $"First cell should be approximately 200px wide (accounting for borders), but was {firstCellWidth}");
     Assert.True(secondCellWidth < 160, $"Second cell should be narrower than the explicitly-widened first cell (proving the rule is selective, not applied to every column), but was {secondCellWidth}");
     }

        [Fact]
        public async Task TableLayout_TbodyBox_GetsRealBoundsSpanningItsRows()
        {
            // Regression test: AssignBoxKinds flattens a <tbody>'s <tr> children directly into the
            // layout engine's row list, laying each row/cell out individually, but historically never
            // touched the <tbody> box itself - it kept whatever default Location/ActualRight/
            // ActualBottom it started with (an effectively empty/zero-area Bounds). That's normally
            // harmless (nothing sizes against a row-group's own box), but CssBox.Paint's
            // visibility-culling optimization intersects a Rectangles.Count==0 box's own Bounds
            // against the current clip whenever the whole document has no floated/absolute/fixed
            // content anywhere - a <tbody> with a never-set Bounds fails that intersection and gets
            // silently culled from painting along with its entire row/cell subtree, even though every
            // row/cell inside it has a perfectly valid, already-computed position. This is exactly
            // the scenario a bare table (the only content in the document) hits.
            var html = "<html><body><table><tbody><tr><td>A</td><td>B</td></tr></tbody></table></body></html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;
            var tbody = table.Boxes.Single(b => b.Display == CssConstants.TableRowGroup);
            var row = tbody.Boxes.Single(b => b.Display == CssConstants.TableRow);

            Assert.Equal(row.Location.X, tbody.Location.X);
            Assert.Equal(row.Location.Y, tbody.Location.Y);
            Assert.Equal(row.ActualRight, tbody.ActualRight);
            Assert.Equal(row.ActualBottom, tbody.ActualBottom);
            Assert.True(tbody.Bounds.Width > 0 && tbody.Bounds.Height > 0,
                $"tbody must have a real, non-degenerate Bounds, but was {tbody.Bounds}");
        }

        [Fact]
        public async Task TableLayout_TheadAndTfootRows_GetRealBoundsSpanningTheirCells()
        {
            // Regression test: unlike the regular body-row loop (which sets row.Location/
            // ActualRight/ActualBottom right after laying out each row's cells), the header- and
            // footer-rows-layout-once loops in LayoutCells only ever set bounds on the row GROUP
            // (_headerBox/_footerBox) - never on each individual <tr> row inside it. A <tr> with a
            // never-set (degenerate) Bounds fails the same paint-time visibility-culling
            // intersection the tbody bug above hit, silently dropping the header/footer row (and
            // therefore its cells' text) from painting even though the row-group and cells around
            // it all have perfectly valid, already-computed positions.
            var html = "<html><body><table>" +
                "<thead><tr><th>H1</th><th>H2</th></tr></thead>" +
                "<tbody><tr><td>A</td><td>B</td></tr></tbody>" +
                "<tfoot><tr><th>F1</th><th>F2</th></tr></tfoot>" +
                "</table></body></html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;

            // The real <thead>/<tfoot> boxes are removed from the live tree in favor of one
            // CssProxyBox per page (see RemoveHeaderFooterFromTree) - reach the original row via
            // the proxy's SourceBox rather than the proxy's own (paint-time-only) Boxes.
            var headerProxy = table.Boxes.OfType<CssProxyBox>().Single(b => b.Display == CssConstants.TableHeaderGroup);
            var headerRow = headerProxy.SourceBox.Boxes.Single(b => b.Display == CssConstants.TableRow);
            AssertRealBounds(headerRow, "thead row");

            var footerProxy = table.Boxes.OfType<CssProxyBox>().Single(b => b.Display == CssConstants.TableFooterGroup);
            var footerRow = footerProxy.SourceBox.Boxes.Single(b => b.Display == CssConstants.TableRow);
            AssertRealBounds(footerRow, "tfoot row");

            static void AssertRealBounds(CssBox row, string label)
            {
                Assert.True(row.Bounds.Width > 0 && row.Bounds.Height > 0,
                    $"{label} must have a real, non-degenerate Bounds, but was {row.Bounds}");
                Assert.Equal(row.Boxes.Min(c => c.Location.X), row.Location.X);
                Assert.Equal(row.Boxes.Min(c => c.Location.Y), row.Location.Y);
                Assert.Equal(row.Boxes.Max(c => c.ActualRight), row.ActualRight);
            }
        }

        [Fact]
        public async Task TableLayout_HeaderAndFooterGroups_ProduceExactlyOneProxyEach()
        {
            // Regression test: CssProxyBox's constructor already appends itself to its parent's
            // Boxes via the base CssBox(parentBox, tag) constructor - CreateHeaderProxy/
            // CreateFooterProxy's callers in LayoutCells also explicitly called
            // _tableBox.Boxes.Add(proxy) right after construction, adding the exact same proxy
            // instance to the table's children a second time. That made every header/footer row
            // get painted (and MCID-tagged, once tagging is enabled) twice at identical
            // coordinates - invisible on the page (exact overlap) but duplicated content-stream
            // bytes and structure-tree entries.
            var html = "<html><body><table>" +
                "<thead><tr><th>H1</th></tr></thead>" +
                "<tbody><tr><td>A</td></tr></tbody>" +
                "<tfoot><tr><th>F1</th></tr></tfoot>" +
                "</table></body></html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;

            var headerProxies = table.Boxes.Count(b => b is CssProxyBox && b.Display == CssConstants.TableHeaderGroup);
            var footerProxies = table.Boxes.Count(b => b is CssProxyBox && b.Display == CssConstants.TableFooterGroup);

            Assert.Equal(1, headerProxies);
            Assert.Equal(1, footerProxies);
        }

        #endregion

   #region Helper Methods

        private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(
    string html, 
 double pageHeight = 842) // A4 height by default
        {
            var adapter = new PdfSharpAdapter();
       var container = new HtmlContainerInt(adapter);

          await container.SetHtml(html, null);

     var size = new XSize(595, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
    container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
   container.MarginTop = 20;
        container.MarginBottom = 20;

   var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

      Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

  private static CssBox? FindTableBox(CssBox box)
     {
          if (box.Display == CssConstants.Table)
      return box;

 foreach (var child in box.Boxes)
       {
          var result = FindTableBox(child);
           if (result != null)
       return result;
  }

      return null;
        }

        #endregion
    }
}
