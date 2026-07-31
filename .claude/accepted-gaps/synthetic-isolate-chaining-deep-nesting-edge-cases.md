# CSS synthetic-isolate BD13 chaining has two narrow, deep-nesting edge cases left unfixed

Tracking issues: [#575](https://github.com/jhaygood86/PeachPDF/issues/575) (override push/pop
ordering for boxes sharing a text index), [#576](https://github.com/jhaygood86/PeachPDF/issues/576)
(eos approximation for isolates 3+ levels deep that all close flush together).

Fixing #554 (the UA stylesheet's `[dir]`/`bdo[dir]` rules using `unicode-bidi: isolate`/
`isolate-override` instead of the legacy `embed`/`bidi-override`) surfaced a real gap in how
`BidiResolver` (`src/PeachPDF/Text/Bidi/BidiResolver.cs`) implements X10/BD13 isolating-run-sequence
chaining for a CSS-driven isolate: a synthetic `BidiIsolateOverride` never occupies a real character
index the way an actual Unicode LRI/RLI/FSI/PDI control character would, so chaining has to be
located by index-adjacency (an override's own `Start`/`End`) instead. Two failure modes from that —
duplicate-processing when nested isolates close at the same index, and dropped positions when
sibling isolates merge into one level run with no boundary between them — were found and fixed as
part of that change (`ComputeIsolatingRunSequences`'s `runEndIndex`-verified chain entries and
`!visited[...]` guard; see `PeachPDF.Tests/Text/Bidi/BidiResolverSyntheticIsolateTests.cs`).

Two narrower issues remain, deliberately out of scope for that fix:

- **#575**: `CssBidiParagraphResolver.Flatten` appends a child box's override before its parent's,
  so two boxes whose overrides share a `Start`/`End` (no character of the outer box precedes/follows
  the inner one) push/pop in child-before-parent order — for opposite-direction nested `dir`
  attributes this can compute a numerically wrong level, not just a chaining order issue.
- **#576**: when isolate scopes nest three or more levels deep and all close at the identical real
  index, the `!visited[...]` guard correctly stops a duplicate add, but the truncated sequence's
  `eos` is then computed against the next real character's level rather than the true immediately-
  enclosing scope's level, which a real, distinctly-indexed PDI character would expose instead.

**Deliberately out of scope for now.** Both require either tracking override nesting depth
explicitly through `Flatten` (#575) or modeling each synthetic isolate close as its own zero-width
slot in the isolating-run-sequence graph (#576) — real architectural work, and narrower/rarer than
the two bugs already fixed alongside #554.
