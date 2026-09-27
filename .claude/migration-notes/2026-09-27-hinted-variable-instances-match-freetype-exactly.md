# Hinted variable-font instances are FreeType's exactly

**Before:** a variable font hinted at a location (the raster backend, or a caller asking the engine for a grid-fitted outline) moved its points by the
`gvar` deltas in floating point, added the `cvar` deltas to the control values rounded to 26.6, and normalized the location to 2.14, so a hinted glyph
could differ from what FreeType makes of the same font at the same location by a fraction of a pixel in places; the ranges of the font's `gasp` table
did not move with the location; the advances of a font with no `HVAR` table came from the phantom points in floating point.

**Now:** the arithmetic is FreeType's (16.16 fixed point, its rounding, its handling of the points a tuple leaves out, of the phantom points, of `HVAR`,
`VVAR`, `MVAR` and `avar` version 2), so the hinted points, advances and the sizes at which the font asks for fitting are FreeType 2.14.3's exactly.
The differences are a fraction of a pixel in the position of some points of hinted variable text, an advance that rounds the other way on occasion, and
a size that is fitted, or is not, because `MVAR` moved a `gasp` range; unhinted text (vector PDF text) is unchanged. A variable font with a malformed
`gvar`, `cvar`, `avar`, `HVAR`, `VVAR` or `MVAR` table is answered as FreeType answers it (a location it refuses to set is not hinted, a glyph whose
deltas are unreadable is the unhinted outline), and a table that asks for an unreasonable amount of work (more than 16 million deltas for one glyph) is
refused.
