# Getting Started

PeachPDF is a pure .NET HTML → PDF rendering library. It doesn't shell out to Puppeteer, wkhtmltopdf, or any other external process — everything from HTML parsing to PDF output runs in-process, so it works in virtually any environment where .NET runs (containers, serverless, trimmed/AOT deployments) and benefits automatically from future .NET performance improvements.

**.NET support.** PeachPDF runs on every currently-supported version of .NET — **.NET 8 and newer** — following [Microsoft's .NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core). The NuGet package builds against the Long-Term-Support targets (`net8.0` and `net10.0`); the Standard-Term-Support releases in between (such as .NET 9) aren't separate build targets but are fully supported — the `net8.0` build runs on them unchanged.

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
- **[Usage Examples](usage-examples.md)** — copy-pasteable examples: local HTML strings, MHTML files, HTTP fetching, shared CSS contexts, saving to disk, fonts, tagged PDF (PDF/UA) output, and returning PDFs from ASP.NET Core (controllers and Minimal APIs) and Azure Functions.
- **[Feature Showcase](showcase.html)** — real PDFs rendered by the current release at site build time, each paired with the exact HTML source it was generated from.
- **[API Reference](api/index.md)** — generated reference for every public type and member, always in sync with the latest source.
