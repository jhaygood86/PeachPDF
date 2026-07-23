# Getting Started

PeachPDF is a pure .NET HTML → PDF rendering library. It doesn't shell out to Puppeteer, wkhtmltopdf, or any other external process — everything from HTML parsing to PDF output runs in-process, so it works in virtually any environment where .NET runs (containers, serverless, trimmed/AOT deployments) and benefits automatically from future .NET performance improvements.

**.NET support.** PeachPDF runs on every currently-supported version of .NET — **.NET 8 and newer** — following [Microsoft's .NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core). The NuGet package builds against the Long-Term-Support targets (`net8.0` and `net10.0`); the Standard-Term-Support releases in between (such as .NET 9) aren't separate build targets but are fully supported — the `net8.0` build runs on them unchanged.

## Image formats

Raster images are decoded through the host OS's native image codec where one is available, falling back to the bundled StbImageSharp decoder otherwise.

- **JPEG, PNG, GIF, and BMP** are always supported, on every OS — StbImageSharp alone is sufficient for all four, regardless of native codec availability.
- **WebP and AVIF** are supported only where the OS (or, on Linux, an installed library) provides a decoder for them — StbImageSharp cannot decode either format, so there is no fallback for these two specifically.
  - **Linux** requires `libwebp` and `libavif` to be installed on the host for WebP/AVIF decoding to work at all. These are commonly-available runtime packages (for example `libwebp7`/`libavif16` on current Ubuntu/Debian releases; on RHEL/CentOS Stream, `libavif` additionally requires the EPEL repository to be enabled). Without them, `.webp`/`.avif` images simply fail to render — everything else in the document still renders normally.
  - **Windows** decodes via Windows Imaging Component (WIC). JPEG/PNG/GIF/BMP are always available (WIC has covered these since Windows Vista). WebP and AVIF require the optional, free Microsoft Store codec packs — "WebP Image Extensions" and "AV1 Video Extension" respectively — which aren't guaranteed to be present on any given Windows installation.
  - **macOS** decodes via Image I/O. WebP requires macOS 11 (Big Sur) or later; AVIF requires macOS 13 (Ventura) or later.
  - **iOS** decodes via Image I/O. WebP requires iOS 14 or later; AVIF requires iOS 16 or later.
  - **Android** decodes via the NDK `AImageDecoder` API, which requires API level 30 (Android 11) or later for JPEG/PNG/GIF/WebP; AVIF additionally depends on the device having an AVIF-capable codec, generally available from API level 31 (Android 12) onward.
  - A known limitation on Windows: some AV1 Video Extension versions decode an AVIF image's color data correctly but do not expose its alpha channel through WIC's standard interface, so an AVIF with real transparency can render fully opaque there. This is a limitation of that OS codec, not of PeachPDF — WebP and PNG transparency are unaffected.

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

For more usage examples — rendering self-contained MHTML files, fetching HTML from a remote URI, sharing a parsed CSS context across renders, saving to disk, working with fonts, enabling tagged PDF (PDF/UA) output, and returning PDFs from ASP.NET Core or Azure Functions endpoints — see [Usage Examples](usage-examples.md).

> **Upgrading from an earlier version?** `px` lengths now resolve at their spec-correct physical size (`1px = 1/96in = 0.75pt`, matching browser print output) instead of the previous `1px = 1pt` convention — px-sized content shrinks by ×0.75 to its true CSS size. See [Length units](html-css-support.md#length-units) for the full unit contract and migration note.

## Thread safety

A `PdfGenerator` instance is not thread-safe. Use a **separate instance per thread** — e.g. one per web request or one per item in a parallel batch — which is safe and is the intended way to generate PDFs concurrently. See [Thread safety](usage-examples.md#thread-safety) in Usage Examples for the full explanation and code samples.

## Guides

- **[Architecture](architecture.md)** — how PeachPDF converts HTML to PDF: the HTML parser, DOM model, CSS parser, layout engine, painting layer, and PDF renderer.
- **[HTML & CSS Support](html-css-support.md)** — the full compatibility matrix of supported HTML elements, CSS properties, selectors, and at-rules, including notes on gaps and PeachPDF-specific extensions.
- **[Supported SVG Features](supported-svg-features.md)** — the full compatibility matrix for inline and standalone SVG, rendered as real vector PDF content rather than rasterized bitmaps.
- **[How PeachPDF Is Tested](testing.md)** — the automated test suite, the CI matrix, the diff-coverage gate, and the two-renderer rasterization checks that verify output actually renders correctly.
- **[Usage Examples](usage-examples.md)** — copy-pasteable examples: local HTML strings, MHTML files, HTTP fetching, shared CSS contexts, saving to disk, fonts, tagged PDF (PDF/UA) output, and returning PDFs from ASP.NET Core (controllers and Minimal APIs) and Azure Functions.
- **[Feature Showcase](showcase.html)** — real PDFs rendered by the current release at site build time, each paired with the exact HTML source it was generated from.
- **[API Reference](api/index.md)** — generated reference for every public type and member, always in sync with the latest source.
