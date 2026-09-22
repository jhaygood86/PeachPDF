# The footnote user-agent stylesheet now matches css-gcpm-3's own

**Before:** PeachPDF's UA stylesheet carried two thin footnote rules -
`*::footnote-call { vertical-align: super; font-size: .7em }` and
`*::footnote-marker { margin-right: 0.3em }` - and everything else about footnote defaults lived in
C#: four hardcoded box-model constants, an implicit per-page counter reset, and the marker's `"N."`
assembled in code.

**Now:** the UA sheet is css-gcpm-3 Appendix B's own, so those defaults are ordinary cascade an
author can override by declaring the same properties.

**What a document author could notice:**

- **The footnote call is drawn as a real superscript glyph.** The spec's sheet includes
  `@supports (font-variant-position: super)`, and PeachPDF now implements that property, so the
  condition is true: the call renders at `font-size: 100%; vertical-align: baseline` with the font's
  own `sups` glyphs (or a synthesized equivalent on a font without them) instead of a 70%-size
  baseline-shifted digit. On a font with real superscript figures this is a visible improvement; on
  one without, the synthesized result is close to the old appearance.
- **The marker's separator changed.** It was the number plus a hardcoded `"."` and a `0.3em` right
  margin; it is now the spec's `counters(footnote, ".") ". "` - so the text is `"1. "` with a real
  trailing space, and there is no margin. Anything asserting the marker's exact text sees `"1. "`.
- **`@page { counter-reset: footnote }` and `@footnote { counter-increment: footnote }` are now
  really declared**, which is what makes an author's own `counter-reset` on `@page` replace them -
  see the footnote-counter migration note for the one regression risk that carries.

**Two deliberate deviations from the spec's text**, both of which render identically:

- The spec writes the marker's suffix as a separate `::footnote-marker::after { content: '. ' }`
  rule. PeachPDF cannot match a pseudo-element on a pseudo-element, so the suffix is folded into the
  marker's own `content`.
- `float: bottom` is declared (to match the spec verbatim) but is a css-page-floats page-float value
  PeachPDF does not implement, so it is dropped as an invalid value rather than being added to the
  `float` keyword set - adding it would make `@supports (float: bottom)` claim support that does not
  exist. `column-span: all` parses and is simply never read. Neither has any effect: the note area is
  always at the bottom of the page and always spans the full content width.
