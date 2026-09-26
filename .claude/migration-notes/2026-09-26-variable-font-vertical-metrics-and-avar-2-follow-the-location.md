# Variable-font instances: vertical advances, the font box and `avar` 2 follow the location

**Before:** text in a variable font at a location other than the default used the default design's vertical advances (vertical writing
stacked the glyphs of every weight at the heavy and light designs' one spacing), described the default design's bounding box in the PDF font
descriptor, and a font with a version 2 `avar` table (an axis whose value depends on the others) was read as if the cross-axis mapping were
absent, so its glyphs were drawn at a wrong location whenever the mapping was not zero there.

**Now:** the vertical advance of each glyph, the vertical origin of a font that has a `VORG` table, and the font bounding box in the embedded
font's descriptor follow the location, and the `avar` 2 mapping is applied before any variation table is read. A document that uses vertical
text in a variable font, or a font with an `avar` 2 table, can therefore lay out and draw differently; a horizontal document in a font
without `avar` 2 differs only in the `/FontBBox` of the embedded font.
