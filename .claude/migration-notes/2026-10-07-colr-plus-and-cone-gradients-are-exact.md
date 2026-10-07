# COLR `PLUS` composites and repeating cone gradients are now drawn exactly

Before: a `COLR` v1 `PaintComposite` with the `PLUS` mode painted source-over (overlapping ink did not brighten), and a
`PaintRadialGradient` between circles with different centers padded past its outer circle even when its extend mode was
`repeat` or `reflect`.
Now: a glyph containing either is painted on a pixel canvas and embedded as a picture (shared between occurrences), so the
overlap adds (yellow + blue is white) and the gradient repeats/reflects. Every other glyph is unchanged vector content. The
picture has an alpha channel, so such a glyph in a PDF/A-1 or PDF/X-1a/X-3 document needs transparency flattening, like
other raster-drawn content. Inside an already rasterized element (filter, shadow) the same glyphs are now exact too.
