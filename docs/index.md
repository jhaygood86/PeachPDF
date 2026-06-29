# PeachPDF
Peach PDF is a pure .NET HTML -> PDF rendering library. This library does not depend on Puppeter, wkhtmltopdf, or any other process to render the HTML to PDF. As a result, this should work in virtually any environment where .NET 8+ works. As a side benefit of being pure .NET, performance improvements in future .NET versions immediately benefit this library. 

## PeachPDF Requirements

- .NET 8

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

## PDF Metadata

PeachPDF automatically extracts standard HTML metadata elements and writes them to the PDF info dictionary. No additional configuration is required — just include the elements in your HTML `<head>`.

| HTML source | PDF info field |
|---|---|
| `<title>` inner text | Title |
| `<meta name="author" content="...">` | Author |
| `<meta name="subject" content="...">` | Subject |
| `<meta name="keywords" content="...">` | Keywords |
| `<meta name="date" content="...">` | Creation date (parsed via `DateTime.TryParse`) |
| `<meta name="generator" content="...">` | Creator |

The **Producer** and **Creator** fields both default to `PeachPDF {version}` when no `<meta name="generator">` is present. The Producer field always identifies PeachPDF as the PDF converter regardless of any generator meta tag.

Example:

```html
<!DOCTYPE html>
<html>
<head>
  <title>Quarterly Report</title>
  <meta name="author" content="Finance Team">
  <meta name="subject" content="Q1 2025 Results">
  <meta name="keywords" content="finance, quarterly, report">
  <meta name="date" content="2025-04-01">
</head>
<body>
  <!-- document content -->
</body>
</html>
```

## Architecture

See [Architecture](architecture.md) for an overview of how PeachPDF converts HTML to PDF, covering the HTML parser, DOM model, CSS parser, layout engine, painting layer, and PDF renderer.

## HTML & CSS Support

See [HTML & CSS Support](html-css-support.md) for a full list of supported HTML elements and CSS properties, including notes on gaps and PeachPDF-specific extensions.

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
