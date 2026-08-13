using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
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
  var tbody = table.Boxes.FirstOrDefault(b => b.Display.Value == DisplayMode.TableRowGroup);
          if (tbody == null)
      {
          // If no explicit tbody, rows might be direct children
     tbody = table.Boxes.FirstOrDefault(b => b.Boxes.Any(child => child.Display.Value == DisplayMode.TableRow));
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

        #region Explicit Cell Height Smaller Than Content Tests

        [Fact]
        public async Task ExplicitCellHeightSmallerThanContent_DoesNotClipCellOrOverlapNextRow()
        {
            // CSS 2.1 §17.5.3: a cell's specified `height` only feeds the row-height calculation
            // (row height = max of the row's own height, each cell's height, and the minimum height
            // the cells' content requires) - it never clips the cell's own content, regardless of
            // `overflow` (td/th default to overflow:hidden in CssDefaults). `height:1px` is a legacy
            // "definite-height containing block" author hack that relies on this: the cell must still
            // grow to its content's full height, and the next row must start below that, not on top of
            // it (issue #729).
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: collapse; }
        td { border: 1px solid black; padding: 4pt; font-size: 14pt; height: 1px; }
    </style>
</head>
<body>
    <table>
        <tr><td id='tall'>Line one<br>Line two<br>Line three</td></tr>
        <tr><td id='next'>Next row</td></tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var tallCell = FindById(rootBox, "tall");
            var nextCell = FindById(rootBox, "next");

            Assert.NotNull(tallCell);
            Assert.NotNull(nextCell);

            var tallCellHeight = tallCell!.ActualBottom - tallCell.Location.Y;

            // Three 14pt lines plus padding is well over 1px - if the specified height still clipped
            // the cell, this would be close to 1px + padding instead.
            Assert.True(tallCellHeight > 30,
                $"first row's cell should grow to its content's height, not the specified 1px (actual: {tallCellHeight})");

            // A 1pt tolerance accounts for border-collapse's shared border sitting on the boundary
            // between the two rows - what this guards against is the tens-of-points overlap the bug
            // produced (the next row starting inside the tall cell's real text), not sub-point border
            // placement.
            Assert.True(nextCell!.Location.Y >= tallCell.ActualBottom - 1,
                $"second row (starting at {nextCell.Location.Y}) must not overlap the first row's actual content bottom ({tallCell.ActualBottom})");
        }

        [Fact]
        public async Task ExplicitCellHeightSmallerThanContent_StretchesEveryCellInTheRow()
        {
            // The row-height reconciliation in CssLayoutEngineTable.LayoutBodyRow takes the max of
            // every cell's ActualBottom for the row - so a short, unconstrained sibling cell in the
            // same row as the height:1px cell must still be stretched down to the tall cell's real
            // content height, per the same §17.5.3 row-height rule.
            //
            // The row's actual final height is cross-checked against a reference table holding only
            // the tall cell's own content (no explicit height, no sibling) rather than an arbitrary
            // threshold - a broken row reconciliation could equalize both cells to some smaller,
            // wrongly-clipped value, which a threshold alone would not catch if picked too low.
            const string cellStyle = "border: 1px solid black; padding: 4pt; font-size: 14pt;";
            var referenceHtml = $@"
<!DOCTYPE html>
<html>
<head><style>table {{ border-collapse: collapse; }}</style></head>
<body>
    <table>
        <tr><td id='reference' style='{cellStyle}'>Line one<br>Line two<br>Line three</td></tr>
    </table>
</body>
</html>";
            var html = $@"
<!DOCTYPE html>
<html>
<head><style>table {{ border-collapse: collapse; }}</style></head>
<body>
    <table>
        <tr>
            <td id='tall' style='{cellStyle} height:1px'>Line one<br>Line two<br>Line three</td>
            <td id='short' style='{cellStyle}'>Short</td>
        </tr>
    </table>
</body>
</html>";

            var (referenceRoot, _) = await BuildCssBoxTree(referenceHtml);
            var referenceCell = FindById(referenceRoot, "reference");
            Assert.NotNull(referenceCell);
            var referenceHeight = referenceCell!.ActualBottom - referenceCell.Location.Y;

            var (rootBox, _) = await BuildCssBoxTree(html);
            var tallCell = FindById(rootBox, "tall");
            var shortCell = FindById(rootBox, "short");

            Assert.NotNull(tallCell);
            Assert.NotNull(shortCell);

            var rowHeight = shortCell!.ActualBottom - shortCell.Location.Y;
            Assert.True(rowHeight >= referenceHeight - 1,
                $"the row (height {rowHeight}) should stretch to at least the tall cell's real, unconstrained content height ({referenceHeight}), not a clipped value");

            Assert.Equal(tallCell!.ActualBottom, shortCell.ActualBottom, 1);
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
   .Where(p => p.Display.Value == DisplayMode.TableHeaderGroup)
  .ToList();

 // Assert
  Assert.True(headerProxies.Count > 0, "Should have at least one header proxy");
  var firstProxy = headerProxies.OrderBy(p => p.Location.Y).First();
 _output.WriteLine($"First header proxy type: {firstProxy.GetType().Name}, Display: {firstProxy.Display}, Location.Y: {firstProxy.Location.Y}");
    Assert.Equal(DisplayMode.TableHeaderGroup, firstProxy.Display.Value);
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
      var hasFooter = table.Boxes.Any(b => b.Display.Value == DisplayMode.TableFooterGroup);

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
    b.Display.Value == DisplayMode.TableHeaderGroup && b is not CssProxyBox).ToList();

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

// Tall enough that css-tables-3 6.2 lets the header repeat at all - it caps a repeated group at a
// quarter of the page, and this harness's unpinned PixelsPerPoint makes the header ~85 units.
var pageHeight = 400.0;
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
   .Where(p => p.Display.Value == DisplayMode.TableHeaderGroup)
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
   var tbody = table.Boxes.FirstOrDefault(b => b.Display.Value == DisplayMode.TableRowGroup);
      if (tbody == null)
     {
         // If no explicit tbody, look for a box that has rows as children
   tbody = table.Boxes.FirstOrDefault(b => b.Boxes.Any(child => child.Display.Value == DisplayMode.TableRow));
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
 table { width: 600pt; border-collapse: collapse; }
    td { border: 1px solid black; padding: 8px; }
      td:first-child { width: 200pt; }
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
   var tbody = table.Boxes.FirstOrDefault(b => b.Display.Value == DisplayMode.TableRowGroup);
  if (tbody == null)
 {
 // If no explicit tbody, look for a box that has rows as children
   tbody = table.Boxes.FirstOrDefault(b => b.Boxes.Any(child => child.Display.Value == DisplayMode.TableRow));
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
        public async Task TableLayout_ColElementPixelWidth_ResolvesSpecCorrect()
        {
            // A <col> px width feeds the fixed-layout column-width array through the shared
            // spec-correct conversion (1px = 0.75pt), so a 200px column is 150pt wide - the same
            // as every other px length in the engine, not the old 1px = 1pt.
            var html = @"
<!DOCTYPE html>
<html><head><style>
    table { border-collapse: collapse; }
    td { padding: 0; border: 0; }
</style></head>
<body>
    <table>
        <colgroup><col style=""width:200px""><col></colgroup>
        <tbody><tr><td>A</td><td>B</td></tr></tbody>
    </table>
</body></html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var tbody = table!.Boxes.FirstOrDefault(b => b.Display.Value == DisplayMode.TableRowGroup)
                        ?? table.Boxes.FirstOrDefault(b => b.Boxes.Any(c => c.Display.Value == DisplayMode.TableRow));
            Assert.NotNull(tbody);
            var firstCell = tbody!.Boxes[0].Boxes[0];
            var firstCellWidth = firstCell.ActualRight - firstCell.Location.X;

            // 200px -> 150pt (0.75x), not 200.
            Assert.Equal(150.0, firstCellWidth, 1);
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
            var tbody = table.Boxes.Single(b => b.Display.Value == DisplayMode.TableRowGroup);
            var row = tbody.Boxes.Single(b => b.Display.Value == DisplayMode.TableRow);

            Assert.Equal(row.Location.X, tbody.Location.X);
            Assert.Equal(row.Location.Y, tbody.Location.Y);
            Assert.Equal(row.ActualRight, tbody.ActualRight);
            Assert.Equal(row.ActualBottom, tbody.ActualBottom);
            Assert.True(tbody.Bounds.Width > 0 && tbody.Bounds.Height > 0,
                $"tbody must have a real, non-degenerate Bounds, but was {tbody.Bounds}");
        }

        [Fact]
        public async Task TableLayout_EmptyTbody_DoesNotThrow_AndIsSkipped()
        {
            // A <tbody> with no <tr> children at all (legal but degenerate markup) has nothing to
            // derive Location/ActualRight/ActualBottom from - SetRowGroupBoxDimensions must skip
            // it rather than crash on an empty rows.Min()/Max() call.
            var html = "<html><body><table>" +
                "<tbody id=\"empty\"></tbody>" +
                "<tbody id=\"real\"><tr><td>A</td></tr></tbody>" +
                "</table></body></html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;
            var tbodies = table.Boxes.Where(b => b.Display.Value == DisplayMode.TableRowGroup).ToList();

            Assert.Equal(2, tbodies.Count);
            var realTbody = tbodies.Single(b => b.Boxes.Count > 0);
            Assert.True(realTbody.Bounds.Width > 0 && realTbody.Bounds.Height > 0);
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
            var headerProxy = table.Boxes.OfType<CssProxyBox>().Single(b => b.Display.Value == DisplayMode.TableHeaderGroup);
            var headerRow = headerProxy.SourceBox.Boxes.Single(b => b.Display.Value == DisplayMode.TableRow);
            AssertRealBounds(headerRow, "thead row");

            var footerProxy = table.Boxes.OfType<CssProxyBox>().Single(b => b.Display.Value == DisplayMode.TableFooterGroup);
            var footerRow = footerProxy.SourceBox.Boxes.Single(b => b.Display.Value == DisplayMode.TableRow);
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

            var headerProxies = table.Boxes.Count(b => b is CssProxyBox && b.Display.Value == DisplayMode.TableHeaderGroup);
            var footerProxies = table.Boxes.Count(b => b is CssProxyBox && b.Display.Value == DisplayMode.TableFooterGroup);

            Assert.Equal(1, headerProxies);
            Assert.Equal(1, footerProxies);
        }

        [Fact]
        public async Task TableLayout_FixedMarginLeft_IsNotDoubleCounted()
        {
            // Regression test: CssBox.PerformLayoutImp's Static/Relative branch already positions a
            // display:table box at ClientLeft + ActualMarginLeft before dispatching into
            // CssLayoutEngineTable.Layout, which used to unconditionally add
            // GetActualMarginLeft(_tableBox, GetWidthSum()) again - for a fixed (non-auto)
            // margin-left that second call returns the same non-zero value (GetWidthSum() only
            // matters for the auto-margin centering case), so the table ended up shifted an extra
            // margin-left's worth to the right. Caught on Acid2's own teeth row
            // ("ul { display: table; margin: -1em 7em 0; }"), which rendered detached to the right
            // of the chin instead of flush against it.
            var html = "<html><body><div id='wrap'>" +
                       "<table id='t' style='margin-left: 50pt; margin-top: 0;'><tr><td>A</td></tr></table>" +
                       "</div></body></html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var wrap = FindById(rootBox, "wrap")!;
            var table = FindTableBox(rootBox)!;

            Assert.Equal(wrap.ClientLeft + 50, table.Location.X, precision: 3);
        }

        [Fact]
        public async Task TableLayout_AutoMargins_CentersTable()
        {
            // Regression test for the deferred-centering branch left behind by the fix above:
            // GetActualMarginLeft(_tableBox) returns 0 during the earlier Static/Relative pass
            // (boxWidth: null, table width not known yet), so this box's margin-left:auto centering
            // offset is entirely computed by CssLayoutEngineTable.Layout's own re-application, once
            // GetWidthSum() is known - that path needs its own coverage, distinct from the
            // fixed-margin case above.
            var html = "<html><body><div id='wrap' style='width: 400px;'>" +
                       "<table id='t' style='width: 100px; margin: 0 auto;'><tr><td>A</td></tr></table>" +
                       "</div></body></html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var wrap = FindById(rootBox, "wrap")!;
            var table = FindTableBox(rootBox)!;

            var wrapWidth = wrap.ClientRight - wrap.ClientLeft;
            var tableWidth = table.ActualRight - table.Location.X;
            var expectedX = wrap.ClientLeft + (wrapWidth - tableWidth) / 2;

            Assert.Equal(expectedX, table.Location.X, precision: 3);
        }

        [Fact]
        public async Task TableLayout_MaxWidthNarrowerThanExplicitWidth_RespectsMaxWidth()
        {
            // Regression coverage for max-width being respected on a table whose explicit width would
            // otherwise be far wider - see .claude/accepted-gaps/table-max-width-clip-branch-coverage.md
            // for why this doesn't directly exercise CssLayoutEngineTable.ClipColumnsToMaxWidth itself.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { width: 1000pt; max-width: 200pt; border-collapse: collapse; }
        td { border: 1px solid black; padding: 4pt; white-space: nowrap; }
    </style>
</head>
<body>
    <table>
        <tr>
            <td>AAAAAAAAAAAAAAAAAAAA</td>
            <td>BBBBBBBBBBBBBBBBBBBB</td>
            <td>CCCCCCCCCCCCCCCCCCCC</td>
        </tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);

            Assert.NotNull(table);
            var tableWidth = table!.ActualRight - table.Location.X;
            _output.WriteLine($"Table width with width:1000pt max-width:200pt and nowrap content: {tableWidth}");

            // Clipped down to max-width (200pt) - nowhere near the 1000pt explicit width.
            Assert.Equal(200, tableWidth, precision: 3);
        }

        [Fact]
        public async Task TableLayout_MaxWidthWiderThanMinimum_SpreadsExtraWidthToColumns()
        {
            // Exercises CssLayoutEngineTable.SpreadExtraWidthToColumns: an explicit width far wider than
            // max-width sizes the columns wide first, wrappable content lets them shrink to a minimum
            // well below max-width, and the remaining room (up to max-width) is spread back across them.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { width: 1000pt; max-width: 400pt; border-collapse: collapse; }
        td { border: 1px solid black; padding: 4pt; }
    </style>
</head>
<body>
    <table>
        <tr>
            <td>Some reasonably long wrappable cell content that can reflow across several lines</td>
        </tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);

            Assert.NotNull(table);
            var tableWidth = table!.ActualRight - table.Location.X;
            _output.WriteLine($"Table width with width:1000pt max-width:400pt and wrappable content: {tableWidth}");

            // Spread back up to max-width (400pt) - nowhere near the 1000pt explicit width, and not left
            // stuck at the wrapped-content minimum either.
            Assert.Equal(400, tableWidth, precision: 3);
        }

        #endregion

        #region Border-collapse Tests

        [Theory]
        [InlineData("collapse", -1)]
        [InlineData("separate", 8)]
        public async Task TableLayout_BorderCollapse_ControlsGapBetweenAdjacentCells(string borderCollapse, double expectedGap)
        {
            // CssLayoutEngineTable.GetHorizontalSpacing() returns -1pt (cells overlap by 1pt, merging
            // their shared border) under "collapse", or the real border-spacing under "separate" - this
            // asserts the actual per-cell X gap that value produces, not just that BorderCollapse is
            // stored on the box.
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        table {{ border-collapse: {borderCollapse}; border-spacing: 8pt; }}
        td {{ border: 2pt solid black; padding: 4pt; }}
    </style>
</head>
<body>
    <table>
        <tr><td id='c1'>Cell 1</td><td id='c2'>Cell 2</td></tr>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var c1 = FindById(rootBox, "c1");
            var c2 = FindById(rootBox, "c2");

            Assert.NotNull(c1);
            Assert.NotNull(c2);

            var gap = c2!.Location.X - c1!.ActualRight;
            Assert.Equal(expectedGap, gap, 1);
        }

        [Fact]
        public async Task EmptyCellsHide_SuppressesPaintForEmptyCellOnly()
        {
            var (rootBox, container) = await BuildCssBoxTree(@"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; }
        td { border: 2pt solid rgb(51,51,51); background-color: rgb(200,200,200); padding: 4pt; }
        #empty { empty-cells: hide; }
        #full { empty-cells: show; }
    </style>
</head>
<body>
    <table>
        <tr><td id='empty'></td><td id='full'>x</td></tr>
    </table>
</body>
</html>");

            var emptyCell = FindById(rootBox, "empty");
            var fullCell = FindById(rootBox, "full");
            Assert.NotNull(emptyCell);
            Assert.NotNull(fullCell);

            var gEmpty = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, emptyCell!, gEmpty);
            Assert.Empty(gEmpty.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.Empty(gEmpty.Log.OfType<TestRecordingGraphics.DrawRectCall>());

            var gFull = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, fullCell!, gFull);
            Assert.NotEmpty(gFull.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
        }

        #endregion

        #region Caption Tests

        [Fact]
        public async Task TableCaption_DefaultCaptionSide_LaysOutAboveRows()
        {
            // caption-side's initial value is "top" (CSS 2.1 §17.4) - a <caption> with no explicit
            // caption-side should land above the row grid, not overlap or follow it.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; }
        td { border: 1px solid black; padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td id='cell1'>A</td><td>B</td></tr>
        <tr><td>C</td><td>D</td></tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            var caption = FindById(rootBox, "cap");
            var firstCell = FindById(rootBox, "cell1");

            Assert.NotNull(table);
            Assert.NotNull(caption);
            Assert.NotNull(firstCell);

            Assert.True(caption!.ActualBottom > caption.Location.Y, "Caption should have height");
            Assert.Equal(table!.Location.Y, caption.Location.Y, 1);
            Assert.True(firstCell!.Location.Y >= caption.ActualBottom - 0.01,
                "First row should be laid out below the caption");
        }

        [Fact]
        public async Task TableCaption_CaptionSideBottom_LaysOutBelowRows()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; }
        td { border: 1px solid black; padding: 4px; }
        caption { caption-side: bottom; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td>A</td><td>B</td></tr>
        <tr><td id='cell2'>C</td><td>D</td></tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            var caption = FindById(rootBox, "cap");
            var lastCell = FindById(rootBox, "cell2");

            Assert.NotNull(table);
            Assert.NotNull(caption);
            Assert.NotNull(lastCell);

            Assert.True(caption!.ActualBottom > caption.Location.Y, "Caption should have height");
            Assert.True(caption.Location.Y >= lastCell!.ActualBottom - 0.01,
                "Caption should be laid out below the last row");
            Assert.Equal(table!.ActualBottom, caption.ActualBottom, 1);
        }

        [Fact]
        public async Task TableCaption_SpansFullTableWidth()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; }
        td { border: 1px solid black; padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Short</caption>
        <tr><td>A longer cell than the caption text</td><td>B</td></tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            var caption = FindById(rootBox, "cap");

            Assert.NotNull(table);
            Assert.NotNull(caption);

            Assert.Equal(table!.Location.X, caption!.Location.X, 1);
            Assert.Equal(table.ActualRight, caption.ActualRight, 1);
        }

        [Fact]
        public async Task TableCaption_TableHasItsOwnBorder_CaptionStillAlignsWithBorderBox()
        {
            // The table element's own border (as opposed to a border on its cells) contributes to
            // GetWidthSum() - LayoutCaptionGroup must position the caption from the table's true
            // border-box left edge (Location.X), not from inside it (ClientLeft), or the caption
            // drifts right by exactly the table's own left border width.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; border: 4px solid black; }
        td { border: 1px solid black; padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Short</caption>
        <tr><td>A longer cell than the caption text</td><td>B</td></tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            var caption = FindById(rootBox, "cap");

            Assert.NotNull(table);
            Assert.NotNull(caption);

            Assert.Equal(table!.Location.X, caption!.Location.X, 1);
            Assert.Equal(table.ActualRight, caption.ActualRight, 1);
        }

        [Fact]
        public async Task TableCaption_TableHasOwnBorderAndBackground_GridDecorationBoxExcludesTopCaption()
        {
            // Issue #721 (CSS 2.1 §17.4): a <table>'s own border/background belong to the row grid
            // alone - a captioned table with a border/background on the <table> element itself must not
            // paint either around the caption too.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; border: 4px solid black; background: #eee; }
        td { border: 1px solid black; padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td>A</td><td>B</td></tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            var caption = FindById(rootBox, "cap");

            Assert.NotNull(table);
            Assert.NotNull(caption);

            var decoration = table!.TableGridDecorationBox;
            Assert.NotNull(decoration);

            // The grid decoration box - which now owns the table's own border/background paint - starts
            // right where the caption ends, not above it.
            Assert.Equal(caption!.ActualBottom, decoration!.Location.Y, 1);
            Assert.True(decoration.Location.Y > table.Location.Y,
                "Grid decoration box should start below the table's own (caption-inclusive) top");

            // Left/right are unaffected by the fix - still match the caption's own span.
            Assert.Equal(table.Location.X, decoration.Location.X, 1);
            Assert.Equal(table.ActualRight, decoration.ActualRight, 1);

            Assert.True(table.SuppressOwnBorderPaint);
            Assert.True(table.SuppressOwnBackgroundPaint);
        }

        [Fact]
        public async Task TableCaption_CaptionSideBottom_TableHasOwnBorderAndBackground_GridDecorationBoxExcludesCaption()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; border: 4px solid black; background: #eee; }
        td { border: 1px solid black; padding: 4px; }
        caption { caption-side: bottom; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td>A</td><td>B</td></tr>
    </table>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            var caption = FindById(rootBox, "cap");

            Assert.NotNull(table);
            Assert.NotNull(caption);

            var decoration = table!.TableGridDecorationBox;
            Assert.NotNull(decoration);

            // The grid decoration box ends right where the bottom caption starts, not below it.
            Assert.Equal(caption!.Location.Y, decoration!.ActualBottom, 1);
            Assert.True(decoration.ActualBottom < table.ActualBottom,
                "Grid decoration box should end above the table's own (caption-inclusive) bottom");

            // Regression guard: the table's own combined extent must end exactly at the caption's own
            // visible bottom edge, not one extra grid-border-width past it - a bottom caption's start
            // position already has that width folded in (it starts flush below the grid's own border),
            // so re-adding it when settling _tableBox.ActualBottom would double-count it.
            Assert.Equal(caption.ActualBottom + caption.ActualMarginBottom, table.ActualBottom, 1);
        }

        [Fact]
        public async Task TableCaption_NoOwnBorderOrBackground_TableStillPaintsNormally()
        {
            // A captioned table whose border only lives on its cells (the common real-world case, per
            // the accepted-gap note this fix closes) gets a grid decoration box too (it doesn't know in
            // advance that the border/background it copies are empty), but must not visibly change
            // anything: no border/background of its own to paint, on either box.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; }
        td { border: 1px solid black; padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td id='cell1'>A</td><td>B</td></tr>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;
            var firstCell = FindById(rootBox, "cell1")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, table, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawRectCall>());
            // The cell's own border still paints - only the table's (absent) own border/background is
            // the point of this test.
            var gCell = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, firstCell, gCell);
            Assert.NotEmpty(gCell.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
        }

        [Fact]
        public async Task TableCaption_TableHasOwnBorder_PaintDrawsBorderOnlyAtGridRect()
        {
            // Painting-level check, per this repo's own testing convention that a layout-geometry
            // assertion alone isn't proof for anything touching the RGraphics adapter layer: confirms
            // the actual paint calls draw the table's own border at the grid decoration box's rect, not
            // somewhere spanning the caption too (_tableBox's own border paint is suppressed - see
            // CssBox.SuppressOwnBorderPaint - so every border-shaped draw call in this log has to be the
            // decoration box's).
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; border: 4px solid rgb(10, 20, 30); }
        td { padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td>A</td></tr>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;
            var caption = FindById(rootBox, "cap")!;
            var decoration = table.TableGridDecorationBox;
            Assert.NotNull(decoration);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, table, g);

            var borderPolygons = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.NotEmpty(borderPolygons);
            Assert.All(borderPolygons, polygon =>
                Assert.All(polygon.Points, point =>
                    Assert.True(point.Y >= caption.ActualBottom - 0.5,
                        $"Table's own border must not paint above y={caption.ActualBottom} (the caption's own area), but a border point painted at y={point.Y}")));
        }

        [Fact]
        public async Task TableCaption_TableHasOwnBoxShadow_PaintsExactlyOnce()
        {
            // Regression guard: box-shadow lives in the same style area (BorderArea) that
            // AdoptBorderAndBackgroundFrom copies onto the grid decoration box, but PaintBoxShadows is
            // gated on SuppressOwnBorderPaint separately from the border stroke itself - without that
            // gate, both _tableBox and its decoration box would paint the shadow, once at each box's own
            // (different) rect.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; box-shadow: 5px 5px black; background: white; }
        td { padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td>A</td></tr>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;
            Assert.NotNull(table.TableGridDecorationBox);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, table, g);

            static bool IsOpaqueBlack(RColor c) => c.A == 255 && c is { R: 0, G: 0, B: 0 };
            var shadowRects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().Where(r => IsOpaqueBlack(r.Color)).ToList();
            Assert.Single(shadowRects);
        }

        [Fact]
        public async Task TableCaption_TableHasOwnBackground_PaintFillsOnlyTheGridRect()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        table { border-collapse: separate; border-spacing: 0; background: rgb(200, 200, 200); }
        td { padding: 4px; }
    </style>
</head>
<body>
    <table>
        <caption id='cap'>Table caption</caption>
        <tr><td>A</td></tr>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox)!;
            var decoration = table.TableGridDecorationBox;
            Assert.NotNull(decoration);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, table, g);

            var backgroundRects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            Assert.NotEmpty(backgroundRects);
            Assert.All(backgroundRects, rect =>
            {
                Assert.Equal(decoration!.Location.Y, rect.Y, 1);
                Assert.Equal(decoration.ActualBottom - decoration.Location.Y, rect.Height, 1);
            });
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
          if (box.Display.Value == DisplayMode.Table)
      return box;

 foreach (var child in box.Boxes)
       {
          var result = FindTableBox(child);
           if (result != null)
       return result;
  }

      return null;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var result = FindById(child, id);
                if (result != null)
                    return result;
            }

            return null;
        }

        #endregion
    }
}
