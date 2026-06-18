# PeachPDF.PdfSharpCore

[![NuGet Version](https://img.shields.io/nuget/v/PeachPDF.PdfSharpCore.svg)](https://www.nuget.org/packages/PeachPDF.PdfSharpCore/)
[![CI](https://github.com/ststeiger/PeachPDF.PdfSharpCore/actions/workflows/build.yml/badge.svg)](https://github.com/ststeiger/PeachPDF.PdfSharpCore/actions/workflows/build.yml)
[![codecov.io](https://codecov.io/github/ststeiger/PeachPDF.PdfSharpCore/coverage.svg?branch=master)](https://codecov.io/github/ststeiger/PeachPDF.PdfSharpCore?branch=master)

**PeachPDF.PdfSharpCore** is a partial port of [PdfSharp.Xamarin](https://github.com/roceh/PdfSharp.Xamarin/) for .NET Standard.
Additionally MigraDoc has been ported as well (from version 1.32).
Image support has been implemented with [StbImageSharp](https://github.com/StbSharp/StbImageSharp/).


## Table of Contents

- [Documentation](docs/index.md)
- [Example](#example)
- [Contributing](#contributing)
- [License](#license)


## Example

The following code snippet creates a simple PDF-file with the text 'Hello World!'.
The code is written for a .NET 8 console app with top level statements.

```csharp
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;

GlobalFontSettings.FontResolver = new FontResolver();

var document = new PdfDocument();
var page = document.AddPage();

var gfx = XGraphics.FromPdfPage(page);
var font = new XFont("Arial", 20, XFontStyle.Bold);

var textColor = XBrushes.Black;
var layout = new XRect(20, 20, page.Width, page.Height);
var format = XStringFormats.Center;

gfx.DrawString("Hello World!", font, textColor, layout, format);

document.Save("helloworld.pdf");
```

## Contributing

We appreciate feedback and contribution to this repo!


## License

This software is released under the MIT License. See the [LICENSE](LICENCE.md) file for more info.

PeachPDF.PdfSharpCore relies on the following MIT-licensed packages:

* *SharpZipLib*
* *StbImageSharp*
* *StbImageWriteSharp*