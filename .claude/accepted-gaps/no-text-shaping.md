# No text shaping (remaining OpenType Layout gaps)

PeachPDF now implements most of real OpenType Layout: GSUB Lookup Types 1 (single), 2 (multiple), 3
(alternate), 4 (ligature), and 5/6 (contextual/chaining context substitution, all three formats -
glyph, class, coverage), unwrapping Type 7 (Extension Substitution) transparently (see
`PeachPDF.Text.GsubShaper`, `PeachPDF.Fonts.OpenType.GsubTable`/`CoverageTable`/`ClassDefTable`);
GDEF-based mark filtering of a lookup's `lookupFlag` for ligature component matching (see
`PeachPDF.Text.GlyphSequenceFilter`/`PeachPDF.Fonts.OpenType.GdefTable`); GPOS Lookup Types 1/2
(single/pair adjustment - kerning) and 4/6 (mark-to-base/mark-to-mark attachment), unwrapping Type 9
(Extension Positioning) (see `PeachPDF.Text.GposPositioner`, `PeachPDF.Fonts.OpenType.GposTable`);
per-language GSUB feature selection (a script's language-specific `LangSys`, chosen via the element's
nearest-ancestor `lang`/`xml:lang` - see `CssBox.Language` - resolved to an OpenType language tag via
a curated `PeachPDF.Text.OpenTypeLanguageTags` table); and a real UAX#9 Unicode Bidi Algorithm (see
`PeachPDF.Text.Bidi.BidiResolver`).

## Remaining gaps

- **GSUB Lookup Type 8** (Reverse Chaining Context Single Substitution) is not implemented - it
  processes its input end-to-start and is specifically Arabic/Nastaliq-shaped, so it has no real
  fonts/scripts to exercise correctly without Arabic joining support (below) existing first.
- **GPOS Lookup Type 3** (Cursive Attachment), **Type 5** (MarkToLigature Attachment), and **Types
  7/8** (Context/Chained Context Positioning) are not implemented. Cursive attachment needs complex-
  script joining (below) to matter; mark-to-ligature needs deeper integration with GSUB's ligature-
  merge cluster bookkeeping to identify the right ligature component; contextual positioning mirrors
  GSUB's own contextual-substitution complexity/value tradeoff, for the rarer positioning case.
- **`lookupFlag`/GDEF mark filtering is not consulted while matching GSUB contextual/chaining
  (Lookup Types 5/6) backtrack/input/lookahead sequences** - only ligature (Type 4) component
  matching and GPOS mark-to-base/mark-to-mark base search honor it. A font whose contextual rule
  specifically depends on skipping an intervening mark inside the matched window may under-match;
  the overwhelming common `calt` case (no mark interspersed in the window) is unaffected.
- **Per-language selection is a curated BCP-47 → OpenType-tag subset**, not the full ~7000-row
  OpenType Language System Tags registry (not mechanically derivable from BCP-47). A language absent
  from the table simply falls back to the script's `DefaultLangSys`, same as before this existed.
- **Arabic/Indic complex-script joining** is still absent (a bidi-reordered/mirrored run still uses
  each codepoint's isolated nominal glyph, not a joining form; no Indic reordering).
- **SVG `<text>`/`<tspan>`/`<textPath>`** always shape as if `font-variant-ligatures: normal` were
  set - no SVG presentation attribute/style property to turn ligatures off per-element yet, and SVG
  text does not yet request GPOS kerning/mark positioning at all (HTML text does, via `font-kerning`
  and unconditional mark attachment - see `DerivedStyle.ActualFontKerning`/`ActualTextShapingFeatures`).

Originally filed as [issue #176](https://github.com/jhaygood86/PeachPDF/issues/176); the narrowed
remainder above is still tracked under [issue #533](https://github.com/jhaygood86/PeachPDF/issues/533)
pending a progress-comment update describing exactly this narrower scope (the same pattern the
Lookup Type 3/discretionary-ligatures slice used). GSUB/GDEF/GPOS/per-language behavior is covered by
`GdefTableSyntheticTests`, `GsubMultipleAndContextualSyntheticTests`, `GposTableSyntheticTests`, and
`ShapingCharacterizationTests` (real-font characterization); the remaining gaps above are pinned by
their own doc comments in those files rather than by a dedicated negative test per gap. See
[Text shaping](docs/html-css-support.md#text-shaping).
