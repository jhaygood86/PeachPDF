# An `<img>`/inline `<svg>` no longer adds a space after itself

**Before (v0.9.18 and earlier):** every atomic inline — `<img>`, inline `<svg>`, `<object>`,
`<video>`, `<iframe>`, and a `list-style-image`/`disc`/`circle`/`square` list marker — reserved one
extra word space after itself,
on top of whatever white space the source actually had. `<img>text` rendered with a full space
between the image and the text; `<img> text` rendered with two.

**Now:** the gap after an atomic inline comes only from white space in the source, matching every
browser and [css-text-3 §4.1.1](https://www.w3.org/TR/css-text-3/#white-space-phase-1).
`<img>text` is contiguous, `<img> text` gets exactly one space.

**Why:** the extra term was inherited from the engine's original HtmlRenderer lineage and had no
spec basis (issue #1011).

**Unchanged:** `text-align: justify` still distributes its expansion over every word boundary on a
justified line, so it can open a gap between two adjacent inline boxes that have no white space
between them. That is a separate, pre-existing deviation (#1013), not part of this change.

**What a document author may notice:** content that sits an icon directly against text — an inline
`<img>`/`<svg>` badge, a generated `content: url(...)` image, an `inside` list marker — draws
slightly tighter than before, and a line holding such content measures slightly narrower, so a
shrink-to-fit box (a table column, a float, an inline-block) around it can come out narrower too.
Markup that relied on the phantom space for separation should add the white space, margin or padding
it actually wanted.
