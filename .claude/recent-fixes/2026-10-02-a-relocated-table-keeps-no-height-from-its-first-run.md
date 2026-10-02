# A table relocated to the next page keeps no height from its first run

**Symptom:** a `<table>` whose cells hold a `break-inside: avoid` block (the `svg` showcase's peach section is the case that was
noticed) came out as tall as the gap it left at the foot of the previous page, and the table after it started that far
below it: about 150pt of blank page between the two tables. Present in v0.9.20 too (any document that happens to put such a
table at a page foot), but #1556 changed the earlier pagination of the `svg` showcase enough to put the peach section there.

**Cause:** the cell's first run broke inside it (the avoid block moved past the page edge, so the cell's bottom was the far
side of the gap), then the whole table was moved to the next page and laid out again. `CssBox.ActualBottom` is
`Location.Y + Size.Height`, and the row loop gives a cell its new `Location` without touching the stored `Size.Height`, so the
second run re-measured the content (389pt) but kept the old, taller bottom (146pt of height instead of 78pt). Found by logging
each cell's bottom right after its layout: `cellBottom` 461.3 against `maxDesc` 389.0 in the final run.

**Fix:** a cell the row loop places afresh starts from no height (`cell.ActualBottom = cell.Location.Y`, horizontal tables;
a vertical table keeps its own row-axis bookkeeping). A resumed cell is not touched.

**Evidence:** `TableRelocationHeightTests` (3 of 4 fail without it); full suite 15018 passed; of the 199 showcases only
`svg`, `border_style`, `background_origin_clip` and `flexbox` change, all by tables shrinking to their content. The `svg`
peach section is back at the 0.9.20 position (384pt rather than 532pt).

**Not done:** the two other hits of #1556's `BandBeingFilled` early return in the `svg` showcase (words whose top is in an
earlier page than the layout cursor) are legitimate: the first one is the heading moved by keep-with-next, the second moves an
SVG that crosses its page foot.
