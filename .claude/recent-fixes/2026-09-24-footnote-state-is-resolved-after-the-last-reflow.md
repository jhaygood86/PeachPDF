# Footnote and page-float state is resolved after the last reflow (#757)

**Symptom, measured rather than reasoned:** a document with a footnote and `target-counter(_, page)` content
(a table-of-contents line whose page number gains a digit and wraps) lost the note area on the page holding the
call. Sweeping the toc measure (40-100pt) and the spacer before the footnote (90-150pt) over 200pt pages, 55 of
77 layouts had a page with a footnote call and no note body. It is not a corner case: any footnote paragraph that
follows a `target-counter` line whose resolved text changes the number of lines is exposed.

**Cause:** `PerformLayout` settles footnotes (`ResolveFootnotesForThisAttempt`, up to six passes) *before* the
`target-counter(_, page)` loop, whose own reflows (up to three more `LayoutDocument` calls) can move a footnote
call across a page break. `FootnoteAreaHeightsBySlot`, the per-slot call grouping and the emitted note areas were
never recomputed after that, so `AttachFootnoteAreas` read state describing a layout that no longer existed.

**Fix:** one resolve merged into the `target-counter` loop body, after each reflow:
`ResolveFootnotesForThisAttempt | ResolvePageFloatsForThisAttempt`, and the loop only exits when the resolved text
is unchanged *and* the resolve did not change anything. The cap is six when footnotes or page floats are in use
(three otherwise), the footnote loop's own. Resolving is the last thing each pass does, so what
`AttachFootnoteAreas` reads describes the layout just produced, including when the cap ends the loop. Page floats
had the same staleness (`TopFloatAreaHeightsBySlot`, `PageFloatPlacements`) and are covered the same way.

**Trap avoided:** the alternative is an outer loop that runs a resolve after the `target-counter` loop and then
re-enters the footnote loop. That does not work: the probe resolve updates the seeds, so the re-entered loop's
first `Resolve` returns "unchanged" and exits without ever relaying out. Merging the resolve into the existing loop
has no such seam.

**Still bounded, not proven:** the footnote loop converges on `maxFootnotePasses`, not by construction, and this
shares that stance (compare the sibling gaps
[container-query-convergence-loop-is-bounded-not-guaranteed](../accepted-gaps/container-query-convergence-loop-is-bounded-not-guaranteed.md)
and [column-scoped-footnote-convergence-is-bounded-not-guaranteed](../accepted-gaps/column-scoped-footnote-convergence-is-bounded-not-guaranteed.md)).
What the fix guarantees is that an area is never attached to a page the call is not on; what the cap can leave is a
reservation off by one oscillation.

**Also removed:** a stale bullet in `footnote-policy-and-footnote-area-scope-narrowing.md` about
`ResolveFootnoteAreaBoxModel` having two call sites that could disagree across the `target-counter` reflow. That
method no longer exists, and the resolve now runs after the last reflow.

Evidence: `FootnoteTargetCounterReflowIntegrationTests.EveryFootnoteCall_IsOnThePageWhoseNoteAreaHoldsItsBody`
sweeps 77 layouts, failed 55 of them before the change and passes all after; full net8.0 suite green.
