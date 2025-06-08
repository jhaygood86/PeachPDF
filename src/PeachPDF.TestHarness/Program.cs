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
    ShrinkToFit = true,
    NetworkLoader = new HttpClientNetworkLoader(httpClient, "https://www.ballentinepointe.com")
};

PdfGenerator generator = new();

var stream = new MemoryStream();

var html = """
           
           
           <!DOCTYPE html>
           <html>
               <head>
                   <style>
                   h2 { string-set: header content(before) ':' content(text) } 
                   </style>
               </head>
               <body>
                 <h2>Chapter 1: The Machine</h2>
               </body>
           </html>
           
           
           """;

var font = File.OpenRead("NotoSerif-Regular.ttf");

await generator.AddFontFromStream(font);
generator.AddFontFamilyMapping("Segoe UI","Noto Serif");

var cssData = await generator.ParseStyleSheet("", false);

var document = await generator.GeneratePdf(html, pdfConfig, cssData);
document.Save(stream);

File.Delete("test_statement.pdf");
File.WriteAllBytes("test_statement.pdf", stream.ToArray());