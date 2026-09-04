# GPOS, GDEF mark filtering, GSUB multiple/contextual/chaining substitution, per-element language

Closes the OpenType-shaping remainder of issue #533 (except GSUB Lookup Type 8, GPOS Types 3/5/7/8,
and Arabic/Indic complex-script joining - see `.claude/accepted-gaps/no-text-shaping.md`), plus
issues #551/#553/#555 (bidi bugs from PR #556's own post-change review).

## Load-bearing ideas

- **GSUB's Extension Substitution wrapper is lookup type 7, not 9** - the pre-existing `GsubTable.cs`
  checked for type 9 (GPOS's own Extension Positioning type, in a different table's type space).
  Confirmed against the current spec before touching anything else; every new lookup-type reader
  added here needed the corrected unwrap logic, so this was fixed first. GPOS's own Extension
  Positioning genuinely is type 9 - the two tables are not parallel here, and it's easy to
  transpose them back by habit; both `GsubTable.cs`/`GposTable.cs` carry an explicit comment.
- **`GdefTable`/`ClassDefTable` are built once and shared** by GSUB mark filtering, GSUB contextual
  format 2, and GPOS Pair Adjustment format 2 - one reader (`ClassDefTable.cs`, sibling to
  `CoverageTable.cs`), not three independent ones, since the byte format is genuinely identical
  everywhere it appears.
- **Contextual (type 5) and chaining (type 6) substitution share one matcher** (`GsubSequenceContextSubtable`,
  `GsubShaper.ApplySequenceContextLookup`) - a non-chaining rule is represented as a chaining rule
  with empty backtrack/lookahead, so the same backtrack/input/lookahead-walking code serves both
  lookup types without duplication.
- **A deliberate, narrower simplification**: contextual/chaining backtrack/input/lookahead matching
  does not consult `GDEF`/`lookupFlag` mark filtering (only ligature matching and GPOS mark-to-base/
  mark-to-mark base search do). Getting a skip-aware position-tracking walk right across three
  sequences plus nested-lookup position-shifting was judged not worth the complexity for the
  overwhelmingly common `calt` case (no mark inside the matched window) - recorded as an accepted
  gap, not silently dropped.

## What was found by running it, not by reading it

- **A real timing bug in `DomParser.CorrectTextBoxes`**, found only once an actual test for issue
  #551 was written and run: `CssBidiParagraphResolver.AssignBidiLevels`'s whole-tree walk runs
  *before* `CorrectTextBoxes`, but a `::before`/`::after`/`::marker`/footnote box's own generated
  `Text` isn't set until `CorrectTextBoxes` reaches it (`CssContentEngine.ApplyContent`, called from
  inside that same loop, immediately followed by `ParseToWords`). So the bidi walk always saw these
  boxes empty - fixing `CssBidiParagraphResolver.ResolveParagraph` alone (the fix the issue's own
  text describes) was necessary but not sufficient; a first attempt at a test using
  `display: block` on the `::before` still failed with the *pre-fix* signature. Root cause required
  a scoped, unconditional call to a new `CssBidiParagraphResolver.ResolveOwnTextAsParagraph(box)`
  right after every `ApplyContent` call site (`CorrectTextBoxes`, the footnote-call/marker synthesis
  in `DomParser.ApplyNumber`'s caller, and `HtmlContainerInt.ReapplyPseudoElementContent`/
  `ResolveTargetPageContent`'s re-application passes).
- **That fix's first version was itself a regression**, caught by the pre-existing bdo/bidi test
  suite (`<bdo dir="rtl">hello</bdo>` stopped mirroring): gating `ResolveOwnTextAsParagraph` on
  `box.BidiLevels is null` seemed like a safe "only touch what's unresolved" guard, but an ordinary
  DOM text box (its `Text` set at parse time, long before `AssignBidiLevels` runs) is *also*
  non-null-Text at this point and had already been correctly resolved as part of a real,
  cross-element paragraph (honoring `<bdo>`'s isolate-override push) - re-resolving it here in
  isolation discarded that context. Fixed by gating on `box.IsPseudoElement` instead (the real,
  unambiguous distinction), not on the incidental `BidiLevels` null-ness.
- **The `IsPseudoElement` gate's own second failure mode**, also only found by running the full
  suite: `HtmlContainerInt.ReapplyPseudoElementContent`/`ResolveTargetPageContent` *re-apply*
  content to a pseudo box whose `BidiLevels` was already set by an earlier round - an
  `is null`-only guard would have skipped re-resolving it, leaving a stale-length `BidiLevels` array
  against the new (possibly longer/shorter) `Text` and crashing `CssBox.AppendWordsFromText` with an
  `IndexOutOfRangeException`. `IsPseudoElement` correctly re-resolves on every call, since it's
  scoped by box kind, not by a proxy for "already touched."

## What was deliberately not done, and why

- GPOS Type 3 (Cursive Attachment), Type 5 (MarkToLigature), Types 7/8 (contextual positioning); GSUB
  Type 8 (Reverse Chaining Single Substitution); GDEF/`lookupFlag` mark filtering inside contextual/
  chaining matching - see `.claude/accepted-gaps/no-text-shaping.md` for the per-item reasoning.
- `XGraphicsPdfRenderer`'s new per-glyph-positioned painting path (`DrawPositionedGlyphs`) only
  covers the plain (non-italic-simulation) case - a GPOS-positioned run combined with italic
  simulation falls back to the ordinary whole-run `Tj`, silently dropping the positioning deltas for
  that specific combination. Narrow in practice (italic simulation only fires when no real oblique/
  italic face exists), not covered by an accepted-gap file given how narrow it is, but worth knowing
  before extending either code path.
- Per-language selection uses a curated ~80-language BCP-47→OpenType-tag table
  (`Text/Resources/opentype-language-tags.txt`), not the full ~7000-row registry - a language absent
  from it simply falls back to `DefaultLangSys`, never worse than before.

## Evidence

- Full suite (`dotnet test --framework net8.0`): 9465 passed, 0 failed, 9 skipped (pre-existing
  platform-specific skips) - no regressions.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors, across all target frameworks
  (net8.0/net10.0) and projects (library, CLI, source generator, Blazor demo).
- New synthetic-byte-blob test coverage (`GdefTableSyntheticTests`, `GsubMultipleAndContextualSyntheticTests`,
  `GposTableSyntheticTests`) exercises every new reader's parsing and `GsubShaper`/`GposPositioner`'s
  matching/application logic directly against hand-built table bytes, including the `#543`-style
  concurrent-access stress pattern for the two new cached table types (`GdefTable`, `GposTable`).
- Each of #551/#553/#555 has a dedicated regression test reproducing the issue's own repro HTML/SVG
  and asserting the corrected output (`CssLayoutEngineBidiTests.GeneratedContent_OnBeforeBox_ParticipatesInBidiResolution`,
  `FirstLinePseudoElementIntegrationTests.TextTransform_OnBidiReorderedWord_PaintsMirroredNotLogicalOrder`,
  `SvgTextBidiTests.Text_DirectionRtl_AstralCharacterAmongLatinText_StaysLogicalNoMisindexing`).
