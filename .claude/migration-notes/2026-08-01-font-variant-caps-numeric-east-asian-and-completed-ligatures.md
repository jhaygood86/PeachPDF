# `font-variant-caps`/`-numeric`/`-east-asian` are new; caps and ligatures now use real GSUB substitution where a font supports it

**Landed:** 2026-08-01
**Doc section:** docs/html-css-support.md § [Color & Typography](../../docs/html-css-support.md#color--typography) (the `font-variant`/`font-variant-caps`/`font-variant-ligatures`/`font-variant-numeric`/`font-variant-east-asian`/`font-feature-settings` rows) and § [Text shaping](../../docs/html-css-support.md#text-shaping)
**Verified against v0.9.7:** the `v0.9.7` tag's `font-variant` row only recognized `normal`/`small-caps` (always synthesized) and its `font-variant-ligatures` row states `discretionary-ligatures`/`historical-ligatures`/`contextual` "parse and cascade but don't yet change rendering" — `font-variant-caps`/`font-variant-numeric`/`font-variant-east-asian` rows didn't exist at all, and `font-feature-settings` had no row (it was parsed into the CSSOM but never registered as a real property) — confirmed genuine behavior change since 0.9.7, in scope for the next release notes.

- **`font-variant: small-caps`/`all-small-caps`** now renders via a font's real `smcp`/`c2sc` OpenType
  substitution when the resolved font has it, instead of always synthesizing (upper-casing and shrinking
  the affected letters). Visually, real substitution typically looks *better* — genuine small-caps
  glyphs rather than shrunk capitals — but a document that depended on the exact synthesized appearance
  (e.g. measuring against it, or matching it against another renderer that always synthesizes) will see
  a different result wherever the resolved font actually implements the feature.
- **`font-variant-caps`** is a new property recognizing all 7 CSS Fonts Level 3 keywords (`normal`,
  `small-caps`, `all-small-caps`, `petite-caps`, `all-petite-caps`, `unicase`, `titling-caps`) —
  previously only `font-variant: small-caps` (a subset) was recognized anywhere.
- **`font-variant-ligatures: discretionary-ligatures`/`historical-ligatures`** (and their `no-*` forms)
  now actually apply a font's `dlig`/`hlig` features. Previously these values parsed and cascaded but
  never changed rendering at all.
- **`font-variant-numeric`** and **`font-variant-east-asian`** are new properties (8 and 9 keywords
  respectively) — previously unrecognized entirely.
- **`font-feature-settings`** now actually activates the OpenType features it names on a font that has
  them — previously it was parsed into the CSSOM but had no registered property and no effect at all.
- **`font-variant`** is now a real, combinable shorthand over all five of the properties above (e.g.
  `font-variant: small-caps common-ligatures oldstyle-nums;` in one declaration) and, like any shorthand,
  now resets every longhand it doesn't mention back to its initial value — previously it only recognized
  `normal`/`small-caps` and didn't reset anything else.

A document relying on the old always-synthesized small-caps appearance, or on `font-variant`/
`font-variant-ligatures` silently ignoring `discretionary-ligatures`/`historical-ligatures`/the new
keywords, may see different (generally more standards-correct) rendering after this change.
