# Stencil masks painted with a pattern

`bug_40721524` (a Type 3 glyph made of a stencil image, shown with a shading pattern as the fill color) drew black: `DrawImage` painted every
stencil in `Fill`, which for the Pattern space is black. `TryPaintStencilWithPattern` now turns the mask's painted pixels into a clip (one rectangle
per run, rows overlapping by 0.02 pixel so the anti-aliased clip shows no seams) and fills the unit square with the pattern through it. Masks over a
million pixels keep the old behavior (a path that size is not worth it). Corpus: 164 -> 165 of 207.

**Second half of the same bug:** `Run` reset the pattern space to the glyph procedure's own at its start, so the shading was placed relative to the
glyph. A pattern fill belongs to the stream that showed the text (PDF 8.7.3.1), so `ShowType3` now runs the procedure with `keepPatternSpace` when a
pattern is the fill. Only the pattern fill case is changed; a glyph that sets its own colors still gets its own space.

Left: `radial_shading_point_at_border` paints only the circle region where PDFium also paints the extended cone (a focal point on the circle's edge).
That is the radial brush of the raster canvas; see [the accepted gap](../accepted-gaps/radial-gradient-cone-beyond-a-focal-point-on-the-circle.md).
