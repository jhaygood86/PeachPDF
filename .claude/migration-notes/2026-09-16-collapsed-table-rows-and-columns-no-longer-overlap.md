# Collapsed-table rows/columns are measurably taller/wider than before

## What changed

Under `border-collapse: collapse`, adjacent rows and adjacent columns previously overlapped by a full
resolved border width at every interior grid line instead of meeting flush. Each row/cell already
reserved its own half of the shared border as an inset (correct), but the row/column cursor also pulled
back by the *whole* border width when crossing an interior line, so the shared border ended up
contributing nothing to the space between two rows (or two columns) — an invisible defect, since nothing
was drawn wrong at any single boundary, but every table with more than one row or column rendered
shorter/narrower than it should have, by one border width per interior boundary, compounding across the
table.

```html
<table style="border-collapse:collapse">
  <tr><td style="border:1px solid #333">row 1</td></tr>
  <tr><td style="border:1px solid #333">row 2</td></tr>
  <tr><td style="border:1px solid #333">row 3</td></tr>
</table>
```

Before this change, row 2 began 1px *before* row 1 ended (an overlap), and row 3 the same relative to
row 2 — the table's total height was two border-widths shorter than a real browser's. It now renders
with every row meeting its neighbor exactly flush, matching Chrome. A document with many rows in a
collapsed-border table (an itemized invoice, a repeating data table) will now render measurably taller
than before — a page count that was already marginal could gain a page. A document that added
compensating padding, margin, or `min-height` to work around the old shortfall will now render with that
compensation on top of the corrected (already-larger) size and should have it removed.

The same fix applies to column widths under `width: auto` or a `<table>` with an explicit `width` whose
columns were sized by content — columns now meet flush too, rather than overlapping by a border width at
each interior boundary.

## Not covered by this change

Nothing about *which* border wins a conflict (CSS 2.1 §17.6.2's resolution — width, then style priority,
then origin, then position) changed; only the *position* two already-agreeing boxes sit at relative to
each other.
