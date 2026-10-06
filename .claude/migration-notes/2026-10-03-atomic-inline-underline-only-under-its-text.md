# An underlined `inline-block` is underlined under its text only

**Before:** an element with `display: inline-block` (or `inline-table`/`inline-flex`/`inline-grid`) and a
text decoration — an `<a>` with the UA's default underline, say — had the line drawn across its whole
content width, even when it contained no text (an icon-only link) or a few characters in a wider box.

**Now:** for those four display types the decoration covers only the box's own inline content, one line per text line, as in a browser
(css-text-decor-3 §2.4). An inline-block with no text has no line at all.

Documents that relied on the stray line, or added `text-decoration: none` to hide it, render the same
apart from the line itself.

Unchanged: an `<input>` or `<select>` with a decoration still gets one line across the whole control. At `v0.9.21` `FragmentPainter` had only the block-level (`Line: null`) propagation branch, so the
inline-block behaviour above did differ at the last release.

Also changed since `v0.9.21`:

- **A block with `overflow: hidden` and a decoration** (`<div style="overflow: hidden; width: 50px; text-decoration:
  underline">longword</div>`, or a `display: block` link) now has its underline clipped to the box, like its
  text. It used to run on along the unclipped text, well past the box.
- **A `<textarea>` with a decoration** is underlined under its text rather than across the whole control (it is a
  static `inline-block`, so it follows the rule above). `<input>` and `<select>` keep the control-wide line.
- **A decorated `inline-block` with `overflow: clip` or `overflow-x: hidden`** now underlines its overflowing text
  instead of stopping at the box edge, because those values do not clip the text itself yet.

