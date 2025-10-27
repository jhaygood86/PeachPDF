using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests that actually render PDFs and verify header/footer repetition
    /// by checking the table structure and page count in the generated PDF.
    /// 
    /// NOTE: These tests verify the PDF is generated with multiple pages and proper structure.
    /// Extracting and decoding PDF text content is complex due to font encoding and would require
    /// a full PDF parsing library. Instead, we verify:
    /// 1. PDF has correct number of pages
    /// 2. Each page has content (not empty)
    /// 3. Table structure causes pagination as expected
    /// 
    /// For full text verification, manual inspection or a specialized PDF text extraction library
    /// would be needed.
    /// </summary>
    public class TableHeaderPdfRenderingTests
    {
        [Fact]
        public async Task TableHeader_MultiPageTable_GeneratesMultiplePages()
        {
            // HTML with a table that spans 2+ pages
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
      <tr><th>Header Column 1</th><th>Header Column 2</th><th>Header Column 3</th></tr>
        </thead>
 <tbody>";

            // Add 100 rows to force multiple pages
            for (int i = 1; i <= 100; i++)
            {
                html += $"<tr><td>Row {i} Col 1</td><td>Row {i} Col 2</td><td>Row {i} Col 3</td></tr>";
            }

            html += @"
 </tbody>
    </table>
</body>
</html>";

            // Generate the PDF
            var generator = new PdfGenerator();
            var pdfDocument = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            // Verify the PDF has at least 2 pages
            Assert.True(pdfDocument.PageCount >= 2,
          $"PDF should have at least 2 pages but has {pdfDocument.PageCount}");

            // Verify both pages have content
            Assert.True(PageHasContent(pdfDocument.Pages[0]), "Page 1 should have content");
            Assert.True(PageHasContent(pdfDocument.Pages[1]), "Page 2 should have content");

            // Verify the PDF structure looks correct
            Assert.NotNull(pdfDocument.Pages[0].Contents);
            Assert.NotNull(pdfDocument.Pages[1].Contents);
        }

        [Fact]
        public async Task TableHeader_ThreePageTable_GeneratesThreePages()
        {
            // HTML with a table that spans 3+ pages
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        @page { size: A4; margin: 20mm; }
     table { width: 100%; border-collapse: collapse; }
    th { background: #4CAF50; color: white; padding: 8px; border: 1px solid black; }
        td { padding: 8px; border: 1px solid black; }
    </style>
</head>
<body>
    <table>
        <thead>
  <tr><th>ID</th><th>Name</th><th>Department</th></tr>
        </thead>
        <tbody>";

            // Add 150 rows to span 3 pages
            for (int i = 1; i <= 150; i++)
            {
                html += $"<tr><td>{i}</td><td>Employee {i}</td><td>Dept {i % 10}</td></tr>";
            }

            html += @"
     </tbody>
    </table>
</body>
</html>";

            // Generate the PDF
            var generator = new PdfGenerator();
            var pdfDocument = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            // Verify we have at least 3 pages
            Assert.True(pdfDocument.PageCount >= 3,
                   $"PDF should have at least 3 pages but has {pdfDocument.PageCount}");

            // Verify all pages have content
            for (int pageNum = 0; pageNum < Math.Min(3, pdfDocument.PageCount); pageNum++)
            {
                Assert.True(PageHasContent(pdfDocument.Pages[pageNum]),
                    $"Page {pageNum + 1} should have content");
            }
        }

        [Fact]
        public async Task TableHeader_ComplexHeaderWithColspan_GeneratesMultiplePages()
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
        <th colspan='2'>Personal Information</th>
                <th colspan='2'>Contact Details</th>
</tr>
  <tr>
         <th>First Name</th>
        <th>Last Name</th>
      <th>Email</th>
       <th>Phone</th>
     </tr>
        </thead>
        <tbody>";

            // Add 80 rows to force pagination
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

            // Generate the PDF
            var generator = new PdfGenerator();
            var pdfDocument = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            // Verify at least 2 pages
            Assert.True(pdfDocument.PageCount >= 2,
        $"PDF should have at least 2 pages for complex header test");

            // Verify both pages have content
            Assert.True(PageHasContent(pdfDocument.Pages[0]), "Page 1 should have content");
            Assert.True(PageHasContent(pdfDocument.Pages[1]), "Page 2 should have content");
        }

        [Fact]
        public async Task TableFooter_MultiPageTable_GeneratesWithFooter()
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
            <tr><th>Item</th><th>Quantity</th><th>Price</th></tr>
        </thead>
        <tbody>";

            // Add 100 rows
            for (int i = 1; i <= 100; i++)
            {
                html += $@"
             <tr>
          <td>Item {i}</td>
      <td>{i}</td>
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

            // Generate the PDF
            var generator = new PdfGenerator();
            var pdfDocument = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            // Verify at least 2 pages
            Assert.True(pdfDocument.PageCount >= 2,
     $"PDF should have at least 2 pages");

            // Verify all pages have content
            for (int i = 0; i < pdfDocument.PageCount; i++)
            {
                Assert.True(PageHasContent(pdfDocument.Pages[i]),
                   $"Page {i + 1} should have content");
            }
        }

        /// <summary>
        /// Helper method to check if a PDF page has any content.
        /// </summary>
        private bool PageHasContent(PdfPage page)
        {
            try
            {
                var content = page.Contents;
                if (content == null)
                    return false;

                // Check if there are any content streams
                if (content.Elements.Count == 0)
                    return false;

                // Check if at least one stream has data
                foreach (var item in content.Elements)
                {
                    if (item is PdfReference reference && reference.Value is PdfDictionary dict)
                    {
                        var stream = dict.Stream;
                        if (stream != null && stream.Value != null && stream.Value.Length > 0)
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
