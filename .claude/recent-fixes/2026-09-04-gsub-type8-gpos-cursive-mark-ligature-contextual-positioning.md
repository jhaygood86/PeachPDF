# GSUB Type 8, GPOS Types 3/5/7/8, and skip-aware contextual/chaining matching

Closes the mechanical OpenType-shaping remainder of issue #533 named in
`.claude/recent-fixes/2026-09-04-gpos-gdef-gsub-contextual-per-language-shaping.md`'s own "what was
deliberately not done" list: GSUB Lookup Type 8 (Reverse Chaining Context Single Substitution), GPOS
Lookup Types 3 (Cursive Attachment), 5 (MarkToLigature Attachment), and 7/8 (Context/Chained Context
Positioning), and `lookupFlag`/GDEF mark filtering inside GSUB Types 5/6's own backtrack/input/
lookahead matching. What remains under issue #533 after this is SVG's own kerning/ligature-toggle gap
and Arabic/Indic complex-script joining (both still tracked in `.claude/accepted-gaps/no-text-shaping.md`).

## Load-bearing ideas

- **The skip-aware matcher was built once and shared, not duplicated per lookup type.** `GsubShaper.FindParticipatingIndices`
  (a directional walk collecting the next N participating-per-`GlyphSequenceFilter` real glyph-list
  indices) replaced the previous `pos ± k` literal-adjacency indexing in `TryMatchRule`/
  `TryMatchCoverageSequence`, and is `internal` specifically so `GposPositioner`'s own Type 7/8 matcher
  could call it directly rather than re-implementing the same walk a second time — it's pure
  `ShapedGlyph`/`lookupFlag`/GDEF logic with nothing GSUB-specific about it, unlike the table-reading
  code (which stays deliberately duplicated between `GsubTable.cs`/`GposTable.cs` per this repo's
  existing convention).
- **`ApplyMatchedLookups` had to change its own contract, not just gain a skip-aware caller.** It used
  to take `(matchStart, inputLength)` and reconstruct each input position's real index as
  `matchStart + s` — correct only under contiguous-adjacency matching. Once matching became skip-aware
  (a matched input position can be separated from its neighbor by a skipped mark), the real indices are
  no longer contiguous, so the method now takes the actual `int[]` of real indices found during
  matching, and returns the (possibly further glyph-count-shifted) indices so the caller can resume
  scanning immediately past the last one — this one change correctly handles nested-substitution
  glyph-count deltas *and* matching-time skip gaps uniformly, where the old code only handled the former.
- **GPOS's own mirror of that matcher is genuinely simpler, not just a copy.** A nested GPOS lookup
  (Types 1/2/4/6) only ever adjusts positioning, never glyph count — so `GposPositioner`'s
  `ApplyMatchedLookups` has no delta-tracking block at all, and its outer per-position walk is a plain
  `for` loop (not GSUB's `while` with a computed resume position), since nothing is ever consumed.
- **GPOS Cursive Attachment's advance/offset split**: per spec, connecting an exit anchor to the next
  entry anchor corrects the *exit* glyph's `XAdvanceDelta` (never either glyph's `XOffset`, for X), and
  the cross-stream `YOffset` correction goes on the entry glyph for LTR, the exit glyph for RTL — with
  the RTL walk direction itself flipped (processing pairs from the end of the run backward) so
  corrections cascade through a multi-glyph chain without a separate connected-component pre-pass. This
  is the one piece of this change with no real-Arabic-font test surface yet (nothing requests `curs`
  today) — flagged in both the code doc comment and the synthetic tests as needing real validation once
  Arabic/complex-script joining support (tracked separately) actually exercises it.
- **GPOS Mark-to-Ligature needed a `ShapedGlyph` field, not just a new lookup reader.** Identifying
  which ligature *component* a mark attaches to requires knowing each component's own source-text
  position, which GSUB's ligature merge (`GsubShaper.TryMatchLigature`) previously discarded once it
  computed the merged glyph's span. `ShapedGlyph.LigatureComponentClusterStarts` (`int[]?`, null for
  every non-merged glyph) now carries that forward; `GposPositioner.ResolveLigatureComponent` picks the
  component whose own cluster start is the closest one at-or-before the mark's — a design choice (not
  spec-mandated), falling back to component 0 for a font's own precomposed ligature glyph GSUB never
  merged.

## What was found by running it, not by reading it

- The very first GPOS Type 3 test written (`CursiveAttachment_Ltr_ConnectsExitToEntry_...`) initially
  asserted `glyphs[1].XOffset` nonzero using a hand-picked exit/entry anchor pair that happened to net
  to exactly the bundled font's own glyph-10 advance width — a coincidental false pass caught only by
  also asserting the actual connection-point equation (`glyph0ExitX == glyph1EntryX`, computed
  independently from the formula under test) rather than trusting a single nonzero check.
- `GposPositioner.Apply`'s own lookup-type dispatch `switch` (cases 3/5/7/8) had zero real test
  coverage even after every underlying `ApplyXxx` method was individually tested — every existing
  synthetic test in this project calls `GsubTable`/`GposTable`'s readers and `GsubShaper`/
  `GposPositioner`'s `Apply*` methods directly against appended-past-EOF bytes, bypassing
  `OpenTypeFontface`'s real "GPOS" table-directory resolution entirely. Closing that gap
  (`GposApplyDispatchSyntheticTests`) required actually splicing a real SFNT table-directory entry via
  `SyntheticFontTables.InsertTableDirectoryEntry` — which in turn required first *removing* the bundled
  font's own pre-existing "GPOS" directory entry, since `OpenTypeFontface.Read()` uses
  `Dictionary.Add` (not indexer assignment) while parsing the directory and throws on a duplicate tag
  rather than letting a later entry win, and doing so without correcting every other entry's own
  offset by the resulting ±16-byte directory-size delta corrupted every other table (first observed as
  cmap parsing throwing `IndexOutOfRangeException` two tables away from the one actually being edited).

## What was deliberately not done, and why

- GPOS Type 3's RTL cascade direction (see above) is implemented per a from-first-principles derivation
  of the spec's "advance of the first glyph, corrections cascade backward from the last" language, not
  verified against a real Arabic cursive font — that validation is Arabic/complex-script joining
  support's job (tracked separately), not this change's.
- `ResolveLigatureComponent`'s nearest-preceding-cluster-start heuristic is this codebase's own design
  for adapting real shapers' `lig_id`/`lig_component` glyph properties to PeachPDF's cluster-tracking
  model, not something the spec mandates a specific algorithm for.
- Nothing in `GposPositioner.GetActiveLookupIndices`/`GsubShaper`'s own feature-tag sets requests
  `curs`, `init`, `medi`, `fina`, or `isol` — GSUB Type 8 and GPOS Type 3 are mechanically complete and
  synthetic-tested, but nothing in a real shaping call activates them yet. That wiring belongs to
  Arabic/complex-script joining support, tracked separately in
  `.claude/accepted-gaps/no-text-shaping.md`.

## Evidence

- Full suite (`dotnet test --framework net8.0`): 9507 passed, 0 failed, 9 skipped (pre-existing
  platform-specific skips) — no regressions.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors, across all target frameworks
  (net8.0/net10.0) and projects.
- Diff coverage against `main` (`diff-cover` over the coverlet Cobertura output): 93%, above the 90%
  gate — `GsubTable.cs` 100%, `GposTable.cs` 99.5%, `GsubShaper.cs`/`GposPositioner.cs` in the mid-80s
  (the remaining gaps are scattered small defensive branches, e.g. "anchor not found" fallthroughs,
  not untested feature logic).
- New synthetic-byte-blob test files/additions: `GsubReverseChainSyntheticTests` (GSUB Type 8,
  including an end-to-start processing-order proof — a rule whose lookahead can only be satisfied by a
  *neighboring* position's already-substituted glyph, which a start-to-end walk would get wrong),
  `GposCursiveMarkLigatureSyntheticTests` (GPOS Types 3/5, including an end-to-end GSUB-ligature-merge-
  into-GPOS-component-resolution test), `GposApplyDispatchSyntheticTests` (real feature-tag-driven
  `Apply` dispatch for all four new lookup types together), plus new cases in
  `GdefTableSyntheticTests`/`GposTableSyntheticTests`/`GsubMultipleAndContextualSyntheticTests` for the
  skip-aware matcher itself (an intervening mark correctly skipped inside a contextual rule's input
  window; a nested lookup correctly resolving its real glyph-list index past a skip) and every new
  format combination (GPOS Types 7/8 formats 1/2/3).
