# CSS Grid Lanes (`display: grid-lanes`)

**Idea.** `grid-lanes` is a grid in one axis and a stack in the other, so it is a partial of `CssLayoutEngineGrid`
(`CssLayoutEngineGrid.Lanes.cs`) that reuses the grid's track sizing (`ResolveColumnTemplate`, `SizeColumnTracks`, `PositionTracks`),
item measuring and `PlaceItemInCell`, and replaces only placement: one running position per lane, the lane chosen within `fit-tolerance`
of the shortest, a cursor that always moves forward. `DisplayMode.GridLanes` reads `grid` through `DerivedStyle.ActualDisplay` (as
`flow-root` reads `block`), so every one of the ~60 string tests of grid display in layout, paint and fragmentation keep working; only the
engine and a few enum-direct sites in `DomParser` tell lanes apart.

**Found by running it.**
- `MeasureColumnIntrinsics` adds a span's contribution to each track it covers once per start line, so feeding it one placement per start
  line (the spec's "copy to every possible start") weighs the middle lanes twice and the lanes come out unequal. The lanes pass has its own
  symmetric `MeasureLaneIntrinsics`.
- The initial value of `grid-auto-flow` had to become `normal` (css-grid-3 adds it, and WebKit's is `normal`): lanes cannot tell
  `grid-auto-flow: row` from the default otherwise. Plain grids read `normal` as `row` (they only look for `column`/`dense`).
- Stretch in the stacking axis is not "fill the lane": an item stretches to where the next item *of its lane* starts (less the gap), or
  to the end of the stacking range, which is the container's content box when its height is definite. Checked against
  `alignment/column-align-items-001` and `-003`.
- Many WPT tests that fail here fail on font metrics: they assume 100px tracks fit the Times text of the reference browser, and the
  widest word here is wider, so `auto` lanes overflow. They are not lanes bugs.

**Second round.** The `grid-lanes` shorthand (`GridLanesShorthandValueConverter`), stacking-axis `align-content`/`justify-content` (the range offset,
`StackingRangeOffset`), `direction: rtl` (the grid engine's own mirror about `_mirrorRight`, which must be set *after* `GetBoxWidth` has sized the
container - set before it, the mirror axis was the zero-width box), grid-axis baseline alignment, vertical writing modes (the lanes pass reads
margin-box extents through `OuterInlineOf`/`OuterBlockOf` and moves items through `Shift`, so one pass serves both orientations), subgrid in the
grid axis (`LaneSubgridContext`) and fragmentation. Fragmentation reuses the grid's machinery: row lanes are grid rows, so
`RelocateRowsAcrossFragmentainers` and `BuildRowGroups` apply unchanged; column lanes use `RelocateLanesAcrossFragmentainers` (per-item
`LineRelocation.DeltaFor`, a per-lane displacement, and a per-lane floor below the continuation of a cut item) and commit items grouped by the
fragmentainer they begin in (parallel flows), through the same `GridBreakToken` resume path.
- The two-keyword `display` syntax had to be read for `inline grid-lanes` (the pinned WPT uses it); `DisplayTwoValueConverter` rewrites any
  outside/inside pair to its single legacy keyword. It costs four css-flexbox `flow-start-flow-end-*` reftests that passed only because their test and
  reference both used `display: inline flex` and both dropped it; they render as intended now and fail on the unsupported `flow-start` keyword.
- `CommitLanes` groups by `SlotStartingAt(top + PageBoundaryEpsilon)`: a bare top flush on a boundary resolves to the earlier slot.
- The estimate of a cut item's continuation needs the line of slack described in the accepted gap; without it the next item overlapped the last
  line of the cut one (seen in `grid_lanes_paged`, rendered by PDFium and MuPDF).

**Third round.**
- *Exact continuation.* A cut item's lane-mates are placed on an estimate first, then `SettleLanes` (a callback `CommitItemContent` calls after each
  row group has finished, fresh or resumed) moves them to exactly below the cut item once its content has been laid out against the next
  fragmentainer. Two traps: the item's own `ActualBottom` is the height it was *pinned* to by the commit (`ItemContentCommit.CommitLayout`), not where
  its content ended - a line the boundary pushed down reaches past it - so the true end is `CssBox.GetMaximumBottom` plus the box's padding and border;
  and the state the settle needs on a resumed pass (lanes, gap, which cut item an item was placed under) cannot live on the engine instance, which is
  new on each pass, so it is a `ConditionalWeakTable` on the item box.
- *Break-aware alignment.* Relocation now runs before the stacking-axis alignment, which clamps an item's room at the page boundary; `fill-reverse` and
  `align-content` positioning are skipped when the stack spans pages.
- *Subgrid contributions.* `MeasureLaneIntrinsics` asks a column subgrid for its items' contributions (`CollectSubgridColumnContributions`) at every
  start line an auto-placed item could take, as a max, so the lanes stay alike.
- *Vertical + rtl + auto height.* Row lanes measure in pass one, then the container is sized and the writing-mode frame rebuilt before pass two places
  the items, so `rtl` starts from the real bottom.
- A vertical container stays whole (accepted gap).

**Property surface.** WebKit ships `display`, `fit-tolerance` (alias `flow-tolerance`) and `grid-auto-flow` (`normal | [row|column] || dense`);
the pinned WPT additionally uses `grid-lanes-direction` and `grid-lanes-pack`, which are implemented too.

**Evidence.** grid-lanes WPT subset 201 -> 260 passing on the first pin; on the newer pin 266 -> 315 after the second round and 326 after the third (local fonts); Engine.Tests, PeachBrackets.Tests,
PeachPDF.Tests and SourceGenerators.Tests green (SlicedMonolithLinesTests is flaky in a full run and passes alone, as before).
