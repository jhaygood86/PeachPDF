# A pass's outgoing-chain boxes are not geometrically settled

The `paged_media_directional_breaks` showcase lost its whole first chapter (the heading and paragraphs 1-5),
and every later chapter opened on the wrong parity, so the book came out one page short (8 pages, the
colophon on the wrong side). The same defect dropped text on the v0.9.18 release for a minimal
`break-before: recto` document with a chapter of about eight long paragraphs - it was a shipped bug that a
later change merely made easy to hit.

## Root cause

`FragmentEmitter.CommitGeometricallySettledObservations` (the #917 cost fix) records "this box emitted
nothing and never will again" the moment a box's own `ActualBottom` proves it ended above the slot just
emitted. The proof is sound for a box that has finished laying out. It is not for one the pass *stops
inside*:

- A box's height is applied in its epilogue, and a box only reaches its epilogue on the pass that completes
  it. While the pass ends with a `BlockBreakToken` naming `<html>`/`<body>`/the chapter, those boxes carry a
  not-yet-applied `ActualBottom` - about 91pt for a document that really ends near 4200pt.
- `break-before: recto` reserves the slot it steps over as a blank page, so pass 0 sweeps slots 0, 1 and 2 in
  **one** `EmitPass`. Slot 1 is empty, `<html>`/`<body>` are in `_emptySincePass`, their stale bottom sits
  above slot 1's top, and the proof fires.
- `BuildDraft`'s `EmittedNothingAtOrBefore` then prunes the whole document from the recorded slot on, so the
  next pass's content (slot 2 onward) is never emitted.

What exposed it: cd793233 (#1056) shifts words by half-leading, so the last line on a page now correctly
straddles the page bottom and the pass ends with a break token *inside* the chapter. Before that the line
fitted, the pass ended at a clean boundary, and nothing was continuing into a next pass. The straddle is
right and stays - this fix does not touch half-leading or loosen the straddle test.

## The fix

One extra term at the proof's `continue` condition: a box whose `(FragmentKey, slot)` is in
`_continuesInto` is not settled. `EmitPass` already records the pass's outgoing chain there for every slot of
the pass (`RecordChain(outgoing, slot, _continuesInto)`) before it walks them, so membership is exact, and it
is the same record `BuildDraft` uses for `ContinuesIntoTheNext`. A box excluded here is still committed by
`CommitRemainingObservations` at the pass's true end, so nothing is lost by declining to decide early. A
stale mark (a chain that a redo has since replaced) can only make the proof more conservative.

## Found by running it

- The existing directional-break fixtures all use fixed-height blocks that finish on the page they land on,
  so no pass ever ended inside the sweep containing the reserved blank slot. That is why 30+ directional
  tests passed against a defect this large. The new tests need a chapter taller than a page.
- Whether a fixture reproduces is a matter of where the last line of the first page falls. On the default
  595pt-wide harness page the showcase shape passed by luck; at A5's width (419.53pt) with a 300pt page it
  lost words for `line-height` of `20pt`, `1.4` and `1.55`, and only `normal` was unaffected. A scan over
  page height x margin x line-height x cover height failed in most cells, so it is not a knife-edge.
- Before the fix the slot list of the failing fixtures read `[0, 1, 4]` - slots 2 and 3 (the chapter's first
  pages) were never materialized. A test that only checked one heading's text would not have pinned it; the
  slot-contiguity assertion is the cheapest first symptom and the full ordered word list is the proof.

## Not done

- Did not revert #1056's half-leading shift or loosen the straddle test: the shifted words are correct, the
  emitter's inference was the defect.
- Did not narrow the proof to `<html>`/`<body>` or to reserved blank slots. The unsoundness is about any box
  a pass stops inside, and the outgoing chain is the exact statement of that set.

## Evidence

Regression tests in `DirectionalPageBreakIntegrationTests`: `LongChapterAfterARectoBreak_LosesNoWords`
(`line-height:20pt` and `1.4` failed before, `normal` is the control),
`RectoChapters_EachSpanningSeveralPages_StartOnRightPagesAndLoseNothing` and
`LongChapterAfterARectoBreak_RendersItsFirstPage`. All fail on the unmodified emitter and pass with the fix.
The related fragmentation/pagination/page-break/paged suites (748 tests, net8.0) pass. See the invariant
[a box continuing into the next pass has no settled bottom](../invariants/fragmentation-a-box-continuing-into-the-next-pass-has-no-settled-bottom.md).
