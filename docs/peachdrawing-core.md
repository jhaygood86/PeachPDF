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

`Flatten` gives you polylines; when the geometry has to stay curved, `GetCurveContours()` gives you the same subpaths as
`CurveContour` values instead - a `Start` point, the `PathCommand` lines and cubic Béziers that follow it (an arc is
already recorded as cubics, so there is no third kind), and whether the subpath was closed. It is a snapshot: editing the
path afterwards does not change a list you already hold.

`PeachDrawing.Core.Geometry.PolygonClipper.ClipToRect` clips a closed polygon (convex, concave or self-intersecting) to an
axis-aligned rectangle. Fewer than three points back means nothing with area is left inside.

## Shapes, measuring and combining paths

The `PeachDrawing.Core.Geometry` namespace works on a `GraphicsPath` without drawing it.

```csharp
using PeachDrawing.Core.Geometry;

using GraphicsPath badge = canvas.GetGraphicsPath();
badge.AddRoundedRectangle(new Rect(10, 10, 200, 80), radius: 12);   // per-corner elliptical radii also work
badge.AddStar(cx: 110, cy: 50, outerRadius: 30, innerRadius: 12, points: 5);

var measure = new PathMeasure(badge);
double length = measure.Length;               // along every subpath, not counting the jump between them
double area = measure.Area;                   // holes (subpaths wound the other way) subtract
Rect box = measure.Bounds;                    // tight: includes where a curve bulges past its end points
PathSample halfway = measure.PointAtLength(length / 2);   // X, Y and the direction of travel there
```

- **`PathShapes`** adds a rounded rectangle, ellipse, circle, pie slice, polygon, regular polygon, star or open
  wavy line (`AddWave`) as a new closed subpath, so shapes combine in one path.
- **`PathMeasure`** reads the path once, so later edits to it are not seen.
- **`PathOperations.Combine(first, second, PathOperation.Union | Intersect | Difference | Xor, destination)`** adds
  the outline of the combined area to `destination`. Each input is read as a filled area under its own
  `FillMode` (so it may have holes and cross itself), and the result **keeps the true curves**: lines stay lines and
  cubic Béziers stay cubic Béziers, cut where the shapes cross. The result is filled with the nonzero rule. Two
  boundaries closer than about a millionth of the inputs' size are treated as touching.
- **`PolygonClipper.ClipToRect`** clips a closed polygon to a rectangle.
- **`PeachDrawing.PathStroker.Stroke(path, pen, destination)`** (in the `PeachDrawing` package) adds the *area* a
  stroke of the path would cover - width, caps, joins, miter limit and dashes - as an ordinary outline you can fill,
  measure or combine. Round caps and joins come out as short straight segments.
- **`GraphicsPath.GetCurveContours()`** is the curve-preserving counterpart of `Flatten`: each subpath as a
  `CurveContour` of `PathCommand` lines and cubics. It is what measuring and combining read.

## Layers and effects

`canvas.BeginLayer(new LayerOptions(...))` starts an isolated group. Draw onto `layer.Canvas` in the same coordinates
as `canvas`; disposing the layer composites everything drawn as one piece.

```csharp
// CSS "opacity" semantics: overlapping shapes inside do not show through each other.
using (var layer = canvas.BeginLayer(new LayerOptions(Opacity: 0.5)))
{
    layer.Canvas.DrawRectangle(red, 10, 10, 100, 100);
    layer.Canvas.DrawRectangle(red, 60, 60, 100, 100);
}

// A soft shadow: the layer is drawn into a bitmap covering Bounds, blurred, then composited.
var effects = new LayerEffect[] { new BlurEffect(sigma: 4) };
var (mx, my) = RasterLayerEffects.GetInkMargin(effects);          // how far the blur spreads
var bounds = new Rect(x - mx, y - my, w + 2 * mx, h + 2 * my);
using (var shadow = canvas.BeginLayer(new LayerOptions(Bounds: bounds, Effects: effects)))
    shadow?.Canvas.DrawRectangle(grey, x, y, w, h);
```

`LayerOptions` carries the layer's opacity, blend mode, an optional `ColorMatrix` and a `Bounds` region; `Effects` adds
`BlurEffect`, `DropShadowEffect` and `ColorMatrixEffect`. `BeginLayer` returns `null` when the canvas cannot make a
layer (a measure-only pass) or, for a layer with effects, cannot apply them - draw straight onto the canvas then. The
default implementation builds a layer from `CreateTile` and the `DrawImage…` methods, so every `Canvas` gets one; a
canvas that can apply effects overrides `SupportsLayerEffects` and `ApplyLayerEffects` (the `PeachDrawing`
package's `RasterLayerEffects.Apply` does the work on a `RasterSurface`).

## Tile and hatch brushes

A `TileBrush` repeats a picture across the plane, so a shape filled with it shows the part of the grid under it and
neighbouring shapes continue the same pattern.

```csharp
var (tile, image) = canvas.CreateTile(20, 20)!.Value;
tile.DrawRectangle(dark, 0, 0, 10, 20);
tile.Dispose();

// 20 x 20 cells, the whole grid turned 30 degrees.
var brush = new TileBrush(image, 20, 20, Matrix3x2.CreateRotation(MathF.PI / 6));
canvas.DrawPath(brush, shape);

canvas.DrawRectangle(new HatchBrush(HatchStyle.DiagonalCross, PaintColor.Black, PaintColor.White, spacing: 8), 0, 0, 200, 100);
```

On a PDF canvas a tile made by `CreateTile` stays vector content and becomes a real PDF tiling pattern: written once,
however many cells the shape covers. A tile of pixels is embedded once inside the pattern, and `TileBrush.Sampling`
says whether a viewer may smooth it. Two things to know about PDF output. A viewer draws a rotated or skewed tiling
pattern cell by cell with anti-aliased edges, which shows as hairline seams, so a grid that the brush transform or the
canvas transform turns is instead painted as separate cells under a clip (unless that would be over ten thousand cells, when
the pattern is used). And a viewer may round the size of a pattern cell to whole device pixels, so a pattern whose cells are
not a whole number of pixels at the viewer's zoom can drift slightly from its true period. The raster canvas reads the tile's
bitmap with wrap-around filtering, so cells join without a seam. `HatchBrush` draws one cell of lines through `CreateTile`; `ToTileBrush` gives that cell for a canvas
that has no hatch of its own. A pen can stroke with either brush.

## Anti-aliasing and image sampling

`canvas.PushAntiAlias(false)` / `PopAntiAlias()` turn edge smoothing off or on for the shapes drawn in between, and
nest like the other state stacks. On a raster canvas this is exact; the render-wide setting
(`RasterAntiAliasing`) is a ceiling, so a request for smoothing never overrides a render that turned it off. The default
implementation can only honour `true`, through the older `SetAntiAliasSmoothingMode` pair it is built on.

`canvas.DrawImage(image, destRect, ImageSampling.…)` picks how the image's pixels are read: `Automatic` (what the image's
own `Interpolate` asks for), `Nearest`, `Bilinear`, `Bicubic` (Catmull-Rom over sixteen pixels; the raster canvas has a
real one, others treat it as `Bilinear`) or `Pixelated` (hard-edged when enlarging, filtered when shrinking). Without
per-draw sampling of its own a canvas falls back to switching `Interpolate` for the duration of the draw.

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
- **The pipeline hooks are public, virtual and optional.** `BeginRasterSurface`/`DrawRaster` (a pixel surface,
  handed out as a `RasterRegion`, for effects vector content cannot express), `PrefersRasterGroups`,
  `FlattensTransparency`, `TransformScale`/`CurrentTransform`, `TileCacheOwner`, `InvisibleText` and
  `RenderContext`'s raster settings (`RasterizationDpi`, `TextHinting`, `MaxRasterPixels`, ...) all default to
  "not supported", so a `Canvas` that ignores them still works; overriding them is how a backend takes part in
  the same effects pipeline PeachPDF drives. `RenderContext.CreateSvgGlyphPainter` is the seam a backend with an
  SVG engine uses to draw OpenType `SVG ` glyphs.

## Drawing shaped text, paragraphs and colour glyphs on any `Canvas`

The package also holds the logic every `Canvas` shares for text that `PeachDrawing.Text` has already
shaped, so an implementation gets it by implementing `DrawGlyphs` and the path/clip/brush members:

- `canvas.DrawParagraph(layout, origin, colour)` and `canvas.DrawGlyphRun(run, size, baselineOrigin, colour)`
  (`CanvasParagraphExtensions`) paint a `ParagraphLayout` or a `GlyphRun` at the positions the shaper and the
  layout chose; `ParagraphPaint`/`TextDecorations` give a per-run colour and underline/overline/line-through
  (see [Drawing a layout](peachdrawing-text.md#drawing-a-layout)).
- `PathText.DrawStringAlongPath(text, typeface, size, colour, path, options)` sets text along a path: it shapes the
  text, then places every glyph at its own distance along the path and turns it to follow the curve, so kerning and
  ligatures survive. `PathText.Layout` returns the placed glyphs (`PathGlyph`: glyph index, advance, transform) for a caller
  that draws them itself, and `PathText.GetGlyphFrame` is the one placement step (a distance along the path, the side, a
  perpendicular offset and an extra rotation in, a transform out). `PathTextOptions` has the start offset, anchor
  (start, middle, end), side and letter spacing.
- `InkCrossings.Measure` finds where a run of text puts ink inside a horizontal band, one stretch per glyph: what a
  `text-decoration-skip-ink` underline needs to break around descenders. It is what `Canvas.GetInkCrossings` answers.
- `ColorGlyphs.ColorGlyphPainter` walks one glyph's COLR v0 layers or v1 paint graph - palette and
  `font-palette` overrides, the foreground-colour sentinel, gradient stops and extend modes, transforms,
  clips and blend modes - and describes it to an `IColorGlyphTarget` as clipped fills.
  `CanvasColorGlyphTarget` maps that onto any `Canvas`'s own paths, clips, brushes and blend modes; a backend
  with a different way of drawing implements `IColorGlyphTarget` itself.

## Licences

`PeachDrawing.Core` is BSD 3-Clause. It carries no third-party code of its own; its one dependency,
`PeachDrawing.Text`, carries its own notices in its `THIRD-PARTY-LICENSES.md`. See [License](license.md)
for the whole list.
