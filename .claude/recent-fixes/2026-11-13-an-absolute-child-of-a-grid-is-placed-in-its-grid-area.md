# An absolutely positioned child of a grid container ignored its `grid-column`/`grid-row`

Found by the WPT runner (css-grid/abspos, `positioned-grid-items-*`): the four absolutely positioned children of a 2x2 grid, each naming a cell with
`grid-column`/`grid-row`, all stacked at the grid's content corner. css-grid-1 section 9: when the grid container is the box's containing block, the containing
block is the grid area the lines name (a line that does not exist - auto, or beyond the explicit and implicit tracks - is the padding edge), and the static
position rectangle is that area too.

## How
- `CssLayoutEngineGrid.StateAbsolutelyPositionedAreas` (called once the tracks are positioned) resolves each absolute child's lines with `ResolveAxis` and records
  `CssBox.GridContainingArea` (a rare-state field), then moves a child that an earlier pass already placed against the padding box (`OffsetLeft`/`OffsetTop`).
- `CssBox`'s absolute placement uses the area as the origin and as the basis for `left`/`top`/`right` percentages, and as the static position when an inset is auto;
  `CssLayoutEngine.AbsolutePaddingBoxWidth/Height` (the percentage width/height basis) read it too. All of it only when the grid is the box's *parent* and its
  containing block.
- **A grid whose only children are absolute used to return before any track sizing** (`items.Count == 0`), so there were no tracks to take an area from. It now
  continues when such a child exists (a grid with no children still returns early; an empty grid with explicit tracks and an abs child now gets the tracks' height).

## Found by running it
The first version set the area but the child stayed at the content corner: the generic placement overrides an auto inset with the *static position* (the content
origin), which is not the grid area. And `width: 100%` still resolved against the padding box until the percentage-basis helpers read the area.

## Not done
- Subgrids (`colSubgrid`/`rowSubgrid`) and vertical or rtl grids keep the old placement: the tracks a subgrid adopts, and the writing-mode mapping, are not handled.
  `css/css-grid/subgrid/line-names-008` passed with the old placement and fails with the area logic on a subgrid, so subgrids are skipped, not fixed.
- The static position ignores `justify-self`/`align-self` alignment inside the area (`grid-abspos-staticpos-align-self*`); `-safe-001` passed by coincidence before.
- Nine `grid-lanes` tests that passed by coincidence now fail; `display: grid-lanes` is not implemented.
- A line's position uses the track edge, ignoring a gap's thickness.

## Evidence
css-grid 398 -> 407 (+19 gained, -10 lost: 9 grid-lanes coincidences and one align-self static position). Engine.Tests 19,852 (5 new `GridAbsolutePositionTests`),
PeachPDF.Tests 1,978, PeachBrackets.Tests 1,215.
