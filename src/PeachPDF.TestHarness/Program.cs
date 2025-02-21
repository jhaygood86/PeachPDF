// See https://aka.ms/new-console-template for more information

using PeachPDF;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore;

var httpClient = new HttpClient();

PdfGenerateConfig pdfConfig = new()
{
    PageSize = PageSize.A4,
    PageOrientation = PageOrientation.Landscape,
    ScaleToPageSize = true
};

PdfGenerator generator = new();

var stream = new MemoryStream();

var html = """
           <html>

           <head>
               <title></title>
               <style>       
                   body {
                       font-family: Verdana;
                       margin: 0;
                   }
                   
                   table {
                       border-collapse: collapse;
                       border: 1px solid grey;
                       border-spacing: 0px !important
                   }
                   
                   tr {
                       border: 1px solid grey;
                   }
                   
                   td {
                       border: 1px solid grey;
                   }
                   
                   th {
                       border: 1px solid grey;
                   }
               </style>

           </head>

           <body>
           	<div class="Title">
           		<h1>Certificate</h1>
           	</div>
           
           	<div class="Section1">
           		<p></p>
           		<br/>
           <table class="RecentWo"><thead><tr><th>Plant</th><th>Line</th><th>Order</th><th>Run No</th><th>Job Status</th><th>Material</th><th>Description</th><th>Start-End Operation</th><th>Start</th><th>Job Duration%</th><th>Setup Time</th><th># Comments</th><th>Quantity Ordered</th><th>Good Quantity</th><th>#IP Out</th><th>#Insp</th><th>PO Status</th><th># Attachments</th><th>Start User</th><th>PO Duration</th></tr></thead><tbody><tr></tr></tbody></table>
           
           	</div>
            </body>
           </html>
           """;

var document = await generator.GeneratePdf(html, pdfConfig);
document.Save(stream);

File.Delete("test_same_page_links.pdf");
File.WriteAllBytes("test_same_page_links.pdf", stream.ToArray());