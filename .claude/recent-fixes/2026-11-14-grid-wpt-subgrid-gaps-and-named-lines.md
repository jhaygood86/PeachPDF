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
