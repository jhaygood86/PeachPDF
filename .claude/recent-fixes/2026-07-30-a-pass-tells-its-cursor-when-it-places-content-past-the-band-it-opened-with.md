# A pass tells its cursor when it places content past the band it opened with

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320). Closes [#453](https://github.com/jhaygood86/PeachPDF/issues/453).
Stage 1 of [#435](https://github.com/jhaygood86/PeachPDF/issues/435).

## The load-bearing idea

`CssRect.WouldStraddleFragmentainer`'s page arm deliberately asks the page grid
(`container.BandStartingAt(Top)`) rather than `container.CurrentFragmentainer.Band` — the band the pass
itself is filling, tracked by `FragmentainerContext.SlotIndex`. A prior attempt to convert it directly is
recorded in the issue's own history as changing 63 of 69 showcases, with visibly overlapping content, because
several mechanisms can place flow past the band a pass opened with **without ever recording that as a break**,
leaving the cursor stale. Converting the predicate before those mechanisms are closed makes every subsequent
question in the pass answer about a band the pass has actually left.

This change closes four of them, each by calling the existing `FragmentainerContext.StepOverTo` — built for a
forced break's own step-over — at the point each mechanism actually crosses a boundary:

1. **`CssBox.PlaceBlockChild`'s unforced §5.2 truncation**, only in the non-fragmenting fallback (a
   measurement pass, or the driver's suppressed last-resort relayout) — the fragmenting branch already returns
   before reaching a child's placement, so its own cursor advance happens naturally on the resumed pass.
2. **`CssLayoutEngine.FlowBox`'s unbreakable-line overflow** (`MonolithicContent.FitsNoFragmentainer`) — a new
   `CssRect.OverflowsEveryFragmentainer()` names the same question `WouldStraddleFragmentainer` already asks,
   factored through a shared `ClonedInsets` helper so the two can't drift onto different reservations.
3. **`LineRelocation.DeltaFor`'s flex/grid line relocation** — steps the cursor whenever a non-zero delta is
   returned. This is exactly the mechanism issue #453's accepted gap described as "unreachable rather than
   merely untested"; it is neither, once the straddle predicate's conversion (stage 2) makes the staleness
   observable.
4. **`TableRowCursor`'s row loop** — `BandReached`'s silent floor-raise and `MoveToSlot`'s explicit break both
   now step the container-level cursor, always **between rows, never mid-row** (the §6.2 repeated-header/footer
   reservations are keyed to the band `BandReached` has just settled, and a mid-row step would strip a sibling
   cell of its inset/claim).

A fifth, optional site (`CssBox.LayoutBlockChildren`'s block-level unbreakable overflow) was also closed,
gated on `MonolithicContent.IsMonolithic` (css-break-3 §2's own set: replaced elements, scroll containers).

## What was found by running it, not by reading it

**Two regressions, same shape, found by the full test suite rather than the corpus diff.** The first attempt
at both the `PlaceBlockChild` site and the `LayoutBlockChildren` site fired the new `StepOverTo` call
unconditionally — after *every* block child's placement, or for *every* `PlacesItselfAsBlockBox` child that
overflowed — rather than only where a genuine crossing question had actually been asked and decided. Both
regressed the same fixture:
`FragmentEmitterTests.MaterializedPages_MatchThePrePagedFragmentTreeBehaviour`'s
`<div style='margin-top:900pt'>far below</div>` case, which the test's own comment names as "a margin large
enough to cross a page is truncated, so it never paginates as blank space." The document root's own margin
absorbs the div's 900pt margin by collapsing through html/body/div (all auto-height, no padding/border), and
§5.2's boundary test is *deliberately* never asked of the root's own placement — it has nothing before it to
cross against. The broad `StepOverTo` calls advanced the cursor for the root's placement anyway, which the
`FragmentEmitter` then read as "this pass is filling slot 4/5", turning one fragmentainer into five or six.

Diagnosed by adding temporary print statements at each branch of `PlaceBlockChild`'s §5.2 test and walking the
box tree after layout (not shown in the final diff) — the root, html, body and div were all placed at
`Y=920`, and *none* of them ever entered the boundary-test branch that should have decided a crossing, because
the root's own placement is exempt by design and its descendants inherit its position without a crossing
question of their own. The fix in both cases was to scope the `StepOverTo` call to the specific branch that
actually decided a crossing (the `!IsFragmenting` fallback for site 1) or to the specific predicate that
identifies genuinely monolithic content (`MonolithicContent.IsMonolithic` for site 5) — not "any block child
that happens to overflow."

**Measured, not assumed, per site.** A throwaway diagnostic counter
(`HtmlContainerInt.CursorSpills`/`BandBeingFilled`, retained as permanent instrumentation) was wired into the
straddle predicate's page arm and run against the full showcase corpus after each site landed:

| after site | showcases with `CursorSpills > 0` | total |
|---|---|---|
| (baseline, no sites) | 16 of 76 | 13,190 |
| table row loop (site 4) | 11 of 76 | 7,917 |
| block-level monolithic overflow (site 5, correctly scoped) | 10 of 76 | 7,564 |

The residual is not a defect: it is almost entirely the ordinary inline tolerance case (a line within
`PageBoundaryEpsilon` of a band bottom is kept rather than moved, and the *next* line starts in the following
band) — which stage 2 (converting the straddle predicate itself) is what turns into a real break, not this
stage — plus the already-tracked [#521](https://github.com/jhaygood86/PeachPDF/issues/521) residual on
`paged_media_table_rowspan_break` (a rowspan cell's own content overflowing its band, needing the flow-level
continuation `#390` describes).

## What was deliberately not done

- **The straddle predicate itself is not converted.** `BandBeingFilled` still returns the grid band
  unconditionally. Flipping it is stage 2, gated on this stage's sites actually closing the mechanisms that
  made the direct conversion unsafe.
- **`CssRect.BreakPage`/`OffsetFlowedWords` were left asking the grid.** They are a *relocation* (into the band
  *after* the one a word is in), not the break decision `WouldStraddleFragmentainer` makes — converting them
  too would relocate a word two bands instead of one.
- **The `#521` residual on `paged_media_table_rowspan_break` was not chased.** It is `#390`'s own tracked
  work (flow-level continuation for a rowspan cell whose content, not just its box, overflows), not a cursor
  staleness this stage's mechanism covers.

## Evidence

Full `net8.0` suite green (7,025 passing, 9 skipped, 0 failed) after every site, including 376 table-focused
and 97 monolithic/scroll-container/replaced-element-focused tests run explicitly. Byte-identical across the
full 76-showcase corpus (normalized per
[`testing-a-pdf-carries-two-timestamps-not-one-when-showcases-are-compared`](../invariants/testing-a-pdf-carries-two-timestamps-not-one-when-showcases-are-compared.md))
after every site — every site in this stage is behaviour-neutral by construction. 100% diff coverage against
`origin/main`. Zero-warning solution rebuild.
