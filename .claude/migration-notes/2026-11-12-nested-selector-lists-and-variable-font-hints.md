# Nested selector lists, variable-font format hints and `min-height` on a column

- A nested rule with a selector list, `.a { & b, i { ... } }`, now applies `i` only inside `.a` (it used to apply to every `i`). A page that depended on the old, wrong reach changes.
- `@font-face` sources written `format("woff2-variations")` (or `truetype-variations`, any case) now load; they were ignored, so text fell back to another font.
- An auto-height column flex container with a taller `min-height` now gives that room to its growing items (`flex-grow`, `flex: 1`), so a footer after a `flex: 1` item sits at the bottom of a `min-height: 100dvh` body.
