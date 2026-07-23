using PeachPDF;

// A minimal end-to-end render exercised under NativeAOT: build a PdfGenerator, render an HTML string
// (touching the HTML parser, CSS cascade, layout, font resolution and the PDF writer) and confirm the
// output is a real PDF. Exits non-zero on any failure so a publish + run doubles as a CI smoke test.

const string html =
    "<html><body><h1 style=\"color:teal\">Hello, PeachPDF</h1>" +
    "<p style=\"font-size:12pt\">Trimming + NativeAOT smoke test.</p></body></html>";

var config = new PdfGenerateConfig
{
    PageSize = PageSize.Letter,
    PageOrientation = PageOrientation.Portrait,
};

var generator = new PdfGenerator();

using var stream = new MemoryStream();
var document = await generator.GeneratePdf(html, config);
document.Save(stream);

var bytes = stream.ToArray();

if (bytes.Length < 5 || bytes[0] != (byte)'%' || bytes[1] != (byte)'P' || bytes[2] != (byte)'D' || bytes[3] != (byte)'F')
{
    await Console.Error.WriteLineAsync($"AOT smoke test FAILED: output is not a PDF ({bytes.Length} bytes).");
    return 1;
}

Console.WriteLine($"AOT smoke test OK: generated a {bytes.Length}-byte PDF.");
return 0;
