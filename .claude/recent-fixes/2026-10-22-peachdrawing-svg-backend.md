# PeachDrawing.Svg: a vector SVG backend for `PeachDrawing.Core`

New package `src/PeachDrawing.Svg` (`SvgCanvas`/`SvgRenderContext`/`SvgComposer`), with `PeachDrawing.Svg.Tests`, the
`PeachDrawing.Svg.Example` sample and a `--svg` mode in the TestHarness that renders every HTML showcase a second time onto an
`SvgCanvas`. User-facing description: [docs/peachdrawing-svg.md](../../docs/peachdrawing-svg.md).

## What carried the design

- **User space = layout units.** The document's `viewBox` is in layout units, so layout-unit calls are written as they arrive and
  the *points* group (`GraphicsPath` coordinates, a pen's width and dash lengths) is multiplied by `PixelsPerPoint`. The first
  version wrote paths unscaled: invisible at `PixelsPerPoint` 1 (every unit test, every example), 0.75 of the right place at the
  96 dpi showcases. See [[svg-canvas-mirrors-pdfcanvas-unit-conventions]]. Only the harness comparison against the PDF found it.
- **One group per push, one definition per thing.** Clips, transforms and layers are nested `<g>`s that must pop in order (a
  mismatched pop throws); gradients, clip paths, patterns, masks, filters, images, tiles and glyph outlines are defined once in
  `<defs>` (`SvgDefs.Define`/`DefineOnce`/`GlyphId`). Glyph outlines as `<use>` instances took the 414 showcase SVGs from 288 MB
  (a `<path>` per run) to 52 MB, which is what made publishing them on the docs site viable.
- **Anything with a transform attribute and a clip/mask/filter reference on the same element is wrong** - the reference's user
  space includes the element's own transform. Images are `<use transform>`, so a crop clip, a mask or a colour-matrix filter goes on
  a wrapping `<g>`.
- **A browser needs more than a PDF font subset.** `TypefaceExporter.ExportSubset` output has no `name`, `OS/2` or `post`, and
  Chromium's sanitiser refuses it (`FontFace.status` = `error`, text silently in the fallback font). `SvgFontPackager` adds minimal
  tables and rewrites the directory and `checkSumAdjustment`; found by loading the file in a browser, not by any assertion.
- **Layer filters need the right region.** `BeginLayer` with no `Bounds` and no blur/shadow writes no filter region at all (the
  default bounding-box region is right for a colour matrix); the first version used the tracked clip, which is in the coordinates
  of wherever it was pushed and cut the `css_filter` showcase's boxes off.

## Clips and blends, pops, and conic strokes

- **A clip group isolates.** A `<g clip-path>` is an isolated group, so a `mix-blend-mode` inside one never reaches the page behind
  it (checked in Chromium: dark green outside the group, plain blue inside). While anything is blended, clips are therefore *flat*:
  `FlattenClips` closes the clip groups above the innermost layer, and every element carries `clip-path` itself, a chain of
  `<clipPath clip-path=...>` intersections each moved into the element's own coordinates (`clip.Ctm * inverse(_ctm)`). An element that
  places itself with a `transform` attribute (images, sheared text) goes in a wrapping `<g>`, because the clip's coordinates would
  be the transformed ones. A layer is isolated by definition, so clips below it are not touched.
- **Pops count pushes, like `PdfCanvas`.** A fixed-position box calls `SuspendClipping`, which pops clips while a layer or transform is
  open; matching pops by kind threw and failed the whole page. Each pop now undoes the innermost push (the same as the PDF canvas's
  saved states), `PopClip` always pops the tracked clip stack (`SuspendClipping` loops on it), and an empty group is taken back out
  instead of written.
- **Colour glyphs at a unit other than 1 to the point were scaled twice - in `RasterCanvas` too.** `CanvasColorGlyphTarget` turned the
  painter's layout-unit coordinates into `GraphicsPath`s, which a canvas reads as points. The target now divides outlines and the
  complement-clip bounds by `Canvas.PixelsPerPoint` (nothing changes at 1, which is what the PDF writer's own use sees);
  `ColorGlyphRasterTests.ColorGlyphOutlinesLand...AtTwoUnitsToThePoint` fails without it.
- A conic **stroke** is the wedges painted through a `<mask>` of the stroke drawn in white; a conic fill stays a clipped fan by default (SVG
  has no conic gradient). `SvgConicGradientMode.Css` opts in to a CSS `conic-gradient()` in a `<foreignObject>` inside a `<switch>` with the
  wedges as the fallback child: Chromium draws the foreign object and nothing else; resvg ignores it and draws the wedges (looked at both).
  Not the default because only browsers render foreign content, and a `<switch>`'s fallback is not honoured the same way by every viewer.

## Not done, and why

- Conic fills are 360 wedges unless the CSS mode is chosen.
- No hinting (vector text), no `<a>`/bookmark/tagging equivalents (PDF concepts, not on `Canvas`).

## Evidence

- `PeachDrawing.Svg.Tests`: 169 tests (net8.0, net10.0 and net11.0), every one parsing the document back as XML and asserting on
  structure; they include bitmap (CBDT/sbix) and OpenType-SVG glyphs, an image storing an EXIF orientation, and real HTML with a
  fixed-position box under a layer, a transform and a blend. The example publishes with Native AOT with no trim or AOT warnings and
  writes byte-identical files to the managed build. Core, raster, PDF, PeachBrackets, engine and PeachPDF suites pass.
- All 414 showcase SVGs (219 showcases) are well-formed XML; rendered by resvg and compared with PDFium at the PDF's own size, the
  median mean-absolute difference is 1.4 levels of 255 (anti-aliasing), worst cases are paged-media text. The gradients, blend
  modes, colour emoji, `background-clip: text` and SVG filter showcases were also looked at in Chromium. PDFium does not apply
  the `/TR` transfer function the PDF writer uses for `filter: brightness()/contrast()/invert()` (its render of `css_filter` shows
  `brightness(1.8)` unchanged; MuPDF's differs from the SVG by about the same amount), so the SVG (a real `feColorMatrix`) shows
  that showcase as intended where the PDFium render does not.
- The one Debug-only crash seen was an existing `Debug.Assert` in the PDF font subsetter when the harness runs in Debug
  (`dictionary_line_breaking`); Release is clean, and CI builds Release.
