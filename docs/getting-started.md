# Getting Started

PeachPDF is a pure .NET HTML → PDF rendering library. It doesn't shell out to Puppeteer, wkhtmltopdf, or any other external process — everything from HTML parsing to PDF output runs in-process, so it works in virtually any environment where .NET runs (containers, serverless, trimmed/AOT deployments) and benefits automatically from future .NET performance improvements.

Targets .NET 8 and .NET 10.

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

## Thread safety

A `PdfGenerator` instance is not thread-safe. Use a **separate instance per thread** — e.g. one per web request or one per item in a parallel batch — which is safe and is the intended way to generate PDFs concurrently. See [Thread safety](usage-examples.md#thread-safety) in Usage Examples for the full explanation and code samples.

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

By default, PeachPDF uses Segoe UI on Windows. Segoe UI isn't installed by default on other platforms, so PeachPDF picks a different platform-appropriate default there instead (see the generic-family table below — the same "verify installed, else fall back" logic applies). You can remap the default font (or any other family) to another one using

```csharp
PdfGenerator generator = new();
generator.AddFontFamilyMapping("Segoe UI","sans-serif"); // or any other system installed font
```

### Generic families and `system-ui`

`serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`, and `system-ui` all resolve to a real installed font, matching actual Chromium behavior per platform rather than one invented cross-platform table:

| Generic | Windows | macOS | Android | Linux |
|---|---|---|---|---|
| `serif` | Times New Roman | Times | Noto Serif | *(delegated to fontconfig's own `serif` alias)* |
| `sans-serif` | Arial | Helvetica | Roboto | *(delegated to fontconfig's own `sans-serif` alias)* |
| `monospace` | Consolas | Menlo | Droid Sans Mono | *(delegated to fontconfig's own `monospace` alias)* |
| `cursive` | Comic Sans MS | Apple Chancery | Dancing Script | *(delegated to fontconfig's own `cursive` alias)* |
| `fantasy` | Impact | Papyrus | Dancing Script | *(delegated to fontconfig's own `fantasy` alias)* |
| `system-ui` | Segoe UI | *(platform default font)* | *(platform default font)* | *(platform default font)* |

On Linux, PeachPDF delegates directly to the system's own `fontconfig` library (`libfontconfig.so.1`) at startup — the managed equivalent of running `fc-match <generic>` — so the resolved family always matches whatever that distro's own font configuration actually maps each generic to, rather than a name that might not be installed. `system-ui` on Windows is an exact match for Chromium's own `system-ui` → Segoe UI resolution; on macOS/Linux/Android it's a pragmatic approximation using the platform's default font rather than true native system-UI-font detection (e.g. macOS's actual system-ui is the private San Francisco font, not something cleanly resolvable via plain TTF/OTF file discovery).

Every mapping above — including a custom one set via `AddFontFamilyMapping` — is verified against the fonts actually installed on the running machine before use; if the target isn't present, PeachPDF falls back to the platform's default font instead of silently substituting whatever arbitrary font happened to be discovered first.

### Font weight, style, and stretch matching

Requesting a `font-weight`/`font-style`/`font-stretch` PeachPDF can't find an exact registered face for doesn't just fall back to Regular:

- **Numeric weight** (`font-weight: 1`–`1000`) is matched to the *nearest* registered face for the family per CSS Fonts Level 4 §5.2 (the same algorithm real browsers use), not just an exact match or a coarse bold/not-bold split. `bolder`/`lighter` step relative to the parent element's own resolved weight, following the CSS2.1 §15.6 worked table.
- **`font-stretch`** (the 9 CSS Fonts Level 3 keywords) is matched the same way when a family has multiple registered faces at different stretch values.
- When no real face is close enough to the request, PeachPDF **synthesizes** a faux-bold (fill+stroke render mode) or faux-italic/oblique (glyph shear) rather than rendering with zero visual distinction. `oblique <angle>` (e.g. `oblique 10deg`) drives the exact synthesized shear amount when declared; otherwise a fixed default angle is used.
- An `@font-face` rule's own declared `font-weight`/`font-style`/`font-stretch` descriptors are authoritative for how that specific registered resource participates in this matching, independent of what the font file's own internal tables say — this is what makes multi-variant web-font families (separate `@font-face` rules per weight) resolve correctly.

### Adding custom fonts

The recommended way to install custom fonts is to install them into your operating system. PeachPDF picks up TrueType/OpenType fonts from the operating system's own installed fonts:

- **Windows**: `%SystemRoot%\Fonts` and `%LOCALAPPDATA%\Microsoft\Windows\Fonts`
- **macOS**: `/System/Library/Fonts`, `/Library/Fonts`, and `~/Library/Fonts`
- **Linux**: primarily the system's own `fontconfig` (`libfontconfig.so.1`) — the same mechanism the generic-family table above uses — which knows about every font directory that distro's `fonts.conf` configures, however unusual. If `libfontconfig.so.1` isn't available at all, PeachPDF falls back to scanning `/usr/share/fonts`, `/usr/local/share/fonts`, and `$HOME/.fonts` directly (parsing `/etc/fonts/fonts.conf` for any additional configured directories first)
- **Android**: `/system/fonts`, `/product/fonts`, and `/data/fonts`
- **iOS**: none — iOS sandboxes apps away from system font files entirely, and CoreText only exposes fonts as opaque handles with no API to extract raw file bytes. iOS apps must embed their own fonts and register them via `AddFontFromStream` below

You can also add a font at runtime by loading the font into a Stream, and then using the AddFontFromStream API:

```csharp
PdfGenerator generator = new();
await generator.AddFontFromStream(fontStream); // Supports TrueType (TTF), CFF, WOFF, and WOFF2 formats
```

Web fonts loaded via `@font-face` (`url()`, with a comma-separated fallback list, and `local()`) are also supported.

### Supported font formats

We support TrueType, CFF, WOFF, and WOFF2 font formats.

## Guides

- **[Architecture](architecture.md)** — how PeachPDF converts HTML to PDF: the HTML parser, DOM model, CSS parser, layout engine, painting layer, and PDF renderer.
- **[HTML & CSS Support](html-css-support.md)** — the full compatibility matrix of supported HTML elements, CSS properties, selectors, and at-rules, including notes on gaps and PeachPDF-specific extensions.
- **[Supported SVG Features](supported-svg-features.md)** — the full compatibility matrix for inline and standalone SVG, rendered as real vector PDF content rather than rasterized bitmaps.
- **[Usage Examples](usage-examples.md)** — copy-pasteable examples: local HTML strings, MHTML files, HTTP fetching, shared CSS contexts, saving to disk, and returning PDFs from ASP.NET Core (controllers and Minimal APIs) and Azure Functions.
- **[API Reference](api/index.md)** — generated reference for every public type and member, always in sync with the latest source.
