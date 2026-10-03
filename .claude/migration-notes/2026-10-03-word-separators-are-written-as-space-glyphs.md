# Word separators are written as space glyphs

**Before:** the space between two words was never written to the PDF. Each word was its own text-showing
operator, and the next word was reached by a position jump only. Viewers and layout-aware extractors rebuilt
the gap from glyph positions, but anything that takes the text in content-stream order got the words run
together: `<p>Premium Widget</p>` extracted as `PremiumWidget`, a justified paragraph as one long string.
Tagged PDF output did not change that.

**Now:** every rendered word separator is shown as a real space glyph in the gap the layout already
reserved, in horizontal, right-to-left and both vertical writing modes (sideways and upright), including in
front of a `text-overflow` truncated word. The glyph is drawn in the font that measured the gap, so it fills
the gap even where the font size changes across it. The same document extracts as `Premium Widget`. Nothing
moves on the page: the glyph has no ink and the gap is unchanged.

Visible in the output in three ways a consumer could notice: the content stream holds one more text-showing
operator per word break; the embedded font subset gains the space glyph wherever one was written; and,
because each relative text position is now split in two and rounded to four decimals at each step, glyph
origins far down a long page can drift by up to about 0.01pt (0.0093pt measured on a 1500-word page), which
can change anti-aliasing of individual pixels but nothing visible. A line's leading space (removed by
white-space processing) and a font without a space glyph get none.
