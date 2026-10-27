# A table nested in a sliced row breaks in the row's run frame (#1795)

**Symptom.** An outer table with a repeated `<thead>` whose one body row holds a multi-page inner
table: from page 3 on the inner rows drifted by one header-height per page, and a row straddling the
page foot was drawn on two pages (`Row 8` twice, its top cut off).

**Cause.** The outer table lays a too-tall row out once, continuously, and
`SliceARowAcrossTheBandsItOverflows` cuts it into strips, each displaced below the repeated header
(`DrawShift`). The inner table broke rows at the *real* page edges, so it and the slicer disagreed about
where a page ends by H (the header room) per page.

**Fix.** `RunBandGrid` (`Fragmentation/`) is the page grid seen from inside the sliced row: band k is the
strip, in the row's continuous coordinates. `RunBandGrid.Strips` is the single computation - the slicer
(`BandsSpannedBy`) and the grid read the same sequence, so they cannot drift apart. The table engine asks an
`IPageGrid` (`Grid(container)`) instead of the container; an outer table publishes `CssBox.SlicedRowGrid`
for the duration of a row's cell layout and a nested table finds it by walking its ancestors.

**Traps found by running it.**
- A nested table in the run frame must not drive the document-level pass cursor (`StepOverTo`): doing so
  ended the outer pass mid-cell (`cursor.Stopped`), so the outer row was re-placed on page 7 and pages 1-5 got
  no header. `PassCursorOwner` returns null inside a sliced row, and `TableRowCursor.BandReached` only steps the
  cursor for the real grid.
- A nested table that repeats a group of its own does not use the run frame: its repeated-group instances
  are captured at their laid-out coordinates and are not displaced with the strips, so the inner header
  landed on the outer one (and was emitted twice). See the accepted gap.
- `RecordRepeatingGroupFragmentInstance` and page-name registration stay on the real grid on purpose.

**Evidence.** `NestedTableUnderRepeatedTheadTests` (header-to-first-row gap equal on every page, each inner
row on exactly one page, a row that begins on a later page, a repeated tfoot), `RunBandGridTests`; Engine.Tests
16475, PeachPDF.Tests 1499, PeachBrackets.Tests 26 in Release; the 50-row repro rasterized with PDFium and
MuPDF (13 pages, header on every page, rows intact).
