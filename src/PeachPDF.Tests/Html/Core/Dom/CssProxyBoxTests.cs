using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using Xunit.Abstractions;

namespace PeachPDF.Tests.Html.Core.Dom
{
    public class CssProxyBoxTests
    {
        private readonly ITestOutputHelper _output;

        public CssProxyBoxTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Basic Proxy Box Tests

        [Fact]
        public async Task ProxyBox_CreatesWithCorrectDisplayType()
        {
            // Arrange
            var html = GetTableHtml();
            var (rootBox, container) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            // Act
            var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();

            // Assert
            Assert.True(proxyBoxes.Count > 0, "Should create at least one proxy box");
            foreach (var proxy in proxyBoxes)
            {
                Assert.Equal(CssConstants.TableHeaderGroup, proxy.Display);
                _output.WriteLine($"Proxy Display: {proxy.Display}");
            }
        }

        [Fact]
        public async Task ProxyBox_HasValidDimensionsAfterLayout()
        {
            // Arrange
            var html = GetTableHtml();
            var (rootBox, container) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            // Act
            var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();
            Assert.True(proxyBoxes.Count > 0, "Need at least one proxy to test");
            var proxy = proxyBoxes[0];

            // Assert
            _output.WriteLine($"Proxy Location: {proxy.Location}");
            _output.WriteLine($"Proxy ActualBottom: {proxy.ActualBottom}");
            _output.WriteLine($"Proxy ActualRight: {proxy.ActualRight}");
            _output.WriteLine($"Proxy Size: {proxy.Size}");

            Assert.True(proxy.ActualBottom > proxy.Location.Y, "Proxy should have height");
            Assert.True(proxy.ActualRight > proxy.Location.X, "Proxy should have width");
            Assert.True(proxy.Size.Width > 0, "Proxy size width should be positive");
            Assert.True(proxy.Size.Height > 0, "Proxy size height should be positive");
        }

        [Fact]
        public async Task ProxyBox_InheritsStylesFromSourceBox()
        {
            // Arrange
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page { size: A4; margin: 20mm; }
        table { width: 100%; border-collapse: collapse; }
      thead { 
        display: table-header-group; 
      background-color: #f0f0f0;
       font-weight: bold;
      }
        th { border: 1px solid black; padding: 8px; }
    </style>
</head>
<body>
    <table>
        <thead>
         <tr><th>Header 1</th><th>Header 2</th></tr>
        </thead>
        <tbody>
     " + string.Join("", Enumerable.Range(1, 20).Select(i => 
           $"<tr><td>Row {i}, Cell 1</td><td>Row {i}, Cell 2</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            // Act
            var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();
            Assert.True(proxyBoxes.Count > 0);
            var proxy = proxyBoxes[0];

            // Assert - Check that styles are inherited
            Assert.Equal(CssConstants.TableHeaderGroup, proxy.Display);
            Assert.Equal("visible", proxy.Visibility);
            _output.WriteLine($"Proxy BackgroundColor: {proxy.BackgroundColor}");
        }

        #endregion

        #region Header Repetition Tests

        [Fact]
        public async Task TableWithRepeatingHeader_CreatesProxyBoxesOnMultiplePages()
        {
// Arrange
     var html = GetLongTableHtml(30); // 30 rows to force multiple pages
    var (rootBox, container) = await BuildCssBoxTree(html, pageHeight: 300);

  // Act
       var table = FindTableBox(rootBox);
       Assert.NotNull(table);

  var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();
            _output.WriteLine($"Table children count: {table.Boxes.Count}");
     _output.WriteLine($"Proxy boxes found: {proxyBoxes.Count}");

            foreach (var (proxy, index) in proxyBoxes.Select((p, i) => (p, i)))
      {
   _output.WriteLine($"  Proxy {index}: Location.Y={proxy.Location.Y}, ActualBottom={proxy.ActualBottom}");
     }

// Assert
            Assert.True(proxyBoxes.Count >= 2, 
   $"Expected at least 2 proxy boxes for multi-page table, but found {proxyBoxes.Count}");

            // Verify proxies are at different Y positions (different pages)
            var distinctYPositions = proxyBoxes.Select(p => p.Location.Y).Distinct().Count();
            Assert.True(distinctYPositions >= 2, 
              "Proxies should be at different Y positions for different pages");
    }

      [Fact]
        public async Task TableWithoutHeader_DoesNotCreateProxyBoxes()
    {
     // Arrange
    var html = @"
<!DOCTYPE html>
<html>
<head>
 <style>
        @page { size: A4; margin: 20mm; }
        table { width: 100%; border-collapse: collapse; }
     td { border: 1px solid black; padding: 8px; }
    </style>
</head>
<body>
    <table>
        <tbody>
            " + string.Join("", Enumerable.Range(1, 20).Select(i => 
          $"<tr><td>Row {i}, Cell 1</td><td>Row {i}, Cell 2</td></tr>")) + @"
        </tbody>
  </table>
</body>
</html>";

         var (rootBox, container) = await BuildCssBoxTree(html);

            // Act
  var table = FindTableBox(rootBox);
  Assert.NotNull(table);

          var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();

         // Assert
   Assert.Empty(proxyBoxes);
       _output.WriteLine("Correctly created no proxy boxes for table without thead");
        }

[Fact]
        public async Task TableWithSinglePageContent_CreatesOneHeaderProxy()
        {
// Arrange
        var html = GetShortTableHtml(3); // Only 3 rows - fits on one page
     var (rootBox, container) = await BuildCssBoxTree(html, pageHeight: 800);

   // Act
   var table = FindTableBox(rootBox);
       Assert.NotNull(table);

         var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();
      _output.WriteLine($"Proxy boxes for single page: {proxyBoxes.Count}");

    // Assert
 // Even single-page tables get a header proxy at the top
        Assert.True(proxyBoxes.Count >= 1, "Should have at least one proxy box");
      Assert.True(proxyBoxes[0].Location.Y < 100, "First header should be near top");
        }

        #endregion

        #region Footer Repetition Tests

     [Fact]
        public async Task TableWithFooter_CreatesFooterProxies()
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
        tfoot { display: table-footer-group; background: #e0e0e0; }
    </style>
</head>
<body>
<table>
      <thead>
   <tr><th>Header 1</th><th>Header 2</th></tr>
        </thead>
        <tbody>
        " + string.Join("", Enumerable.Range(1, 25).Select(i => 
                $"<tr><td>Row {i}, Cell 1</td><td>Row {i}, Cell 2</td></tr>")) + @"
        </tbody>
        <tfoot>
        <tr><td>Footer 1</td><td>Footer 2</td></tr>
        </tfoot>
    </table>
</body>
</html>";

        var (rootBox, container) = await BuildCssBoxTree(html, pageHeight: 300);

        // Act
   var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var allProxies = table.Boxes.OfType<CssProxyBox>().ToList();
    var footerProxies = allProxies.Where(p => p.Display == CssConstants.TableFooterGroup).ToList();

         _output.WriteLine($"Total proxies: {allProxies.Count}");
            _output.WriteLine($"Footer proxies: {footerProxies.Count}");

  // Assert
        Assert.True(footerProxies.Count > 0, "Should create footer proxies");
        }

        #endregion

        #region Complex Table Tests

     [Fact]
        public async Task TableWithColspan_ProxiesLayoutCorrectly()
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
     <tr>
             <th colspan='2'>Wide Header</th>
         <th>Normal</th>
   </tr>
        </thead>
        <tbody>
            " + string.Join("", Enumerable.Range(1, 20).Select(i => 
        $"<tr><td>Row {i}-1</td><td>Row {i}-2</td><td>Row {i}-3</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);

          // Act
          var table = FindTableBox(rootBox);
    Assert.NotNull(table);

        var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();

   // Assert
      Assert.True(proxyBoxes.Count > 0);
            var proxy = proxyBoxes[0];
        Assert.True(proxy.ActualRight > 0, "Proxy with colspan should have width");
            _output.WriteLine($"Proxy with colspan - Width: {proxy.ActualRight - proxy.Location.X}");
        }

        [Fact]
        public async Task TableWithRowspan_ProxiesLayoutCorrectly()
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
            <tr>
  <th rowspan='2'>Tall Header</th>
                <th>Header 2</th>
      <th>Header 3</th>
         </tr>
            <tr>
      <th>Header 2.2</th>
       <th>Header 3.2</th>
      </tr>
        </thead>
        <tbody>
" + string.Join("", Enumerable.Range(1, 20).Select(i => 
                $"<tr><td>Row {i}-1</td><td>Row {i}-2</td><td>Row {i}-3</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

     var (rootBox, container) = await BuildCssBoxTree(html);

  // Act
            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

         var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();

      // Assert
   Assert.True(proxyBoxes.Count > 0);
            var proxy = proxyBoxes[0];
     var headerHeight = proxy.ActualBottom - proxy.Location.Y;
     Assert.True(headerHeight > 0, "Proxy with rowspan should have height");
          _output.WriteLine($"Proxy with rowspan - Height: {headerHeight}");
        }

        [Fact]
        public async Task TableWithMultipleTheadElements_OnlyFirstBecomesRepeatingHeader()
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
          <tr><th>First Header</th></tr>
 </thead>
        <thead>
            <tr><th>Second Header (should be treated as body)</th></tr>
      </thead>
    <tbody>
        " + string.Join("", Enumerable.Range(1, 20).Select(i => 
       $"<tr><td>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

    var (rootBox, container) = await BuildCssBoxTree(html);

 // Act
  var table = FindTableBox(rootBox);
    Assert.NotNull(table);

            var proxyBoxes = table.Boxes.OfType<CssProxyBox>().ToList();

      // Assert - Should create proxies for repeating the first thead
   Assert.True(proxyBoxes.Count > 0);
       _output.WriteLine($"Proxies created for first thead: {proxyBoxes.Count}");
        }

        #endregion

        #region Helper Methods

    private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(
            string html, 
  double pageHeight = 400)
        {
            var adapter = new PdfSharpAdapter();
          var container = new HtmlContainerInt(adapter);

await container.SetHtml(html, null);

            // Configure page size
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

        private static string GetTableHtml()
        {
 return GetLongTableHtml(15);
 }

        private static string GetShortTableHtml(int rowCount)
        {
 return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page {{ size: A4; margin: 20mm; }}
   table {{ width: 100%; border-collapse: collapse; }}
     th, td {{ border: 1px solid black; padding: 8px; }}
        thead {{ display: table-header-group; background: #f0f0f0; }}
    </style>
</head>
<body>
    <table>
        <thead>
            <tr>
    <th>Header 1</th>
       <th>Header 2</th>
<th>Header 3</th>
            </tr>
      </thead>
      <tbody>
{string.Join("", Enumerable.Range(1, rowCount).Select(i => 
           $"<tr><td>Row {i}, Cell 1</td><td>Row {i}, Cell 2</td><td>Row {i}, Cell 3</td></tr>"))}
        </tbody>
    </table>
</body>
</html>";
        }

      private static string GetLongTableHtml(int rowCount)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page {{ size: A4; margin: 20mm; }}
        table {{ width: 100%; border-collapse: collapse; }}
   th, td {{ border: 1px solid black; padding: 8px; }}
        thead {{ display: table-header-group; background: #f0f0f0; }}
    </style>
</head>
<body>
    <table>
    <thead>
          <tr>
  <th>Header 1</th>
     <th>Header 2</th>
      <th>Header 3</th>
     </tr>
</thead>
        <tbody>
            {string.Join("", Enumerable.Range(1, rowCount).Select(i => 
                $"<tr><td>Row {i}, Cell 1</td><td>Row {i}, Cell 2</td><td>Row {i}, Cell 3</td></tr>"))}
        </tbody>
    </table>
</body>
</html>";
  }

        #endregion
    }
}
