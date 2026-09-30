# `context-fill` and `context-stroke` are a real notion in the SVG engine

Before, `SvgGlyphDocument.MakeContextPaintTheTextColour` rewrote both keywords to `currentColor` in the glyph document's text, so they were
one colour (the text's), the SVG engine did not know the keywords, and a standalone SVG could not use them. Now `SvgPaintKind` has
`ContextFill`/`ContextStroke`, `fill`/`stroke` (and the CSS-OM `PaintConverter`, which had been dropping them from stylesheets) accept them,
and they resolve by SVG 2's rule of a context element.

**The load-bearing decisions**

- **Where each context is decided.** `<use>` is built per instance, so its context is known when the tree is built: `InheritedPaint` gained
  `ContextFill`/`ContextStroke`, `BuildUse` sets them to the use's own resolved fill and stroke, and `ApplyCommon` substitutes a keyword the
  moment it resolves an element's paint (an inherited paint was already substituted where it was written, so a child inherits the paint, not
  the keyword). A marker definition is built *once* for every shape it is placed on, so there the keyword stays in the tree
  (`InheritedPaint`'s context is the sentinel kind) and `SvgRenderer.ResolveInMarker` resolves it at paint time from `s_markerContext`, which
  `PaintMarkers` sets to the marked shape's own (already-resolved) paints and restores around the loop, so markers on shapes in markers nest.
  Everything else gets the *seed*: `SvgTreeBuilder.Build(..., contextFill, contextStroke)`, default none (no context element paints nothing).
- **A glyph document is built with the text as its seed**: fill = the text colour, stroke = none. That is the text paint model, not an
  omission: PeachPDF has no text stroke paint (`-webkit-text-stroke` does not exist; faux bold strokes with the fill colour). A glyph that
  asked for `context-stroke` at its top level used to get the text colour and now gets nothing, which is what a text without a stroke gets in
  a browser. The seam takes a stroke paint for the day text can have one. The migration note says so.
- **A gradient through `use`** must be measured against the *use's* box, not the shape that draws it: `SvgPaint.ContextElement` remembers the
  use and `ContextBounds` takes the box of what the use instantiates (not `GetBoundingBox(use)`, which includes the use's x/y offset that the
  content is already drawn under; that offset was the first thing the gradient test showed wrong).

**Found by running it, not reading it:** an `<style>` rule `fill: context-fill` painted black until `PaintConverter` learned the keywords: the CSS
parser dropped the declaration before the SVG layer ever saw it, and the attribute form worked, which hid it. The CSS-OM `Or(keyword)` is one
line.

**Deliberately not done** at the time, recorded in a gap file since closed - see
[the follow-up fix](2026-09-27-svg-context-paint-markers-use-transforms-text.md): gradients/patterns through a
marker, a transform under a `use`, text in a marker.

**Evidence.** `SvgContextPaintTests` paints through the raster graphics and reads pixels (a keyword that painted nothing or the wrong paint
fails; distinct fill and stroke, two uses of one shape, nested uses, a keyword on the use itself, the seed, stylesheet and `style=` forms, markers on
two shapes, a marker with a gradient host, a gradient across a two-shape use), `SvgGlyphDocumentTests`, the fixture font's glyph `F` in the raster
and PDF paths, and the showcase `svg_opentype_glyphs` rasterized with PDFium and MuPDF.
