# PeachPDF
Peach PDF is a pure .NET HTML -> PDF rendering library. This library does not depend on Puppeter, wkhtmltopdf, or any other process to render the HTML to PDF. As a result, this should work in virtually any environment where .NET 8+ works. As a side benefit of being pure .NET, performance improvements in future .NET versions immediately benefit this library. 

## Features

- Native vector SVG rendering (inline `<svg>` and standalone `<img src="x.svg">`/`data:image/svg+xml`) — never rasterized
- CSS custom properties (`--foo`) and `var()`, including fallbacks and inheritance
- CSS math functions: `calc()`, `min()`, `max()`, `clamp()`
- 2D and 3D CSS transforms (`translate`, `scale`, `rotate`, `skew`, `matrix`, and their variants)
- Flexbox layout (CSS Flexbox Level 1)
- All five CSS-wide keywords: `inherit`, `initial`, `unset`, `revert`, `revert-layer`
- Gradients (`linear-gradient`, `radial-gradient`, `conic-gradient`, and repeating variants) with CSS Color Level 4 interpolation
- CSS Paged Media: `@page` rules, named pages, margin boxes, and running headers/footers via `string-set`/`string()`
- Automatic PDF metadata extraction from HTML `<title>` and `<meta>` elements
- Web fonts (`@font-face`), custom fonts loaded from a stream, and system font discovery

See [HTML & CSS Support](https://peachpdf.net/html-css-support.html) for the full compatibility matrix, and [Supported SVG Features](https://peachpdf.net/supported-svg-features.html) for the full SVG compatibility matrix.

## PeachPDF Requirements

- .NET 8 or .NET 10

_Note: This package embeds a fork of PdfSharpCore directly in its source tree; that fork carries its own license (see `src/PeachPDF/PdfSharpCore/LICENSE.md`), but the end result is still open source_

## Installing PeachPDF

Install the PeachPDF package from nuget.org

```
dotnet add package PeachPDF
```

## Using PeachPDF

### Simple example
Simple example to render PDF to a Stream. All images and assets must be local to the file on the file system or in data: URIs

```csharp
PdfGenerateConfig pdfConfig = new(){
  PageSize = PageSize.Letter,
  PageOrientation = PageOrientation.Portrait
};

PdfGenerator generator = new();

var stream = new MemoryStream();

var document = await generator.GeneratePdf(html, pdfConfig);
document.Save(stream);
```

### Rendering an MHTML file

You can generate PDF documents using self contained MHTML files (what Chrome calls "single page documents") by using the included MimeKitNetworkLoader

```csharp
PdfGenerateConfig pdfConfig = new(){
  PageSize = PageSize.Letter,
  PageOrientation = PageOrientation.Portrait,
  NetworkLoader = new MimeKitNetworkLoader(File.OpenRead("example.mhtml"))
};

PdfGenerator generator = new();

var stream = new MemoryStream();

// Passing null to GeneratePdf will load the HTML from the provided network loader instance instead
var document = await generator.GeneratePdf(null, pdfConfig);
document.Save(stream);
```

### Rendering HTML from a URI

You can also render HTML from the Internet to a PDF

```csharp
HttpClient httpClient = new();

PdfGenerateConfig pdfConfig = new(){
  PageSize = PageSize.Letter,
  PageOrientation = PageOrientation.Portrait,
  NetworkLoader = new HttpClientNetworkLoader(httpClient, new Uri("https://www.example.com"))
};

PdfGenerator generator = new();

var stream = new MemoryStream();

// Passing null to GeneratePdf will load the HTML from the provided network loader instance instead
var document = await generator.GeneratePdf(null, pdfConfig);
document.Save(stream);
```

Note that loading images using relative paths will default to the local file system unless an `HttpClientNetworkLoader` (or custom `RNetworkLoader`) with an appropriate `BaseUri` is provided, or if the HTML has a `<base>` element with an `href` set. Images will need to be in the current working directory when using the default loader.

## Fonts

### Default Font

By default, PeachPDF uses Segoe UI. Segoe UI is installed by default on Windows, but isn't necessarily available on other platforms. You can remap Segoe UI to another font using

```csharp
PdfGenerator generator = new();
generator.AddFontFamilyMapping("Segoe UI","sans-serif"); // or any other system installed font
```

### Adding custom fonts

The recommended way to install custom fonts is to install them into your operating system.
PeachPDF by default picks up TrueType fonts from the operating system (%SystemRoot%\Fonts and %LOCALAPPDATA%\Microsoft\Windows\Fonts on Windows, /Library/Fonts on Mac, and /usr/share/fonts, /usr/local/share/fonts/, and $HOME/.fonts on Linux)

You can also add a font at runtime by loading the font into a Stream, and then using the AddFontFromStream API:

```csharp
PdfGenerator generator = new();
await generator.AddFontFromStream(fontStream); // Supports TrueType (TTF), CFF, WOFF, and WOFF2 formats
```

Web fonts loaded via @font-face are also supported.

### Supported font formats

We support TrueType, CFF, WOFF, and WOFF2 font formats.
