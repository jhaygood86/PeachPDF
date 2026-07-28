# A finished table cell continues as an empty fragment, not as nothing

_Landed 2026-07-28._

[#478](https://github.com/jhaygood86/PeachPDF/issues/478), the second of the three
[#464](https://github.com/jhaygood86/PeachPDF/issues/464) unblocked (#439 was the first, #432 remains).
[css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#fragmentation) continues a row's box into the
next fragmentainer and **every cell's box with it**, so a cell that finished earlier is its borders and
background running that fragment's depth with no content in them. PeachPDF drew nothing at all there.

The half that landed with #488 — a continuation places *nothing* for a finished cell — is untouched and
still correct; it is what stops the earlier pass's content being re-placed. This adds the other half.

## The load-bearing idea: layout states this fragment, it is not discovered

`FragmentEmitter` gains `RecordContinuationShell`/`ClearContinuationShells`, the second place layout
*states* geometry rather than leaving the emitter to read it off the boxes.
`RecordNestedFragmentainer` exists because a box's live geometry describes only its last column; this
exists for the sharper reason that **the box has no geometry here at all**. A continuation deliberately
leaves the finished cell's single `Location` naming the fragmentainer that placed it
([the invariant](../invariants/fragmentation-a-continuation-may-not-move-geometry-an-earlier-fragmentainer-emitted.md)),
and nothing downstream could re-derive the answer anyway: a cell that finished and a cell no pass ever
entered are indistinguishable from geometry, which is the whole reason `TableBreakToken.FinishedCells`
exists.

`CssLayoutEngineTable.LayoutBodyRow` states the rectangle after `cursor.MaxBottom = rowMaxBottom`, since
that is the first moment the row's own depth is settled. `BuildDraft`'s null-return gate then gains one
arm, `ExtentOf` reads `draft.ShellRect ?? BoundsOf(...)`, and the rest — `RectOf`, `LinesOf`, the whole
`UsesOwnBounds` path that already produces one whole-box `LineFragment` with a null `Line` — needed no
change at all.

## Three things the design got wrong on paper and a review caught before any code

**The §6.2 bottom edge cannot be recorded when the draft is built.** The first design set
`ContinuesIntoTheNext` on the draft in slot *s* when a shell existed at a slot after it. It cannot:
`EmitPass` freezes slot *s* at the end of pass *s*, and the shell for *s+1* is stated during pass *s+1*.
The flag would have been `false` every time, and nothing would have failed — the borders would simply
have closed on both sides of every break. It is resolved at materialization instead (`HasShellBeyond`),
which is what
[everything defined over the whole box is resolved at materialization](../invariants/fragmentation-everything-defined-over-the-whole-box-is-resolved-at.md)
already says to do.

**`cursor.SlotIndex` is the wrong thing to ask membership of.** The row loop's band counter is stale by
construction — [and load-bearing for it](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
— so a shell keyed to band *k* can carry a rectangle lying in band *k+1*, and asking the key would have
made the fragment land nowhere in exactly the documents most likely to fragment. The rectangle is stored
in document space and `FragmentRegion.Contains` decides, as it does for every other piece of geometry
here. The slot survives **only** as what `ClearContinuationShells` sweeps by, where staleness errs safe:
a low counter clears more than it had to and the pass doing the clearing re-states.

**A `CssSpacingBox` does reach the skip arm.** `LayoutBodyRow` calls `RecordIfUnfinished` for every cell
it enters, spacers included, and a spacer never sets a `PendingBreakToken` — so it lands in
`FinishedCells` and a later pass skips it. It is excluded explicitly: it is constructed with a bare tag
and never inherits style, so it has no border or background to draw, and the geometry §6.1 wants belongs
to the cell it stands in for, which lives in an earlier row.

## What was found by running it

**The showcase could not show the fix, and that was the fix's own evidence problem.** With the change
in, `paged_media_table_row_continuation` rasterized *identically* to the eye. The fragment tree was
correct — probes showed one decoration-only fragment per continuation with `edges = Left,Right` and both
block edges open — but the fixture hid all three of its consequences: the `note` cell had no background,
its left border fell 1pt outside the page margin and was clipped, and its right border coincided with
the neighbour's under `border-collapse: collapse`. The pixel diff was a 2px-wide strip. **A correct
change that a showcase cannot display is indistinguishable from no change**, so the showcase now gives
that cell a tint, which is the thing §6.1 is actually about; both renderers now show it running the full
depth of pages 1–2 and closing at the row's real bottom on page 3.

**A row can span far more pages than the fixture author expects.** The "a shell must not leak past the
row" test was first written with 244 words and a trailing block, and failed claiming the row reached the
last page — because it *did*: 244 words in a narrow `<td>` genuinely paginate over seven bands, and the
trailing content started on the last of them. The leak the test was written for does not exist; the
fixture is now 60 words plus 40 short paragraphs so there are real pages beyond the row. Worth knowing
before reading a "reaches every page" failure as a leak.

## Deliberately not done

- **Paint is untouched**, and there is no `RGraphics` call-log test. The change is layout and emission;
  what paint consumes is `BoxFragment.Lines` and its `SliceGeometry`, and those are asserted directly.
- **§6.2's block-axis strip for a shell is the shell rectangle**, not the concatenation of the cell's
  fragments — `UnbrokenBlockStripOf` returns early on the page grid. A `border-radius` or a
  `background-position: bottom` on such a cell therefore resolves per fragment. This is the same
  imprecision the cell *beside* it already has, and is not this change's to claim.
- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) untouched.** The row loop's band is still a
  counter.
- **A `rowspan` cell's own continuation.** Excluded above, and covered by the pre-existing `CssSpacingBox`
  duplication case in `TableCellBreakTokenTests.APaginatingTable_DropsNoWord`.

## Evidence

Full net8.0 suite green (**6,878 tests**, up 9), CLI suite green (96), `dotnet build PeachPDF.slnx
-t:Rebuild` with **zero warnings**, `diff-cover` **100% over 74 changed lines** against `origin/main`.
**69 of 70 showcases byte-identical** after normalizing creation date, `/ID`, subset tags and the
annotation `/M`/`/NM` — the one that changes is the one this is about, and that the other 69 do not is
the measured form of the `_frozen`-membership argument below.

That argument, since it is the one
[which drafts exist decides whether a frozen slot is emitted again](../invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md)
warns about: a shell is honoured only for a box `_frozen` already holds, so it can continue a fragment
but never invent one, and `_frozen` is unchanged. `_frozen` is monotone and the fragmentainer a box began
in is always frozen before the one it continues into is emitted, so the guard never rejects a shell that
should stand.

Tests: new `TableFinishedCellContinuationTests` (9). **Six fail on `main`**; the two that pass there do so
by design — `AnUnfragmentedRow_GivesItsCellsOneFragmentEach` is the control that stops "it has two
fragments" passing against a change that gives everything two, and
`AFinishedCellsContinuation_DoesNotMoveTheBoxTheFirstFragmentWasBuiltFrom` is the guard that the tree
gained a fragment without the box gaining a position. Three of the six needed an explicit
"the fixture continues" assertion added before they failed on `main` at all — their loops were over
`.Skip(1)` and passed vacuously.

**What a future change should take from this:** the per-word "claimed exactly once" check is blind here
by construction, and so is a green suite — the subject is a fragment that was never emitted. But the
sharper lesson is the showcase one: rendering the document is only evidence if the document can *show*
the thing. The fixture, not the renderer, was what made a working fix look like a no-op.
