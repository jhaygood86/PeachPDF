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

## Item sizing (replaced items, declared widths)
css-grid 726 -> 737 passing.
- **A declared width is kept when an item's height is measured** (`MeasureItemHeightHorizontal`): it pinned every item's width to its track, so an item narrower than its
  track was measured at the track's width - a 100px-wide ratio-only svg in an 800px row measured 800px tall. Only an `auto` width takes the track's now.
  This also fixed the `grid-item-block-axis-content-contribution-*` tests.
- **A ratio-only replaced item with a declared height** (an `<svg viewBox>` with `height: 100px`) gets its width through the ratio instead of filling the area
  (`transfersHeight` in `PlaceItemInCellHorizontal`), unless `justify-self: stretch` is explicit. The doc line saying a ratio-only svg "fills its area's width under any
  alignment" holds only while its height is also auto.
- **An image or svg with a declared height contributes its transferred width** to an `auto` track (`MeasureItemContribution`): its measured contribution is 0 until the
  image loads, which measuring the word does; that transferred width is also its automatic minimum (`grid-minimum-size-grid-items-009`). Not done for an image with no declared size: making
  its natural width a contribution broke `grid-item-minmax-img-001`, where `minmax(auto, 0)` must not size the track.
- Machine load matters for this suite: with leftover `dotnet` processes running, a full engine run failed 6-9 different timing-sensitive tests (page-break, paint statistics,
  transforms); after `dotnet build-server shutdown` only the known intermittent `PaintStatisticsTests` remained.

## Item sizing: ratio transfer from fixed rows, min-height on auto rows

- An item with a percentage height over fixed-size rows and an `aspect-ratio` (or a replaced item's natural ratio) contributes the transferred width to
  intrinsic column sizing (`TransferredFromFixedRows`), as css-sizing-4 transfers a definite block size through the ratio.
- An auto-height grid whose `min-height` exceeds its rows stretches the auto rows (`StretchAutoRows`) before the `align-content: stretch` pass, so
  `min-height` behaves like a definite height for stretching (css-grid 11.1).
- Remaining sizing failures need logical size properties (`inline-size`, `block-size`), which are not implemented.

## Logical size properties

`inline-size`, `block-size` and their `min-`/`max-` forms are scratch longhands like the logical margins (`css-properties.json` category `logical`,
`BoxRareState`, `CssBox.ResolveLogicalProperties`/`ApplySize`): they resolve to width/height by the box's own writing mode, ordered against the physical
declaration with `LogicalDeclarationOrder`. The old `SvgOnlyProperty` registration of `inline-size` (SVG text wrapping, never read) was removed since the
generated entry replaces it. Gained: `grid-track-ignores-max-size-001/002`. Lost (passed only because test and reference both dropped the declaration, now
exposing real gaps): `replaced-element-016` (canvas percentage block-size) and `subgrid/scrollbar-gutter-001/002` (inline-grid in vertical-rl, scrollbar
gutters in subgrids).

## Row-axis baseline model, inline-block line sizing

- `CssLayoutEngineGrid.Baseline.cs`: an item's baseline is a `BaselineSpan(Above, Below)` to its **margin box** edges (the old code measured the ascent from the
  border box and put the top margin into the descent, which only worked while every item had the same margins). First and `last baseline` are separate
  groups per row; a last-baseline item falls back to the end edge and the group lines up by its distance to the row end. An item with no line in that
  axis - an empty box, or an orthogonal one whose row axis is its inline axis - synthesizes the baseline from the bottom border edge (its bottom margin
  stays below it). Lanes sizing and placement use the same model.
- Two line-box defects found by checking test and reference against Chrome (the references are inline-blocks, rendered by this engine, so a wrong
  reference fails a correct grid): an inline-block's bottom padding, border and margin never grew the line below the baseline (`GrowLineBelowBaselineForAtomicInset`,
  single-line content only), and an orthogonal inline-block took a baseline from its own lines instead of resting its bottom margin edge on the line's
  baseline (`AtomicInlineBaselineOf`, `BaselineIsBottomMarginEdge`).
- Gained `grid-self-baseline-horiz-001`, `-changes-grid-area-size-003/006`; `row-fill-reverse-align-items-002` and `row-fill-reverse-align-self-indefinite-size-001`
  were passing only because `last baseline` was treated as `first` on both sides (the flex reference still does).
- Not done: baseline sharing across row-spanning items, the column axis for orthogonal items (central baseline of the first line), vertical writing modes,
  and a multi-line inline-block's bottom inset (the reference of `grid-self-baseline-horiz-002` still falls short by that).
