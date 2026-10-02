# SVG text: font lists, decoration geometry, skip-ink, shadows, paint-order, whitespace, baselines, textLength

Before: SVG `<text>` used only the first family of `font-family`, drew a character missing from that font as a blank/`.notdef`,
ignored the `font`/`font-variant`/`text-decoration` shorthands outside a stylesheet, always drew a 1-unit underline straight through
descenders, collapsed all whitespace, and ignored `text-shadow`, `paint-order`, `dominant-baseline`, `alignment-baseline`,
`baseline-shift`, `textLength`, `xml:space` and `tab-size`.

Now: all of them apply (see `docs/supported-svg-features.md`). Visible changes for existing documents:

- Underlines and overlines **skip glyph ink by default** (`text-decoration-skip-ink: auto`), as HTML text does; add
  `text-decoration-skip-ink: none` for the old unbroken line.
- A character missing from the first family now falls back to the next listed family or a system font.
- `xml:space="preserve"` text keeps its spaces.
- `text-decoration: <line> <thickness>` is now valid in the `text-decoration` shorthand for HTML as well (it resets
  `text-decoration-thickness` to `auto` when omitted, per CSS Text Decoration 4).
