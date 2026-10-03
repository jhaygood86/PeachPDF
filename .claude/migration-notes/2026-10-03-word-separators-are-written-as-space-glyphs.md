# Word separators are written as space glyphs

**Before:** the space between two words was never written to the PDF. Each word was its own text-showing
operator, and the next word was reached by a position jump only. Viewers and layout-aware extractors rebuilt
the gap from glyph positions, but anything that takes the text in content-stream order got the words run
together: `<p>Premium Widget</p>` extracted as `PremiumWidget`, a justified paragraph as one long string.
Tagged PDF output did not change that.

**Now:** every rendered word separator is shown as a real space glyph, in horizontal, right-to-left and
both vertical writing modes (sideways and upright), including in front of a `text-overflow` truncated word.
In horizontal text the glyph starts right where the word before it ends, with any widening from
justification, `word-spacing` or `letter-spacing` after it, as a browser writes it; it is drawn in the font
that measured the gap. The same document extracts as `Premium Widget`. Nothing moves on the page: the glyph
has no ink, the gaps are unchanged, and every word is drawn at the position it was drawn at before.

Visible in the output in four ways a consumer could notice:

- The content stream holds one more text-showing operator per word break, normally a bare `<space> Tj`
  straight after the word before it, which leaves every following text position byte for byte as it was.
  A run of preserved white space (`white-space: pre`) that starts where the word before it ends is now
  written the same way, without the text position operator it had; the words after it are positioned
  relative to an earlier point, which can round differently (see below).
  Uncompressed page content grows by about 9% across the showcases (up to about 40% on a page of nothing but
  prose); compressed files by about 0.3% (up to about 2%). Generation time is unchanged within noise.
- The embedded font subset gains the space glyph wherever one was written.
- Where the space after a word cannot continue from where that word ended - right-to-left text, a word
  raised or lowered by more than a quarter of an em, a background or marked-content boundary in between, a
  hidden word in between - it is positioned with its own text position operator. PDF readers hold text
  positions in single precision, so the words after such a space, or after a preserved run as above, can
  land a few hundred-thousandths of a point off where they landed before; that is enough to flip an
  anti-aliased pixel at a glyph edge, nothing more (1 of 202 showcases changes a pixel in PDFium, 4 or 5 in
  MuPDF).
- Where the font draws a no-break space with its space glyph (as Arial does), a no-break space now extracts
  as an ordinary space once the font has also drawn one, rather than every space of the document extracting
  as a no-break space - or as one or the other depending on which was drawn last.

A line's leading space (removed by white-space processing), a space in front of an image or other replaced
element, and a font without a space glyph get none.
