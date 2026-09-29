# CSS filter chains and outset box-shadows do not use `BeginLayer` effects

`FragmentPainter.PaintRasterized` (CSS `filter`, `backdrop-filter`, `mix-blend-mode` on a raster canvas), the outset
`box-shadow` painter and the 3D/projective painters still call `BeginRasterSurface`/`DrawRaster` directly. Text shadows and
inset box-shadows moved to `BeginLayer` with a `BlurEffect`.

Why: the filter chain interleaves the raster pass with tagged-PDF artifact marking and an invisible selectable-text overlay
(`PaintSelectableText`), and an outset shadow erases the box's own area *after* blurring - neither is expressible as a list of
layer effects. Moving them would mean widening `LayerOptions` (a knock-out mask, a hook around the composite) for one caller
each. `BeginRasterSurface`/`DrawRaster` remain the documented lower-level API for exactly these cases.
