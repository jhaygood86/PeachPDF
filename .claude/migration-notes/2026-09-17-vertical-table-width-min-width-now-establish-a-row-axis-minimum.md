# `width`/`min-width` on a `writing-mode: vertical-*` `<table>`/`<tr>` now establish a row-axis minimum

## What changed

**A `width`/`min-width` declared on a `<table>` or `<tr>` under `writing-mode: vertical-rl`/
`vertical-lr` previously had no effect on the table's row axis at all.** For a vertical table the row
axis (where [CSS 2.1 §17.5.3](https://www.w3.org/TR/CSS21/tables.html#height-layout) applies) is
physical *width*, not physical height — but this engine only ever read `height`/`min-height` for that
enforcement, which is the correct physical property for a horizontal-tb table and the wrong one for a
vertical table (there, `height`/`min-height` size the table's *columns* instead, an entirely separate
axis). The result: an author writing what looks like the natural vertical-table analog of an ordinary
horizontal table's `height`/`min-height` saw it silently do nothing to row spacing.

```html
<table style="writing-mode: vertical-rl; width: 400pt">
  <tr><td>A</td></tr>
  <tr><td>B</td></tr>
</table>
```

Before this change, the two rows above rendered at their natural (content-driven) row-axis extent
regardless of the table's own `400pt` `width`. Now, the table's row axis is stretched to `400pt` if the
rows' natural total falls short, with the surplus distributed proportionally across the rows — the same
rule this engine already applies to an ordinary horizontal table's `height`/`min-height` surplus, and to
column-width surplus. A `<tr>`'s own `width`/`min-width` works the same way, one level down, as a
per-row minimum that never clips a row shorter than its own content requires.

Rendered output for a vertical table that already declares a `width`/`min-width` on the `<table>` or a
`<tr>` may change: rows may now be taller (wider, along the row axis) than before, and any explicit
`vertical-align` on a cell may now have a visible effect where it previously didn't (the row had no
extra room to align within). A vertical table with no `width`/`min-width` set anywhere is unaffected.
