# PeachDrawing.Svg

`PeachDrawing.Svg` is a vector SVG backend for [PeachDrawing.Core](peachdrawing-core.md): the third concrete
`Canvas`/`RenderContext` beside the [PDF writer](peachdrawing-pdf.md) and the [raster canvas](peachdrawing.md),
usable with **no PeachPDF reference at all**. Draw shapes, gradients, clips, text and images onto an `SvgCanvas` and save
the result as an SVG document - no HTML, CSS or PDF involved anywhere. It keeps everything SVG can express as vector
content: paths keep their curves, gradients stay gradients, clips stay clips, glyphs are exact outlines (or selectable
`<text>` with an embedded subset font on request), and PNG, JPEG, GIF, WebP and AVIF images are embedded exactly as they
came. It depends on `PeachDrawing.Core`, `PeachDrawing` (for its raster fallback) and `PeachImage`, is trimmable and
Native AOT compatible, and is versioned in lockstep with PeachPDF: the same version number for every release.

```bash
dotnet add package PeachDrawing.Svg
```

> **Status: pre-1.0.** `SvgRenderContext`, `SvgComposer`, `SvgComposerOptions`, `SvgTextMode` and
> `SvgCanvas` are the package's full public surface - see `PublicApi.txt` for the exact, machine-checked snapshot. Until
> 1.0 the public API may change between releases.

## Quick start

```csharp
using PeachDrawing.Core;
using PeachDrawing.Svg;

var context = new SvgRenderContext();
var composer = new SvgComposer(context, widthPoints: 480, heightPoints: 300);
var g = composer.Canvas;

g.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), x: 20, y: 20, width: 200, height: 100);

var font = context.GetFont("Arial", size: 24, PaintFontStyle.Regular);
if (font is not null)
{
    g.DrawString("Hello, SVG", font, PaintColor.Black, new PaintPoint(20, 150), g.MeasureString("Hello, SVG", font));
}

await using var file = File.Create("out.svg");
composer.Save(file);
```

An `SvgComposer` is one SVG document - SVG has no pages, so there is no page to add; make one composer for each image. Like the PDF writer, `SvgComposer` works in points with the origin at the top left and y growing
downwards; the document's `width`/`height` are in `pt` and its `viewBox` is in the render context's layout units
(`SvgRenderContext.PixelsPerPoint` of them to a point, `1` by default). The `SvgRenderContext` constructor registers every font
installed on the machine; `AddFont` registers a TrueType, OpenType, WOFF or WOFF2 font from a stream under a family name, which is
the way to get the same text on every machine.

With `PixelsPerPoint` at its default of `1` every coordinate is simply a point. A host that lays out in other units (PeachPDF's
HTML engine works in `PixelsPerInch / 72` units to the point) sets `PixelsPerPoint` and then follows the same reading the PDF and
raster canvases give the `Canvas` API: rectangles, lines, polygons, image rectangles, clip rectangles, brush geometry and glyph
positions are in layout units, while the coordinates of a `GraphicsPath` (for `DrawPath` and `PushClip`), and a `Pen`'s width and
dash lengths, are in points. `SvgCanvas` scales those to the document's units when it writes them.

The repository's `src/examples/PeachDrawing.Svg.Example` project is a runnable version of this with a handful of scenes (shapes and
dashes, gradients, clips and transforms, blend modes and layer effects, patterns and images, text in both modes); run it and open the
files in a browser.

## What each `Canvas` call becomes

| `Canvas` | SVG |
| --- | --- |
| `DrawRectangle`, `DrawLine`, `DrawPolygon` | `<rect>`, `<line>`, `<polygon>` |
| `DrawPath` | `<path>`, written from the path's own curves (cubic Béziers, lines, closed figures) - nothing is flattened; `FillMode.EvenOdd` is `fill-rule="evenodd"` |
| `Pen` | `stroke`, `stroke-width`, `stroke-linecap`, `stroke-linejoin`, `stroke-miterlimit`, `stroke-dasharray`, `stroke-dashoffset`; a zero width is a one-pixel `non-scaling-stroke` hairline |
| `SolidBrush` | a colour (`fill`, with `fill-opacity` for a translucent one); a CMYK colour is written as its RGB rendition |
| `LinearGradientBrush`, `RadialGradientBrush` | `<linearGradient>`, `<radialGradient>` in user space: `Pad` and `Repeat` spread, the focus and focus radius, and an elliptical radius or a brush transform as a `gradientTransform` |
| `TileBrush`, `HatchBrush` | `<pattern>` carrying the brush's transform; a tile made by `CreateTile` stays vector content |
| `ConicGradientBrush` | no SVG equivalent: a fan of 360 thin wedges clipped to the shape |
| `PushClip`, `PushClipExclude` | `<clipPath>` and a group that uses it; an exclusion is an even-odd path of everything but the rectangle. While something is blended the clip is not a group (a clipped group would isolate the blend from the page behind it): each element carries the clip itself |
| `PushTransform` | `<g transform="matrix(...)">` |
| `PushBlendMode` | `mix-blend-mode` on each shape drawn while it is pushed |
| `BeginLayer` | `<g opacity mix-blend-mode filter>` as an isolated group; `BlurEffect`, `DropShadowEffect` and `ColorMatrixEffect` are `feGaussianBlur`, `feDropShadow` and `feColorMatrix` primitives of one `<filter>` |
| `DrawImage` | `<use>` of an `<image>` defined once in `<defs>`; a source rectangle is a clip over the scaled whole; `ImageSampling.Nearest`/`Pixelated` is `image-rendering: pixelated` |
| `DrawImageMasked`, `DrawImageAlphaMasked` | `<mask>` (a luminance mask; an alpha mask first turns the mask image into white of its own, or its inverse, opacity) |
| `DrawImageWithColorMatrix`, `DrawImageBlendedOver`, `DrawImageWithOpacity` | `feColorMatrix`, an isolated group blending the top image, and `opacity` |
| `CreateTile` | the content drawn into the tile becomes a clipped `<g>` in `<defs>`, referenced wherever the tile is drawn |
| `DrawString`, `DrawGlyphs` | see [Text](#text) |
| `AddLink`, `AddDestinationLink`, `AddDestination` | `<a href>` around an unpainted, clickable `<rect>` (or a `<polygon>` for a link over a rotated element), written after everything else so it lies on top; a destination is an empty `<rect id="anchor-...">` at its height, and a link to it is `href="#anchor-..."` |
| `BeginRasterSurface`, `DrawRaster` | the one non-vector path: a bitmap a caller asks for is embedded as a PNG |

An image a browser can show (PNG, JPEG, GIF, WebP, AVIF) is embedded byte for byte, never re-encoded, so an animated GIF
stays animated; any other format PeachImage reads is embedded as a PNG of its pixels. An image is embedded once however often
it is drawn, and gradients, clip paths, patterns, masks and filters that are identical are defined once and shared.

Each push (clip, transform, blend mode, layer) is counted like the PDF canvas's saved graphics states: a pop undoes the innermost
push, and a pop with nothing pushed throws `InvalidOperationException`. Anything still open when the document is saved is closed, and
a group that holds nothing is not written.

## Text

`SvgComposerOptions.TextMode` chooses how text is written:

- **`SvgTextMode.Outlines`** (the default) - every glyph is an exact vector outline. The text looks identical in every viewer,
  whatever fonts it has; shaping, kerning, synthetic bold and italic, colour glyphs (COLR/CPAL, drawn as vector paths and
  gradients), bitmap glyphs and OpenType-SVG glyphs are all handled. Each glyph's outline is defined once per size and slant and
  every occurrence is a `<use>`, so a screenful of text is far smaller than the outlines written out again at every position. The
  text is not selectable.
- **`SvgTextMode.Text`** - selectable, searchable `<text>`, with each character placed at the position the shaper chose and a
  subset of the font embedded in the document as an `@font-face`. A run a viewer could not reproduce that way - ligatures,
  reordering, colour or bitmap glyphs, or a font that cannot be embedded as an OpenType subset - is still written as outlines, so
  the text always looks right.

`Canvas.InvisibleText` is honoured in text mode (the text is written transparent, so it can be selected over content drawn
some other way) and skipped in outline mode.

## Limitations

- **Conic gradients** have no SVG paint server, so by default they are a fan of 360 thin wedges (one degree each) clipped to the
  shape, or, for a `Pen`, painted through a mask of the stroke; a very large gradient shows the steps. `SvgComposerOptions.ConicGradients =
  SvgConicGradientMode.Css` writes a CSS `conic-gradient()` in a `<foreignObject>` instead - exact and smooth in a browser - inside a
  `<switch>` whose fallback is the wedges, so a viewer that ignores foreign content (resvg, Inkscape and most others) still shows them.
- **Colour.** Gradient stops interpolate in non-premultiplied sRGB, the SVG default; a CMYK colour is written as its RGB rendition.
- **Links to another document.** A destination is a fragment of its own SVG, so a link to a place named on another canvas cannot be followed.
  That happens only when one multi-page source (an HTML document with paper pages) is written as several SVGs; a source written as one
  continuous sheet, as the showcases are, has every destination in the file.
- **Bookmarks, forms and tagging** are PDF concepts and have no counterpart: `BeginMarkedContent` and its relatives do nothing.

## Licences

PeachDrawing.Svg is BSD 3-Clause-licensed like the rest of PeachPDF; it contains no third-party code (see its
`THIRD-PARTY-LICENSES.md`).
