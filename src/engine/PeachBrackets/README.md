# PeachBrackets

The HTML, CSS, SVG and MathML engine behind [PeachPDF](https://peachpdf.net/), as a library of its own. It parses a document,
resolves the cascade, lays it out into pages and paints each page onto any
[PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/) `Canvas`: a PDF, a bitmap, or one you implement. It
runs in-process in pure .NET, with no browser and no external tool, and it does not depend on the PDF writer.

```csharp
using var document = await HtmlDocument.LoadAsync(html, renderContext, new HtmlDocumentOptions());
await document.LayoutAsync(measuringCanvas);

for (var i = 0; i < document.Pages.Count; i++)
{
    await document.PaintPageAsync(i, canvasForPage);
}
```

The links, outline, form controls, logical structure and the words that were clipped are available alongside the pixels, so a
consumer that writes a format with those concepts can carry them over. A tree built in code (not parsed from HTML) is
supported through `PeachBrackets.Building`.

```bash
dotnet add package PeachBrackets
```

See the [PeachBrackets guide](https://peachpdf.net/peachbrackets.html) for the pipeline, the options and the extension seams. To
render HTML to a PDF file, use [PeachPDF](https://www.nuget.org/packages/PeachPDF/), which drives this package for you.
