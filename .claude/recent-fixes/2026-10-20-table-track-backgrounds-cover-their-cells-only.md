# Table track backgrounds cover only their own cells

[#1894](https://github.com/jhaygood86/PeachPDF/issues/1894), found while removing the duplicate rowspan copies in #1839.

CSS 2.1 §17.5.1 puts column groups, columns, row groups and rows on layers beneath every cell, each one's background covering
"exactly the full area of all cells that originate in" it ("even if they span outside", which "does not affect background
image positioning"); css-tables-3 §5.3.2 describes the same result as each cell drawing them. PeachPDF painted each track's
whole rectangle in tree order instead, and a comment in `CssLayoutEngineTable.SetColumnBoxDimensions` said the layering "falls
out of DOM tree order for free". It does not for spans: a later row's background was painted after, and over, a cell spanning
down into it; a `colspan` cell showed the backgrounds of every column it crossed; and every track filled the `border-spacing`
gaps and missing-cell slots. The rowspan duplicates the emitter used to draw (#1834) hid the first case by repainting the cell
over each later row; removing them (#1839) made zebra-striped tables cover their spanning cells' text.

The load-bearing idea: a track still paints its own background, at its own place in the tree (a column before the row groups,
a row group before its rows, a row before its cells - §17.5.1's layer order), but only over the areas of the cells that
originate in it on this page, positioned against its own rectangle (`FragmentPainter.TableBackgrounds`). The cell areas of
different rows (or columns) never overlap, so tree order can no longer let a later track cover an earlier one's cell.

## Traps and decisions

- **The first version had each cell paint its tracks' backgrounds** (css-tables-3's wording). It put them under the cell's
  effects: a cell's `opacity`, `filter` or `transform` faded or moved its row's and column's backgrounds, and a row's reached
  its column's and row group's - against Chrome, which keeps a track's background under the track's own effects (the #1895
  review: tdopacity, tdfilter and tdtransform mismatched Chrome 9, 9 and 6 times of 9). The track painting over its cells
  fixes all of them without a second paint pass.
- **Who paints what is decided once per page.** `CollectTrackBackgrounds` walks the page's fragment tree (or the detached tree
  of a footnote, running element or margin box; keyed by root) and, for each cell fragment and each of its tracks, finds the
  track's fragment over the cell's area on both axes (a table split across multicol columns has a `<col>` fragment per column;
  matching on y alone placed a gradient against the first column's). The track paints that area from `PaintBoxContent`.
- **A track with no fragment over the cell is painted by the cell**, beneath its own background: the row of a `rowspan` cell
  continuing onto a page the row does not reach still has a fragment there, but with no rectangle of its own (it only holds the
  cell), so matching requires one. So is a column that is not visible: CSS 2.1 §17.3 gives a column's `visibility` a meaning
  for `collapse` only (and css-tables-3 §5.4.2 keeps a collapsed track's background on cells it does not collapse), but a
  hidden column's fragment paints nothing (`PaintFragment` skips it).
- **Once one track goes to the cell, so do the tracks layered above it.** The cell paints after every track fragment, so a
  hidden column painted by the cell landed over its row's background until `Assign` started chaining (`beneathCell`).
- **Copies of one cell are painted under once.** Without #1839 a `rowspan` cell is reached through every row it spans; keyed by
  (cell, area), the tracks paint under it once, where painting per copy darkened a translucent row (the #1895 review: an rgba
  row about 3 times too dark under a spanning cell).
- **The painting area is the cell, the positioning area is the track.** `PaintBackground` takes an optional painting area: the
  fill (or each image layer's clip) is the cell's rectangle, while the track's geometry places images and gradients. The track's
  own break clip is not pushed: it would cut a spanning cell back to the track's strip.
- **A repeated `<thead>`/`<tfoot>` is not the table's child.** `RemoveHeaderFooterFromTree` detaches the group for the whole of
  paint, so `TableOf` walks `EffectiveParentBox`; walking `ParentBox` lost every header and footer cell's column background.
- **`empty-cells: hide` applies to the separated model only** (CSS 2.1 §17.6.1.1); the guard in `PaintBoxContent` also hid a
  collapsed table's empty cells. It now asks the table's `border-collapse` (`HidesAsAnEmptyCell`).
- **The row and row group come from the box tree, the column from the grid.** A cell's parent is the row it originates in,
  wherever a fragment of it is reached from, and that row's parent is its group - `<thead>`/`<tfoot>` included, which
  `TableGrid.RowGroupAt` leaves out. `CssLayoutEngineTable` now keeps the grid on the table box (`CssBox.TableGrid`) for every
  table with column boxes, not only `collapse` ones.
- **Not changed:** borders. Collapsed-border resolution and painting already treat tracks through `CollapsedBorderSegments`.

## Costs and differences, measured

- **File size grows** where a track's background was one fill and is now one per cell. The reviewer measured the first version
  (`537a6124`): a 5,000-row striped table 195 KB against 124 KB, 200 rows by 50 columns with column backgrounds 98 KB against
  23 KB (+35% over their corpus). This version still fills once per cell. Merging the areas of adjacent cells into one fill
  would win most of it back; not done.
- **Differences from Chrome** that follow the specification (`empty-cells: hide` in the separated model, spanning cells with a
  wide `border-spacing`) are in
  [table-backgrounds-follow-the-spec-where-chrome-does-not](../accepted-gaps/table-backgrounds-follow-the-spec-where-chrome-does-not.md).

## Found to be older than this change, and left alone

- **PDF/A-1 flattening misses a repeated translucent fill**
  ([#1918](https://github.com/jhaygood86/PeachPDF/issues/1918)). `TransparencyProbe` paints every probe into one shared
  scratch page, and the PDF writer checks transparency only when it realizes a new fill colour, so a probe whose fill matches
  the previous probe's never trips the guard: two sibling `<div>`s with the same `rgba()` background throw a
  `PdfAConformanceException` under `TransparencyPolicy.Flatten` on `main`. A striped table now paints its row's translucent
  background once per cell, so it hits this the same way (and `main` threw for that table too, from the row).

## Evidence

- `TableTrackBackgroundTests` (20, on the paint log, asserted against the cells' own rectangles): layer order by colour; a row
  gradient and a positioned image measured against the row; a cell's and a row's `opacity` layer against the track fills; a
  hidden column, column group and row; a hidden cell; a column split across multicol columns; a translucent row under a
  `rowspan` cell; snapping; a repeated header; a `rowspan` cell continuing onto the next page. `RecordingGraphics` now records
  each fill's brush and, when asked (`RecordsLayers`), each layer it opens.
- Mutants that fail tests: matching a track fragment on y alone, or without a rectangle of its own; positioning against the
  cell, here or in the cell's fallback; dropping the painting-area clip; not handing a hidden column to its cells; not
  chaining the tracks above one handed to the cell, or reordering them; dropping the per-cell dedupe, the hidden-cell or
  hidden-empty-cell exclusion, or the snapping of the cell areas. Mutants that survive, each with a comment in the code: the
  `CssSpacingBox` exclusion (a placeholder has no area), the `NeedsClip` slice (no tested cell is sliced narrower than its
  rectangle on the page), snapping a fallback track's geometry (only an image against a hidden column could show it), the
  `i == 0` guards (a table box has one rectangle per page), and the line rectangle against the fragment's (the same for a
  table box).
- Rendered against Chrome 156 (print to PDF) side by side for the first version: zebra rows over a rowspan cell, a transparent
  rowspan cell under two coloured rows, column and column-group backgrounds with `colspan`, `thead`/`tbody`/row backgrounds
  with `border-spacing` and a missing cell, a row gradient, and the `table_track_backgrounds` showcase matched Chrome's
  backgrounds. This version was not rendered again; its tests assert the same cell areas, painted from the track instead.
- Full suites, net8.0, Debug, with the invariant culture this machine's comma-decimal locale needs: `PeachBrackets.Engine.Tests`
  16,103, `PeachPDF.Tests` 1,435 (9 skipped), `PeachBrackets.Tests` 26, `PeachPDF.Cli.Tests` 213; `PeachDrawing.PDF.Tests`
  1,163 (run without it: its own culture tests need real cultures). `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
  Diff coverage of the library lines: 81/83 (97.6%); the two left are the fallback's snapping and `TableOf`'s `return null`.
