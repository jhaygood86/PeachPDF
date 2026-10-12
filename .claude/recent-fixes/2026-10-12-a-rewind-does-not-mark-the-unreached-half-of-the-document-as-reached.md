# A rewind no longer marks the not-yet-reached half of the document as reached

## The load-bearing idea

`CssBox.NeverTouchedThisLayout` is how the emitter tells content layout has not reached yet ("the whole second half of a long
document") from content that is finished: the first write that positions a box clears its "emitted nothing" record and stamps
it touched (`DiscardEmittedNothing`). An unreached box is given an "emitted nothing" observation, so each page's walk returns
at the mark instead of recursing into the paragraph.

`PassRewind.RollBackTo` - run on every widows and keep-with-next rewind - resets **every** child the re-run pass was going to
lay out, which includes every paragraph layout has not reached. `ResetForRefill` reported each one's geometry as changed,
and that notification is the "touched" stamp. So the first rewind, a few pages in, marked the whole rest of the document as
reached; none of it was ever marked empty again, and every later page's emission walked into all of it.

`ResetForRefill` now skips the notification for a box that is `NeverTouchedThisLayout`. There is nothing to retire on a box
layout has not reached (no fragments, no observation of this generation), so skipping loses nothing, and the box stays
unreached until layout really does reach it.

## What was found by running it

- A forward-walk classification of the body's 13,500 children (1,315-page prose): at page 0 `never=13489`; by page 5 `never=0`
  and `behindUnmarked=13448` - children that had not been laid out, reporting `ActualBottom` 0. The cursor advanced ~10 per
  page; the rest were walked again every page (9.2M forward visits, ~7,000 per page).
- First-touch stack of the top-level children: 1,319 of 1,350 via `TryRewindForWidows -> PassRewind.RollBackTo ->
  ResetForRefill -> NotifyGeometryChanged`; the other 31 via `ResolveOwnInlineSize`. One cause.
- The comment on `ResetForRefill` already named the symptom ("re-asks this for children the page in hand will never
  reach, over and over, across every later page") and added an `_awaitingRefill` guard for the repeated *work*; the *touch* it
  caused was the part that did the damage.
- Box visits do not change (122k at 131 pages either way): the unreached children are still visited, but now return at the
  mark instead of recursing. The saving is allocation and the time that goes with it.
- Allocation by phase (EventPipe allocation sampling, `tools/` analyzer not committed): at 1,315 pages `BuildDraft` was 65% of
  all allocation (15.9GB of 24.5GB) against 22% at 131 pages, which is why per-page allocation more than doubled with size.

## What was not done

`ChildrenOf`'s iterator state machine (~16% of allocation at medium sizes) and the visit itself are untouched: a marked
child still goes through `ComputeInlineExtentDelta`/`DisplacementIn` before `BuildDraft` reaches its mark check, and the
ahead-of-slot skip still needs `_currentPassIsFinal`. Moving the mark check earlier, or advancing the live-child cursor past
marked children that follow an unmarked one, are the next steps on this path.

## Evidence

- Output identical to the build without it: same words and drawing operations on every page for 131-page prose, 80-page table,
  100-page invoices, 127-page two-column, math, images and two small documents, and the 33/66/131-page prose sweeps with default
  and `widows/orphans: 1` (PyMuPDF `get_text("words")` + `get_drawings()`).
- In-process, warm: 1,315-page prose 21.4s -> 16.3s and 24.5GB -> 10.0GB allocated per render (7.6MB/page, matching the
  131-page document's 7.3MB/page - linear again); 131-page prose 1.14s -> 1.10s, 1,092MB -> 952MB. Tables, invoices and
  multi-column are unchanged: they do not rewind the way prose does.
- `RefillOfUnreachedBoxesTests`: an unreached box stays unreached through `ResetForRefill` (fails without the change); a reached
  box still retires its observation.
- Full suite green on net8.0 in Release. With `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` the failing set is the 9 tests that already fail
  on `main`, plus two box-visit-count tests from the earlier emitter PRs (`MulticolUnheldChildrenTests.TheBoxVisitsAPageMakes_*`,
  `StaleSlotReopeningCostTests.EmissionWork_*`), which fail identically without this change - the oracle's unpruned reference
  walk inflates the visit counts they assert on.
