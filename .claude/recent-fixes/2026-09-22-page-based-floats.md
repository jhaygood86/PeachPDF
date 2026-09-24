# CSS Page Floats: `float: top/bottom/top-bottom/snap/inside/outside` (issue #699)

## Load-bearing idea

`float: footnote` already solved the hard part of this problem shape - "a box needs to leave normal
flow, land on whatever page its source position implies, and reserve room there that shrinks the
flow content around it" - via a convergence loop (`HtmlContainerInt.PerformLayout`'s footnote loop):
lay the document out once, discover which page each footnote call landed on, measure the reserved
area's real height, seed it back into the next attempt's `FragmentainerContext.ReserveBandEnd`, and
repeat until it stops changing. Page floats reuse that exact loop rather than inventing a second one -
`HtmlContainerInt.ResolvePageFloatsForThisAttempt` runs alongside `ResolveFootnotesForThisAttempt` in
the same pass, since a page float and a footnote can legitimately compete for the same page's bottom
edge (`TotalBandEndReservationFor` composes both into the one `ReserveBandEnd` call a
`FragmentainerContext` can hold).

Unlike a footnote, a page float is never detached from the tree - it has no numbered call to leave
behind, so `CssLayoutEngine.FloatBox`'s existing per-child dispatch (already called for every
static/relative/sticky block-flow child via `CommitBlockChildOffset`, regardless of `Float` value)
is where discovery and final placement both happen: the float's first, reservation-free layout
attempt leaves it at the position it would have if `float` were `none` - which is exactly the
position `ResolvePageFloatsForThisAttempt` reads to discover its landing page - and once a placement
is decided, `FloatBoxPageArea` moves it there before its own content lays out (`PerformLayoutImp`'s
placement phase runs before its content phase), so no synthetic containing block or detached
re-layout pass is needed the way a footnote body's is.

## What running it turned up, not just reading

Band-**end** reservation (`ReserveBandEnd`/`BandEndInsetOf`, built for the footnote area and repeating
`<tfoot>`) only ever needed to make flow content stop *earlier* - it never had to move where flow
content *starts*. `float: top` needs exactly that: the mirror `ReserveBandStart`/`BandStartInsetOf`
was cheap to add (`FragmentainerContext`), but nothing before this made a fresh page's first content
start anywhere other than its ordinary, continuously-computed block-flow position - because pages tile
with zero gap (`PageBottomOf(k) == PageTopOf(k+1)`), that position already lands in the right page
without any explicit "jump to page top" for the common, unforced case.

The fix ended up being a single, unconditional floor at the one place every static-position `top`
value already funnels through before being committed
(`CssBox.ResolveBlockChildOffset`'s final `return new BlockChildOffset(...)`, after the resumed/
forced-break/margin-truncation branches, not before them): `top = Math.Max(top, PageTopOf(slot) +
TopFloatAreaHeightsBySlot[slot])`. For a box comfortably mid-page this is a no-op (the right-hand side
is always far below `top` already); it only has any effect exactly where `top` would otherwise land
inside a reserved strip at a page's own start. Because it can only push `top` later, never earlier, it
composes safely with every existing branch (resumed pass, forced break, margin truncation) without
needing to special-case any of them - all of them already compute some page's own content-band top as
their target, and the floor just refines that target.

`BlockConstraint.RemainingBlockSize` needed the same floor's mirror, and it is *not* a bare further
subtraction of the full reservation: a box already placed at the head of a reserved slot has the
reservation baked into its own `BlockOffset` by the floor above, so subtracting the whole amount again
would double-count it. `Math.Max(0, BandStartInset - BlockOffset)` - "whatever of the reservation
`BlockOffset` hasn't already spent" - is the amount that's still owed, which is the full amount for a
`BlockOffset` of 0 (an `AtNextSlot()`/`AtSlot()` hypothetical that hasn't been placed yet) and zero for
a box the floor already pushed past it.

## Deliberately not done, and why

- **Inline content continuing across a reserved page boundary is not clamped** - only the block-level
  floor above is. A paragraph that started on an earlier page and is still flowing
  (`CssLayoutEngine.CreateLineBoxes`) when it crosses into a page with a top reservation is not pushed
  below the strip. The common case this feature targets - a float placed at a block-level position
  between paragraphs - already works, since the float and its siblings are ordinary block boxes; the
  narrower gap is filed rather than chased down `CreateLineBoxes`'s own line-breaking state in the same
  change (closed since: `2026-09-24-a-resumed-paragraph-starts-below-a-top-page-float.md`, issue #1273).
- **`float-reference: column` is ignored for page floats** - every page float resolves against the
  page regardless of its own `float-reference`, and a page float placed directly inside a multicol
  container is dropped by `CssLayoutEngineColumns.Layout`'s existing out-of-flow filter (the same
  pre-existing gap `float: left/right` and absolute/fixed children already have, #1203). Column-scoped
  footnote areas were themselves a dedicated follow-up after page-level footnotes shipped; page floats
  take the same path. See `.claude/accepted-gaps/page-floats-are-not-column-scoped.md` (issue #1272).
- **`top-bottom`/`snap`'s disambiguation rule is this implementation's own interpretation.** The exact
  keyword set the issue asks for (matching Prince XML, named in the issue) predates the current
  [CSS Page Floats](https://www.w3.org/TR/css-page-floats-3/) draft, which renamed/restructured the
  vocabulary (`block-start`/`block-end`/`snap-block`, no `top-bottom` at all) and does not define these
  two keywords' resolution rule under this name. `top-bottom` tries top, falls back to bottom on
  insufficient remaining room; `snap` picks whichever edge the float's own natural position is nearer
  to - both documented as this implementation's choice, not a spec citation, in
  `docs/html-css-support.md`.
- **`inside`/`outside` reuse `FloatBoxLeft`/`FloatBoxRight` unchanged** - they resolve to an effective
  left/right via `PageRuleResolver.IsRightPage` (the same page-parity primitive `@page :left`/`:right`
  selection already uses) before dispatching, rather than needing any new positioning code. Sign
  convention worth remembering: *inside* is the edge nearest the spine, so it resolves to **left** on a
  **right**/recto page and to **right** on a **left**/verso page - `effectiveRight = Inside ?
  !onRightPage : onRightPage`. Getting this inverted was the one implementation bug integration testing
  actually caught (`FloatInsideOutside_ResolveByPageParity` failed with the float on the wrong side of
  a right-hand page) - worth a second look if this ever needs touching again.

## The bug the showcase actually caught

Rasterizing the showcase (not just running the unit tests) turned up a real defect the tests above
had not: a page float sandwiched between two in-flow siblings (`<h1>`, `<p>`, `float: top`, `<p>`) left
the *second* paragraph rendering on top of the heading and first paragraph instead of after them.
`DomUtils.GetPreviousSibling(box, includeFloats: false)` - the "previous *in-flow* sibling" walk every
block-level placement calls - excluded a sibling only by checking `sib.IsFloated`, which this feature
deliberately does *not* extend to `Top`/`Bottom`/`TopBottom`/`Snap` (see `IsPageFloated`, a separate
predicate from `IsFloated`, in §1 above). The second paragraph's own placement therefore read the float
itself as its previous in-flow sibling and computed its top from the float's bottom, rather than
skipping the excluded float to reach the first paragraph - simple content ("float, then two ordinary
paragraphs with the float second in DOM order") never exercised this because the float being *first*
in the sibling list means there is no in-flow predecessor for it to be mistaken for.

Fixed by adding `|| sib.IsPageFloated` alongside the existing `(!includeFloats && sib.IsFloated)`
check in both of `GetPreviousSibling`'s two branches - unconditionally, not gated on `includeFloats`,
since a page float is never in-flow the way `left`/`right` sometimes needs to be for other callers.
This is the concrete instance of exactly the trap CLAUDE.md's testing conventions warn about: a
content-stream-substring or single-sibling unit test would never have caught it, and did not - only
rendering the showcase and looking at it did, on a shape (float placed *between* two flow siblings
rather than before both) the original unit tests happened not to cover. `FloatTop_SandwichedBetweenTwoInFlowSiblings_IsSkippedAsPreviousInFlowSibling`
was added to `PageFloatIntegrationTests.cs` to pin this specific shape going forward.

## Evidence

`PageFloatIntegrationTests.cs` (23 tests) covers keyword parsing, `IsPageFloated`/`IsFloated`/
`IsOutOfFlow` classification, top/bottom reservation sizing and placement (including the "following
content lands below the reserved strip" and "forces an earlier page break" no-op-guarding assertions
CLAUDE.md's testing conventions call for), `top-bottom`/`snap`'s edge choice under both branches,
multiple-floats-on-one-edge stacking order, `inside`/`outside` on both an odd (recto) and even (verso)
page, a page float nested inside inline content, and the sandwiched-siblings regression below. Full
`PeachPDF.Tests` suite (net8.0): 13290 passed, 0 failed, 9 skipped (pre-existing platform-specific
skips) - no regressions.
