# `text-transform: full-width` now transforms text

Previously, `text-transform: full-width` was parsed, cascaded, and reported as supported by
`getPropertyValue`/`@supports`, but `CssBox.cs`'s `ApplyTextTransform` only implemented
`uppercase`/`lowercase`/`capitalize` — `full-width` fell through to a no-op default and rendered
text was unaffected.

`ApplyTextTransform` now converts each character to its Unicode fullwidth compatibility form:
ASCII `!`-`~` to U+FF01-FF5E, space to the ideographic space U+3000, and a handful of Latin-1
currency/symbol characters to their U+FFE0-FFE6 fullwidth forms, matching
[CSS Text Module Level 3 §2.1](https://www.w3.org/TR/css-text-3/#text-transform-property)'s `<wide>`-tagged
half of the Unicode `Decomposition_Mapping`. The `<narrow>`-tagged half (halfwidth katakana,
halfwidth Hangul jamo, halfwidth symbol variants) is not implemented — see
`.claude/accepted-gaps/text-transform-full-width-halfwidth-cjk-forms.md`. Characters with no
fullwidth form of either kind are left unchanged. `full-size-kana` remains unsupported and is
still rejected outright.

`@supports (text-transform: full-width)` now reports `true` (previously `false`). See
`src/PeachPDF/css-properties.json`'s `text-transform` entry.
