# Usage Examples

This page collects practical, copy-pasteable examples for common PeachPDF scenarios, including hosting it behind an HTTP endpoint. For the full list of supported HTML elements and CSS properties, see [HTML & CSS Support](html-css-support.md); for how the rendering pipeline works internally, see [Architecture](architecture.md). If you'd rather build a document directly in C# than author HTML, see the [Declarative Document-Building API](declarative-api.md).

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
- [Disabling local file access](#disabling-local-file-access)
- [Rendering in the browser (Blazor WebAssembly)](#rendering-in-the-browser-blazor-webassembly)
- [Sharing a parsed CSS context across renders](#sharing-a-parsed-css-context-across-renders)
- [Saving a PDF to a file](#saving-a-pdf-to-a-file)
- [Detecting text that was clipped away](#detecting-text-that-was-clipped-away)
- [Fonts](#fonts)
- [Rendering MathML formulas](#rendering-mathml-formulas)
- [Enabling tagged PDF (PDF/UA) output](#enabling-tagged-pdf-pdfua-output)
- [PDF 2.0 output](#pdf-20-output)
- [Enabling interactive PDF forms](#enabling-interactive-pdf-forms)
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

The simplest case: render an in-memory HTML string to a PDF stream. With no `NetworkLoader` configured, `data:` URIs are always resolved, and relative `src`/`href`/`url()` references are loaded from the local file system, resolved against the current working directory (the same way a browser resolves them against the document's location). A `<base href>` element overrides that base.

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

## Rendering a local HTML file

`FileUriNetworkLoader` renders a local `.html` file and every resource it references (stylesheets, images, fonts) from disk. It sets the base URL to the file's own location — exactly like opening the file in a browser — so relative references resolve against the file's directory. Pass `null` as the HTML argument to load the root document from the file.

```csharp
var pdfConfig = new PdfGenerateConfig
{
    PageSize = PageSize.Letter,
    PageOrientation = PageOrientation.Portrait,
    NetworkLoader = new FileUriNetworkLoader("report/index.html")
};

var generator = new PdfGenerator();

var stream = new MemoryStream();

// Passing null to GeneratePdf loads the HTML from the configured NetworkLoader instead
var document = await generator.GeneratePdf(null, pdfConfig);
document.Save(stream);
```

`file:` URIs are always resolved from disk regardless of which loader is configured (the same way `data:` URIs always are), so a document loaded over HTTP or from an MHTML archive can still reference an absolute `file:` resource.

### How a local file's content type is determined

Each local file's `Content-Type` is resolved from the **operating system's own MIME mechanism by default** — the shell file-association database on Windows, the Uniform Type Identifiers API on macOS/iOS, and `/etc/mime.types` on Linux — **falling back to a built-in set** when the OS provides nothing: HTML, CSS, SVG, common raster image extensions (PNG, JPEG, BMP, GIF, WebP, AVIF, TGA, PSD, HDR — this only resolves a MIME type string for the extension, independent of whether PeachPDF can actually decode that format; it decodes PNG/JPEG/BMP/GIF/WebP/AVIF/TIFF, not TGA/PSD/HDR — TIFF decodes even though `tif`/`tiff` have no entry in this built-in list, since images are recognized by their bytes rather than their resolved content type, as described below), and the TTF/OTF/WOFF/WOFF2 font formats. Anything else resolves to `application/octet-stream`.

If you reference a local file whose extension falls outside that built-in set and your OS has no MIME association for it, **register the type with the OS** — for example, set the shell `Content Type` association for the extension on Windows, or add an entry to `/etc/mime.types` (or `~/.local/share/mime`) on Linux — so PeachPDF can resolve it. This matters only where the content type is actually enforced: as in a browser, PeachPDF accepts a linked stylesheet only when its type is `text/css`, whereas images and fonts are recognized by their bytes and load regardless of content type.

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

Relative references resolve against the configured `NetworkLoader`'s `BaseUri` (as above), or a `<base href>` element if the document has one. With no loader configured, they resolve against the current working directory and load from the local file system (see [Rendering a local HTML file](#rendering-a-local-html-file) to make that base an explicit file location instead, or [Disabling local file access](#disabling-local-file-access) to switch that behaviour off).

## Disabling local file access

By default a document can reach the local file system: a `file:` reference is read from disk, and a relative reference resolves against the process's working directory. That's what makes rendering a local HTML file work like opening it in a browser — but it is rarely what you want when the HTML comes from somewhere you don't control.

Set `AllowLocalFileAccess = false` to refuse it:

```csharp
var config = new PdfGenerateConfig
{
    PageSize = PageSize.Letter,
    AllowLocalFileAccess = false
};

var document = await new PdfGenerator().GeneratePdf(untrustedHtml, config);
```

Two things change. Every `file:` resource request — `<img>`, `<link rel="stylesheet">`, CSS `url()`, an SVG `<image href>`, an `@font-face src` — resolves to "not found". And a loader that supplies no `BaseUri` of its own (`DataUriNetworkLoader` and `MimeKitNetworkLoader` are both in that category) no longer inherits the working directory as a base, so a relative reference goes unresolved rather than being turned into a `file:` URI. A deny wins even over an explicitly configured `FileUriNetworkLoader`.

The document you pass in is unaffected: it's an input, not something the document asked for. The same goes for a file a `FileUriNetworkLoader` was explicitly constructed with — that root document is still read; only the resources it references are refused.

## Rendering in the browser (Blazor WebAssembly)

PeachPDF is pure managed code, so it runs unmodified in a browser under WebAssembly — the whole pipeline, from HTML parsing through font subsetting to PDF output, with no server round-trip. [PeachPDF's own demo](https://peachpdf.net/demo/) does exactly this; its source is in `src/PeachPDF.Demo.BlazorWasm/`.

Three things differ from a server or desktop host:

**You must supply the fonts.** A browser exposes no system fonts to WebAssembly, so PeachPDF discovers none. Register your own before rendering, or nothing can be measured, let alone drawn:

```csharp
var generator = new PdfGenerator();

foreach (var file in new[] { "LiberationSans-Regular.woff", "LiberationSerif-Regular.woff" })
{
    using var stream = new MemoryStream(await httpClient.GetByteArrayAsync($"fonts/{file}"));
    await generator.AddFontFromStream(stream);
}

// The generic families were resolved before any of these existed, so re-point them.
generator.AddFontFamilyMapping("sans-serif", "Liberation Sans");
generator.AddFontFamilyMapping("serif", "Liberation Serif");
```

The default font on a browser host is Liberation Sans; register a family under that name and it is used directly. Register something else and PeachPDF adopts the first family you register as the default, so text still renders. Note that font-family mapping is consulted only when the requested family isn't registered, and it is single-hop — every mapping has to name a real registered family.

**Use WOFF or TrueType, not WOFF2.** WOFF2 is Brotli-compressed and a browser/WebAssembly host has no Brotli decoder — `System.IO.Compression.Brotli` throws there. WOFF 1.0 uses deflate and works, at roughly 55% of the TrueType size. The same limitation makes `hyphens: auto` unavailable in the browser: PeachPDF's hyphenation patterns are Brotli-compressed, so text lays out unhyphenated rather than failing.

**Pin the culture.** A Blazor WebAssembly app adopts the browser's locale, and CSS is invariant by definition — a visitor whose browser is set to a comma-decimal locale would otherwise have lengths misparsed. Set `<InvariantGlobalization>true</InvariantGlobalization>` in the project file, which also drops the ICU data from the download.

Rendering is synchronous once it starts, and WebAssembly in the browser is single-threaded, so a long document will make the tab unresponsive for the duration. Say so in your UI.

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

### Applying compatibility styles to legacy HTML

Caller-supplied stylesheets can also provide compatibility rules when the input HTML cannot be changed. For example, the obsolete, non-standard `<nobr>` element is intentionally not included in PeachPDF's user-agent stylesheet. To give ordinary, well-formed `<nobr>` elements their historical non-wrapping behavior, add the equivalent CSS explicitly:

```csharp
var generator = new PdfGenerator();

var styles = await generator.ParseStyleSheet(
    "nobr { white-space: nowrap; }",
    combineWithDefault: true);

var document = await generator.GeneratePdf(html, pdfConfig, styles);
```

This adds an author stylesheet on top of PeachPDF's defaults, so styles in the document can still override it. It only supplies the layout rule: it does not implement a browser's special HTML parser error recovery for malformed or nested `<nobr>` markup. When you control the HTML, prefer a standard element such as `<span class="nobr">` with `.nobr { white-space: nowrap; }` instead.

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

## Detecting text that was clipped away

A word can be drawn into the PDF and then truncated by a clip — an `overflow: hidden` box narrower than an unbreakable value, most often. The glyphs are still in the content stream, so a reader that parses that stream finds them and reports the document complete, while anything that honours the clip shows only part of the word. Both are behaving correctly; the difference simply is not recorded in the file.

That makes it the one loss class only the engine can report, because only the painter knows the word's rect was wider than the clip it was drawn into. `PeachPdfDocument.ClipReport` is that report:

```csharp
var document = await generator.GeneratePdf(html, pdfConfig);

if (document.ClipReport.ClippedWords.Count > 0)
{
    foreach (var word in document.ClipReport.ClippedWords)
    {
        Console.WriteLine(
            $"'{word.Text}' kept {word.KeptFraction:P0} " +
            $"({word.VisibleWidth:F1}pt of {word.DrawnWidth:F1}pt)");
    }
}
```

Useful when generating documents from templates against data you do not control, where a value one character longer than its column is the difference between a correct invoice and a truncated one that nothing flagged.

Each `ClippedWord` carries the geometry rather than a verdict — `DrawnWidth`, `VisibleWidth`, `DrawnHeight`, `VisibleHeight`, and `KeptFraction` — so what counts as material loss is yours to decide. A word is reported when **either** dimension was reduced, so a word clipped only vertically — a box short enough to cut a line's height but wide enough to keep the whole word — comes back with `VisibleWidth` equal to `DrawnWidth`. `ClipReport.ClippedChars` totals the characters carried by truncated words; it counts the whole word, since a word is the smallest unit the painter knows and apportioning characters to a sub-rectangle would invent precision the measurement does not have.

The report **accumulates** across repeated `AddPdfPages` calls, so a document assembled from several calls carries every call's findings rather than only the last one's.

Two things are deliberately **not** reported:

- **`text-overflow: ellipsis`.** That is truncation the author asked for and the reader can see, which is the opposite of the silent loss this exists for.
- **Whitespace.** A clipped space is not something anyone can see or act on.

**The report under-reports, and that is the safe direction rather than completeness.** It measures against the renderer's tracked clip-rect stack, and two clips never reach it: a `border-radius` or `clip-path` clip, and the page-level clip applied outside that stack. An empty report is therefore a weaker statement than "nothing was clipped". A word that fell *entirely* outside its clip is not reported here either — it is never drawn at all, so reading the output back detects it as missing text.

## Fonts

For the full compatibility details of the font-related CSS properties themselves (`font-family`, `font-weight`, `font-style`, `font-stretch`, `@font-face`), see [Color & Typography](html-css-support.md#color--typography) and [CSS At-Rules](html-css-support.md#css-at-rules) in HTML & CSS Support.

### Default Font

By default, PeachPDF uses Segoe UI on Windows. Segoe UI isn't installed by default on other platforms, so PeachPDF picks a different platform-appropriate default there instead (see the generic-family table below — the same "verify installed, else fall back" logic applies). On a browser/WebAssembly host, where no system font is discoverable at all, the default is Liberation Sans; if you register some other family instead, the first one you register becomes the default, so text still renders (see [Rendering in the browser](#rendering-in-the-browser-blazor-webassembly)). You can remap the default font (or any other family) to another one using

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
| `system-ui` | Segoe UI | *(platform default font)* | *(platform default font)* | *(delegated to fontconfig's own `system-ui` alias)* |

On Linux, PeachPDF delegates directly to the system's own `fontconfig` library (`libfontconfig.so.1`) at startup — the managed equivalent of running `fc-match <generic>` — so the resolved family always matches whatever that distro's own font configuration actually maps each generic to, rather than a name that might not be installed. `system-ui` on Windows is an exact match for Chromium's own `system-ui` → Segoe UI resolution, and on Linux it goes through fontconfig exactly like the five generics above — which is also what Chromium does there — falling back to the platform default font only if fontconfig cannot answer or names a family that is not installed. On macOS/Android it remains a pragmatic approximation using the platform's default font rather than true native system-UI-font detection (e.g. macOS's actual system-ui is the private San Francisco font, not something cleanly resolvable via plain TTF/OTF file discovery).

Every mapping above — including a custom one set via `AddFontFamilyMapping` — is verified against the fonts actually installed on the running machine before use; if the target isn't present, PeachPDF falls back to the platform's default font instead of silently substituting whatever arbitrary font happened to be discovered first.

### Font weight, style, and stretch matching

Requesting a `font-weight`/`font-style`/`font-stretch` PeachPDF can't find an exact registered face for doesn't just fall back to Regular:

- **Numeric weight** (`font-weight: 1`–`1000`) is matched to the *nearest* registered face for the family per CSS Fonts Level 4 §5.2 (the same algorithm real browsers use), not just an exact match or a coarse bold/not-bold split. `bolder`/`lighter` step relative to the parent element's own resolved weight, following the CSS2.1 §15.6 worked table.
- **`font-stretch`** (the 9 CSS Fonts Level 3 keywords) is matched the same way when a family has multiple registered faces at different stretch values.
- When no real face is close enough to the request, PeachPDF **synthesizes** a faux-bold (fill+stroke render mode) or faux-italic/oblique (glyph shear) rather than rendering with zero visual distinction. `oblique <angle>` (e.g. `oblique 10deg`) drives the exact synthesized shear amount when declared; otherwise a fixed default angle is used.
- An `@font-face` rule's own declared `font-weight`/`font-style`/`font-stretch` descriptors are authoritative for how that specific registered resource participates in this matching, independent of what the font file's own internal tables say — this is what makes multi-variant web-font families (separate `@font-face` rules per weight) resolve correctly.

See [Color & Typography](html-css-support.md#color--typography) in HTML & CSS Support for per-property compatibility notes, including the [per-character font matching model](html-css-support.md#per-character-font-matching-and-coverage-fallback).

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

## Rendering MathML formulas

MathML embedded directly in HTML renders as real vector PDF content — no configuration needed:

```csharp
var html = @"
<html><body>
  <p>The quadratic formula:</p>
  <math display=""block"">
    <mi>x</mi><mo>=</mo>
    <mfrac>
      <mrow><mo>-</mo><mi>b</mi><mo>&#177;</mo>
        <msqrt><mrow><msup><mi>b</mi><mn>2</mn></msup><mo>-</mo><mn>4</mn><mi>a</mi><mi>c</mi></mrow></msqrt>
      </mrow>
      <mrow><mn>2</mn><mi>a</mi></mrow>
    </mfrac>
  </math>
</body></html>";

var generator = new PdfGenerator();
var document = await generator.GeneratePdf(html, PageSize.A4);
```

A formula renders in the `font-family` its `<math>` element resolves to via ordinary CSS — for correct
fraction bars, radicals, and stretchy operator sizing, that font should carry a real OpenType `MATH`
table (e.g. [STIX Two Math](https://github.com/stipub/stixfonts), Latin Modern Math, or any other
dedicated math font); a font without one still renders structurally correctly using approximate
fallback metrics. See [Fonts](#fonts) above for how to register a custom font, and
[Supported MathML Features](supported-mathml-features.md) for the full compatibility matrix.

```csharp
using var fontStream = File.OpenRead("STIXTwoMath-Regular.ttf");
await generator.AddFontFromStream(fontStream);
// html's <math> elements then use font-family: "STIX Two Math";
```

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

PDF outline (bookmark) generation is a separate, always-on feature — no `PdfGenerateConfig` flag needed — driven purely by the `bookmark-level`/`bookmark-label`/`bookmark-state` CSS properties; see [PDF Bookmarks (Outline) Support](html-css-support.md#pdf-bookmarks-outline-support) in HTML & CSS Support.

## PDF 2.0 output

PeachPDF defaults to PDF 1.7 output. Set `PdfVersion` to target a real PDF 2.0 ([ISO 32000-2](https://www.iso.org/standard/75839.html)) file header instead:

```csharp
var config = new PdfGenerateConfig
{
    PdfVersion = PdfVersion.Pdf20
};
```

This is needed for full spec conformance when combined with `EnableTaggedPdf` on a document containing `<math>` elements: the `/AF` (Associated Files) array PeachPDF attaches to a `Formula` structure element to carry the original MathML source (see [MathML Associated Files](html-css-support.md#mathml-associated-files)) is a PDF 2.0 addition to the structure element dictionary. `/AF` is still written under `PdfVersion.Pdf17` (the default) and tolerated by most real-world readers, but only `Pdf20` makes the file's own header agree with the features it uses.

`PdfVersion.Pdf20` is incompatible with `PdfAConformance` set to anything other than `PdfAConformance.None` — PeachPDF doesn't implement PDF/A-4 (the PDF-2.0-based PDF/A level), and every PDF/A level it does implement is defined against PDF 1.4 or 1.7. Requesting both throws.

## Enabling interactive PDF forms

PeachPDF can optionally produce a *fillable* PDF — real AcroForm text, checkbox, radio, and select (combo-box) fields a reader can actually fill in, instead of the default static rendering of `<input>`/`<select>` elements. Interactive forms are **off by default**; enable them with:

```csharp
var config = new PdfGenerateConfig
{
    EnableInteractivePdfForms = true
};
```

```html
<form>
  <label>Name: <input type="text" name="name" value="" /></label>
  <label><input type="checkbox" name="subscribe" checked /> Subscribe to updates</label>
  <label><input type="radio" name="plan" value="basic" checked /> Basic</label>
  <label><input type="radio" name="plan" value="pro" /> Pro</label>
  <select name="country">
    <option value="us">United States</option>
    <option value="ca">Canada</option>
  </select>
</form>
```

With the flag on, each `<input>`/`<select>` above becomes a real AcroForm field: `type="checkbox"`/`type="radio"` inputs become checkbox/radio fields (radios sharing a `name` become one mutually-exclusive group), every other `<input>` becomes a text field, and `<select>` becomes a combo-box field populated from its `<option>` children. `<textarea>` and `<button>` are not supported and always render as static boxes.

When `EnableInteractivePdfForms` is left at its default (`false`), none of this runs — no AcroForm object is created and the page's static rendering is unchanged.

The field-kind inference is CSS-driven and author-overridable via the `-peachpdf-pdf-form-field` custom property (plus three text-field-only sub-setting properties for auto-sizing, comb layout, and scroll behavior) — see [Interactive PDF Forms Support](html-css-support.md#interactive-pdf-forms-support) in HTML & CSS Support for the full property reference and default inference table.

## Generating PDF/A-conformant output

PeachPDF can optionally produce a PDF/A (ISO 19005) conformant file — the archival PDF profile many government, legal, and long-term-records workflows require. PDF/A conformance is **off by default**; opt in with `PdfGenerateConfig.PdfAConformance`:

```csharp
var config = new PdfGenerateConfig
{
    PdfAConformance = PdfAConformance.PdfA2B,
    // XMP metadata needs a real creation date - see "The XMP creation date" below.
    Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow }
};
```

### Choosing a conformance level

`PdfAConformance` covers all three ISO 19005 parts, each with a "B" (visual-only), "U" (guaranteed Unicode text extraction), and accessible "A" variant:

| Part | Levels | Notes |
|---|---|---|
| PDF/A-1 | `PdfA1B`, `PdfA1A` | Based on PDF 1.4. **Forbids PDF transparency groups entirely** — see below. |
| PDF/A-2 | `PdfA2B`, `PdfA2U`, `PdfA2A` | Based on PDF 1.7. Permits transparency. |
| PDF/A-3 | `PdfA3B`, `PdfA3U`, `PdfA3A` | Same as PDF/A-2, plus permission to embed arbitrary files (PeachPDF has no "attach a file" API, so this allowance goes unused). |

If you don't have a specific requirement for PDF/A-1 or PDF/A-3, `PdfA2B` (or `PdfA2U`, effectively free once you're already targeting 2B — see below) is the least restrictive, most broadly useful choice.

`U` levels cost nothing extra over the matching `B` level: every embedded font already carries a `ToUnicode` CMap, so the guaranteed-text-extraction requirement is already met.

### PDF/A-1 and transparency

CSS/SVG `opacity` below 1, `fill-opacity`/`stroke-opacity` below 1, a semi-transparent gradient color stop, and an SVG `<mask>` all render via a PDF transparency group — a construct PDF/A-1 forbids outright. PeachPDF has no engine to flatten these into a PDF/A-1-legal form, so rather than silently emit a non-conformant file, generation throws an `InvalidOperationException` naming the offending feature if the document uses any of them under `PdfA1B`/`PdfA1A`. A document that doesn't use any of these features generates normally under PDF/A-1. If your content needs transparency, target `PdfA2*`/`PdfA3*` instead — both permit it.

### The accessible "A" levels

`PdfA1A`/`PdfA2A`/`PdfA3A` build on the same tagged-structure-tree machinery as [tagged PDF](#enabling-tagged-pdf-pdfua-output) — requesting one of them enables tagging for that render even if `EnableTaggedPdf` is left `false`. Two things follow:

- **A document language is required.** Generation throws an `InvalidOperationException` if neither the document's own `<html lang="...">` nor `PdfGenerateConfig.DefaultLanguage` resolves to a language.
- **Every tagged image gets an `/Alt` entry**, even one with no `alt` attribute (an empty `/Alt` marks it decorative — PDF/A-a validation requires the entry to be *present*, not necessarily meaningful). Plain tagged-PDF output (`EnableTaggedPdf` alone, no PDF/A) is unaffected — a missing `alt` there still produces no `/Alt` entry, exactly as before.

### XMP metadata

Requesting any `PdfAConformance` level writes an XMP metadata stream (the document catalog's `/Metadata` entry), since PDF/A requires one. You can also opt into an XMP stream independently of PDF/A conformance with `EnableXmpMetadata`:

```csharp
var config = new PdfGenerateConfig { EnableXmpMetadata = true };
```

The XMP packet's Dublin Core/`pdf:`/`xmp:` fields are always derived from the same Document Information dictionary values `PdfDocumentMetadata` already controls (`Title`, `Author`, `Subject`, `Keywords`), so the two stay consistent by construction. `pdfaid:part`/`pdfaid:conformance` are added only when `PdfAConformance` is set — they're derived from it, not independently settable (a caller can't claim a conformance level the actual output doesn't meet).

To include your own additional XMP metadata (an internal provenance or records-management schema, for example), add `System.Xml.Linq.XElement`s to `PdfDocumentMetadata.CustomXmpProperties` — each becomes its own `rdf:Description` in the packet, alongside the built-in one:

```csharp
using System.Xml.Linq;

XNamespace acme = "urn:acme:records";
var metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow };
metadata.CustomXmpProperties.Add(new XElement(acme + "retentionPolicy", "7-years"));

var config = new PdfGenerateConfig { EnableXmpMetadata = true, Metadata = metadata };
```

#### The XMP creation date

`xmp:CreateDate` needs a real value. PeachPDF uses the same date HTML `<meta>`-extraction already populates the Document Information dictionary's `/CreationDate` from, or `PdfDocumentMetadata.CreationDate` when you set it explicitly (it wins over the HTML-extracted date, same override pattern as `Title`/`Author`/etc.). Whenever an XMP stream is being written (`EnableXmpMetadata` or `PdfAConformance`) and neither is available, generation throws an `InvalidOperationException` rather than writing a placeholder date — set `PdfDocumentMetadata.CreationDate` to fix it.

### Color and ICC profiles

A PDF/A `OutputIntent` needs a device-independent ICC profile to name — PeachPDF embeds the ICC's own freely-redistributable `sRGB2014.icc` profile for this and identifies it as `"sRGB IEC61966-2.1"`. PeachPDF's document-wide color mode stays mixed/undefined under `PdfAConformance` (a `device-cmyk()`-authored color still writes as real `DeviceCMYK`, unaffected by PDF/A's own sRGB `OutputIntent`), so the bundled sRGB profile is always the right one to name here. It's unrelated to how a *source raster image* embeds its own color data, covered next.

#### CMYK and embedded ICC profiles in source images

A CMYK or YCCK JPEG — the form a print-ready image typically arrives in, separated for a specific press profile — is embedded via byte-for-byte pass-through rather than converted to RGB: PeachPDF has no general color-management engine, so preserving the original bytes unchanged is the only way to guarantee the source's separations survive intact. The PDF `ColorSpace` is `DeviceCMYK`, or `ICCBased` (referencing the JPEG's own embedded ICC profile, carried through verbatim) when one is present. An Adobe-authored CMYK JPEG's inverted-sample convention is corrected via a PDF `Decode` array rather than by re-encoding the pixel data.

A CMYK/YCCK JPEG is always embedded at its natural pixel size — `DownscaleImages` and `MaximumDownscaleMultiplier` don't apply to it, since PeachPDF has no CMYK JPEG encoder to re-encode a resized copy with.

A CMYK TIFF is preserved the same way in spirit, but by a different mechanism: TIFF has no PDF-native byte-for-byte pass-through filter the way JPEG's `DCTDecode` is, so PeachPDF decodes it natively (never forced through RGB) and embeds the decoded pixel buffer as a raw, Flate-compressed `DeviceCMYK` (or `ICCBased`, when the TIFF carries a usable embedded ICC profile) stream instead. Like a CMYK JPEG, it's always embedded at its natural pixel size.

An RGB or grayscale JPEG carrying a usable embedded ICC profile is *also* embedded via byte-for-byte pass-through, specifically to preserve that profile (`ICCBased` referencing it, rather than the usual bare `DeviceRGB`/`DeviceGray`). Unlike a CMYK source, this doesn't disable resizing: if the image is being downscaled for its on-page display size, that particular embed falls back to the ordinary re-encoded path instead (losing the embedded profile for that embed, not the image) — pass-through and downscaling are mutually exclusive for a given embed, and downscaling wins when both would otherwise apply. An RGB/grayscale JPEG with no embedded ICC profile is unaffected by any of this.

PNG, WebP, and AVIF sources may also carry an embedded ICC profile, and PeachPDF preserves it. A PNG's `iCCP` chunk rides along its own byte-for-byte pass-through path (below): the color space PeachPDF would otherwise write as a bare `DeviceGray`/`DeviceRGB`/`Indexed` becomes `ICCBased` (referencing the profile, carried through verbatim) instead — pixel data is untouched either way, since the profile only changes how the `ColorSpace` entry names the space those pixels live in. WebP and AVIF have no pass-through mechanism (their pixel data is always decoded and embedded as a raw bitmap, as before), but PeachPDF now decodes a WebP/AVIF source a second time, natively, purely to read its embedded profile — the ordinary pixel decode still runs once, so this doesn't affect what gets embedded, only the `ColorSpace` it's tagged with. Unlike the JPEG case above, this isn't defeated by downscaling or `ImageCompression.Lossy`: a PNG/WebP/AVIF that falls back to a re-encoded JPEG embed for either reason still carries its source profile forward onto that embed, since re-encoding only recompresses already-decoded samples — it never changes what color space they're in. A source PNG/WebP/AVIF with no embedded profile is unaffected: it embeds with a bare `DeviceGray`/`DeviceRGB` exactly as before.

Requesting `PdfAConformance` on a document containing a CMYK image without an embedded ICC profile throws an `InvalidOperationException` at generation time: a bare `DeviceCMYK` image has no relationship to PeachPDF's RGB-based `OutputIntent`, so it isn't PDF/A-conformant on its own. An `ICCBased` CMYK image (one with an embedded profile) is self-describing and doesn't have this problem. An RGB or grayscale image is unaffected either way — it stays conformant with or without an embedded ICC profile.

#### Lossless embedding for opaque PNG/BMP/GIF images

An opaque PNG — a screenshot, chart, logo, QR code, or line art, with no real per-pixel alpha channel and not Adam7-interlaced — is embedded via the same kind of byte-for-byte pass-through as a CMYK JPEG, but for its pixel data rather than its color separations: the PDF `Filter` is `FlateDecode`, with `DecodeParms` describing PNG's own predictor/color layout, and the stream bytes are the PNG's own compressed `IDAT` data, unchanged. This is smaller and pixel-exact, unlike re-encoding as JPEG (which this replaces) — a QR code, for example, stays scannable regardless of how sharp its edges are. A PNG carrying a `tRNS` chroma-key chunk still qualifies too: a grayscale/truecolor `tRNS` (always a single exact transparent color) or a palette `tRNS` where every listed entry is fully opaque or fully transparent both pass through the same way, with a PDF color-key `Mask` array built from the chunk instead of a separate alpha plane. Like a CMYK JPEG, a pass-through-eligible PNG is always embedded at its natural pixel size; `DownscaleImages` and `MaximumDownscaleMultiplier` don't apply to it.

A PNG with a real per-pixel alpha channel (color type 4/6, or a palette PNG whose `tRNS` has a genuinely partial-alpha entry — neither fully opaque nor fully transparent) passes through too, when it isn't interlaced: the color data embeds as its own `FlateDecode` XObject (PeachPDF splits it out of the PNG's interleaved color+alpha `IDAT` into an independent, PNG-row-filtered stream — a real, if narrow, decode step, unlike the byte-for-byte opaque/chroma-key case above) alongside a child `SMask` image carrying just the alpha samples, itself `FlateDecode`-compressed with the same PNG-predictor `DecodeParms`. A palette source with partial-alpha `tRNS` needs no color-side work at all — its original indexed `IDAT`/palette embed unchanged, with only the alpha plane newly split out. An interlaced alpha-bearing PNG (or, in this version, one with 16-bit-per-channel color type 4/6) still falls back to the older full decode plus a raw alpha mask.

An opaque BMP — no lossy encoding mode at all — also stops being silently re-encoded as JPEG at its own natural display size, embedding via a raw `FlateDecode` RGB stream instead. Downscaling one, though, keeps the existing JPEG-at-`DownscaleQuality` behavior, since that's a deliberate, separate size/quality trade-off.

A GIF — eligible for pass-through when it isn't interlaced, its LZW minimum code size is exactly 8 (GIF starts codes at `MinCodeSize + 1` bits with clear code `1 << MinCodeSize`, matching the fixed 9-bit/256-clear-code start PDF's own `LZWDecode` filter always uses), and its frame covers the whole logical screen — embeds the same LZW-compressed codes its own encoder produced as `LZWDecode`, with an `Indexed` color space built from its palette and a color-key `Mask` for a declared transparent index (GIF transparency is always exactly one palette index, so this is even simpler than a PNG's `tRNS`). This isn't a literal byte-for-byte copy the way PNG's pass-through is: GIF packs LZW codes least-significant-bit-first where PDF's `LZWDecode` expects most-significant-bit-first, and the two conventions grow code width by one bit at slightly different points, so PeachPDF re-packs the underlying codes into PDF's bit order rather than reusing the GIF's bytes directly — still no LZW decompress-then-recompress round trip, just a bit-order transform. Like a pass-through-eligible PNG, this is always embedded at natural size, regardless of `DownscaleImages`. A GIF that doesn't qualify (small palette, interlaced, or a partial-canvas frame) falls back to the same raw `FlateDecode` path a BMP uses.

`PdfGenerateConfig.ImageCompression` controls all of this:

```csharp
var config = new PdfGenerateConfig { ImageCompression = ImageCompression.Lossless };
```

| Value | Behavior |
|---|---|
| `Auto` (default) | The behavior described above: an eligible PNG or GIF always passes through; a PNG/BMP/GIF that can't (interlaced, small-palette, partial-canvas, or being downscaled) still avoids lossy JPEG only at its own natural size. |
| `Lossless` | Same as `Auto`, but a *downscaled* PNG/BMP/GIF also never gets JPEG'd — it's decoded, resampled, and re-`FlateDecode`-encoded instead, at any size. Larger downscaled files, always pixel-exact. |
| `Lossy` | Always re-encode an opaque PNG/BMP/GIF as JPEG — the behavior every PeachPDF version before this option used. An explicit opt-in for the smallest files when fidelity doesn't matter, even for diagram/line-art content. |

An interlaced PNG (alpha-bearing or not) is unaffected by `ImageCompression` in every mode — it still falls back to the existing decode-and-`FlateDecode` path regardless of the setting. A non-interlaced alpha-bearing PNG, though, is pass-through-eligible like any other (see above) and follows the same `Auto`/`Lossless`/`Lossy` table: `Lossy` doesn't force a JPEG re-encode for it either, since JPEG has no alpha channel to hold the transparency in at all.

A losslessly-encoded WebP, AVIF, or TIFF source gets the same protection as an opaque PNG/BMP/GIF: PeachPDF can tell whether a given source actually used its format's lossless mode (WebP's VP8L, AVIF's lossless AV1 tool, or TIFF's uncompressed/LZW/PackBits compression), and only re-encodes it as lossy JPEG under `ImageCompression.Lossy`. A *lossy*-encoded WebP/AVIF/TIFF source is unaffected by `ImageCompression` in every mode — re-encoding an already-lossy source as JPEG loses nothing a lossless re-embed would have recovered, so it stays on the JPEG-re-encode path regardless of the setting.

### Deduplicating repeated images

Referencing the same image source more than once in a document — the same `<img src>` twice, an `<img>` and a `background-image`/`list-style-image`/`content: url()` sharing a source, or an `<object>` pointing at the same source as an `<img>` — fetches and decodes it only once per `GeneratePdf`/`AddPdfPages` call, and every reference at the same on-page size shares one embedded PDF image object. This happens automatically; there's nothing to opt into.

What that doesn't cover is a duplicate that spans two separate `AddPdfPages`/`AddPages` calls into the same document, or two different sources that happen to encode to identical bytes. For those, call `PeachPdfDocument.ConsolidateImages()` after adding all pages and before `Save`:

```csharp
var document = await generator.GeneratePdf(html, config);
await generator.AddPdfPages(document, moreHtml, config);
document.ConsolidateImages();
document.Save(stream);
```

It merges embedded images whose encoded bytes are identical, keeping one copy and repointing every other reference at it. It's opt-in rather than automatic, since hashing every embedded image isn't a cost most documents should pay on every `Save`.

## Authoring colors in CMYK

Colors written with the [CSS Color 5 `device-cmyk()`](https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/device-cmyk) function are carried through PeachPDF's whole pipeline natively — never approximated to RGB — and reach the PDF as a real `DeviceCMYK` fill or stroke operator:

```html
<p style="color: device-cmyk(0 0.81 0.94 0)">Pantone-adjacent orange, defined in ink, not light.</p>
```

`device-cmyk()` works anywhere `color` accepts any other `<color>` — `color`, `background-color`, `border-color` and the `border`/`background` shorthands, and SVG `fill`/`stroke`/`stop-color`. PDF itself allows a single page to freely mix `DeviceRGB` and `DeviceCMYK` content, so an ordinary RGB-authored color elsewhere on the same page is completely unaffected — there is no whole-document conversion, and none is attempted.

A gradient (`linear-gradient`/`radial-gradient`/`conic-gradient`, or SVG `<linearGradient>`/`<radialGradient>`) whose stops are all `device-cmyk()` interpolates directly in C/M/Y/K space, the same way an all-RGB gradient interpolates in RGB space. `color-mix()` between two `device-cmyk()` operands mixes the same way, ignoring the declared `in <space>` keyword (which has no CMYK equivalent). A gradient or `color-mix()` mixing `device-cmyk()` and RGB-authored operands has no defined conversion and is rejected — see the [`color` row](html-css-support.md#color--typography) in the compatibility matrix.

## Generating PDF/X-conformant output

PeachPDF can optionally produce a PDF/X (ISO 15930) conformant file — the print-production profile a commercial press or prepress workflow requires. Like PDF/A, PDF/X conformance is **off by default**; opt in with `PdfGenerateConfig.PdfXConformance`, and (unlike PDF/A, which bundles a default sRGB profile) you must supply your own output-intent ICC profile via `ColorOptions` — PeachPDF has no single correct default press profile to assume:

```csharp
var config = new PdfGenerateConfig
{
    PdfXConformance = PdfXConformance.X4,
    ColorOptions = new ColorOptions
    {
        OutputIntentProfile = File.ReadAllBytes("CoatedFOGRA39.icc"),
        OutputIntentIdentifier = "Coated FOGRA39",
    },
};
```

`PdfAConformance` and `PdfXConformance` are mutually exclusive on the same document — archival and print-production are different documents in practice, and generation throws an `InvalidOperationException` if both are set to a level other than `None`.

### Choosing a conformance level

| Level | Content restriction | Transparency | PDF version |
|---|---|---|---|
| `X1a` | CMYK (or a named spot color — PeachPDF has no spot-color support, so in practice CMYK-only) content only | Forbidden | 1.4 |
| `X3` | ICC-managed color permitted — an RGB- or Gray-tagged color may stay in its own space | Forbidden | 1.4 |
| `X4` | ICC-managed color permitted | Permitted | 1.6 |

`X4` is the modern, most commonly requested level and the least restrictive of the three — if you don't have a specific requirement for `X1a`/`X3`, start there. Each level's PDF version header, and its `GTS_PDFXVersion`/`GTS_PDFXConformance` identification (in both the document information dictionary and, for `X4`, its XMP metadata — the mechanism ISO 15930-7 names as primary for that level) are set automatically; nothing further to configure.

### X1a and X3: no live transparency

Same restriction, and the same mechanism, as [PDF/A-1](#pdfa-1-and-transparency): CSS/SVG `opacity` below 1, `fill-opacity`/`stroke-opacity` below 1, a semi-transparent gradient color stop, and an SVG `<mask>` all render via a PDF transparency group, which `X1a`/`X3` forbid outright. Generation throws an `InvalidOperationException` naming the offending feature rather than silently emitting a non-conformant file. Target `X4` instead if your content needs transparency.

### X1a: CMYK-only content

`X1a` additionally rejects any *chromatic* (non-gray) RGB-authored color the moment it would be written — `color: red` throws, `color: device-cmyk(0 1 1 0)` doesn't. There is no whole-document RGB→CMYK conversion in PeachPDF to fall back on (see [Authoring colors in CMYK](#authoring-colors-in-cmyk) above), so under `X1a` you need to author any chromatic color you actually want to appear with `device-cmyk()`.

An **achromatic** RGB color (gray, including pure black — `color: black`, an un-set default text color, `border: 1px solid #ccc`) is the one exception: rather than reject it, PeachPDF converts it to CMYK deterministically via `ColorOptions.BlackGeneration` — an exact, lossless ink mapping for a gray value, unlike an arbitrary hue, so this isn't the kind of approximation PeachPDF otherwise declines to compute:

```csharp
ColorOptions.BlackGeneration = ColorBlackGeneration.UseRichBlack; // default: UseTrueBlack
```

- **`UseTrueBlack`** (default): K-only (`C=M=Y=0`), scaled to the source gray's darkness.
- **`UseRichBlack`**: the same K, plus proportionally-scaled C/M/Y for ink density on large solid-black areas on press — only the darkest values get meaningful CMY; lighter grays stay close to neutral.

This is what makes `X1a` practical without rewriting every `color: black`/`border-color: #ccc` in a stylesheet as `device-cmyk()` — only genuinely chromatic colors need to be authored explicitly.

### Converting document colors to a target ICC profile

`ColorOptions.ConversionMode` applies a real, colorimetric ICC device-to-device conversion to every color in the document — no naive RGB↔CMYK formula is used anywhere. `PreserveAsAuthored` (the default) writes every color in whichever space it was authored in, exactly as described above; the other modes convert instead:

```csharp
var config = new PdfGenerateConfig
{
    PdfXConformance = PdfXConformance.X4,
    ColorOptions = new ColorOptions
    {
        OutputIntentProfile = File.ReadAllBytes("CoatedFOGRA39.icc"),
        OutputIntentIdentifier = "Coated FOGRA39",
        ConversionMode = ColorConversionMode.ConvertToOutputIntent,
        RenderingIntent = ColorRenderingIntent.RelativeColorimetric, // default
        UseBlackPointCompensation = true, // default
    },
};
```

- **`ConvertToOutputIntent`** converts every color into `OutputIntentProfile`'s space.
- **`ConvertToProfile`** converts every color into a separately-supplied `ConvertToProfile` profile — useful when the conversion target differs from the document's own output intent.
- **`GrayscaleViaK`** converts every color into `FallbackCmykProfile`'s CMYK space and keeps only the resulting K (black) channel — true ink-based grayscale, not a luminosity approximation.

A color's source profile is always well-defined for an RGB-authored color (the ICC-published sRGB profile PeachPDF already bundles for PDF/A — CSS colors are sRGB by definition outside of `device-cmyk()`). A `device-cmyk()`-authored color is uncalibrated ink by definition and has no source profile unless `FallbackCmykProfile` supplies one — without that set, a `device-cmyk()` color is left exactly as authored even under a conversion mode, since there is nothing to convert *from*.

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
