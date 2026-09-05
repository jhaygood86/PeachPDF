# Bengali, Gujarati, and Tamil USE support, and nested GSUB contextual lookups (issue #533, Phase 5c)

## What landed

Real text shaping for three more Brahmic scripts, extending the Devanagari-only Universal Shaping
Engine (USE) port (see [2026-09-05-devanagari-use-syllable-reordering.md](2026-09-05-devanagari-use-syllable-reordering.md)):
Bengali, Gujarati, and Tamil now get the same syllable classification, conjunct (`cjct`)/reph
(`rphf`) formation, and two-pass glyph reorder (repha repositioning, pre-base matra movement) as
Devanagari. Bengali needed two genuinely new `UseCategory` members - `GB` (Consonant Placeholder,
its own base-consonant-substitute construct, reachable only via U+0980 BENGALI ANJI) and `FMAbv`
(Syllable Modifier resolved to above-base position, reachable only via U+09FE BENGALI SANDHI MARK)
- plus a scanner-grammar extension (`GB` grouped with `B` as an alternate syllable-start token,
matching HarfBuzz's own `complex_syllable_start = (R | CS)? (B | GB)`; a trailing `FMAbv*`
consumption step in `ConsumeTail`, matching HarfBuzz's own `final_modifiers`) and a reorderer
change (`FMAbv` added to the post-base-glyph set, matching HarfBuzz's own `POST_BASE_FLAGS64`, so a
reph's forward move still stops before it). Gujarati and Tamil needed **zero** new classifier/
scanner/reorderer code, confirming the pre-existing research this phase started from - verified by
enumerating every codepoint in all three scripts' own Unicode blocks against the real UCD data
(`assets/unicode/IndicSyllabicCategory.txt`/`IndicPositionalCategory.txt`), not assumed. The
`CssBidiParagraphResolver` gate that used to check `resolvedScripts[c] != "Devanagari"` is now a
`UseShapedScripts` set containing all four script names.

Also landed, discovered by this phase's own real-font testing rather than planned up front: GSUB's
`ApplyMatchedLookups`/`ApplyNestedLookup` (the machinery a contextual/chaining-context lookup's own
`SequenceLookupRecord`s use to apply a *further* lookup at a matched position) now recurses into a
nested lookup that is itself Type 5/6 (contextual/chaining-context), instead of silently skipping
it. `GsubShaper.TryApplySequenceContextAt` gained a `depth` parameter threaded through
`ApplyMatchedLookups`/`ApplyNestedLookup`, reusing the existing `MaxNestedContextDepth` guard
against runaway recursion - no new mechanism, just closing a `case 5/6:` gap the `switch` in
`ApplyNestedLookup` previously fell through with a comment.

New code: `PeachPDF.Text.Shaping.Use.UseCategory.GB`/`FMAbv`; the corresponding
`UseCategoryClassifier` branches (`Consonant_Placeholder` → `GB`, `Syllable_Modifier` → `FMAbv`,
plus `Bindu` added to the existing Lo-gated base clause for Bengali's own U+09FC BENGALI LETTER
VEDIC ANUSVARA - a full letter, unlike every other Bindu codepoint in these four scripts, which are
combining marks); `UseSyllableScanner.ConsumeFinalModifiers`; `UseReorderer.IsPostBaseCategory`'s
`FMAbv` addition; `CssBidiParagraphResolver.UseShapedScripts`; `GsubShaper.ApplyNestedLookup`'s
case 5/6; three new bundled test fonts (`NotoSansBengaliSubset.ttf`/`NotoSansGujaratiSubset.ttf`/
`NotoSansTamilSubset.ttf`) and their generator scripts.

## The load-bearing idea

Real HarfBuzz's own `gen-use-table.py` source (fetched directly, not recalled from memory) is the
ground truth for exactly which UCD predicate maps to which USE category - not the existing
Devanagari-scoped classifier's own comments, which document *why* certain clauses were omitted for
Devanagari specifically but don't claim completeness for a different script. Cross-checking every
Bengali/Gujarati/Tamil codepoint's `Indic_Syllabic_Category`/`Indic_Positional_Category`/
General_Category triple against `gen-use-table.py`'s real `is_BASE`/`is_BASE_OTHER`/
`is_CONS_FINAL_MOD`/etc. predicates (via a small Python script enumerating each script's Unicode
block) is what surfaced Bengali's `GB`/`FMAbv` need and confirmed Gujarati/Tamil needed nothing -
guessing from the existing classifier's shape alone would not have caught either.

The GSUB nested-lookup gap was found the same way: a straightforward `UseCategoryClassifier`/
`UseSyllableScanner`/`UseReorderer` extension plus new fonts should have been sufficient on its
own, matching the "no new code for Gujarati/Tamil" prediction - but the real Noto Sans Gujarati
font's own `abvs` feature resolves a pre-base matra's contextual glyph variant through a **chain of
two independently-classed `ContextSubst` lookups** (an outer class-based rule narrows by which
consonant follows, then invokes a second, differently-classed contextual lookup to narrow further
to the exact glyph variant) - a real, spec-legal OpenType pattern
(`SequenceLookupRecord.lookupListIndex` may name any lookup type, including another contextual
one) that `ApplyNestedLookup`'s pre-existing `switch` didn't handle, silently leaving the matra at
its un-substituted default glyph. This was invisible from reading the classifier/scanner/reorderer
code alone; it only surfaced by shaping real Gujarati text through the real font and finding the
resulting glyph ID didn't match real HarfBuzz's own output for the identical input, then tracing
the font's own GSUB tables (via fontTools) to find the two-level `ContextSubst` chain and matching
it against `ApplyNestedLookup`'s own documented "nested contextual lookup is skipped" comment.

## What was found by running it, not by reading the spec alone

- Enumerating the full Bengali/Gujarati/Tamil Unicode blocks against the real UCD data (not just
  spot-checking a few letters) is what confirmed Gujarati and Tamil are exact zero-new-code cases -
  every one of their codepoints' UISC/UIPC/GC triples already resolves correctly through the
  existing Devanagari-scoped predicate set. A spot-check alone could easily have missed an edge
  case (e.g. Bengali's own U+09FC, a Bindu codepoint that is a full letter rather than a combining
  mark, needing the pre-existing Lo-gated base clause widened by one entry).
- Tamil's own SSA/HA letters (Grantha-origin, borrowed for Sanskrit loanwords) still ligate into a
  real conjunct glyph via the bundled font's `cjct`/`half` features, exactly like a native Bengali/
  Devanagari/Gujarati conjunct - confirmed by real HarfBuzz output for the bundled subset font, not
  assumed from Tamil's own orthographic reputation for not fusing consonant clusters visually (that
  reputation holds for Tamil's own *native* consonants; a Sanskrit-loanword conjunct is a different,
  font-decided case).
- The real Noto Sans Gujarati font's own `abvs` feature fuses a formed reph with the repositioned
  matra into one combined presentation glyph, exactly like the Devanagari bundled font's own
  headline reph+matra fusion case - confirming that mechanism (skip-aware ligature matching across
  an intervening base) generalizes across fonts/scripts with zero additional code, once the nested
  contextual-lookup fix let the matra reach its correct pre-fusion glyph variant in the first place.
- The width-based "did ligation actually reduce glyph count" HTML-layout wiring tests needed a
  self-calibrating relative comparison (against the sum of each codepoint measured individually in
  the same font), not a hardcoded absolute point threshold copied from the Devanagari test - Bengali
  and Tamil's own glyph advances in their bundled fonts are meaningfully wider than Devanagari's,
  so a `< 15pt` threshold tuned for one font's metrics produced false failures against another's.

## What was deliberately not done

- **Only the Bengali/Gujarati/Tamil-reachable subset of HarfBuzz's real USE category set is
  ported**, exactly like the Devanagari-only port before it - a script needing medial consonants,
  a static Sakot/Reordering_Killer, or a statically-assigned repha still falls back to `O`.
- **`FMBlw`/`FMPst` (the other two members of HarfBuzz's own `FM`-family category) are not added** -
  Bengali's own Sandhi Mark is the only codepoint across all four scripts needing any `FM`-family
  category at all, and it resolves to `FMAbv` specifically (Indic_Positional_Category=Top). Adding
  the other two would be speculative for a codepoint no current script reaches.
- **The GSUB nested-lookup fix supports lookup types 1/2/3/4/5/6 as a nested target, not 8** -
  a nested reverse-chaining-context single substitution has not been found in any real font tested
  so far; adding it would mean guessing at a shape no test can currently confirm.
- **SVG text shaping is still entirely out of scope** for both Arabic-family joining and all four
  USE-shaped scripts - unchanged from before this phase.

## Evidence

- `UseCategoryClassifierTests`: 33 new assertions (GB/FMAbv-specific cases, the Bindu/Lo edge case,
  Consonant_Dead/Modifying_Letter catch-all-correctness cases, and per-script base/vowel/modifier
  sanity checks for Bengali/Gujarati/Tamil), alongside the existing 12 Devanagari-only tests.
- `UseSyllableScannerTests`/`UseReordererTests`: 5 new tests each pinning `GB`'s syllable-start
  grouping, `FMAbv`'s tail-consumption/post-base-category treatment, and a broken-cluster case for a
  leading `FMAbv`.
- `GsubMultipleAndContextualSyntheticTests`: 1 new synthetic test (`lookup 17 → lookup 16 → lookup
  15`, two levels of nested `ContextSubst`) proving the recursion fix, alongside the existing 15.
- Per-script real-font characterization: `BengaliUseShapingCharacterizationTests` (10 tests),
  `GujaratiUseShapingCharacterizationTests` (8 tests, including the reph+matra fusion case),
  `TamilUseShapingCharacterizationTests` (7 tests) - every expected glyph ID/order cross-checked
  against real HarfBuzz's own output for that exact bundled font via `uharfbuzz`, the same standard
  the Devanagari work was held to. The Gujarati tests specifically failed against the pre-fix GSUB
  engine (wrong glyph ID for the matra in 3 of 8 tests) and passed once `ApplyNestedLookup`'s case
  5/6 was added - direct evidence the fix was necessary, not speculative.
- `BengaliUseCharacterizationTests`/`GujaratiUseCharacterizationTests`/
  `TamilUseCharacterizationTests`/`MixedUseShapedScriptsCharacterizationTests`: 12 end-to-end
  HTML-layout wiring tests (script tag resolution, measured-width ligation proof via the
  self-calibrating relative comparison, and the generalized `UseShapedScripts` gate correctly
  handling two different USE-shaped scripts - or a USE-shaped script and Latin - sharing one
  paragraph).
- Full showcase (`bengali_gujarati_tamil_use`) rendered to PDF and rasterized with both PDFium and
  MuPDF - both renderers agree, showing correctly-formed conjuncts (including Tamil's
  Grantha-loanword conjunct), correctly-positioned pre-base matras across all three scripts,
  Gujarati's correctly-formed/repositioned/fused reph, and Bengali's own GB/FMAbv categories
  rendering correctly.
- Full suite: 9803 passed, 0 failed, 9 skipped (pre-existing platform-specific skips unrelated to
  this change). Full solution rebuild: 0 warnings, 0 errors.
