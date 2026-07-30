# No text shaping (beyond GSUB ligatures)

PeachPDF applies GSUB ligature substitution (`liga`/`clig`/`rlig`, see `PeachPDF.Text.GsubShaper` and
`PeachPDF.Fonts.OpenType.GsubTable`/`CoverageTable`), driven by `font-variant-ligatures`, and a real UAX#9
Unicode Bidi Algorithm (see `PeachPDF.Text.Bidi.BidiResolver`), but still has no full OpenType Layout /
text-shaping engine. Remaining gaps: no `GPOS` (kerning, mark positioning — no `GPOS` parser exists), no
`GDEF`-based mark filtering in the ligature engine (a lookup's `lookupFlag` requesting mark-skipping
mid-sequence isn't honored), no chaining-context substitution (GSUB lookup types 5-8 are silently skipped
rather than mis-applied if a font routes ligatures through one), no per-language feature selection (always
a script's `DefaultLangSys`, never a document's declared `lang`), and no Arabic/Indic complex-script
joining (a bidi-reordered/mirrored run still uses each codepoint's isolated nominal glyph, not a joining
form). `font-variant-ligatures`'s `discretionary-ligatures`/
`historical-ligatures`/`contextual` values (and their `no-*` forms) parse and cascade correctly but don't
yet drive `dlig`/`hlig`/`calt` lookup application. SVG `<text>`/`<tspan>`/`<textPath>` always shape as if
`font-variant-ligatures: normal` were set - there is no SVG presentation attribute/style property to turn
ligatures off per-element yet. Originally filed as [issue #176](https://github.com/jhaygood86/PeachPDF/issues/176);
this narrowed remainder is tracked as [issue #533](https://github.com/jhaygood86/PeachPDF/issues/533).
Ligature behavior (and the remaining neighbor-independence gap above) is pinned by
`ShapingCharacterizationTests`, alongside the bidi reordering/mirroring behavior that is now real UAX#9
support rather than a gap. See [Text shaping](docs/html-css-support.md#text-shaping).
