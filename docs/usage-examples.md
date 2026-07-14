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

Any state PeachPDF shares across `PdfGenerator` instances internally (such as system font discovery) is synchronized specifically to support many instances being used concurrently, one per thread.

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
