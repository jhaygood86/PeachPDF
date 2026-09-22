# `font-variant-position` is now a supported property

**Before:** `font-variant-position` was not registered at all. A declaration was dropped during
parsing, so `sub`/`super` had no effect, `@supports (font-variant-position: super)` evaluated false,
and the `font`/`font-variant` shorthands did not reset it (there was nothing to reset).

**Now:** `normal | sub | super` parse and cascade. `sub`/`super` activate the OpenType `subs`/`sups`
GSUB features on a font that has them; on a font that doesn't, the run is synthesized - drawn with a
reduced-size face and a shifted baseline, using the scale and offset that font's own `OS/2` table
recommends (`ySuperscriptYSize`/`ySuperscriptYOffset` and the subscript pair), falling back to
representative ratios only when those fields are zero. Synthesized sub/superscripts deliberately do
not change line height or any other box dimension, matching the spec's statement that these glyphs
"have no effect on line-height and other box characteristics".

**What a document author could notice, beyond the new property working:**

- `font: <...>` now resets `font-variant-position` to `normal` along with the other
  `font-variant-*` longhands, per CSS Fonts 4 §7.7 "Reset Implicitly". A document that set
  `font-variant-position` on an ancestor and then used the `font` shorthand on a descendant will see
  the descendant fall back to `normal`, where previously the property did nothing anywhere.
- `font-variant: sub`/`super` is now accepted as part of the shorthand.
- `@supports (font-variant-position: super)` now evaluates **true**, so a stylesheet that used it as
  a feature test will start applying that block.
- `font-feature-settings: "sups"`/`"subs"` no longer acts independently: those tags joined the
  reserved set the `font-variant-*` longhands own, so `font-variant-position` governs them, per CSS
  Fonts precedence.

SVG text requests the real feature but does not synthesize; see the accepted-gap note for that and
for how this interacts with small-caps synthesis on the same element.
