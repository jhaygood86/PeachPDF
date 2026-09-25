# `font-family: math` is recognised, and `<math>` uses it by default

**Before:** `math` was treated as an unknown family name, so `font-family: math` (with nothing after it)
rendered in the platform default font, and a `<math>` element - which had no user-agent font rule - was
typeset in whatever font its surroundings used.

**Now:** `math` is a generic family that resolves to the first *installed* family of a per-platform list
(Windows: Latin Modern Math, Cambria Math; macOS: Latin Modern Math, STIX Two Math; Android: Noto Sans
Math, STIX Two Math; elsewhere: Latin Modern Math, STIX Two Math, DejaVu Math TeX Gyre), falling back to
the platform default font when none is installed. The user-agent stylesheet now has
`math { font-family: math }`, per MathML Core, so a formula with no author `font-family` is typeset in a
math font - which changes the glyphs and, where the font carries a `MATH` table, the metrics of every
`<math>` element that did not already set its own `font-family`. An author `math { font-family: ... }`
rule still wins.

On a host with none of those fonts installed (a minimal Linux container, say), `math` resolves to the platform default
font, so a `<math>` element now renders in that font instead of inheriting the surrounding text's font, as it did
before the UA rule existed. Set `math { font-family: ... }` (or register a math font) to keep a specific font.
