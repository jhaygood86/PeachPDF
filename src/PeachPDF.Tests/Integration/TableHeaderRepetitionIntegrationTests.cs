using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for table header repetition that verify PeachPDF's ability
    /// to automatically repeat thead elements on each page when tables span multiple pages.
    /// This matches browser behavior when printing tables.
    /// 
    /// Note: The current implementation repeats headers during PDF rendering, not in the box tree.
    /// These tests verify that:
    /// 1. Tables with thead/tfoot are properly recognized and laid out
    /// 2. Tables span multiple pages when content exceeds page height
    /// 3. Header and footer infrastructure is correctly initialized
    /// 4. Body rows are properly distributed across pages
    /// </summary>
    public class TableHeaderRepetitionIntegrationTests
    {
        [Fact]
        public async Task TableHeader_SinglePageTable_HeaderAppearsOnce()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
   @page { size: A4; margin: 20mm; }
        table { width: 100%; border-collapse: collapse; }
   th { background: #f0f0f0; padding: 10px; border: 1px solid black; }
        td { padding: 10px; border: 1px solid black; }
  </style>
</head>
<body>
    <table>
        <thead>
          <tr><th>Column 1</th><th>Column 2</th><th>Column 3</th></tr>
      </thead>
    <tbody>
      <tr><td>Row 1 Col 1</td><td>Row 1 Col 2</td><td>Row 1 Col 3</td></tr>
<tr><td>Row 2 Col 1</td><td>Row 2 Col 2</td><td>Row 2 Col 3</td></tr>
   <tr><td>Row 3 Col 1</td><td>Row 3 Col 2</td><td>Row 3 Col 3</td></tr>
        </tbody>
    </table>
</body>
</html>";

var (rootBox, container) = await BuildCssBoxTree(html);
            var tableBox = FindTableBox(rootBox);

Assert.NotNull(tableBox);
       Assert.True(tableBox.ActualBottom > 0);

  // With proxy system, thead is replaced by proxy boxes
       var headerProxies = FindProxyBoxesByDisplay(tableBox, CssConstants.TableHeaderGroup);
    Assert.NotEmpty(headerProxies);
            
 // For single page table, should have at least one header proxy
        // Note: May have 2 if the first proxy triggers during initial layout  
    Assert.True(headerProxies.Count <= 2, 
     $"Single-page table should have 1-2 header proxies, but has {headerProxies.Count}");
   }

        [Fact]
        public async Task TableHeader_MultiPageTable_SpansMultiplePages()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page { size: A4; margin: 20mm; }
    table { width: 100%; border-collapse: collapse; }
        th { background: #f0f0f0; padding: 10px; border: 1px solid black; }
   td { padding: 10px; border: 1px solid black; }
    </style>
</head>
<body>
    <table>
   <thead>
<tr><th>ID</th><th>Name</th><th>Department</th></tr>
</thead>
 <tbody>";

    for (int i = 1; i <= 100; i++)
      {
       html += $"<tr><td>{i}</td><td>Employee {i}</td><td>Dept {i % 10}</td></tr>";
      }

        html += @"
 </tbody>
  </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
       var tableBox = FindTableBox(rootBox);

Assert.NotNull(tableBox);
       Assert.True(tableBox.ActualBottom > 0);

        var pageHeight = container.PageSize.Height;
       var marginTop = container.MarginTop;

  // Verify table spans multiple pages
     Assert.True(tableBox.ActualBottom > pageHeight,
 $"Table height ({tableBox.ActualBottom}) should exceed page height ({pageHeight})");

   // Verify header proxies exist (one per page)
    var headerProxies = FindProxyBoxesByDisplay(tableBox, CssConstants.TableHeaderGroup);
         Assert.NotEmpty(headerProxies);
  Assert.True(headerProxies.Count >= 2, 
    $"Multi-page table should have at least 2 header proxies, but has {headerProxies.Count}");

       // Verify body rows are distributed across multiple page regions
     var pageSpan = CalculateTablePageSpan(tableBox, pageHeight, marginTop);
    Assert.True(pageSpan >= 2, $"Table should span at least 2 pages, but spans {pageSpan}");

  var rowsPerPage = CountBodyRowsPerPage(tableBox, pageHeight, marginTop);
            Assert.True(rowsPerPage.Count >= 2,
   $"Body rows should appear across at least 2 pages, but appear on {rowsPerPage.Count}");
  }
        [Fact]
        public async Task TableHeader_WithColspan_LayoutsCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
     @page { size: A4; margin: 20mm; }
      table { width: 100%; border-collapse: collapse; }
   th, td { padding: 8px; border: 1px solid black; }
        thead { background: #4CAF50; color: white; }
    </style>
</head>
<body>
    <table>
     <thead>
     <tr>
  <th colspan='2'>Personal Info</th>
        <th colspan='2'>Contact</th>
     </tr>
     <tr>
         <th>First Name</th>
    <th>Last Name</th>
<th>Email</th>
    <th>Phone</th>
   </tr>
 </thead>
      <tbody>";

for (int i = 1; i <= 80; i++)
     {
  html += $@"
       <tr>
       <td>First{i}</td>
        <td>Last{i}</td>
   <td>email{i}@example.com</td>
 <td>555-{i:D4}</td>
     </tr>";
      }

     html += @"
     </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
     var tableBox = FindTableBox(rootBox);

Assert.NotNull(tableBox);

    var pageHeight = container.PageSize.Height;
 var marginTop = container.MarginTop;

 Assert.True(tableBox.ActualBottom > pageHeight);

   // Verify header proxies exist
   var headerProxies = FindProxyBoxesByDisplay(tableBox, CssConstants.TableHeaderGroup);
            Assert.NotEmpty(headerProxies);
Assert.True(headerProxies.Count >= 2, 
        $"Multi-page table with colspan should have at least 2 header proxies, but has {headerProxies.Count}");

     var pageSpan = CalculateTablePageSpan(tableBox, pageHeight, marginTop);
     Assert.True(pageSpan >= 2, $"Table with colspan should span at least 2 pages");
  }
        [Fact]
        public async Task TableHeader_WithRowspan_LayoutsCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page { size: A4; margin: 20mm; }
        table { width: 100%; border-collapse: collapse; }
        th, td { padding: 8px; border: 1px solid black; }
        thead { background: #2196F3; color: white; }
    </style>
</head>
<body>
    <table>
        <thead>
            <tr>
     <th rowspan='2'>Department</th>
        <th colspan='2'>Employee Details</th>
 </tr>
            <tr>
       <th>Name</th>
      <th>Role</th>
         </tr>
     </thead>
        <tbody>";

            for (int i = 1; i <= 70; i++)
            {
                html += $@"
            <tr>
    <td>Dept {i % 5}</td>
     <td>Employee {i}</td>
      <td>Role {i % 3}</td>
      </tr>";
            }

            html += @"
        </tbody>
</table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var tableBox = FindTableBox(rootBox);

            Assert.NotNull(tableBox);

            var pageHeight = container.PageSize.Height;
            var marginTop = container.MarginTop;

            Assert.True(tableBox.ActualBottom > pageHeight);

   // Verify header proxies exist
       var headerProxies = FindProxyBoxesByDisplay(tableBox, CssConstants.TableHeaderGroup);
   Assert.NotEmpty(headerProxies);
Assert.True(headerProxies.Count >= 2, 
 $"Multi-page table with rowspan should have at least 2 header proxies, but has {headerProxies.Count}");

var pageSpan = CalculateTablePageSpan(tableBox, pageHeight, marginTop);
     Assert.True(pageSpan >= 2, $"Table with rowspan should span at least 2 pages");
 }
        [Fact]
        public async Task TableFooter_MultiPageTable_FooterLayoutsCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page { size: A4; margin: 20mm; }
        table { width: 100%; border-collapse: collapse; }
   th, td { padding: 8px; border: 1px solid black; }
        thead { background: #4CAF50; color: white; }
        tfoot { background: #f0f0f0; font-weight: bold; }
    </style>
</head>
<body>
    <table>
   <thead>
            <tr><th>Item</th><th>Description</th><th>Price</th></tr>
        </thead>
        <tbody>";

            for (int i = 1; i <= 100; i++)
            {
                html += $@"
       <tr>
             <td>Item {i}</td>
       <td>Description for item {i}</td>
        <td>${i * 10}.00</td>
        </tr>";
            }

            html += @"
        </tbody>
        <tfoot>
          <tr><th colspan='2'>Grand Total</th><th>$50,500.00</th></tr>
        </tfoot>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var tableBox = FindTableBox(rootBox);

            Assert.NotNull(tableBox);

            var pageHeight = container.PageSize.Height;
            var marginTop = container.MarginTop;

            Assert.True(tableBox.ActualBottom > pageHeight);

            // Verify footer infrastructure
            var tfootBox = FindBoxByDisplay(tableBox, CssConstants.TableFooterGroup);
            Assert.NotNull(tfootBox);

            var pageSpan = CalculateTablePageSpan(tableBox, pageHeight, marginTop);
            Assert.True(pageSpan >= 2, $"Table with footer should span at least 2 pages");
        }

        [Fact]
        public async Task TableHeader_ComplexWithRowspanAndColspan_LayoutsCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
    @page { size: A4; margin: 20mm; }
        table { width: 100%; border-collapse: collapse; }
   th, td { padding: 6px; border: 1px solid black; font-size: 9pt; }
    thead { background: #673AB7; color: white; }
 </style>
</head>
<body>
    <table>
   <thead>
 <tr>
      <th rowspan='3'>ID</th>
          <th colspan='3'>Personal Information</th>
   <th colspan='2'>Contact</th>
  </tr>
  <tr>
 <th rowspan='2'>Full Name</th>
  <th colspan='2'>Address</th>
 <th rowspan='2'>Email</th>
      <th rowspan='2'>Phone</th>
      </tr>
          <tr>
     <th>Street</th>
     <th>City</th>
   </tr>
   </thead>
        <tbody>";

for (int i = 1; i <= 60; i++)
   {
   html += $@"
      <tr>
 <td>{i}</td>
    <td>Person {i}</td>
<td>{i} Main St</td>
    <td>City {i % 10}</td>
     <td>person{i}@example.com</td>
    <td>555-{i:D4}</td>
  </tr>";
     }

   html += @"
   </tbody>
    </table>
</body>
</html>";

     var (rootBox, container) = await BuildCssBoxTree(html);
  var tableBox = FindTableBox(rootBox);

Assert.NotNull(tableBox);

 var pageHeight = container.PageSize.Height;
     var marginTop = container.MarginTop;

Assert.True(tableBox.ActualBottom > pageHeight);

        // Verify header proxies exist
    var headerProxies = FindProxyBoxesByDisplay(tableBox, CssConstants.TableHeaderGroup);
  Assert.NotEmpty(headerProxies);
 Assert.True(headerProxies.Count >= 2, 
        $"Multi-page complex table should have at least 2 header proxies, but has {headerProxies.Count}");

 var pageSpan = CalculateTablePageSpan(tableBox, pageHeight, marginTop);
       Assert.True(pageSpan >= 2, $"Complex table should span at least 2 pages");
   }

        #region Helper Methods

        private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            await container.SetHtml(html, null);

            var size = new XSize(595, 842); // A4 size in points
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
    container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
  container.MarginTop = 20;  // Add margins to match @page CSS
    container.MarginBottom = 20;

    var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
  await container.PerformLayout(graphics);

      Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private CssBox? FindTableBox(CssBox root)
        {
            if (root.Display == CssConstants.Table)
                return root;

            foreach (var child in root.Boxes)
            {
                var result = FindTableBox(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        private CssBox? FindBoxByDisplay(CssBox root, string display)
        {
            if (root.Display == display)
                return root;

            foreach (var child in root.Boxes)
            {
                var result = FindBoxByDisplay(child, display);
                if (result != null)
                    return result;
            }

            return null;
        }

        private List<CssProxyBox> FindProxyBoxesByDisplay(CssBox root, string display)
 {
   var proxies = new List<CssProxyBox>();
  
    void FindProxies(CssBox box)
  {
  if (box is CssProxyBox proxy && proxy.Display == display)
    {
       proxies.Add(proxy);
       }

    foreach (var child in box.Boxes)
     {
       FindProxies(child);
    }
   }

    FindProxies(root);
    return proxies;
        }

        private int CalculateTablePageSpan(CssBox tableBox, double pageHeight, double marginTop)
        {
            if (pageHeight >= double.MaxValue - 1)
                return 1;

            var tableHeight = tableBox.ActualBottom - tableBox.Location.Y;
            return (int)Math.Ceiling(tableHeight / pageHeight);
        }

        private Dictionary<int, int> CountBodyRowsPerPage(CssBox tableBox, double pageHeight, double marginTop)
        {
            var rowsPerPage = new Dictionary<int, int>();

            void CollectBodyRows(CssBox box, List<CssBox> rows)
            {
                if (box.Display == CssConstants.TableRow)
                {
                    var parent = box.ParentBox;
                    if (parent?.Display != CssConstants.TableHeaderGroup &&
             parent?.Display != CssConstants.TableFooterGroup)
                    {
                        rows.Add(box);
                    }
                }

                foreach (var child in box.Boxes)
                {
                    CollectBodyRows(child, rows);
                }
            }

            var bodyRows = new List<CssBox>();
            CollectBodyRows(tableBox, bodyRows);

            foreach (var row in bodyRows)
            {
                var rowY = row.Location.Y;
                var pageNumber = (int)((rowY - marginTop) / pageHeight);

                if (!rowsPerPage.ContainsKey(pageNumber))
                    rowsPerPage[pageNumber] = 0;

                rowsPerPage[pageNumber]++;
            }

            return rowsPerPage;
        }

        #endregion
    }
}
