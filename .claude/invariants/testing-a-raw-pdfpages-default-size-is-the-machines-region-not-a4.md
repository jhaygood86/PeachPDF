# A raw `PdfPage`'s default size is the machine's region, not A4

`PdfSharpCore.Pdf.PdfPage.Initialize()` sets the new page's size from the ambient region:

```csharp
Size = RegionInfo.CurrentRegion.IsMetric ? PageSize.A4 : PageSize.Letter;
```

So a test that builds a document by hand — `new PdfDocument()` + `document.AddPage()`, the shape
every adapter/`XGraphics`-level test uses — gets an **A4** page (595x842pt) on a metric machine and a
**Letter** page (612x792pt) on a US-region one. Nothing in the test says which.

This only matters where the test asserts a literal page-space coordinate, which for a PDF content
stream is almost every interesting assertion: PDF's y-up space measures from the page's *bottom*, so
every y a drawing operator emits is `pageHeight - layoutY`, and the page height is exactly what
changes. The measured symptom, from `GraphicsAdapterImageCropTests`: the same cropped-image draw
emits

```
10 832 m ... q 1 0 0 1 -10 812 cm /Fm0 Do    # A4   (842 - 10, 842 - 30)
10 782 m ... q 1 0 0 1 -10 762 cm /Fm0 Do    # Letter (792 - 10, 792 - 30)
```

— a green local run on a metric machine and a hard failure on both `windows-latest` and
`macos-latest` in CI, on an assertion with nothing platform-specific in it. The 50pt difference reads
as if the geometry were wrong rather than as if the paper were a different size.

**A test that asserts literal page-space coordinates must pin the page size** (`page.Size =
PageSize.A4;` right after `AddPage()`), or derive its expected values from `page.Height.Point`.

This does not affect tests that render through `PdfGenerator` — the HTML pipeline resolves its own
page size (A4 by default, `@page` otherwise) and assigns it, so the region default never applies.
It is specifically the hand-built `PdfDocument` fixtures that inherit it.
