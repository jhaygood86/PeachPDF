# Grid WPT round 1: abspos static position, percentage gaps, `grid-gap`, alignment keywords

css-grid module (pinned WPT 33926aca, local fonts): 644 -> 691 passing, no test lost.

- **Static position of an abspos grid child** (`CssBox.AlignStaticPositionInGrid`): the rectangle it aligns in is the grid area its lines name *when the grid is its
  containing block* (a missing line is the padding edge), otherwise the area with the content edges standing in for missing lines (`GridStaticArea`, set beside
  `GridContainingArea` in `StateAbsolutelyPositionedAreas`). Using the content box for both broke `descendant-static-position-003` (a positioned grid, whose area
  has padding-edge rows) - test and reference only agreed by accident before. The alignment is now absolute (target = area start + free space x fraction) instead of an
  offset from wherever the earlier pass left the box, `direction: rtl` reflects logical keywords and leaves `left`/`right` physical, and only `auto` defers to the
  container's `*-items`: `normal` and `stretch` are the box's own and behave as `start`.
- **`bottom` on an abspos grid child** resolves against its grid area's bottom edge, not the container's padding box (`PlaceAbsoluteBottomEdge`).
- **Alignment keywords.** `justify-self`/`justify-items` did not accept `flex-start`, `flex-end` or `self-start`, and none of `align-*`/`justify-*` accepted `safe X` /
  `unsafe X` or (align) `first|last baseline` in the stored keyword table - the CSS-OM converter did, the registry's table did not, so the declaration silently fell back
  to `auto`. `Map.*Keywords` has them now. Fixing the parse changes `align-self: safe center` on an abspos grid child from start to centered; the old unit test asserted the
  fallback.
- **`grid-gap`, `grid-row-gap`, `grid-column-gap`** are aliases of `gap`, `row-gap`, `column-gap` (`VendorPropertyAliases`).
- **Percentage row gaps** resolve against the container's height; against an auto height the percentage counts as 0 while the rows are sized (`calc(20px + 5%)` is
  20px) and the gap then takes its final value from the height the rows made, overflowing rather than growing the container (css-align-3 8.1). Same for grid-lanes rows.
  Trap: `LengthOrCalc.ToString()` is the record dump, not CSS text - test `IsCalc`/`Length.Type`, do not string-match `%`.

Not done (still failing): inline-grid with an auto width and a percentage column gap (`grid-gutters-009/010/013-016`), the small-container static-position cases (`*-002`,
float-placed unpositioned grids), last-baseline static positions, vertical-writing-mode and orthogonal abspos (`*-vertWM-*`, `orthogonal-positioned-grid-items-*`), and
everything in the other workstreams of the plan.

## Round 2: intrinsic item contributions
- `CssBox.GetMinMaxWidth` (and so `GetMinContentWidth`/`GetMaxContentWidth`) already holds the box's *own* padding and border; the grid engine's
  `MeasureItemContribution` and `MeasureItemContributionSync` added them again, so every auto/max-content track holding a padded or bordered text item was
  wider than its content, and a shrink-to-fit (floated, inline) grid was wider still. Plain tests hid it because stretch/`1fr` tracks absorb the excess.
  Found by bisecting a copy of `grid-self-baseline-horiz-001` in a scratch WPT directory (`run --wpt <dir> --test ...`; copy `fonts/Ahem.ttf` and
  `css/support/*.css` beside it) - removing padding/border from the item was the change that made the width exact.
- `MeasureIntrinsicWidth` now handles `grid-auto-flow: column` (it returned false, so such grids took the available width).
- Net: css-grid 691 -> 698 passing. The baseline reftests are mostly still failing for baseline-algorithm reasons (shared sets, `align-content: baseline`,
  size contributions of baseline-aligned items, last baseline, vertical writing modes), not width.
