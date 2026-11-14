# Grid WPT round 3: subgrid gap differences, named lines inside repeat(), `span <name>`

css-grid module (pinned WPT, local fonts): 698 -> 710 passing.

- **Subgrid gaps** (css-grid-2 9.4). A subgrid whose own `gap` differs from its parent's puts half the difference on each side of a line inside it as margin on
  the items there (`CssLayoutEngineGrid._colGapHalf` / `_rowGapHalf`, `SubgridGapHalf`); `gap: normal` is the parent's, so no difference. The parent's gaps travel in
  `GridSubgridContext.ParentColumnGap/ParentRowGap`, set by `BuildChildSubgridContext` (and the lanes' `LaneSubgridContext`). The extra margin feeds the parent's track
  sizing: a subgrid's contribution to a column carries it in `SubgridContribution.Edge`, and a row's size grows by it. `grid-gap-001` still differs in the end padding of the
  last subgrid row (the reference has none; this engine adds the subgrid's bottom padding), and the orthogonal/percentage/border variants are untouched.
- **Named lines inside `repeat()`** (`GridTrackListGrammar`): `repeat(N, [a] 10px [b])` names the lines of every repetition (a line shared by two repetitions carries both
  names); inside `auto-fill`/`auto-fit` the template keeps one repetition's names (`GridTemplate.AutoRepeatLineNames`) and `BuildLineNames(template, resolvedTrackCount)`
  repeats them with the count layout resolved. Lines named *after* an auto-repeat are counted from its end (`LineNamesAfterAutoRepeat`) and shifted by the resolved
  length - before, they were positioned as if the repeat were empty.
- **`span <name>` / `span <n> <name>`** (`GridLine.SpanNamed`, `ResolveAxis`): runs to the Nth line with that name counted from the definite edge on the other side. A positive
  `<name> N` past the last line with the name lands on an implicit line (implicit lines all count as having every name). The lanes test `row-auto-repeat-014` ("only one `u`")
  passed with the old clamp; it may expect the clamp instead - not investigated.
- Tests that asserted the old limits (`repeat(2, [x] 1fr)` invalid, `span foo` invalid) now assert the opposite.

Not done: see the plan's remaining subgrid groups (orthogonal writing modes, standalone-axis size, repeat auto-fill in subgrids, intrinsic size transfer, baseline).

## Subgrid sizing (same round, later)
css-grid 710 -> 718 passing.
- A *row*-only subgrid is an ordinary item for column sizing: `MeasureColumnIntrinsics` skipped every subgrid item, so a row subgrid with `grid-column: 2 / span 2` never made
  its spanned `auto` column wide enough. Only column subgrids are measured through their items now (`auto-track-sizing-001`).
- `MeasureIntrinsicWidth` (shrink-to-fit) no longer returns false for a row subgrid, and a column-flow grid that is a row subgrid has as many rows as the parent rows it spans
  (`standalone-axis-size-002`, `-007`).
- Subgrid edge placeholders: a subgrid with nothing on an edge still contributes its own margin, border and padding there, and a nested subgrid's contributions get this
  level's edges added (`CollectSubgridColumnContributions`). `subgrid-no-items-on-edges-001` still fails: its container is an `inline-grid`, whose shrink-to-fit width
  (`MeasureIntrinsicWidth`) skips subgrid items entirely.

## `body { overflow: hidden }` (found through a css-grid test, fixes seven modules' worth)
The `overflow` of `<body>` goes to the viewport when `<html>`'s is `visible` (css-overflow-3 3.3), so the body is not a scroll container: it does not establish a
formatting context and its margins collapse with its children's. This engine made it a formatting context, so `body { overflow: hidden }` pushed everything down by the
first child's margin (8px in `grid-as-flex-item-should-not-shrink-to-fit-*`, whose reference has no overflow). `DomUtils.OverflowPropagatesToViewport` is asked by
`EstablishesIndependentFormattingContext` and, through `CssBox.OverflowKeepsFlow`, by the five margin-folding checks in `CssBox` that read `Overflow.Value`. Across all 16 WPT
modules: +11 passing (css-grid +8, css-flexbox +2, css-backgrounds +1), none lost. The body is still painted clipped; only the layout predicate changed.
