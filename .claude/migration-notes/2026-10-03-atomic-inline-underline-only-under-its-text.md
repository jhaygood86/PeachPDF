# An underlined `inline-block` is underlined under its text only

**Before:** an element with `display: inline-block` (or `inline-table`/`inline-flex`/`inline-grid`) and a
text decoration — an `<a>` with the UA's default underline, say — had the line drawn across its whole
content width, even when it contained no text (an icon-only link) or a few characters in a wider box.

**Now:** for those four display types the decoration covers only the box's own inline content, one line per text line, as in a browser
(css-text-decor-3 §2.4). An inline-block with no text has no line at all.

Documents that relied on the stray line, or added `text-decoration: none` to hide it, render the same
apart from the line itself.

Unchanged: a form control (`<input>`, `<select>`, `<textarea>`) with a decoration still gets one line across the
whole control. At `v0.9.20` `FragmentPainter` had only the block-level (`Line: null`) propagation branch, so the
inline-block behaviour above did differ at the last release.
