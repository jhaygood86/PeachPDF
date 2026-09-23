# A table's width no longer grows on every re-layout (issue #1267)

**Symptom.** The `border_style` showcase's "table is exempt" swatch (a `width: 100%`,
`border: 16px inset`, `border-collapse: separate` table nested in a 25% cell) painted one right
border wider than its cell, overrunning the next swatch. A single-pass layout test did *not*
reproduce it; the overrun only appears when the same box tree is laid out more than once.

**Cause.** `CssLayoutEngineTable`'s step 7 settled the table's inline-end edge as
`max(cursor.MaxRight, Location.X + ActualWidth) + trailingSpacing + TableInlineBorderEnd`.
`ActualWidth` is not an input - it is `ActualRight - Location.X` from the *previous* layout, which
already includes the trailing border. On the first pass it is harmless (the cursor wins); on every
later pass the old right edge wins and gets the border added again, so the table grows by exactly
`TableInlineBorderEnd` + the trailing spacing slot per re-layout (measured: 197 -> 209 -> 221 for a
12pt border, `border-spacing: 0`).
`PdfGenerator` lays out twice whenever `ScaleToPageSize` or `ShrinkToFit` is set (measure pass,
then final pass), which is what the showcase hit.

**Fix.** The floor is gone: the edge is `cursor.MaxRight` + trailing spacing + inline-end border.
Nothing sets the table's width before step 7, so the old floor never contributed on a first pass -
dropping it keeps single-pass output byte-for-byte what it was. Same change on the vertical
(`ActualBottom`) branch.

**Rejected: flooring at `Location + GetWidthSum()`** (the first version of this fix). It counts a
spacing slot for every column, including trailing `<col>`-only columns no cell reaches, so a
`<col><col><col>` table with one cell grew from 17pt to 25pt on a *single* layout. Headless Edge
measures that table at 18pt, the same as without the `<col>`s - browsers leave that spacing out.
`TableLayout_TrailingColOnlyColumns_AddNoSpacing` guards it. (Edge also gives a table whose extra
columns come only from a `colspan` 18pt where PeachPDF gives 25pt - Chromium merges such tracks.
That predates this fix and is not touched by it.)

**Trap for the future.** Never read a box's own `ActualWidth`/`ActualHeight`/`ActualRight` as an
input while computing that same box's extent: layout is re-entrant (measure pass, a parent table
re-running a nested one), so those values are last pass's output. A layout test that only calls
`PerformLayout` once cannot catch this - `TableLayout_NestedPercentWidthSeparateBorders_FitsCellAcrossRelayouts`
re-runs layout three times on one container and asserts the geometry each time.

**Evidence.** Full net8.0 suite green (13177 passed); the changed lines are 100% covered; the
`border_style` showcase rasterized through PDFium and MuPDF now spans ~247 raster px at 2x,
matching the Chromium print cited in the issue (was 279).
