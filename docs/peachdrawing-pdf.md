# PeachDrawing.PDF

`PeachDrawing.PDF` writes PDF files from the same drawing surface every PeachDrawing target shares. It is the PDF `Canvas`
implementation of [PeachDrawing.Core](peachdrawing-core.md), usable with **no PeachPDF reference at all**: add pages to a
`PdfComposer`, draw on each one through a `PdfCanvas` - rectangles, paths, text, images, gradients, clipping, transforms,
blend modes - and save. Fonts are embedded and subset, so only the glyphs a document draws end up in the file. It is pure .NET,
with no browser or external process, is trimmable and Native AOT compatible, and is versioned in lockstep with PeachPDF: the same
version number for every release.

```bash
dotnet add package PeachDrawing.PDF
```

> **Status: pre-1.0.** `PdfRenderContext`, `PdfCanvas`, `PdfComposer`, `PdfComposerOptions`, `PdfVersion`,
> `PdfEncryptionOptions` and `PdfPermissions` are the package's public surface today - see `PublicApi.txt` for the exact,
> machine-checked snapshot. PeachPDF is the package's other consumer: it renders HTML and CSS onto these same canvases, so
> this is the PDF writer PeachPDF uses, not a copy built beside it. PDF/A and PDF/X conformance, tagged output, interactive
> forms, links and bookmarks are available through PeachPDF today and are not yet part of this package's own API. Until 1.0, the
> public API may change between releases.

## Quick start

```csharp
using PeachDrawing.Core;
using PeachDrawing.PDF;

var context = new PdfRenderContext();
await using (var fontFile = File.OpenRead("SourceSans3-Regular.ttf"))
{
    await context.AddFont(fontFile, "Source Sans");
}

var font = context.GetFont("Source Sans", size: 28, PaintFontStyle.Regular)!;

using var composer = new PdfComposer(context, new PdfComposerOptions { Title = "Hello" });

PdfCanvas page = composer.AddPage(widthPoints: 400, heightPoints: 300);   // 400 x 300 points
page.DrawRectangle(page.GetSolidBrush(PaintColor.FromArgb(255, 200, 30, 60)), x: 40, y: 40, width: 160, height: 90);
page.DrawString("Hello, PeachDrawing.PDF", font, PaintColor.Black, new PaintPoint(40, 170), new Size(340, 40));

await using var output = File.Create("hello.pdf");
composer.Save(output);
```

## `PdfRenderContext`

A `PdfRenderContext` is the `RenderContext` a PDF canvas draws with. Like every `RenderContext` it hands out the objects you draw
with - brushes, pens, fonts, images - and its constructor registers every font installed on the machine, so `GetFont` works
immediately for installed families. Beyond that:

- **`AddFont(Stream, familyName)`** registers a font file (TrueType, OpenType, WOFF or WOFF2, recognised by content) under a family
  name. A host with no installed fonts (a browser running WebAssembly, say) has to do this before it can draw any text.
- **`PixelsPerPoint`** is the scale between the units you draw in and PDF points, `1.0` by default: one drawing unit is one point.
  Set it **before** asking the context for fonts: a font is sized in the context's units, so a font made earlier keeps the size it
  was made with. A context with `PixelsPerPoint = 2` draws in half-points, which is how PeachPDF lays HTML out in CSS pixels.
- **`GetCssMediaType`** answers `print` where a document offers it, since a PDF is a print medium.

The class is open for deriving: a host adds what is specific to it by overriding `OnFontFamilyAdded` (react when a font is registered)
and `ResolveVariationAxes` (turn its own encoding of variable-font axis settings into axis values). PeachPDF's `HtmlRenderContext` does
exactly that to add the CSS generic font families, `font-variation-settings` and the loading of a document's resources.

## `PdfComposer`

A composer writes one file:

1. **`new PdfComposer(context, options)`** starts a document. `options` is optional.
2. **`AddPage(widthPoints, heightPoints)`** appends a page and returns its `PdfCanvas`. Page sizes are in points, 1/72 inch: `595 x 842`
   is A4 and `612 x 792` is US Letter. Pages can differ in size.
3. **`Save(stream)`** finishes every page and writes the file. The stream is not closed. A composer cannot be added to after it is
   saved, and a document needs at least one page.

A canvas stays usable until the composer is saved or disposed, so several pages can be drawn in any order. Neither a composer nor its
canvases are thread safe: use one composer per file.

### `PdfComposerOptions`

| Option | Meaning |
|---|---|
| `Title`, `Author`, `Subject`, `Keywords`, `Creator` | The document information shown in a reader's properties dialog. Non-ASCII text is written correctly. |
| `CreationDate` | When the document was made. Leave it unset for a file that is byte-for-byte the same every time you write it. |
| `Version` | `Pdf20` (the default) or `Pdf17`. A 1.7 file whose content needs nothing newer is written with a 1.4 header. |
| `CompressContentStreams` | `true` by default. Turn it off to read the drawing operators in a text editor. |
| `Encryption` | A `PdfEncryptionOptions`: AES-256 password protection with a user password, an owner password and a `PdfPermissions` set. |

The options are read once, when the composer is created.

## `PdfCanvas`: drawing a page

A `PdfCanvas` is a `Canvas`, so everything in [Canvas: the drawing surface](peachdrawing-core.md#canvas-the-drawing-surface) applies:
`DrawRectangle`, `DrawPath`, `DrawLine`, `DrawString`, `DrawImage` and its blended and masked variants, `PushClip`, `PushTransform`,
`PushBlendMode`, `CreateTile`, brushes with gradients and patterns. Code written against `Canvas` draws a PDF page the same way it
draws a bitmap with [PeachDrawing](peachdrawing.md)'s `RasterCanvas`.

- **Units and origin.** The origin is the top-left corner of the page and y grows downward, in the context's units (points unless
  `PixelsPerPoint` says otherwise).
- **Vector output.** Shapes, text and gradients are written as vector content. An effect PDF cannot express as vector content - a blur,
  a drop shadow, an arbitrary filter - is drawn into a bitmap at the resolution the context asks for (`RasterizationDpi`) and embedded
  as an image.
- **Text.** A run is shaped, with ligatures, kerning, bidirectional reordering and complex scripts, and written with the glyphs it uses
  embedded in a subset of the font. Text stays selectable and searchable. Colour fonts are drawn as vector artwork.
- **Colour.** An RGB colour is written as `DeviceRGB` and a CMYK colour as `DeviceCMYK`, each in the space it was made in.

## Encryption

```csharp
var options = new PdfComposerOptions
{
    Encryption = new PdfEncryptionOptions
    {
        UserPassword = "to open",
        OwnerPassword = "to lift the restrictions",
        Permissions = PdfPermissions.ReadOnly,
    },
};
```

The file is written with the PDF standard security handler using AES-256 (revision 6 of ISO 32000-2); older handlers (RC4, AES-128) depend
on deprecated algorithms and are not offered. A reader enforces `Permissions` voluntarily: they are a request made of well-behaved
software, not a cryptographic barrier. An encrypted file has a header of at least 1.7. With no `UserPassword` the document opens without
a password and the permissions still apply; with no `OwnerPassword` a random one is generated and discarded, so the restrictions can
never be lifted. The options are copied when the composer is created, so changing them afterwards does not affect a document in progress.

## Using it from PeachPDF

You do not need this package to render HTML to PDF - [PeachPDF](getting-started.md) depends on it and uses it for you. Reach for it
directly to write a PDF from your own drawing code, to build a report or label with the drawing API rather than markup, or to implement
a feature PeachPDF does not need.

## Licences

`PeachDrawing.PDF` is BSD 3-Clause. Its PDF writing engine is adapted from [PDFsharp](https://github.com/empira/PDFsharp) (MIT) and its
notice ships in the package; the engine is internal and not part of the API. See [License](license.md) for the whole list.
