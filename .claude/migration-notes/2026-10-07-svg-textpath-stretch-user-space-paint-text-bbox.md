# SVG textPath: stretch, user-space paint, text bounding box, selectable outlined text

- `<textPath method="stretch">` used to behave as `align`; it now bends each glyph's outline along the path. `spacing` is accepted (`auto` lays out as `exact`).
- A gradient/pattern `fill` or `stroke` on a curved `<textPath>` used to paint each glyph in its own rotated frame, so a `userSpaceOnUse` server turned with the glyph and an `objectBoundingBox` one used the single glyph's box. Glyph outlines are now painted in the text's user space: `userSpaceOnUse` stays fixed and `objectBoundingBox` spans the whole text.
- `clipPath`/`mask` with `objectBoundingBox` units on a `<text>` element used to resolve against no box; they now use the text's bounding box.
- Outlined text (gradient/pattern fill, stroke, stretched glyphs) used to be vector art only; the same text is now also placed as invisible text, so it is selectable and searchable. A PDF text extractor now returns text for such SVG content.
- A bitmap-only font with a gradient/pattern fill or a stroke used to fall back to a plain solid fill (or nothing for a stroke); it is now drawn through the raster backend with the paint cut to the glyph coverage.
