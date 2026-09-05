# PR3 Phase 3 (partial): ported Arabic/Syriac joining state machine + GSUB/GPOS wiring

Continues issue #533's last gap. Adds the actual joining-FORM computation (a faithful C# port of
HarfBuzz's Arabic joining state machine) and wires it through to real glyph substitution/positioning.
Still not reachable from the rendering pipeline - `TextShapingFeatures.ScriptTag`/`JoiningForms` are
real, tested, working parameters, but nothing in `CssBox`/`CssLayoutEngine`/`SvgTreeBuilder` constructs
one yet. That pipeline wiring (Phase 2's own scope - script-run detection into the box tree, and the
"never mutate `word.Text` for joining-bearing words" architectural fix) is the next, larger step.

## Load-bearing ideas

- **`ArabicJoiningShaper`/`ArabicJoiningStateTable` are a line-by-line port, not a reimplementation from
  first principles.** Ported directly from HarfBuzz's `hb-ot-shaper-arabic.cc` (`arabic_state_table`,
  `arabic_joining`), retrieved 2026-09-04, with HarfBuzz's exact "Old MIT" license header (verified
  byte-for-byte against HarfBuzz's own `COPYING` file, not assumed) preserved in both ported files and a
  new `THIRD-PARTY-LICENSES.md` section. HarfBuzz's own buffer pre/post-"context" handling (needed
  because it shapes fixed-size streaming buffer chunks) was deliberately dropped - PeachPDF shapes a
  whole logical-order span in one call, which already gives the state machine full context with nothing
  left outside it to special-case, a real simplification rather than a missing feature.
- **Seven joining-form values, not four.** A pure-Arabic reading of "isolated/initial/medial/final" would
  miss `Fin2`/`Fin3`/`Med2` - alternate final/medial forms only Syriac's `ALAPH`/`DALATH_RISH` joining
  groups ever produce, each mapping to its own distinct OpenType feature tag (`fin2`/`fin3`/`med2`) a
  real Syriac font defines separately from `fina`/`medi`. Collapsing them would silently misfire on any
  Syriac text once Phase 4 gets there. The state table's two "joining group" columns needed only 5 hard-
  coded codepoints (U+0710 ALAPH; U+0715/0716/072A/072F DALATH_RISH - verified directly against
  `ArabicShaping.txt`'s own `Joining_Group` field), not a whole new data table, since nothing else in
  this codebase's v1 scope needs `Joining_Group` beyond this exact 5-codepoint special case.
- **Positional joining-form substitution is a dedicated stage before the general feature pass, not a
  mask threaded through the existing dispatch.** `isol`/`fina`/`fin2`/`fin3`/`medi`/`med2`/`init` are
  always OpenType Lookup Type 1 (a 1:1 glyph swap) by convention - single substitution never changes
  glyph count, so `GsubShaper.ApplyArabicJoiningFeatures` can map `joiningForms[i]` straight onto
  `glyphs[i]` with no index bookkeeping, reusing the existing `ApplySingleSubstitutionAt` helper as-is.
  This runs in `GsubShaper.Shape` *before* the ordered lookup-index loop (which includes `rlig`/`calt`/
  `liga`), matching HarfBuzz's own staged order - confirmed from HarfBuzz's `collect_features_arabic`,
  whose own comment says the pause between the positional features and `rlig` is required (a lam-alef
  ligature rule is keyed on the already-joining-form-selected glyphs, not the nominal isolated ones).
  This also means lam-alef composition needs **no dedicated shaper-side code at all** - it's an ordinary
  GSUB ligature rule the font itself defines under `rlig`, which already works once the positional stage
  runs first, so nothing in this PR builds a lam-alef special case (the plan's own text flagged this as
  a risk; reading HarfBuzz's real `collect_features_arabic` resolved it before writing any code).
- **`TextShapingFeatures.ScriptTag` is a general per-run GSUB/GPOS script-selection improvement, not
  Arabic-specific** - `GsubShaper`/`GposPositioner` both previously hardcoded `["latn", "DFLT"]"`
  regardless of the actual text's script (a pre-existing, explicitly-documented gap). `ResolveScriptPreference`
  prepends the caller's resolved tag ahead of that same fallback chain, so a run that supplies no tag (the
  overwhelming majority of existing call sites, today) is byte-for-byte unaffected - confirmed by the
  full suite passing unchanged. `curs` (GPOS cursive attachment, mechanically implemented since an
  earlier PR but never requested by anything) is now requested exactly when a run carries `JoiningForms`
  - the first real "something asks for it" wiring, closing that specific half of the accepted-gap note.

## What was found by running it, not by reading it

- Every manually-traced test case (three-letter Arabic word Init/Medi/Fina, a Right-joining letter's
  "doesn't extend forward" behavior, a Transparent diacritic not breaking a join, ALAPH's own Fin2-not-
  Fina behavior) matched real Arabic/Syriac typographic behavior on the first run - the traces were done
  by hand against the ported table *before* running the tests, then confirmed, rather than the tests
  being fitted to whatever the port happened to produce.
- A test asserting a "wrong" script tag falls through to no match was itself wrong, not the
  implementation: `GsubTable.GetActiveLookupIndices` already falls back to the font's first script when
  none of the requested preference tags are found (a pre-existing, already-relied-upon behavior) - since
  the synthetic test font here has only one script ("arab"), *any* preference list resolves to it. Caught
  by the test failing with the substitution having applied anyway, not by re-reading the assertion.

## What was deliberately not done, and why

- No pipeline wiring - `CssBox`/`CssLayoutEngine`/`CssBidiParagraphResolver`/`SvgTreeBuilder` never
  construct a `ScriptTag`/`JoiningForms`-carrying `TextShapingFeatures` yet. This is a large, separate
  piece of work (Phase 2's own scope: per-character script-run detection wired into the box tree, plus
  the "never mutate `word.Text` for joining-bearing words - reverse only the final glyph list, not the
  source string" architectural fix `CssLayoutEngine.ApplyBidiReordering`/`CssRectWord` need) - bundling
  it into the same change as the shaper port would make either piece harder to review or bisect on its
  own.
- No real-font validation yet (Phase 4) - everything here is proven against synthetic byte-blob GSUB
  tables and hand-traced expected values, not an actual Arabic font's real `isol`/`init`/`medi`/`fina`/
  `curs` tables. The GPOS Type 3 (cursive attachment) RTL-cascade design flagged as unverified in the
  PR2-era work remains unverified until then.

## Evidence

- New files: `src/PeachPDF/Text/Shaping/Arabic/{ArabicJoiningForm,ArabicJoiningStateTable,ArabicJoiningShaper}.cs`,
  `src/PeachPDF.Tests/Text/Shaping/Arabic/ArabicJoiningShaperTests.cs` (12 tests, values hand-traced
  against the ported table and cross-checked against real Arabic/Syriac typography),
  `src/PeachPDF.Tests/PdfSharpCore/Fonts/GsubArabicJoiningSyntheticTests.cs` (5 tests: per-position
  substitution, `None` no-op, a form the font doesn't define no-ops rather than throwing, empty
  input, and `IsEmpty`'s early-return fix).
- `TextShapingFeatures` gained `ScriptTag`/`JoiningForms` (both default `null`) - every existing call
  site is unaffected; full suite proves it (9636 passed, 0 failed, 9 pre-existing skips, up from 9619 -
  17 new tests, zero regressions).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- `THIRD-PARTY-LICENSES.md` and `docs/license.md` updated with a new HarfBuzz section (verified against
  HarfBuzz's own `COPYING` file directly, not from memory).
