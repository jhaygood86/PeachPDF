# Table track backgrounds cover only their own cells

CSS 2.1 §17.5.1 puts column groups, columns, row groups and rows on layers beneath every cell, each one's background covering
"exactly the full area of all cells that originate in" it ("even if they span outside", which "does not affect background
image positioning"); css-tables-3 §5.3.2 says the same by having each cell draw them. PeachPDF painted each track's whole
rectangle in tree order instead, and a comment in `CssLayoutEngineTable.SetColumnBoxDimensions` said the layering "falls out
of DOM tree order for free". It does not for spans: a later row's background was painted after, and over, a cell spanning down
into it; a `colspan` cell showed the backgrounds of every column it crossed; and every track filled the `border-spacing` gaps
and missing-cell slots. The rowspan duplicates the emitter used to draw (#1834) hid the first case by repainting the cell over
each later row; removing them (#1839) made zebra-striped tables cover their spanning cells' text.

The load-bearing idea: a track never paints its own background (`FragmentPainter.IsTableTrack`); every real cell paints, just
before its own, the backgrounds of the column group, column, row group and row it originates in
(`FragmentPainter.TableBackgrounds`).

- **The row and row group come from the box tree, the column from the grid.** A cell's parent is the row it originates in,
  wherever a fragment of it is reached from (its own row, a `CssSpacingBox` placeholder, a repeated header's captured source),
  and that row's parent is its group - `<thead>`/`<tfoot>` included, which `TableGrid.RowGroupAt` leaves out. Only the column
  needs `TableGrid.TryGetSpan`, so `CssLayoutEngineTable` now keeps the grid on the table box (`CssBox.TableGrid`) for every
  table with column boxes, not only `collapse` ones.
- **The painting area is the cell, the positioning area is the track.** First attempt clipped the track's background to the
  cell but still filled the track's rectangle, so a transparent rowspan cell showed row 1's colour over row 1's height only.
  `PaintBackground` now takes an optional painting area: the fill (or each image layer's clip) is the cell's rectangle, while
  the geometry - the track's fragment on this page, found by walking `_pageRoot` once per page - places the images. A track
  with no fragment on this page (the row of a spanning cell's continuation) positions against the cell. The track's own
  break clip is not pushed: it would cut the cell back to the track's strip.
- **A repeated `<thead>`/`<tfoot>` is not the table's child.** `RemoveHeaderFooterFromTree` detaches the group
  (`ParentBox = null`, the table kept as `DomParentBox`) for the whole of paint, so `TableOf` walks `EffectiveParentBox`;
  walking `ParentBox` lost every header and footer cell's column background (found in review, pinned by
  `AHeaderCell_ShowsItsColumnsBackground`).
- **`empty-cells: hide` now costs the tracks' backgrounds too**, so it had to be right about where it applies: the separated
  model only (CSS 2.1 §17.6.1.1). The guard in `PaintBoxContent` hid a collapsed table's empty cells as well; it now asks the
  table's `border-collapse` (`HidesAsAnEmptyCell`).
- **A track fragment is matched on both axes**, and a cell with none over it positions against itself: a table split across
  columns has a `<col>` fragment per column, and matching on y alone placed a gradient against the first column's. A table
  painted from a detached tree (a footnote, a running element, a margin box) indexes that tree.
- **Not changed:** borders. Collapsed-border resolution and painting already treat tracks through `CollapsedBorderSegments`.

## Found to be older than this change, and left alone

- **PDF/A-1 flattening misses a repeated translucent fill.** `TransparencyProbe` paints every probe into one shared scratch
  page, and the PDF writer checks transparency only when it realizes a new fill colour, so a probe whose fill matches the
  previous probe's never trips the guard: two sibling `<div>`s with the same `rgba()` background throw a
  `PdfAConformanceException` under `TransparencyPolicy.Flatten` on `main`. A striped table's cells, which now each paint their
  row's translucent background, hit it the same way (and `main` threw for that table too, from the row). Not tables-specific;
  a follow-up, as is the reviewer's point that flattening would then run once per cell rather than once per row.

## Evidence

- `TableTrackBackgroundTests` (10, paint log against the cells' own rectangles): 6 fail with `main`'s painter; the layering
  order test passes on both, the repeated-header test fails when cells skip their row group, the header-column test fails
  when `TableOf` walks `ParentBox`, and the collapsed empty-cell test fails without the `border-collapse` check.
- Rendered against Chrome 156 (print to PDF) and compared side by side: zebra rows over a rowspan cell, a transparent rowspan
  cell under two coloured rows, column and column-group backgrounds with `colspan`, `thead`/`tbody`/row backgrounds with
  `border-spacing` and a missing cell, a row gradient, and the new `table_track_backgrounds` showcase all match Chrome's
  backgrounds; `main` differs in each.
- No existing test pinned the old painting: `PeachBrackets.Engine.Tests`, `PeachPDF.Tests` and `PeachBrackets.Tests` pass as
  before (net8.0, Debug).
