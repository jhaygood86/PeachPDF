# `background-clip: text` now supports `vertical-rl`/`vertical-lr`

Previously, `background-clip: text` on a box set to `writing-mode: vertical-rl`/`vertical-lr` always
fell back to a plain `border-box` clip - the same fallback a font with no decodable glyph outlines gets
(a CID-keyed CFF or bitmap font). The background layer (a solid color, gradient, or image) filled the
whole box rather than being cut to the shape of the text.

It now clips to the actual glyph-outline union, matching `background-clip: text` under a horizontal
writing mode. An **upright** run (each character stacked top-to-bottom down the column, its own reading
orientation preserved) builds its outline character-by-character, one `GetTextOutline` call per
character, translated to that character's own cell. A **rotated** (sideways) run - one ordinary
horizontal glyph run reoriented as a whole - builds its outline once, in that natural (pre-rotation)
frame, then carries it into its actual physical footprint with the same rotation matrix paint uses.

`writing-mode: sideways-rl`/`sideways-lr` - a different, unrelated writing-mode value - is unaffected
and still falls back to `border-box`, as does a font whose outlines this engine cannot decode at all.
