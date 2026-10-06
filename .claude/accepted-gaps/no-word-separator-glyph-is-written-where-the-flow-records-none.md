# No word separator glyph is written where the flow records none

A space glyph for a word separator is written only in front of a text word whose
`CssRect.PrecededByWordSeparator` the flow set, in the font of the box that measured the gap
(`CssRect.WordSeparatorStyle`). Text extracted from the PDF therefore has fewer spaces than a browser's in three
places:

- **In front of an atomic inline's content when its first word opens the atomic's own line.**
  `before <span style='display:inline-flex'><span>f1</span> <span>f2</span></span> after` extracts as
  `beforef1f2 after` (Chrome: `before f1f2 after`). An `inline-block` carries the flag into its first word and
  gets the glyph; a flex or grid item lays out its own line, whose first word is at a line start.
- **In front of images, inline SVG, form controls and other replaced content.** They are not words drawn
  through `DrawWordGlyphs`, so only the separator after them is written: `aa <img> bb` extracts as `aa bb`
  (Chrome: `aa  bb`).
- **Where the measuring font has no space glyph** (an emoji or symbol font as the primary family). Drawing
  U+0020 there would show `.notdef` on the page and fail PDF/A; a fallback face that has a space would close
  this.

The gaps on the page are all correct; only the extracted text is affected. Not a CSS deviation.
