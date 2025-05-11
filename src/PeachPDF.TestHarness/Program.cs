// See https://aka.ms/new-console-template for more information

using PeachPDF;
using PeachPDF.Html.Core;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore;

var httpClient = new HttpClient();

PdfGenerateConfig pdfConfig = new()
{
    PageSize = PageSize.A4,
    PageOrientation = PageOrientation.Portrait,
    ShrinkToFit = true
};

PdfGenerator generator = new();

var stream = new MemoryStream();

var css = """
          .box > * {
            border: 2px solid #608BA8;
            border-radius: 5px;
            background-color: #D3E5ED;
          }
          
          .box {
            border: 2px dotted rgb(96 139 168);
            display: flex;
            flex-direction: row-reverse
          }
          """;

var html = """
           <div class="box">
             <div>One</div>
             <div>Two</div>
             <div>Three <br />has <br />extra <br />text</div>
           </div>
           """;

var cssData = await generator.ParseStyleSheet(css);
var document = await generator.GeneratePdf(html, pdfConfig, cssData);
document.Save(stream);

File.Delete("test_flex.pdf");
File.WriteAllBytes("test_flex.pdf", stream.ToArray());