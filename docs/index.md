# PeachPDF

PeachPDF is a pure .NET HTML → PDF rendering library. It doesn't shell out to Puppeteer, wkhtmltopdf, or any other external process — everything from HTML parsing to PDF output runs in-process, so it works in virtually any environment where .NET runs (containers, serverless, trimmed/AOT deployments) and benefits automatically from future .NET performance improvements.

Targets .NET 8 and .NET 10.

## Features

- Native vector SVG rendering (inline `<svg>` and standalone `<img src="x.svg">`/`data:image/svg+xml`) — never rasterized
- CSS custom properties (`--foo`) and `var()`, including fallbacks and inheritance
- CSS math functions: `calc()`, `min()`, `max()`, `clamp()`
- 2D and 3D CSS transforms, and Flexbox layout (CSS Flexbox Level 1)
- All five CSS-wide keywords: `inherit`, `initial`, `unset`, `revert`, `revert-layer`
- Gradients with CSS Color Level 4 interpolation
- CSS Paged Media: `@page` rules, named pages, margin boxes, and running headers/footers
- Automatic PDF metadata extraction from HTML `<title>` and `<meta>` elements
- Web fonts (`@font-face`), custom fonts loaded from a stream, and system font discovery

## Guides

- **[Architecture](architecture.md)** — how PeachPDF converts HTML to PDF: the HTML parser, DOM model, CSS parser, layout engine, painting layer, and PDF renderer.
- **[HTML & CSS Support](html-css-support.md)** — the full compatibility matrix of supported HTML elements, CSS properties, selectors, and at-rules, including notes on gaps and PeachPDF-specific extensions.
- **[Supported SVG Features](supported-svg-features.md)** — the full compatibility matrix for inline and standalone SVG, rendered as real vector PDF content rather than rasterized bitmaps.
- **[Usage Examples](usage-examples.md)** — copy-pasteable examples: local HTML strings, MHTML files, HTTP fetching, shared CSS contexts, saving to disk, and returning PDFs from ASP.NET Core (controllers and Minimal APIs) and Azure Functions.
- **[API Reference](api/index.md)** — generated reference for every public type and member, always in sync with the latest source.

## Quick Start

Install the PeachPDF package from nuget.org:

```
dotnet add package PeachPDF
```

Render HTML to a PDF stream. All images and assets must be local to the file system or in `data:` URIs:

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

For more usage examples — rendering self-contained MHTML files, fetching HTML from a remote URI, sharing a parsed CSS context across renders, saving to disk, and returning PDFs from ASP.NET Core or Azure Functions endpoints — see [Usage Examples](usage-examples.md).

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
