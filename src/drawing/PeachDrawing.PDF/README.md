# PeachDrawing.PDF

A PDF writer for .NET: draw shapes, text, images, gradients and effects with the drawing API every
[PeachDrawing](https://www.nuget.org/packages/PeachDrawing.Core/) target shares, and save them as a PDF file. Fonts are
embedded and subset, text stays selectable, and the writer covers PDF/A, PDF/X and PDF/UA conformance, AES-256 encryption,
links, bookmarks, tagged output, interactive forms, and merging or overlaying existing PDFs, in pure .NET with no browser or
external process. It is the PDF writer [PeachPDF](https://peachpdf.net/) renders HTML and CSS through, and it is usable with
**no PeachPDF reference at all**. It is trimmable and Native AOT compatible.

The PDF engine inside this package is derived from [PDFsharp](https://github.com/empira/PDFsharp) (MIT) and is not part of the
public API; what you program against is the PeachDrawing drawing surface.

```bash
dotnet add package PeachDrawing.PDF
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change
between releases. Targets .NET 10 and later, on any platform.

## Quick start

```csharp
using PeachDrawing.Core;
using PeachDrawing.PDF;

var context = new PdfRenderContext();   // registers the fonts installed on this machine

// Optional: ship your own font so the output is the same everywhere (TrueType, OpenType, WOFF, WOFF2).
await using (var fontFile = File.OpenRead("SourceSans3-Regular.ttf"))
{
    await context.AddFont(fontFile, "Source Sans");
}

var font = context.GetFont("Source Sans", size: 28, PaintFontStyle.Regular)!;

using var composer = new PdfComposer(context, new PdfComposerOptions { Title = "Hello" });

PdfComposerPage page = composer.AddPage(widthPoints: 400, heightPoints: 300);   // 1/72 inch; 595 x 842 is A4
PdfCanvas canvas = page.Canvas;                                                  // origin top-left, y grows downward

canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 200, 30, 60)), x: 40, y: 40, width: 160, height: 90);
canvas.DrawString("Hello, PeachDrawing.PDF", font, PaintColor.Black, new PaintPoint(40, 170), new Size(340, 40));

await using var output = File.Create("hello.pdf");
composer.Save(output);                                                           // the stream is not closed
```

Every drawing call is a `Canvas` call, so code written against `PeachDrawing.Core` draws a PDF page the same way it draws a
bitmap or an SVG. Shapes, text and gradients are written as vector content; an effect PDF cannot express (a blur, a drop shadow)
is drawn as a high-resolution bitmap and embedded.

## Drawing

```csharp
// Paths, gradients, strokes
using var path = canvas.GetGraphicsPath();
path.Start(40, 220); path.LineTo(200, 220); path.AddBezierTo(240, 220, 240, 280, 200, 280); path.CloseFigure();

var gradient = canvas.GetLinearGradientBrush(new PaintPoint(40, 0), new PaintPoint(240, 0),
    [(PaintColor.FromArgb(255, 255, 140, 0), 0), (PaintColor.FromArgb(255, 200, 30, 60), 1)]);
canvas.DrawPath(gradient, path);
canvas.DrawLine(canvas.GetPen(PaintColor.Black), 40, 290, 360, 290);

// Clips, transforms, blend modes: push, draw, pop
canvas.PushClip(new Rect(0, 0, 200, 100));
canvas.PushTransform(System.Numerics.Matrix3x2.CreateRotation(0.2f, new System.Numerics.Vector2(100, 50)));
canvas.DrawRectangle(gradient, 20, 20, 160, 60);
canvas.PopTransform();
canvas.PopClip();

// Images (PNG, JPEG, GIF, WebP, AVIF, ... via PeachImage)
using var stream = File.OpenRead("photo.jpg");
canvas.DrawImage(context.ImageFromStream(stream), new Rect(220, 40, 140, 100));
```

Text is shaped with ligatures, kerning, bidirectional reordering and complex scripts, written with only the glyphs used, and
color fonts are drawn as vector artwork. `MeasureString` measures against the same shaping `DrawString` uses.

## Links, destinations and bookmarks

```csharp
page.AddLink(new Rect(40, 170, 160, 20), "https://example.com/");            // a clickable address
page.AddDestinationLink(new Rect(40, 200, 90, 14), "terms");                 // a link to a named place...
composer.AddNamedDestination("terms", pageIndex: 1, topPoints: 40);          // ...which can be on a later page

composer.AddBookmarks([new PdfBookmark(level: 1, title: "Chapter one", isOpen: true) { PageIndex = 0, TopPoints = 40 }]);
```

## Options: metadata, version, compression, images

`new PdfComposerOptions { ... }` (or `composer.Configure(options)`):

| Option | Meaning |
|---|---|
| `Title`, `Author`, `Subject`, `Keywords`, `Creator`, `Producer`, `Language` | Document information. `Language` is a BCP 47 tag, required by the accessible PDF/A levels and PDF/UA. |
| `CreationDate` | Leave it unset for output that is byte-for-byte reproducible. |
| `Version` | `PdfVersion.Pdf20` (default) or `Pdf17`. |
| `Linearize` | Write the file for "fast web view". |
| `CompressContentStreams`, `BrotliCompression` | Flate is on by default; Brotli (PDF 2.0) is opt-in. |
| `ImageCompression`, `DownscaleImages`, `DownscaleQuality`, `MaximumDownscaleMultiplier`, `JxlPassthrough` | How images are embedded; `composer.ConsolidateImages()` stores repeated pictures once. |
| `FlattenTransparency`, `PortableColorFilters` | Draw transparency and color filters as plain content for readers that handle them poorly. |
| `Conformance`, `XConformance`, `UAConformance`, `ColorOptions` | PDF/A, PDF/X and PDF/UA levels, and the output-intent ICC profile and color conversion they need. |
| `Encryption` | AES-256 with a user password, an owner password and `PdfPermissions`. |
| `EmbeddedFiles`, `FacturX` | Attached files, including a Factur-X / ZUGFeRD invoice. |

## Conformance: PDF/A, PDF/X, PDF/UA

```csharp
var options = new PdfComposerOptions
{
    Title = "Archive copy",
    Language = "en-US",
    CreationDate = DateTimeOffset.UtcNow,
    Conformance = PdfAConformance.PdfA2B,          // PdfA1A..PdfA4F; or XConformance = PdfXConformance.X4
};
```

The composer writes what the level requires (output intent, XMP metadata, header version) and refuses what it forbids
(encryption, transparency in a level without it) with a `CanvasConstraintException` when the offending content is drawn.
A file claims PDF/UA with `UAConformance = PdfUAConformance.UA1` or `UA2`, which needs a tag recorder, a `Title`, a `Language` and a
`CreationDate`.

## Tagged PDF, forms and annotations

```csharp
var tags = composer.CreateTagRecorder();       // logical structure: headings, paragraphs, figures with alt text
tags.BeginPage(page);
using (tags.OpenContentElement(page.Canvas, headingKey, "H1"))
    page.Canvas.DrawString("Chapter one", font, PaintColor.Black, new PaintPoint(40, 60), new Size(320, 30));
tags.Finish();                                 // call after the last page, before Save
```

`composer.CreateFormBuilder(context)` places text fields, checkboxes, radio buttons, drop-downs, list boxes, push buttons and signature
fields (`PdfFormFieldDefinition`, drawn by your `IPdfFormAppearance`); `composer.CreateAnnotationBuilder(context)` places highlights, notes, stamps, ink and
other annotations.

## Encrypt, merge and work on existing PDFs

```csharp
var options = new PdfComposerOptions
{
    Encryption = new PdfEncryptionOptions { UserPassword = "open", OwnerPassword = "admin", Permissions = PdfPermissions.ReadOnly },
};

PdfOperation.LoadFile("a.pdf")
    .MergeFile("b.pdf", pageSelector: "1-3")             // merge pages of another file
    .OverlayFile(new PdfLayerConfiguration { FilePath = "letterhead.pdf" })
    .Linearize()
    .Save("merged.pdf");

var source = PdfPageSource.Open(File.ReadAllBytes("letterhead.pdf"));   // paint a page of an existing PDF onto any Canvas
var notDrawn = source.Paint(0, page.Canvas);
```

Only ciphers .NET provides are supported, so RC4-encrypted files are refused.

## Not thread safe

A `PdfComposer` and its canvases are not thread safe: use one composer per file.

## More

- [The PeachDrawing.PDF guide](https://peachpdf.net/peachdrawing-pdf.html): units, bleed and printer's marks, every option, PDF/A and PDF/X details, tagged PDF, forms, annotations, importing pages.
- [API reference](https://peachpdf.net/api/PeachDrawing.PDF/index.html)
- [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): the `Canvas` API. Sibling backends: [PeachDrawing](https://www.nuget.org/packages/PeachDrawing/) (bitmaps), [PeachDrawing.Svg](https://www.nuget.org/packages/PeachDrawing.Svg/).
- [PeachPDF](https://peachpdf.net/): renders HTML and CSS to PDF through this package.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## License

BSD 3-Clause. The PDF engine is adapted from PDFsharp (MIT); its notice is in `THIRD-PARTY-LICENSES.md`, which ships in this package.
