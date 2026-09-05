# PR3 Phase 1: Unicode Script/Joining_Type data tables + script-run detection

First landing toward closing issue #533's last gap (Arabic/Indic complex-script joining). Pure
infrastructure - no pipeline wiring, no behavior change yet. Adds the data tables and the
script-run detection algorithm Phase 2 (wiring into `CssBidiParagraphResolver`/`CssBox`/`CssRectWord`)
will consume; nothing in this change is called from the existing rendering pipeline.

## Load-bearing ideas

- **`DerivedJoiningType.txt`, not `ArabicShaping.txt` directly, is the joining-type source of truth.**
  `ArabicShaping.txt` only lists ~830 codepoints explicitly; every codepoint it omits gets its
  `Joining_Type` from a derivation rule (a combining mark - General_Category Mn/Me/Cf - defaults to
  Transparent rather than Non_Joining, so an Arabic diacritic between two joined letters doesn't break
  the join). Re-deriving that rule from General_Category data would be a second, independently-fragile
  implementation of a computation the UCD already ships pre-computed, in the same
  `extracted/DerivedJoiningType.txt` file its own `DerivedBidiClass.txt` precedent already established
  this repo trusts. Confirmed by direct inspection: `ArabicShaping.txt` has no entry at all for U+064B
  (ARABIC FATHATAN), while `DerivedJoiningType.txt` correctly lists the whole `064B..065F` diacritic
  block as `T`. `ArabicShaping.txt` itself is kept in `assets/unicode/` unused by any generator, purely
  for THIRD-PARTY-LICENSES.md provenance (it's the file `DerivedJoiningType.txt` is itself derived from
  upstream) - Phase 3 may still need its `Joining_Group` field (lam-alef composition) once the actual
  ported HarfBuzz shaping code's real requirements are known.
- **`ScriptTable`/`ArabicShapingTable` are a straight structural copy of `VerticalOrientationTable`'s
  own shape** (Brotli-compressed run-length-encoded `(start, end, value)` text, binary-searched, safe
  zero-runs fallback for a host with no Brotli decoder) - the third and fourth data properties to reuse
  this exact reader pattern, not a new one invented for this PR. `ScriptTable` returns the raw Unicode
  script name as a `string` rather than a fixed enum (~170 possible values makes an enum pure
  boilerplate with no type-safety benefit a dictionary key doesn't already provide), while
  `ArabicShapingTable` returns a proper 6-value `ArabicJoiningType` enum (small, fixed vocabulary, worth
  the type safety).
- **`OpenTypeScriptTags` is a small hand-authored `Dictionary` literal, not an embedded text resource**
  like its `OpenTypeLanguageTags` sibling - the OpenType old-style script-tag registry has no
  regenerate-from-upstream story the way UCD data or the BCP-47 language-tag registry do (it's a fixed,
  rarely-changing ~170-entry table with a handful of real exceptions - `Lao`→`"lao "`, `Nko`→`"nko "`,
  `Vai`→`"vai "`, `Yi`→`"yi  "`, `Hiragana`/`Katakana`→the single combined `"kana"` - verified against
  HarfBuzz's own `hb_ot_old_tag_from_script` exception list, not guessed from memory), so a plain
  dictionary literal is simpler than resource-file machinery for something this small and static.
  Curated, not exhaustive (~35 entries: the 13 scripts `ArabicShaping.txt`/`DerivedJoiningType.txt`
  themselves cover, plus the common non-joining scripts ordinary text needs a sensible tag for) -
  mirrors `OpenTypeLanguageTags`'s own "curated subset, absent-from-table falls back to existing
  behavior, never worse than before" convention.
- **`ScriptRunResolver` is the UAX #24 §5.1 "resolve Common/Inherited against surrounding text"
  algorithm**, kept as its own standalone, pipeline-independent unit (per the plan's own Phase 1 scope:
  "`GetScript(codepoint)`, Common/Inherited merged into the nearest preceding script run") rather than
  folded directly into whatever Phase 2 pipeline code eventually calls it - two passes: forward-fill
  every `Common`/`Inherited` codepoint from the nearest preceding real script, then backward-fill any
  *leading* run (text that opens with punctuation before any real script appears) from the first real
  script found. A sequence with no real script anywhere (all punctuation, or all combining marks with no
  base) resolves `Inherited` down to `Common` rather than leaving it claiming an inheritance that was
  never there, and leaves `Common`/`Unknown` untouched - there's nothing to resolve against.

## What was found by running it, not by reading it

- U+064B (ARABIC FATHATAN)'s `Script` value is `Inherited`, not `Arabic`, despite living inside the
  Arabic Unicode block - confirmed directly against the downloaded `Scripts.txt` before writing test
  fixtures, rather than assumed. This is exactly the case `ScriptRunResolver` exists to handle: a raw
  per-codepoint `ScriptTable.Of` lookup alone would classify this diacritic as script-less, when what a
  GSUB script-tag decision actually needs is "the same script as its base letter."

## What was deliberately not done, and why

- No pipeline wiring at all - `CssBidiParagraphResolver`/`CssBox`/`CssRectWord` are untouched; `GsubShaper.ScriptPreference`
  still hardcodes `["latn", "DFLT"]`. This is Phase 2's job, and doing it here would mean landing
  Arabic-shaping-relevant pipeline changes with no actual Arabic shaping behind them yet.
- No OpenType *new-style* script tags (`dev2`/`bng2`/etc., the 2016-era second `Script` table some
  fonts define for a handful of Indic scripts with an incompatible newer shaping model) - flagged in
  `OpenTypeScriptTags`'s own doc comment as a Phase 4/5b, real-font-testing-driven follow-up, not a
  mechanical table extension.
- No `Joining_Group` data table (needed for Arabic's lam-alef special-casing per the plan) - deferred
  until Phase 3 pins down exactly what the ported HarfBuzz joining state machine actually reads, rather
  than guessing the shape now and re-deriving it later if wrong.

## Evidence

- New files: `assets/unicode/Scripts.txt`/`DerivedJoiningType.txt`/`ArabicShaping.txt` (downloaded
  directly from unicode.org, Unicode 17.0.0 - the same version already pinned for the bidi/vertical-
  orientation tables), `generate_script_table.py`/`generate_arabic_joining_table.py` (mirror
  `generate_vertical_orientation_table.py` exactly), `ScriptTable.cs`/`ArabicShapingTable.cs`/
  `ArabicJoiningType.cs`/`OpenTypeScriptTags.cs`/`ScriptRunResolver.cs`.
- New tests: `ScriptTableTests`/`ArabicShapingTableTests` (values independently verified against the
  downloaded UCD files via a throwaway script, not from memory), `OpenTypeScriptTagsTests` (including
  every legacy-exception tag), `ScriptRunResolverTests` (9 cases: pass-through, mid-run Common/Inherited
  resolution, leading-run backward-fill, Common-between-different-scripts, all-Common, all-Inherited,
  empty).
- Full suite (`dotnet test --framework net8.0`): 9619 passed, 0 failed, 9 pre-existing skips - no
  regressions (58 new tests total across the four new test files).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- `THIRD-PARTY-LICENSES.md`'s bidi-only UCD section generalized to cover all four UCD-derived data-table
  groups (bidi, vertical-orientation, script, joining-type) - also fixing a pre-existing gap where
  `VerticalOrientation.txt` (landed in an earlier PR) had no license-doc entry of its own at all.
