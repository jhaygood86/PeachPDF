# Atomic inline isolated measurement (#1032) and line-wrap parity (#1105)

Two issues in the same area, landed as sequential commits in one PR because the second's correctness
for `inline-table`/`inline-grid` depends on the first's corrected output.

## #1032 - isolated measurement

`CssBox.GetMinMaxSumWords` (the intrinsic min-content/max-content walk) carries one running line total
across a whole subtree. `IsFlexRow` already special-cases a flex row's own items - each is measured via
its own top-level `GetMinMaxWidth` call and the result is ADDED to the row's total, because one shared
running total cannot express "this participant is measured on its own and then placed on the line."
Every other atomic inline-level box (`inline-block`, `inline-table`, `inline-grid`, a column-direction
`inline-flex`) was NOT given this treatment: the walk descended straight into its children, and a
block-level child inside it reset the shared `maxSum`, restored afterward by `Math.Max` rather than
addition - discarding whatever inline content preceded the atomic box on its line, in the same call
frame `paddingSum` was also being contaminated by (see below).

The fix, `IsAtomicInlineRequiringIsolatedMeasurement`, is a branch sibling to `IsFlexRow` placed in the
PARENT's recursive loop (not inside the atomic box's own call frame) - deliberately mirroring where the
existing float-isolation branch lives, not the flex-row branch: entering a recursive frame for the
atomic child at all would fold its own border/padding into the parent frame's ambient `paddingSum`
(unconditional at the top of every call), and then a second time into the isolated `itemMax` from its
own `GetMinMaxWidth` call - the double-count trap the issue named explicitly. Skipping the recursive
call avoids the first half of that double count, same as the float branch already does.

**What running it turned up, beyond the issue's own repro table:**

- **A flex/grid ITEM must be excluded**, even though its own computed display can itself be one of the
  atomic-inline values (`inline-block` etc., since PeachPDF deliberately leaves an inline-level flex/grid
  item's COMPUTED display alone and blockifies it only at layout time). Isolating it anyway reintroduced
  exactly the bug `IsFlexOrGridItem`'s own doc comment describes: a single-line flex COLUMN of
  `inline-block` items measured as their SUM (72.5742pt) instead of their widest (39.5859pt) - caught by
  the pre-existing `AFlexOrGridItem_StartsItsOwnLine_WhateverItsOwnDisplayIs` test, which the first
  version of this fix broke for the `inline-block`/`inline-flex`/`inline-table`/`inline-grid` cases.
- **A childBox's own explicit `width` has to be re-applied inside the new branch.** `GetMinMaxWidth`
  deliberately excludes a box's own top-level explicit width (a non-replaced box's width has no effect
  on the measurement of its OWN content, CSS 2.1 §10.3.1) - normally the CALLER folds a recursive child's
  width in afterward (the `includeExplicitWidth` fold later in the same method), but that fold never runs
  for a box this new branch isolates, since it never enters a recursive frame. An empty
  `<span style="display:inline-block;width:50pt">` measured as 0 until this was added; fixed by
  replacing (not flooring) `itemMin`/`itemMax` with the declared width + `ActualBoxSizeIncludedWidth`
  when non-auto and non-percentage, the same replace-not-floor treatment the float branch already gives
  its own declared width (CSS 2.1 §10.3.5/§10.3.9: non-auto width fixes the used width regardless of
  content). Caught by two pre-existing tests
  (`AnInlineLevelChildsExplicitWidth_LandsOnTheLine_WithoutNeedingNowrap`,
  `AnExplicitChildWidthLandingOnTheLineAfterASpace_DoesNotSwallowIt`).

## #1105 - line-wrap parity

`CssLayoutEngine.FlowBox`'s per-child dispatch had a width-preflight-and-`OpenNextLine` fit check
(added for `inline-block` by #1103) living ONLY inside that display's own `if` branch, even though
`inline-table`/`inline-grid` reach the identical downstream placement call
(`FlowAtomicBlockContentChild`) through a sibling branch of the SAME `if`/`else if` chain, and
`inline-flex` (via `FlowInlineFlexChild`) had no fit check anywhere. Net effect: three of the four
atomic inline-level displays never wrapped onto a line of their own no matter how far they overflowed
the containing block, even after #1103 fixed the fourth.

Extracted the fit-check-and-wrap sequence into `FitAtomicInlineOnLine` (returning a three-way
`AtomicInlineLineFit` result: `Fits` / `Wrapped` / `ClampedStop`, since line-clamp can still intercept
the wrap the same way it already could for `inline-block`) and called it for all four displays.
`inline-block` passes its already-resolved used width unchanged; `inline-table`/`inline-grid`/
`inline-flex` pass a new `GetAtomicInlineFitCheckWidth` estimate (declared non-percentage width as-is,
else the box's own - now #1032-corrected - max-content width bounded by the containing block, mirroring
`GetFitContentWidth`'s existing shrink-to-fit clamp) that is used for the fit check ONLY and discarded;
each engine still settles its real used width independently once its own layout runs.

## What was deliberately not done

- The approximated `inline-block` one-line path (content that fits on one line, flowed word-by-word
  into the parent's own line boxes) still discovers a declared-width mismatch only after placing some
  content, rather than preflighting as a unit. Out of scope for both issues; narrowed the existing
  `.claude/accepted-gaps/an-atomic-inline-does-not-wrap-onto-a-line-of-its-own.md` to just this
  remaining case rather than deleting it.
- No showcase change: this is a rendering-correctness fix restoring already-expected wrap behavior,
  not a new visible capability - consistent with how other pure layout bug fixes in this series were
  landed.

## Evidence

11 new `IntrinsicWidthWalkTests` cases (the issue's own repro table variants, the padding
double-count trap, and a new inline-flex-column case not in the original issue) plus 6 new
`AtomicInlineLevelBlockContentIntegrationTests` cases (declared-width and auto-width wrap parity across
`inline-table`/`inline-grid`/`inline-flex`). Full net8.0 suite green (12,367 total, 9 platform skips,
zero failures) after both commits, including the full `Flexbox`/`Table`/`Grid`/`InlineBlock` filtered
subset (1,606 tests) run in isolation after the #1032 commit alone. Solution rebuild with 0 warnings.
