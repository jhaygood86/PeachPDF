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

> **Status: pre-1.0.** The package's public surface is everything on this page - `PdfRenderContext`, `PdfComposer`,
> `PdfComposerPage`, `PdfCanvas`, the document options, links, bookmarks, the tag recorder and the form builder - and
> `PublicApi.txt` is the exact, machine-checked snapshot of it. PeachPDF is the package's other consumer: it renders HTML and CSS
> onto these same canvases and builds PDF/A, tagged, linked and interactive documents through this same API, so this is the PDF
> writer PeachPDF uses, not a copy built beside it. Until 1.0, the public API may change between releases.

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

PdfComposerPage page = composer.AddPage(widthPoints: 400, heightPoints: 300);   // 400 x 300 points
PdfCanvas canvas = page.Canvas;
canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 200, 30, 60)), x: 40, y: 40, width: 160, height: 90);
canvas.DrawString("Hello, PeachDrawing.PDF", font, PaintColor.Black, new PaintPoint(40, 170), new Size(340, 40));

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
2. **`AddPage(widthPoints, heightPoints)`** appends a page and returns a `PdfComposerPage`: its `Canvas` is what you draw with, and its
   links make parts of it clickable. Page sizes are in points, 1/72 inch: `595 x 842` is A4 and `612 x 792` is US Letter. Pages can
   differ in size.
3. **`Save(stream)`** finishes every page and writes the file. The stream is not closed. A document needs at least one page; pages can
   be added and the document saved again, and each save writes everything so far.

A canvas stays usable until the composer is saved or disposed, so several pages can be drawn in any order. `page.CreateCanvas(context)`
makes another canvas over the same page, drawing with a different `PdfRenderContext` - handy for a piece of work that needs its own
fonts - and `page.Shift(x, y)` moves the origin for a scope, which is how a layout engine places content inside a page's margins.
Neither a composer nor its canvases are thread safe: use one composer per file.

`Configure(options)` applies a `PdfComposerOptions` after construction, for a document whose settings are not known when the composer is
created. The document information (title, author, ...) can be written again by a later call; the format settings - version,
conformance, colour management, encryption - are fixed by the first call, and a later call that asks for different ones is rejected
rather than producing a file whose header disagrees with how its pages were written.

### `PdfComposerOptions`

| Option | Meaning |
|---|---|
| `Title`, `Author`, `Subject`, `Keywords`, `Creator` | The document information shown in a reader's properties dialog. Non-ASCII text is written correctly. |
| `CreationDate` | When the document was made. Leave it unset for a file that is byte-for-byte the same every time you write it. |
| `Language` | The document's natural language (BCP 47, such as `en-US`), which the accessible PDF/A levels (PDF/A-1A, 2A and 3A) require. |
| `Producer` | The software that wrote the file. Defaults to this package. |
| `Version` | `Pdf20` (the default) or `Pdf17`. A 1.7 file whose content needs nothing newer is written with a 1.4 header. |
| `CompressContentStreams`, `BrotliCompression` | Content streams are Flate-compressed by default. Turn compression off to read the drawing operators in a text editor; Brotli (PDF 2.0) is opt-in, because not every reader decodes it yet. |
| `Conformance`, `XConformance` | The PDF/A and PDF/X level the file claims - see [Archival and print conformance](#archival-and-print-conformance). |
| `ColorOptions` | Colour management: the output intent profile and how colours are converted to it. |
| `EmbeddedFiles`, `FacturX` | Files attached to the document. |
| `WriteXmpMetadata`, `CustomXmpProperties` | The XMP packet, written for you whenever a PDF/A level is claimed. |
| `ImageCompression`, `DownscaleImages`, `DownscaleQuality`, `MaximumDownscaleMultiplier`, `JxlPassthrough` | How images are embedded and how large they may be. |
| `FlattenTransparency`, `PortableColorFilters` | Draw transparency and colour filters as plain content, for readers that handle them poorly. |
| `Encryption` | A `PdfEncryptionOptions`: AES-256 password protection with a user password, an owner password and a `PdfPermissions` set. |

The options are read when they are applied: a later change to the object does not reach a document already configured.

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

## Archival and print conformance

Set `Conformance` to a `PdfAConformance` (`PdfA1B`, `PdfA2B`, `PdfA2A`, `PdfA3B`, `PdfA4`, ...) or `XConformance` to a `PdfXConformance`
(`X1a`, `X3`, `X4`, `X6`) and the composer writes what that level requires of the file - the output intent, the XMP metadata, the right
header version - and refuses what it forbids: encryption, transparency in a level that has none, an image in a colour space the output
intent cannot describe. A violation is reported when the offending content is drawn, as an exception derived from
`CanvasConstraintException`, rather than discovered by a validator afterwards. `ColorOptions` carries the ICC profile the output intent
names and how colours are converted to it (`PreserveAsAuthored`, `ConvertToOutputIntent`, `GrayscaleViaK`, `ConvertToProfile`).

`EmbeddedFiles` attaches files to the document - the PDF/A-3 and PDF/A-4F way to carry a source or data file next to a page rendering.
A `PdfEmbeddedFile` has the bytes, a file name, a MIME type, a description, a modification date and a `PdfEmbeddedFileRelationship`
(`Source`, `Data`, `Alternative`, `Supplement`, `Unspecified`). `FacturX` (a `PdfFacturXInfo`) marks the attachment as the Factur-X /
ZUGFeRD invoice XML of an e-invoice.

## Links, destinations and bookmarks

Positions are in points from the page's top-left corner, y down, and the page does the conversion to PDF's own convention.

```csharp
composer.AddNamedDestination("terms", pageIndex: 2, topPoints: 120);

PdfLink external = page.AddLink(new Rect(40, 170, 160, 20), "https://example.com/");
PdfLink internalLink = page.AddDestinationLink(new Rect(40, 200, 160, 20), "terms");

composer.AddBookmarks(
[
    new PdfBookmark(level: 1, "Introduction", isOpen: true) { PageIndex = 0, TopPoints = 40 },
    new PdfBookmark(level: 2, "Background", isOpen: false) { PageIndex = 1, TopPoints = 60 },
    new PdfBookmark(level: 1, "Project site", isOpen: false) { Uri = "https://example.com/" },
]);
```

`AddLink` and `AddDestinationLink` take an optional quadrilateral for a link over rotated or skewed content; without one the link covers
`bounds`. A bookmark nests under the nearest preceding bookmark of a shallower level, and its destination is a page index (and a position)
or a URI.

## Tagged PDF

`composer.CreateTagRecorder()` returns a `PdfTagRecorder` that records the logical structure of the document while its pages are drawn, so
a reader can navigate by heading and assistive technology can read the page in order. Call `BeginPage(page)` as each page is added, wrap
the drawing of each piece of content in the scope an `Open` method returns, and call `Finish()` after the last page:

```csharp
var tags = composer.CreateTagRecorder();
tags.BeginPage(page);

using (tags.OpenGroupingElement(chapterKey, "Sect"))
{
    using (tags.OpenContentElement(page.Canvas, headingKey, "H1"))
        page.Canvas.DrawString("Chapter one", font, PaintColor.Black, new PaintPoint(40, 60), new Size(320, 30));

    using (tags.OpenContentElement(page.Canvas, imageKey, "Figure", altText: "A red square"))
        page.Canvas.DrawRectangle(redBrush, 40, 100, 80, 80);
}

using (tags.OpenArtifact(page.Canvas))
    page.Canvas.DrawLine(rulePen, 40, 280, 360, 280);        // decoration that is not part of the structure

tags.Finish();
composer.Save(output);
```

An element is identified by a *key* - any object you choose - so the same key opened on a later page continues the same element (a paragraph
that runs across a page break is one element). `OpenContentElement` tags what is drawn inside the scope, `OpenGroupingElement` nests the
elements opened inside it, `OpenListItemBodyElement` and `OpenPhantomAncestors` cover the list-item and anonymous-wrapper shapes HTML
layout produces, `AttachMathMlSource` attaches a formula's MathML as an associated file, and `LinkAnnotation(key, link)` ties a `PdfLink` to
the element of the content it covers, which PDF/UA requires. Pass `requireAlternateText: true` to `CreateTagRecorder` to make a figure
without alternate text an error instead of a silent gap.

## Interactive forms

`composer.CreateFormBuilder(context)` returns a `PdfFormBuilder`; `AddField(page, definition)` places a `PdfFormFieldDefinition` on a page.
A definition has a `Kind` (`Text`, `Checkbox`, `Radio`, `Select`), a `Name`, a `Value` (or `IsChecked`, or `Options` for a drop-down), the
`Bounds` on the page in points, and the flags a field can carry (`ReadOnly`, `Required`, `Password`, `MaxLength`, `CombCells`, ...). The
look of each state is drawn by your `IPdfFormAppearance`, which receives a `Canvas` the size of the control, so a field looks like the
rest of your page. Radio buttons that share a `Name` are one field.

## Drawing a page of an existing PDF

`PdfPageSource` opens an existing PDF and paints its pages onto any `Canvas` - a `PdfCanvas` to stamp a page over or under a document you are writing, a raster canvas to render it:

```csharp
var source = PdfPageSource.Open(File.ReadAllBytes("letterhead.pdf"));
var (width, height) = source.GetPageSize(0);          // points, after the page's rotation

var page = composer.AddPage(width, height);
var notDrawn = source.Paint(0, page.Canvas);          // features of the page that could not be drawn
```

`Paint` draws the page's content again rather than copying the file's objects, so it works the same on every canvas. The canvas' origin is the top-left of the area the page occupies and its units are points; the page is clipped to its own crop box (or media box) and honours `/Rotate`. On a `PdfCanvas`, text from an embedded TrueType or OpenType font comes out as real text with the original's text map, so it still selects, searches and copies as the same characters.

The importer reads classic and stream cross-reference tables, object streams and incremental updates, and rebuilds a file whose table is missing or damaged. It draws paths (fill, stroke, clip, dashes, caps and joins), the graphics state, device, ICC-based, Lab and indexed colour, constant alpha, blend modes, luminosity and alpha soft masks (the masked content is drawn into a tile, the mask into another, and the two composited, so a mask applies to a group as a whole), form XObjects, images (JPEG, also with a mask; Flate, LZW, run-length and ASCII-filtered samples in any bit depth and colour space the importer reads, with `/Decode`, stencil and colour-key masks and `/SMask`) and text in simple TrueType fonts and Type 3 fonts (filled, stroked and clipping render modes for the former) and Type0 fonts with an `Identity-H` encoding. Axial and radial shadings are drawn whether used as a pattern or with `sh`, with their colour functions (sampled, exponential, stitching and PostScript calculator) read into gradient stops; a shading that does not extend past its ends paints as if it did. Mesh shadings (free-form and lattice triangle meshes, Coons and tensor-product patches) are rasterized into an image sized to how large the mesh is drawn, so on a PDF canvas a mesh becomes a raster. What it cannot draw yet - JPEG 2000, CCITT and JBIG2 images, function-based shadings, tiling patterns, and Type 1 and bare CFF fonts - is skipped, and `Paint` returns a name for each such feature it met. Files protected by the Standard security handler with AES-128 or AES-256 are read with the empty user password, which is what "restrict editing and copying" protection uses. Only ciphers that .NET provides are supported, so a file encrypted with RC4 (the older 40 and 128 bit protection) throws `NotSupportedException`, as does a file that needs a password to open or uses a handler other than the Standard one. Data that is not a PDF throws `InvalidDataException`.

## Using it from PeachPDF

You do not need this package to render HTML to PDF - [PeachPDF](getting-started.md) depends on it and uses it for you. Reach for it
directly to write a PDF from your own drawing code, to build a report or label with the drawing API rather than markup, or to implement
a feature PeachPDF does not need.

## Licences

`PeachDrawing.PDF` is BSD 3-Clause. Its PDF writing engine is adapted from [PDFsharp](https://github.com/empira/PDFsharp) (MIT) and its
notice ships in the package; the engine is internal and not part of the API. See [License](license.md) for the whole list.
