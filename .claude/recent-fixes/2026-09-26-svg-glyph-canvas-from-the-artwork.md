# An SVG glyph's canvas grows to hold its artwork

Before, `SvgGlyphDocument` replaced the document's viewBox with a fixed box around the glyph origin (1 em left, 1.5 above, 2 right, 0.5
below), and `SvgGlyphPainter` recomputed the same box from the same constants for the viewport, so artwork beyond it was cut off, although
OpenType SVG says the glyph is not clipped to the viewport at all.

**What was done.** `SvgInkExtent.Of(document)` is a conservative box of what the built tree draws (shape geometry with its stroke's
reach, through groups, `use`, transforms, nested viewports and images), and `SvgGlyphDocument.CanvasFor` grows the *least* canvas (the old
box) to hold it, with a hundredth of an em of margin, and no further than `MaxCanvasReachEms` (8) from the origin in each direction. The
result is written to `SvgDocument.ViewBox`, and the painter now reads the viewport from there instead of from the constants, so the form
the PDF path caches (keyed by drawing identity and size) and the raster path's direct paint both use the grown canvas.

**Two things the review found in the first cut.** `SvgGeometryBounds` counts only an arc's end point, so `M0 0 A5000 5000 0 0 1 100 0` was a
100 x 0 box although it bulges thousands of units: `SvgInkExtent` adds an allowance (every point of an arc is within twice the larger,
chord-scaled radius of its end point). And a `transform` can overflow a finite box to infinity, which made an infinite minus an infinite a NaN
canvas: the transformed box is re-checked and `CanvasFor` ignores a non-finite extent.

**Why "grow the least canvas" and not "the exact extent".** The extent is a box for the shapes, not a measurement of the ink, and it cannot
see everything (a filter's painted region, marker content, text). A tight canvas from an underestimate would clip artwork that the fixed box
used to keep; never going below the old box means nothing that rendered before can render less. The tree is still built against the least canvas
(the root's viewBox attribute) because percentage lengths in the document resolve against the viewport while it is built; only the finished
document's box is replaced.

**The bound that matters** is the cap: a hostile document could ask for a 10^9-unit canvas, which is a huge clip rectangle and a huge form
BBox. Eight ems each way is far past any real glyph and keeps every coordinate small. The existing limits (4 MiB inflate, depth 48, 20,000
expanded elements, DTDs prohibited, `data:` images only) are untouched, and `SvgInkExtent` walks only the tree those limits already bounded.

**Evidence.** `SvgInkExtentTests` (each kind of element), `SvgGlyphDocumentTests` (least canvas kept, grown on each side, stroke and
transform, capped, no known extent), the fixture font's glyph `G` (blocks 1.2 to 1.6 ems left of and 2.2 to 2.8 ems right of the origin) drawn
on the raster surface at the exact expected pixel columns and in the PDF with a form BBox of 176.8 x 80 pt at 40 pt where the least canvas
is 120 x 80, and the showcase `svg_opentype_glyphs` rasterized with PDFium and MuPDF.
