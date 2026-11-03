# SVG text: sideways-rl / sideways-lr

`SvgRenderer.IsVerticalWritingMode` now uses `WritingMode.IsVertical()`, and the tree builder forces `text-orientation: sideways` under the two modes. `sideways-lr` runs upward: `LayoutGlyphs(bottomToTop)` mirrors each chunk's glyph positions about the text position (`2*origin - Py - shift`, the anchor shift moving the run the other way) and `PaintRotatedGlyph` turns the glyph -90 degrees about its pen position, so the glyph body extends up from `Py`. The text bounding box uses the same angle.

Not done: bidi reordering assumes positions grow along the run, so `sideways-lr` text keeps logical order. Checked by rasterizing `start`/`middle`/`end` anchors in MuPDF; tests assert the pushed rotation matrices and pen travel.
