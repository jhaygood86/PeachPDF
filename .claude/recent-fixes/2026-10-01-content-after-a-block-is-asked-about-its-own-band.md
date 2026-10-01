# Content after a block whose child ran past its end is asked about its own band (#1480)

When a block's last child reaches past the block's own end (a fixed height the content overflows, a negative
`margin-bottom`, a large `position: relative` offset), the siblings after the block are placed at the block's end
(CSS 2.1 §10.5, §9.4.3; a relative offset does not move the flow). The words that were laid out past the end had
already stepped the pass cursor (`FragmentainerContext.SlotIndex`) on to later slots through `LineRelocation`, so those
siblings, whose own top is in an earlier slot, were judged against the **cursor's** band
(`HtmlContainerInt.BandBeingFilled`). A line crossing the foot of its own page therefore never counted as
straddling, was not broken, and was clipped away: `w4_4` was painted at y=178 of a page that ends at 180.

**Fix.** `BandBeingFilled` answers the grid band of the content's own top when that top is in a slot **below** the
cursor. The direction matters: the case `CursorSpills` guards (a top that drifted into a *later* band while nothing
advanced the cursor) still asks the cursor's band, so the #435 conversion's behaviour for it is unchanged.

**What was tried and rejected.** Stepping the cursor back after a finished child (`StepBackTo`, with a separate
high-water slot so the emitter's `throughSlot` kept covering the overflow). It made the fixed-height case lose three
words that `main` kept (`w4_1`..`w4_3`, which layout placed correctly at doc Y 130-162 but which no emitted slot
held), so it is worse; the emitter's per-pass freeze/prune bookkeeping is the unknown. The band answer needs none of it.

**Measured** with the visible-band check (a word counts only if inside its page band), not the paint log, which counts
a clipped word as drawn: on `main` the negative-margin case lost `w4_1` and the fixed-height case lost `w4_4`; the
relative-offset case, which the issue lists as losing six words, drew everything on `main` at this commit. The
issue's measurement for all three was on an older `main`.

Evidence: `ChildReachingPastItsParentTests` (the three triggers, plus the same with an `overflow: hidden` wrapper);
full net8.0 suite (14844 tests) passes.
