# Devanagari Universal Shaping Engine (USE) syllable reordering (issue #533, Phase 5b)

## What landed

Real Devanagari text shaping: syllable classification from Unicode `Indic_Syllabic_Category`/
`Indic_Positional_Category`, conjunct formation (`cjct`/`half`/`rkrf`/`abvf`/`blwf`/`pstf`/`vatu`) and
reph formation (`rphf`) via the font's own GSUB features, then a two-pass glyph-array reorder (repha
repositioning, pre-base matra movement) before the font's own presentation features
(`abvs`/`blws`/`haln`/`pres`/`psts`) apply. Ported from HarfBuzz's Universal Shaping Engine
(`hb-ot-shaper-use.cc`/`hb-ot-shaper-use-machine.rl`/`gen-use-table.py`) - see
`.claude/accepted-gaps/no-text-shaping.md` for the exact scope (Devanagari only; other USE-driven
Brahmic scripts need their own follow-on).

New code: `PeachPDF.Text.IndicSyllabicCategory`/`IndicSyllabicCategoryTable`,
`IndicPositionalCategory`/`IndicPositionalCategoryTable` (UCD-derived data tables, same Brotli-compressed
run-length-encoded shape as `ArabicShapingTable`); `PeachPDF.Text.Shaping.Use.UseCategory`/
`UseCategoryClassifier`/`UseSyllableType`/`UseSyllable`/`UseSyllableScanner`/`UseReorderer`; a new
`GsubShaper.ApplyUseShaping`/`TryApplyRphf` stage, gated on a new `TextShapingFeatures.UseCategories`
field; a new `CssBox.UseCategories`/`CssRectWord.EffectiveUseCategories` field pair, populated by
`CssBidiParagraphResolver.ResolveScriptsAndJoining` alongside the existing `CharScripts`/`JoiningForms`
pass.

## The load-bearing idea

Devanagari's own reph is **not** a static Unicode property the way Arabic joining forms are - no
Devanagari codepoint carries `Indic_Syllabic_Category` = `Consonant_Preceding_Repha` at all. Reph forms
**dynamically**, only when the font's own `rphf` GSUB feature actually substitutes a word-initial
RA+VIRAMA sequence - so `GsubShaper.TryApplyRphf` tries applying `rphf` (Type 1/4 only, matching
`ApplyArabicJoiningFeatures`'s own documented scope limit) at exactly each syllable's own start
position, and retags the category to `R` only if a real substitution fired. This mirrors real
HarfBuzz's own `setup_rphf_mask`/`record_rphf_use` split, without needing a general OpenType
feature-masking mechanism this codebase has no other use for - restricting *where* the lookup is tried
achieves the same observable result as restricting a mask would.

The other key piece: syllable boundaries and per-glyph categories are tracked by
**`ShapedGlyph.ClusterStart`**, not raw glyph-list position - identical to how
`ApplyArabicJoiningFeatures` already handles `ccmp`/`locl` changing glyph count mid-pipeline. A
`Dictionary<int, UseCategory>` keyed by ClusterStart is built once (pre-substitution) and re-consulted
after every stage (nukt/ccmp/locl/akhn, rphf, the 7 basic features) to re-derive each *current* glyph's
category before the reorder pass runs - so a font that ligates an entire conjunct into one glyph (KA +
VIRAMA + SSA → one glyph) naturally reports that merged glyph's category as the base's own (`B`), not a
stale pre-ligation value, with zero special-casing needed.

## What was found by running it, not by reading the spec alone

The real bundled test font (a Noto Sans Devanagari subset) fuses a **reph and a following matra into
one combined presentation glyph** via `pres`/`abvs`, even when a base consonant sits physically between
them in the glyph array after this port's own reorder pass produces `[matra, base, reph]`. This only
works because the existing (already-implemented, pre-dating this feature) skip-aware ligature matching
in `GsubShaper.ApplyLigatureAt`/`TryMatchLigature` (driven by `lookupFlag`/GDEF mark filtering) already
handles "match glyphs 1 and 3, skip glyph 2, reinsert the skipped glyph right after the merged output" -
exactly the mechanism needed here, requiring zero new code. This was not obvious from reading the USE
algorithm description alone; it only became clear by cross-checking real HarfBuzz's own output
(`uharfbuzz`) against the bundled subset font and tracing the exact glyph IDs it produced.

## What was deliberately not done

- **Only the Devanagari-reachable subset of HarfBuzz's real ~35-member USE category set and grammar is
  ported.** `UseCategoryClassifier` omits every predicate no Devanagari codepoint can ever satisfy
  (medial consonants, Sakot, Reordering_Killer, hieroglyph categories, etc.) - a genuinely different,
  larger engineering effort per script family, deferred to a future PR per the original plan.
- **`nukt`/`ccmp`/`locl`/`akhn` and the 7 basic features apply globally, not masked to each syllable's
  own span** the way real HarfBuzz masks them - an accepted v1 simplification (see the accepted-gap
  entry) since a font's own coverage/context tables only match sequences they're authored for anyway.
- **CGJ/ZWNJ transparency in the syllable scanner is narrower than HarfBuzz's own** (no
  `Default_Ignorable_Code_Point` fallback check, no "resume the interrupted grammar after a mid-syllable
  CGJ" behavior) - both documented, narrow gaps rather than silent bugs.
- **`pref` and the topographical `isol`/`init`/`medi`/`fina` features are not requested** - Devanagari
  structurally never needs them (see the accepted-gap entry for why).

## Evidence

- `UseCategoryClassifierTests`/`UseSyllableScannerTests`/`UseReordererTests`: 48 unit tests against the
  pure classify/scan/reorder logic in isolation, including a headline case (repha + pre-base vowel)
  independently hand-derived against HarfBuzz's own documented algorithm before writing the test.
- `DevanagariUseShapingCharacterizationTests`: 7 tests asserting exact glyph-ID sequences from
  `OpenTypeDescriptor.Shape` against the real bundled font, each cross-checked glyph-for-glyph against
  real HarfBuzz's own output (`uharfbuzz`) for the identical font file and text - including the
  reph+matra fusion case, which matched on the first attempt once the ClusterStart-based re-derivation
  design was in place.
- `DevanagariUseCharacterizationTests`: 3 end-to-end HTML-layout wiring tests (script tag resolution,
  measured-width proof that ligation actually reduced glyph count, Latin-text no-op regression guard).
- Full showcase HTML rendered to PDF and rasterized with both PDFium and MuPDF - both renderers agree,
  showing correctly-formed conjuncts, correctly-positioned pre-base matras, and a correctly-formed/
  repositioned reph.
- Full suite: 9713 passed, 0 failed. Full solution rebuild: 0 warnings, 0 errors.
