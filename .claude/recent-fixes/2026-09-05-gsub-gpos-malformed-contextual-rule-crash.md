# Fix a crash on a malformed GSUB/GPOS contextual-rule `glyphCount`/`inputGlyphCount` of 0

Found by a code-review pass (line-by-line diff scan) against the recently-merged Arabic/Indic
complex-script joining work, not by a real font reproducing it - a defensive robustness fix, not a
behavior change for any well-formed font.

## The bug

`GsubTable.ReadSequenceRule`/`ReadChainedSequenceRule` (and the byte-for-byte identical `GposTable`
readers for Lookup Types 7/8) computed a rule's `Input` array as `new ushort[glyphCount - 1]` /
`new ushort[inputGlyphCount - 1]`. Per the OpenType spec, a `SequenceRule`/`ChainedSequenceRule`'s own
`glyphCount`/`inputGlyphCount` field always includes the first glyph (already matched via the
subtable's own `Coverage` table), so a spec-conformant font always has this field `>= 1`. Nothing
validated that assumption: a malformed or adversarially-crafted font claiming `glyphCount == 0` computed
`new ushort[-1]`, throwing `OverflowException` and crashing the entire render - not confined to the one
malformed lookup, and not caught anywhere upstream.

## Load-bearing idea

**Clamp, don't validate-and-throw.** The surrounding code already has a house style for malformed input
in this exact family of readers (`IndexOfGlyph` bounds checks, `TryGetAlternate`'s range checks) of
degrading to "this can never match" rather than raising - a `Coverage`-matched glyph should still be
recognized as such, but a rule that can never actually complete a match (an empty `Input` beyond the
already-matched first glyph) is a safe, silent no-op consistent with how the rest of this codebase
already treats out-of-spec table data. `Math.Max(0, glyphCount - 1)` is the minimal fix that preserves
this: a well-formed font's `glyphCount >= 1` computes the exact same array length as before (no
behavior change for any real font), and a malformed `glyphCount == 0` degrades to an empty array instead
of throwing.

## What was deliberately not done

- No attempt to reject the whole subtable/lookup/font on a malformed rule, or to log/report it - this
  codebase's own convention for malformed-but-parseable table data throughout `GsubTable`/`GposTable` is
  silent graceful degradation (see `IndexOfGlyph`, `TryGetAlternate`), not validation errors, and this
  fix follows that precedent rather than introducing a new policy for one corner case.
- The identical `Math.Max` guard was applied to all four call sites (GSUB `ReadSequenceRule`/
  `ReadChainedSequenceRule`, GPOS's own two) in one pass, since they are the exact same duplicated code
  this codebase already runs at parallel, per this repo's own established GSUB/GPOS convention (see each
  file's own header comment) - not a partial fix to only the file the review happened to name.

## Evidence

- New `GsubGposMalformedContextualRuleSyntheticTests.cs`: three synthetic-byte-blob tests (GSUB
  Contextual Format 1, GSUB Chaining Format 1, GPOS Context Positioning Format 1), each building a
  minimal table with a rule whose `glyphCount`/`inputGlyphCount` is 0. Confirmed to reproduce the exact
  `OverflowException` (temporarily reverting the `Math.Max` guard) before restoring the fix, then
  asserting the read succeeds with an empty `Input` array.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9807 passed, 9 pre-existing
  platform-specific skips, 0 failed (full suite).
