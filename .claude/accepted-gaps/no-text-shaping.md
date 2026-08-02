# No text shaping (beyond GSUB ligature, single, and alternate substitution)

PeachPDF applies GSUB ligature substitution (`liga`/`clig`/`rlig`/`dlig`/`hlig`, see
`PeachPDF.Text.GsubShaper` and `PeachPDF.Fonts.OpenType.GsubTable`/`CoverageTable`), GSUB single
(glyph-for-glyph) substitution, and GSUB alternate (glyph-to-one-of-several) substitution (both
driving `font-variant-caps`, `font-variant-numeric`, `font-variant-east-asian`, and arbitrary tags via
`font-feature-settings`), plus a real UAX#9 Unicode Bidi Algorithm (see
`PeachPDF.Text.Bidi.BidiResolver`), but still has no full OpenType Layout / text-shaping engine.
Remaining gaps: no `GPOS` (kerning, mark positioning — no `GPOS` parser exists), no multiple
substitution (GSUB lookup type 2, one glyph expanding to several), no `GDEF`-based mark filtering in
the substitution engine (a lookup's `lookupFlag` requesting mark-skipping mid-sequence isn't honored),
no chaining-context substitution (GSUB lookup types 5-8 are silently skipped rather than mis-applied if
a font routes a feature through one — this is what keeps `font-variant-ligatures:
contextual`/`no-contextual` from driving `calt`), no per-language feature selection (always a script's
`DefaultLangSys`, never a document's declared `lang`), and no Arabic/Indic complex-script joining (a
bidi-reordered/mirrored run still uses each codepoint's isolated nominal glyph, not a joining form). SVG
`<text>`/`<tspan>`/`<textPath>` always shape as if `font-variant-ligatures: normal` were set - there is
no SVG presentation attribute/style property to turn ligatures off per-element yet. Originally filed as
[issue #176](https://github.com/jhaygood86/PeachPDF/issues/176); this narrowed remainder is tracked as
[issue #533](https://github.com/jhaygood86/PeachPDF/issues/533). Ligature/single/alternate-substitution
behavior (and the remaining neighbor-independence gap above) is pinned by `ShapingCharacterizationTests`,
alongside the bidi reordering/mirroring behavior that is now real UAX#9 support rather than a gap. See
[Text shaping](docs/html-css-support.md#text-shaping).
