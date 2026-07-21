# Usage Examples

This page collects practical, copy-pasteable examples for common PeachPDF scenarios, including hosting it behind an HTTP endpoint. For the full list of supported HTML elements and CSS properties, see [HTML & CSS Support](html-css-support.md); for how the rendering pipeline works internally, see [Architecture](architecture.md).

All examples assume:

```csharp
using PeachPDF;
using PeachPDF.Network;
```

## Contents

- [Thread safety](#thread-safety)
- [Rendering HTML from a local string](#rendering-html-from-a-local-string)
- [Rendering an MHTML file](#rendering-an-mhtml-file)
- [Fetching HTML over HTTP](#fetching-html-over-http)
- [Sharing a parsed CSS context across renders](#sharing-a-parsed-css-context-across-renders)
- [Saving a PDF to a file](#saving-a-pdf-to-a-file)
- [Fonts](#fonts)
- [Enabling tagged PDF (PDF/UA) output](#enabling-tagged-pdf-pdfua-output)
- [ASP.NET Core controller endpoint](#aspnet-core-controller-endpoint)
- [ASP.NET Core Minimal API endpoint](#aspnet-core-minimal-api-endpoint)
- [Azure Functions (isolated worker) HTTP handler](#azure-functions-isolated-worker-http-handler)

## Thread safety

A `PdfGenerator` instance is **not thread-safe**: don't call methods on the same instance concurrently from multiple threads, and don't reuse one instance across overlapping renders.

Using a **separate `PdfGenerator` instance per thread** — one per incoming web request, one per item in a parallel batch — is safe, and is the intended way to generate PDFs concurrently. Every one of the ASP.NET Core, Minimal API, and Azure Functions examples below already follows this pattern by constructing a new `PdfGenerator` inside the request handler; that's not incidental, it's the correct usage.

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

```csharp
// Unsafe: sharing one PdfGenerator across concurrent renders.
var generator = new PdfGenerator();

var results = await Task.WhenAll(htmlDocuments.Select(async html =>
{
    var document = await generator.GeneratePdf(html, pdfConfig); // do not do this
    var stream = new MemoryStream();
    document.Save(stream);
    return stream;
}));
```

Every custom font registered on a `PdfGenerator` — via `@font-face` or `AddFontFromStream` — is owned exclusively by that instance, so two instances that register *different* font bytes under the *same* font family name (a realistic multi-tenant scenario) never collide. Pure system-font data (fonts already installed on the machine) is the one thing genuinely shared across instances internally, since it's read-only and safe to share — this is what actually supports many instances being used concurrently, one per thread.

## Rendering HTML from a local string

The simplest case: render an in-memory HTML string to a PDF stream. All images and assets must be local to the file system or in `data:` URIs, since no `NetworkLoader` is configured.

```csharp
var html = "<html><body><h1>Hello, PeachPDF</h1></body></html>";

var pdfConfig = new PdfGenerateConfig
{
    PageSize = PageSize.Letter,
    PageOrientation = PageOrientation.Portrait
};

var generator = new PdfGenerator();

var stream = new MemoryStream();
var document = await generator.GeneratePdf(html, pdfConfig);
document.Save(stream);
```

## Rendering an MHTML file

Self-contained MHTML archives (what Chrome calls "single page documents") bundle the HTML plus every referenced image, stylesheet, and font into one file. `MimeKitNetworkLoader` resolves all of those references from the archive, so nothing is fetched from disk or the network.

```csharp
using var mhtmlStream = File.OpenRead("example.mhtml");

var pdfConfig = new PdfGenerateConfig
{
    PageSize = PageSize.Letter,
    PageOrientation = PageOrientation.Portrait,
    NetworkLoader = new MimeKitNetworkLoader(mhtmlStream)
};

var generator = new PdfGenerator();

var stream = new MemoryStream();

// Passing null to GeneratePdf loads the HTML from the configured NetworkLoader instead
var document = await generator.GeneratePdf(null, pdfConfig);
document.Save(stream);
```

## Fetching HTML over HTTP

`HttpClientNetworkLoader` fetches the root document and every referenced resource (stylesheets, images) through a caller-supplied `HttpClient`, so you control headers, authentication, proxies, and timeouts.

```csharp
var httpClient = new HttpClient();

var pdfConfig = new PdfGenerateConfig
{
    PageSize = PageSize.Letter,
    PageOrientation = PageOrientation.Portrait,
    NetworkLoader = new HttpClientNetworkLoader(httpClient, new Uri("https://www.example.com"))
};

var generator = new PdfGenerator();

var stream = new MemoryStream();

// Passing null to GeneratePdf loads the HTML from the configured NetworkLoader instead
var document = await generator.GeneratePdf(null, pdfConfig);
document.Save(stream);
```

Loading images via relative paths falls back to the local file system unless the configured `NetworkLoader` has an appropriate `BaseUri` (as above), or the HTML has a `<base href>` element.

## Sharing a parsed CSS context across renders

If you're rendering many documents against the same stylesheet — for example, a batch of invoices that all use one company template — parse the CSS once with `PdfGenerator.ParseStyleSheet` and reuse the resulting `PeachPdfCssContent` instead of re-parsing the same CSS for every document.

```csharp
const string css = """
    body { font-family: Arial, sans-serif; }
    h1 { color: #2c3e50; }
    .total { font-weight: bold; }
    """;

var generator = new PdfGenerator();

// combineWithDefault: true (the default) merges this stylesheet on top of the
// W3 user-agent defaults; false replaces the defaults entirely.
var sharedCssData = await generator.ParseStyleSheet(css, combineWithDefault: true);

var pdfConfig = new PdfGenerateConfig
{
    PageSize = PageSize.Letter,
    PageOrientation = PageOrientation.Portrait
};

foreach (var invoiceHtml in invoiceHtmlDocuments)
{
    var document = await generator.GeneratePdf(invoiceHtml, pdfConfig, sharedCssData);

    using var fileStream = File.Create($"invoice-{Guid.NewGuid()}.pdf");
    document.Save(fileStream);
}
```

Reusing `sharedCssData` this way avoids re-parsing identical CSS on every iteration; the same `PdfGenerator` instance can also be reused across renders like this — sequentially, as in the loop above (font mappings and loaded fonts persist on it). See [Thread safety](#thread-safety) if you're parallelizing this loop across threads: use one `PdfGenerator` per thread rather than sharing this one.

## Saving a PDF to a file

`PeachPdfDocument.Save` writes to any `Stream`, so saving directly to disk just means opening a file stream instead of a `MemoryStream`:

```csharp
var html = "<html><body><h1>Hello, PeachPDF</h1></body></html>";
var pdfConfig = new PdfGenerateConfig { PageSize = PageSize.Letter };

var generator = new PdfGenerator();
var document = await generator.GeneratePdf(html, pdfConfig);

using var fileStream = File.Create("output.pdf");
document.Save(fileStream);
```

## Fonts

For the full compatibility details of the font-related CSS properties themselves (`font-family`, `font-weight`, `font-style`, `font-stretch`, `@font-face`), see [Color & Typography](html-css-support.md#color--typography) and [CSS At-Rules](html-css-support.md#css-at-rules) in HTML & CSS Support.

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

See [Color & Typography](html-css-support.md#color--typography) in HTML & CSS Support for per-property compatibility notes, including the [per-run font selection model](html-css-support.md#font-selection-is-per-run-not-per-character).

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

To restrict a stream-registered font to specific codepoints — the programmatic equivalent of an `@font-face` [`unicode-range`](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-face/unicode-range) descriptor — pass a list of `RuneRange`s. Characters outside the declared ranges resolve to another registered font (per-character font matching):

```csharp
using System.Text;

PdfGenerator generator = new();
// Use this font only for Basic Latin; other characters fall back to another registered font.
await generator.AddFontFromStream(fontStream,
    [new RuneRange(new Rune(0x0000), new Rune(0x00FF))]);
```

Web fonts loaded via `@font-face` (`url()`, with a comma-separated fallback list, and `local()`) are also supported, including per-character selection via `unicode-range` — see [`@font-face` in CSS At-Rules](html-css-support.md#css-at-rules) and [Per-character font matching](html-css-support.md#per-character-font-matching-and-coverage-fallback) for the full descriptor support notes.

### Supported font formats

We support TrueType, CFF, WOFF, and WOFF2 font formats.

## Enabling tagged PDF (PDF/UA) output

PeachPDF can optionally produce a *tagged* PDF — one with a logical structure tree (`/StructTreeRoot`) exposing the document's headings, paragraphs, lists, tables, links, and images to assistive technology (e.g. screen readers). Tagging is **off by default**; enable it with:

```csharp
var config = new PdfGenerateConfig
{
    EnableTaggedPdf = true
};
```

When enabled:

- The document's language (`/Lang`) is set automatically from `<html lang="...">` (falling back to `PdfGenerateConfig.DefaultLanguage` if the document declares none).
- Every element's HTML tag is mapped to a PDF standard structure type (`/H1`, `/P`, `/Table`, etc.).
- `<img>` (and other elements with an `alt` attribute) carry their alt text into the structure element's `/Alt` entry.
- `<a href="...">` links get a `/Link` structure element, and the underlying PDF Link annotation is cross-referenced with it in both directions — a reader can navigate from either side.
- List items (`<li>`) are split into sibling `/Lbl` (the marker) and `/LBody` (the rest of the item's content) structure elements under `/LI`, per the tagged-PDF list convention.

When `EnableTaggedPdf` is left at its default (`false`), none of this runs — output is byte-for-byte the same as if tagging didn't exist in the codebase at all.

The HTML-tag → structure-type mapping is CSS-driven and author-overridable via the `-peachpdf-pdf-tag-type` custom property — see [Tagged PDF (PDF/UA) Support](html-css-support.md#tagged-pdf-pdfua-support) in HTML & CSS Support for the property's accepted values, the full default mapping table, and known limitations.

## ASP.NET Core controller endpoint

Render to a `MemoryStream`, rewind it, and return it with `File()` so ASP.NET Core streams it to the client with the right content type. `BuildInvoiceHtml` below stands in for whatever HTML-generation logic you use (a Razor template, a string builder, etc.) — the PDF-specific part is everything after `var html = ...`.

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("reports")]
public class ReportsController : ControllerBase
{
    [HttpGet("invoice/{id:int}")]
    public async Task<IActionResult> GetInvoice(int id)
    {
        var html = await BuildInvoiceHtml(id);

        var pdfConfig = new PdfGenerateConfig
        {
            PageSize = PageSize.Letter,
            PageOrientation = PageOrientation.Portrait
        };

        var generator = new PdfGenerator();
        var document = await generator.GeneratePdf(html, pdfConfig);

        var stream = new MemoryStream();
        document.Save(stream);
        stream.Position = 0;

        return File(stream, "application/pdf", $"invoice-{id}.pdf");
    }
}
```

## ASP.NET Core Minimal API endpoint

The same approach works with `Results.File`, mapped on the `WebApplication` built in `Program.cs`:

```csharp
app.MapGet("/reports/invoice/{id:int}", async (int id) =>
{
    var html = await BuildInvoiceHtml(id);

    var pdfConfig = new PdfGenerateConfig
    {
        PageSize = PageSize.Letter,
        PageOrientation = PageOrientation.Portrait
    };

    var generator = new PdfGenerator();
    var document = await generator.GeneratePdf(html, pdfConfig);

    var stream = new MemoryStream();
    document.Save(stream);
    stream.Position = 0;

    return Results.File(stream, "application/pdf", $"invoice-{id}.pdf");
});
```

## Azure Functions (isolated worker) HTTP handler

`HttpResponseData.Body` is itself a writable `Stream`, so `PeachPdfDocument.Save` can write straight to the response body with no intermediate buffer.

```csharp
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public class GenerateInvoicePdf
{
    private readonly ILogger _logger;

    public GenerateInvoicePdf(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<GenerateInvoicePdf>();
    }

    [Function("GenerateInvoicePdf")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "invoice/{id:int}")] HttpRequestData req,
        int id)
    {
        var html = await BuildInvoiceHtml(id);

        var pdfConfig = new PdfGenerateConfig
        {
            PageSize = PageSize.Letter,
            PageOrientation = PageOrientation.Portrait
        };

        var generator = new PdfGenerator();
        var document = await generator.GeneratePdf(html, pdfConfig);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/pdf");
        response.Headers.Add("Content-Disposition", $"attachment; filename=invoice-{id}.pdf");

        document.Save(response.Body);

        return response;
    }
}
```
