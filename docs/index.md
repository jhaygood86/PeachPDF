# PeachPDF
Peach PDF is a pure .NET HTML -> PDF rendering library. This library does not depend on Puppeter, wkhtmltopdf, or any other process to render the HTML to PDF. As a result, this should work in virtually any environment where .NET 8+ works. As a side benefit of being pure .NET, performance improvements in future .NET versions immediately benefit this library. 

## PeachPDF Requirements

- .NET 8

_Note: This package depends on PeachPDF.PdfSharpCore which has its own license, but the end result is still open source_

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
