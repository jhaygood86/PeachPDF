# PeachDrawing.Core

`PeachDrawing.Core` is the drawing-surface abstraction PeachPDF targets to write PDF: a `Canvas` to
draw shapes, text and images on, a `RenderContext` to resolve colors, fonts and paint objects from, and the
paint-ready value types (`Brush`, `Pen`, `PaintColor`, `Rect`, and the rest) that connect them. It has no
third-party dependencies beyond its own sibling package `PeachDrawing.Text` (for font/typeface types),
is trimmable and Native AOT compatible, and is versioned in lockstep with PeachPDF: the same version number
for every release, and PeachPDF depends on it.

```bash
dotnet add package PeachDrawing.Core
```

> **Status: pre-1.0.** Every type described below is the package's full public surface today - see
> `PublicApi.txt` for the exact, machine-checked snapshot. Two concrete `Canvas`/`RenderContext`
> implementations exist today: `PeachPDF` (`PdfSharpAdapter`/`GraphicsAdapter`, writing PDF, and
> `RasterCanvas`/`RasterRenderContext` as PeachPDF's own raster fallback for effects PDF can't express as
> vector content) and its own sibling package, `PeachDrawing` (a standalone CPU software rasterizer, usable
> with no PeachPDF reference at all - see [docs/peachdrawing.md](peachdrawing.md)). Until 1.0, the public
> API may change between releases.

## What this package is for

A document- or drawing-producing library needs somewhere to draw: rectangles, paths, text, images, all
composited with clips, transforms and blend modes. `PeachDrawing.Core` is that surface, deliberately
kept independent of any one backend:

- **`RenderContext`** is the per-render factory: it resolves colors and fonts, and builds the paint objects
  (`Brush`, `Pen`) a `Canvas` draws with.
- **`Canvas`** is the surface itself: push/pop clip, transform and blend-mode stacks (the same shape
  `System.Drawing.Graphics`/Java2D's `Graphics2D` use), and draw rectangles, polygons, paths, lines, text
  and images.
- **`Brush`/`Pen`/`GraphicsPath`** and the value types (`PaintColor`, `Rect`, `PaintPoint`, `Size`,
  `Matrix3x2`) are plain, readable data - not opaque, backend-owned handles - so any `Canvas`
  implementation, including one you write yourself, can interpret what another implementation built without
  reaching into its internals.

PeachPDF is one `Canvas` implementation among potentially several. Nothing in this package's public surface
names a PDF-specific or raster-specific type - see [Implementing your own Canvas](#implementing-your-own-canvas)
below for what that buys you.

## `RenderContext`: colors, fonts and paint objects

A `RenderContext` is the object a render is built around: it caches resolved brushes, pens and fonts for
the life of one render, and is where you register fonts before drawing with them.

```csharp
using PeachDrawing.Core;

// A concrete RenderContext implementation - PeachPDF's own PdfSharpAdapter, or your own.
RenderContext context = ...;

context.AddFontFamily(myFontFamily);
context.AddFontFamilyMapping(fromFamily: "Arial", toFamily: "Liberation Sans");

Font? font = context.GetFont("Liberation Sans", size: 16, PaintFontStyle.Bold);
Brush brush = context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255));
Pen pen = context.GetPen(PaintColor.Black);
```

- **Fonts.** `GetFont` resolves a font by family/size/style, applying any mapping registered with
  `AddFontFamilyMapping` and returning `null` when nothing matches. `GetFontForCodepoint` and
  `GetSystemFallbackFontForCodepoint` are the per-codepoint steps of CSS Fonts 4 §5's font-matching
  algorithm (a family restricted to faces that cover one codepoint, then any registered family at all) -
  what per-character font fallback needs. `AddFontFamilyFromUrl` and `AddLocalFontFamily` load a font from
  a (possibly relative, resolved via `GetResourceStream`/`BaseUri`) URL or a locally-installed face, the way
  a CSS `@font-face` rule's `url()`/`local()` sources do; `FontFaceDescriptors` carries the rule's own
  `font-weight`/`font-style`/`font-stretch` ranges, and `RuneInterval` (from `PeachDrawing.Text`) its
  `unicode-range`.
- **Colors.** `GetColor(string)` resolves a color by name (`"red"`, a hex string, whatever your
  implementation of `GetColorInt` accepts).
- **Brushes and pens.** `GetSolidBrush`, `GetLinearGradientBrush`, `GetRadialGradientBrush` and
  `GetConicGradientBrush` build (and, for a solid color or a color-keyed pen, cache) the `Brush`/`Pen`
  objects a `Canvas` draws with - see [Brush and Pen](#brush-and-pen-what-you-draw-with) below. A gradient
  whose stops mix `device-cmyk()` colors with RGB-authored ones throws `NotSupportedException`: a PDF
  shading's color space is one value for the whole object, so there is no single component count that fits
  a mixed-space stop list, and there is no defined RGB↔CMYK conversion in this project to paper over it.
- **Resource loading.** `BaseUri` and `GetResourceStream` are both `virtual` with an inert `null` default:
  resource loading is entirely optional for a `RenderContext` that doesn't need it (one driving a
  standalone drawing surface with no document/network concept at all). A concrete implementation overrides
  both to resolve `@font-face` URLs against a real document.
- **`Canvas` also exposes `GetSolidBrush`/`GetLinearGradientBrush`/`GetRadialGradientBrush`/
  `GetConicGradientBrush`/`GetPen`** as convenience wrappers that delegate to the `RenderContext` it was
  built from, so paint code holding a `Canvas` doesn't need to separately thread a `RenderContext` through
  just to build a brush.

## `Canvas`: the drawing surface

```csharp
Canvas canvas = ...; // a concrete implementation

canvas.PushTransform(Matrix3x2.CreateRotation(float.Pi / 6));
canvas.PushClip(new Rect(0, 0, 200, 100));

canvas.DrawRectangle(brush, x: 10, y: 10, width: 180, height: 80);
canvas.DrawString("Hello, PeachDrawing", font, PaintColor.Black,
    new PaintPoint(20, 40), new Size(160, 20));

canvas.PopClip();
canvas.PopTransform();
```

- **State stacks.** `PushClip`/`PopClip` (a rectangle, or a `PushClip(GraphicsPath)` for a non-rectangular
  region such as an SVG `clip-path`), `PushClipExclude` (subtract a rectangle from the current clip),
  `PushTransform`/`PopTransform` (a `System.Numerics.Matrix3x2`, composed before/with the current
  transform), and `PushBlendMode`/`PopBlendMode` (`PaintBlendMode`, the 16 PDF 32000-1 §11.3.5 separable
  and non-separable modes, the same set CSS Compositing and Blending Level 1's `mix-blend-mode` uses) are
  all stack-shaped: what you push, you pop, in order. `SuspendClipping`/`ResumeClipping` temporarily
  restore the clip stack to its initial (construction-time) state and put it back - what a `position: fixed`
  element's paint needs to reach its containing block regardless of ancestor clips.
- **Drawing.** `DrawRectangle`, `DrawPolygon`, `DrawPath`, `DrawLine` (each with a `Brush` fill or `Pen`
  stroke overload), `DrawString` (a run of already-shaped text - see below), `DrawGlyphs` (already-placed
  glyphs, e.g. from `PeachDrawing.Text.Shaping.Shaper.Shape`), `DrawImage` and its
  `DrawImageWithOpacity`/`DrawImageBlendedOver`/`DrawImageWithColorMatrix`/`DrawImageMasked`/
  `DrawImageAlphaMasked` variants (opacity, a `PaintBlendMode`, a `ColorMatrix` color transform, and two
  masking forms).
- **Text measurement.** `MeasureString` (both the "how big is this text" overload and the
  "how much of this text fits in this width" one), `CountShapedGlyphs` and `GetTextOutline` (the stroked
  outline of a run - what a `text-decoration: underline wavy` or similar needs to actually stroke) let a
  caller measure and lay out text before committing to draw it, all against the same shaping `Canvas.DrawString`
  itself uses internally, so a measurement never disagrees with what gets painted.
- **Tiles.** `CreateTile(width, height)` returns a fresh, independent `(Canvas, Image)` pair for
  content that's drawn once and reused many times (an SVG `<pattern>`'s cell, or an isolated group for CSS
  `opacity`/blend modes): draw into the returned `Canvas`, then `DrawImage` the returned `Image` - a
  backend that can represent this as real shared vector content (PDF's Form XObjects) does so, one copy
  regardless of how many times you draw it.
- **Tagged-PDF structure.** `BeginArtifact`, `BeginMarkedContent`/`EndMarkedContent` and
  `BeginVariableText`/`EndVariableText` bracket content for a PDF/UA structure tree; a `Canvas` with no
  notion of tagged output treats them as no-ops.

`PixelsPerPoint` is the scale between this `Canvas`'s own coordinate space and true PDF points (`1.0`, the
base default, for a `Canvas` with no such distinction); `IsOffscreenTile` says whether this `Canvas` is a
tile from `CreateTile` rather than a page/document's own surface.

## `Brush` and `Pen`: what you draw with

`Brush` is an abstract base with a closed set of concrete, plain-data subtypes - not an opaque handle a
`Canvas` implementation has to downcast to reach real data out of:

```csharp
Brush solid = context.GetSolidBrush(PaintColor.FromArgb(255, 231, 76, 60));

Brush gradient = context.GetLinearGradientBrush(
    new PaintPoint(0, 0), new PaintPoint(200, 0),
    [(PaintColor.White, 0), (PaintColor.Black, 1)]);

if (gradient is LinearGradientBrush linear)
{
    // linear.Start, linear.End, linear.Stops (GradientStop: PaintColor + Position), linear.Spread
}
```

- **`SolidBrush`** (`PaintColor`) is a single fill color.
- **`LinearGradientBrush`** (`Start`, `End`, `Stops`, `Spread`) varies along a line between two points.
- **`RadialGradientBrush`** (`Center`, `Focus`, `RadiusX`, `RadiusY`, `Stops`, `Spread`) varies with
  distance from a center (or an off-center focus) out to an ellipse.
- **`ConicGradientBrush`** (`Center`, `OuterRadius`, `Stops`, `AnglesRadians`) varies with angle around a
  center - CSS `conic-gradient()`; it has no repeating form yet, so it carries no `GradientSpread`.
- **`GradientSpread`** is `Pad` (the CSS default, a gradient's own two ends hold their end colors past
  their own extent) or `Repeat` (`repeating-*-gradient()`).

A `Canvas` implementation pattern-matches on the concrete `Brush` subtype to build whatever native paint
representation it needs - the same discriminated-union dispatch this codebase's own `CssImage`/
`CssImagePainter.Paint` already uses for background/gradient/list-marker images.

`Pen` is a concrete, mutable class - `Width`, `MiterLimit`, `DashStyle`, `LineCap`, `LineJoin`,
`DashPattern`/`DashOffset` (or `SetDashPattern(pattern, offset)` to set both at once) and `Paint` (a
`Brush` - a pen strokes with any brush, not just a solid color, for e.g. an SVG `stroke="url(#gradient)"`).

## `GraphicsPath`: recording and flattening vector geometry

```csharp
using GraphicsPath path = canvas.GetGraphicsPath();
path.Start(0, 0);
path.LineTo(100, 0);
path.AddBezierTo(150, 0, 150, 100, 100, 100);
path.CloseFigure();

canvas.DrawPath(brush, path);

// Read the path's geometry back, independent of whatever native representation the concrete
// implementation also built - what a raster fallback, or your own Canvas, needs:
foreach (PathContour contour in path.Flatten(tolerance: 0.1))
{
    // contour.Points is a polyline within `tolerance` of the true curve; contour.Closed says
    // whether it was closed (CloseFigure) - an open subpath gets caps, a closed one a join.
}
```

`GraphicsPath`'s recorder methods (`Start`, `LineTo`, `ArcTo` - a CSS border-radius-style corner arc -,
`AddMove`, `AddBezierTo`, `AddArc` - a general SVG-style endpoint-parameterized arc -, `CloseFigure`,
`Transform`, `AddPath`) are `virtual`, not `abstract`: the base implementation also records a concrete,
backend-agnostic segment list (every arc converted to an equivalent cubic Bézier at record time), so
`Flatten` works for any subclass with no extra work - a subclass overriding a recorder method to also
build its own native representation still calls `base.MethodName(...)` to keep the shared list in sync.
`FillMode` (`Nonzero`/`EvenOdd`, PDF 32000-1 §8.5.3's two fill rules) and `ClipToRect`/`Dispose` are the
only genuinely abstract members.

## `Font`, `FontFamily` and `Image`

`Font` is a resolved, sized typeface, built from a `PeachDrawing.Text.Typeface` (`Font.Typeface`) plus
whatever synthetic bold/italic a match required (`Font.SyntheticStyle`) - deliberately still a size-bound
"font" object, unlike `Typeface` itself, because it's the paint-ready object `Canvas.DrawString` actually
needs. `FontFamily` is the family a font was matched from. `Image` (`Width`, `Height`, `Interpolate`,
`GetPixels()`) is a drawable raster or vector image; `GetPixels()` returns a `PixelBuffer` (premultiplied
RGBA8) for an image whose pixels a `Canvas` implementation needs to read directly, or `null` for one with
none (a vector tile from `CreateTile`, say).

## Implementing your own `Canvas`

Nothing in `Canvas`'s or `RenderContext`'s public surface names a PDF-specific or raster-specific type -
that's the actual point of publishing this package separately from PeachPDF. A `Canvas` implementation
needs to:

1. Derive from `RenderContext`, implementing its abstract members (`GetCssMediaType`, color resolution,
   image decoding, font creation) for whatever backend you're targeting.
2. Derive from `Canvas`, implementing its abstract drawing/clip/transform/blend-mode members against your
   backend's own native drawing calls.
3. Derive from `GraphicsPath` only if you need your own native path representation alongside the shared
   one `Flatten` already gives you for free - many backends can get away with the base implementation
   entirely.
4. Derive from `Image`/`FontFamily`/`Font` as your backend's font/image loading needs.

PeachPDF's own `PdfSharpAdapter`/`GraphicsAdapter` (writing vector PDF content), its `RasterCanvas`
(rasterizing effects PDF can't express as vector content, such as `filter: blur()`), and `PeachDrawing`'s
own standalone `RasterCanvas`/`RasterRenderContext` (see [docs/peachdrawing.md](peachdrawing.md)) are all
independent, complete implementations of exactly this - reading their source is the closest thing to a
worked example. A few things worth knowing before you start:

- **The paint objects are already backend-agnostic.** `Brush`/`Pen`/`PaintColor`/`Rect`/`PaintPoint`/`Size`
  carry real, readable data - there's nothing to downcast to another implementation's internals to read.
- **`GraphicsPath.Flatten` does the curve-to-polyline work for you.** Unless your backend has genuine native
  curve support you want to preserve, you likely never need to override any of `GraphicsPath`'s recorder
  methods at all.
- **A handful of `Canvas`/`RenderContext` members are `internal`, not part of this public surface**: they're
  PeachPDF's own HTML/CSS/SVG/PDF rendering-pipeline integration points (an SVG filter's access to what's
  behind an element, a raster-fallback seam for effects vector content can't express, PDF/A transparency
  policy, and the like) - not "the drawing API," which is everything described above in full. You won't
  need them to draw shapes, text and images on a `Canvas`.

## Licences

`PeachDrawing.Core` is BSD 3-Clause. It carries no third-party code of its own; its one dependency,
`PeachDrawing.Text`, carries its own notices in its `THIRD-PARTY-LICENSES.md`. See [License](license.md)
for the whole list.
