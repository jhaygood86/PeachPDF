# Cells of a `visibility: collapse` column are not painted

**Before (v0.9.20):** the column took no space, but a cell in it that held text still drew that text, over the cells beside it.

**Now:** none of the column's cells paint (CSS 2.1 §17.5.5). Nothing else about the table changes.
