# PeachPDF
Peach PDF is a pure .NET HTML -> PDF rendering library. This library does not depend on Puppeter, wkhtmltopdf, or any other process to render the HTML to PDF. As a result, this should work in virtually any environment where .NET 8+ works. As a side benefit of being pure .NET, performance improvements in future .NET versions immediately benefit this library. 

## Features

- Native vector SVG rendering (inline `<svg>`, standalone `<img src="x.svg">`/`data:image/svg+xml`, and as a `background-image`/`list-style-image` source) — never rasterized
- CSS custom properties (`--foo`) and `var()`, including fallbacks and inheritance
- CSS math functions: `calc()`, `min()`, `max()`, `clamp()`
- 2D and 3D CSS transforms (`translate`, `scale`, `rotate`, `skew`, `matrix`, and their variants)
- Flexbox layout (CSS Flexbox Level 1) and CSS Multi-column Layout
- All five CSS-wide keywords: `inherit`, `initial`, `unset`, `revert`, `revert-layer`
- Gradients (`linear-gradient`, `radial-gradient`, `conic-gradient`, and repeating variants) with CSS Color Level 4 interpolation
- CSS Paged Media: `@page` rules, named pages, margin boxes, and running headers/footers via `string-set`/`string()`
- Automatic [PDF metadata extraction](https://peachpdf.net/html-css-support.html#pdf-metadata-extraction) from HTML `<title>` and `<meta>` elements
- Optional Tagged PDF (PDF/UA) output — logical structure tree, automatic document language, CSS-driven tag mapping via `-peachpdf-pdf-tag-type` (see `PdfGenerateConfig.EnableTaggedPdf`)
- Web fonts (`@font-face`), custom fonts loaded from a stream, and system font discovery — with per-character font matching (`@font-face` `unicode-range` and coverage-based fallback across the `font-family` stack) and monochrome emoji / supplementary-plane (astral) text via `cmap` format-12 (outlines only; color-emoji tables are not embedded)

See [HTML & CSS Support](https://peachpdf.net/html-css-support.html) for the full compatibility matrix, and [Supported SVG Features](https://peachpdf.net/supported-svg-features.html) for the full SVG compatibility matrix.

> **Breaking change — spec-correct CSS pixels:** `px` lengths now resolve at the CSS-specified physical ratio (`1px = 1/96in = 0.75pt`) everywhere — layout, borders, images, and `@page` geometry — matching browser print output. Earlier versions treated `1px` as `1pt` for non-font lengths, rendering px-sized content 33% larger than its true CSS size; px-derived lengths shrink by ×0.75 when upgrading. Absolute units (`pt`/`mm`/`cm`/`in`/`pc`) and px font sizes (which already used the correct ratio) are unaffected. See [Length units](https://peachpdf.net/html-css-support.html#length-units).

## PeachPDF Requirements

- .NET 8 or newer (every currently-supported .NET version, per [Microsoft's .NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core))

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

### Rendering a local HTML file

You can render a local HTML file and all of its relative resources (stylesheets, images, fonts) from disk with `FileUriNetworkLoader`. It sets the base URL to the file's own location, just like opening the file in a browser.

```csharp
PdfGenerateConfig pdfConfig = new(){
  PageSize = PageSize.Letter,
  PageOrientation = PageOrientation.Portrait,
  NetworkLoader = new FileUriNetworkLoader("report/index.html")
};

PdfGenerator generator = new();

var stream = new MemoryStream();

// Passing null to GeneratePdf will load the HTML from the provided network loader instance instead
var document = await generator.GeneratePdf(null, pdfConfig);
document.Save(stream);
```

Note that loading resources using relative paths resolves against the configured `NetworkLoader`'s `BaseUri` (e.g. an `HttpClientNetworkLoader` or `FileUriNetworkLoader`), or a `<base href>` element if the HTML has one. With the default loader, relative paths resolve against the current working directory and load from the local file system. `file:` URIs are always loaded from disk regardless of which loader is configured, the same way `data:` URIs always are.

A local file's content type is resolved from the OS's own MIME mechanism by default (Windows shell associations, macOS/iOS Uniform Type Identifiers, or Linux `/etc/mime.types`), falling back to a built-in set for HTML, CSS, SVG, PeachPDF's raster image formats, and TTF/OTF/WOFF/WOFF2 fonts. For a local file with an extension outside that set, register its MIME type with the OS so PeachPDF can resolve it. See [Rendering a local HTML file](docs/usage-examples.md#rendering-a-local-html-file) for details.

## Command-line tool

If you just want to convert HTML to PDF from the shell, the standalone **`peachpdf`** command-line tool needs no .NET runtime — it is a single, self-contained Native AOT binary:

```bash
peachpdf report.html -o report.pdf
```

Prebuilt binaries for Windows (x64/ARM64), Linux (x64/ARM64), and macOS (Apple Silicon) are attached to each [GitHub Release](https://github.com/jhaygood86/PeachPDF/releases). See the [Command-Line Tool guide](docs/cli.md) for the full option reference.

## Thread safety

A `PdfGenerator` instance is **not thread-safe** — don't call it concurrently from multiple threads, and don't reuse one instance across overlapping renders.

Using a **separate `PdfGenerator` instance per thread** (one per web request, one per item in a parallel batch, etc.) is safe and is the intended way to generate PDFs concurrently:

```csharp
// Safe: each thread/task gets its own PdfGenerator.
var results = await Task.WhenAll(htmlDocuments.Select(async html =>
{
    var generator = new PdfGenerator();
    var document = await generator.GeneratePdf(html, pdfConfig);

    var stream = new MemoryStream();
    document.Save(stream);
    return stream;
}));
```

See [Thread safety](https://peachpdf.net/usage-examples.html#thread-safety) for more detail.

## Trimming and Native AOT

PeachPDF is trimming-safe and [Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)-compatible (`IsTrimmable` + `IsAotCompatible`). It publishes cleanly under `PublishTrimmed` and `PublishAot` — all platform interop uses source-generated `LibraryImport` marshalling, so nothing PeachPDF needs is trimmed away and there is no runtime code generation. See [Trimming and Native AOT](https://peachpdf.net/getting-started.html#trimming-and-native-aot) for more detail.

## Fonts

### Default Font

By default, PeachPDF uses Segoe UI on Windows. Segoe UI isn't installed by default on other platforms, so PeachPDF picks a different platform-appropriate default there instead. You can remap the default font (or any other family) to another one using

```csharp
PdfGenerator generator = new();
generator.AddFontFamilyMapping("Segoe UI","sans-serif"); // or any other system installed font
```

### Generic families and `system-ui`

`serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`, and `system-ui` resolve to a real installed font, matching actual Chromium behavior per platform (Times New Roman/Arial/Consolas/Comic Sans MS/Impact on Windows, Times/Helvetica/Menlo/Apple Chancery/Papyrus on macOS, Noto Serif/Roboto/Droid Sans Mono/Dancing Script on Android, and delegated to the system's own `fontconfig` on Linux) rather than one invented cross-platform table — see [Fonts](https://peachpdf.net/usage-examples.html#fonts) for the full breakdown. Every mapping, including custom ones set via `AddFontFamilyMapping`, is verified against what's actually installed before use.

### Font weight, style, and stretch matching

A requested `font-weight`/`font-style`/`font-stretch` PeachPDF can't find an exact face for is matched to the *nearest* registered face (CSS Fonts Level 4 §5.2 — the same algorithm real browsers use), not just Regular. When nothing close enough exists, PeachPDF synthesizes a faux-bold (fill+stroke) or faux-italic/oblique (glyph shear, following an explicit `oblique <angle>` when declared) instead of rendering with zero visual distinction. See [Fonts](https://peachpdf.net/usage-examples.html#fonts) for details.

### Adding custom fonts

The recommended way to install custom fonts is to install them into your operating system.
PeachPDF by default picks up TrueType/OpenType fonts from the operating system (`%SystemRoot%\Fonts` and `%LOCALAPPDATA%\Microsoft\Windows\Fonts` on Windows; `/System/Library/Fonts`, `/Library/Fonts`, and `~/Library/Fonts` on macOS; primarily the system's own `fontconfig` on Linux, falling back to `/usr/share/fonts`, `/usr/local/share/fonts`, and `$HOME/.fonts` if `fontconfig` isn't available; `/system/fonts`, `/product/fonts`, and `/data/fonts` on Android). iOS has no system font file discovery at all — apps must embed and register their own fonts via `AddFontFromStream` below.

You can also add a font at runtime by loading the font into a Stream, and then using the AddFontFromStream API:

```csharp
PdfGenerator generator = new();
await generator.AddFontFromStream(fontStream); // Supports TrueType (TTF), CFF, WOFF, and WOFF2 formats
```

Web fonts loaded via @font-face (`url()`, with fallback lists, and `local()`) are also supported.

### Supported font formats

We support TrueType, CFF, WOFF, and WOFF2 font formats.
