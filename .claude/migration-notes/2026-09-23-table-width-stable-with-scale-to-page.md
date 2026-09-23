# Tables no longer widen under `ScaleToPageSize` / `ShrinkToFit`

**Before.** With `PdfGenerateConfig.ScaleToPageSize` or `ShrinkToFit` enabled, a table with an
inline-end border or a non-zero `border-spacing` came out wider than it should, by its right border
width plus one `border-spacing` gap (the document is laid out twice in that mode, and the second
pass added both again). A `width: 100%` table in a cell therefore overran its cell.

**Now.** A table's width is the same no matter how many times layout runs: a `width: 100%` table
fits its containing block exactly, as it already did with both options off.

**Why.** The table's final inline-end edge was derived from the previous layout's own settled width
instead of the width resolved on the current pass.
