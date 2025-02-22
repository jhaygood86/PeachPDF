// See https://aka.ms/new-console-template for more information

using PeachPDF;
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

var html = """
           <p style=""font-size:75%;""></p>
           <table style=""font-weight:bold;"">
           	<tr>
           		<td>Field 1</td>
           		<td>blank</td>
           	</tr>
           	<tr>
           		<td>Field 2:</td>
           		<td>blank</td>
           	</tr>
           	<tr>
           		<td>Field 3</td>
           		<td>blank</td>
           	</tr>
           	<tr>
           		<td>Field 4</td>
           		<td>Blank</td>
           	</tr>
           </table>
           """;

var document = await generator.GeneratePdf(html, pdfConfig);
document.Save(stream);

File.Delete("test_flyer.pdf");
File.WriteAllBytes("test_flyer.pdf", stream.ToArray());