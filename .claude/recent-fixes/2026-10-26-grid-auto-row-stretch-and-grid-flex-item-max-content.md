# Auto grid rows stretch to a definite height; a grid flex item is sized from its tracks

Two grid gaps closed together (issues #1681 and #1663).

**Row stretch (css-grid-2 §12.8).** `CssLayoutEngineGrid.StretchAutoRows` shares the positive free space of a
definite-height grid equally among rows whose max track sizing function is `auto` (`auto`, `minmax(*, auto)`),
when `align-content` is `normal`/`stretch`. It runs after item measuring and before `PositionTracks`. Rows are
never `fr`-expanded in this engine, so a grid holding an `fr` row is deliberately left alone (§12.7 would have
let the `fr` row absorb the space first); closing that is a separate gap. A subgrid row axis is skipped.
Not covered: an indefinite height with a definite `min-height` (§12.8 uses it for the free space).

**Grid as a flex item (css-grid-2 §5.2, css-flexbox-1 §9.2).** The flex item measure explicitly skipped grid
containers and used the laid-out (container-fill) width. `CssLayoutEngineGrid.TryMeasureIntrinsicWidth` now
sums the column tracks under no definite space (auto/intrinsic from item contributions, `fr` from contribution
over flex factor, percentage tracks as `auto`, plus gaps) and `CssBox.GetMinMaxSumWords` calls it, so every
intrinsic-width consumer benefits, not only flex. It bails (old behaviour) for subgrid, `auto-fill`/`auto-fit`,
`grid-template-areas` and column auto-flow, which need real placement.

**Trap found by running it:** a 100pt grid in a 40pt flex row is *not* shrunk to 40pt; the automatic minimum
size (css-flexbox-1 §4.5) floors it at its min-content, 100pt. The first version of the test asserted 40.

Evidence: Engine.Tests suite 16413/16413 on net8.0 (Debug). Not yet run: Release, PeachPDF.Tests, coverage.
