# A vertical table's captions, `<thead>`/`<tfoot>`, collapsed borders, cell `vertical-align`, and rowspan sizing are now writing-mode-aware

**Landed:** 2026-08-20 — Support `<caption>`, `<thead>`/`<tfoot>` placement, `border-collapse: collapse`,
`vertical-align`-in-cell, and `rowspan` row-axis sizing under `writing-mode: vertical-rl`/`vertical-lr` (#762)
**Doc sections:** docs/html-css-support.md § [writing-mode row](../../docs/html-css-support.md)

As of the previous release, a `display: table` under `writing-mode: vertical-rl`/`vertical-lr` had its
overall sizing and body-row/cell placement laid out along the correct (block) axis, but several
table-specific features still assumed `horizontal-tb` internally: a `<caption>` stacked and sized on the
wrong axis (or landed at a degenerate position), `<thead>`/`<tfoot>` header/footer proxies were positioned
using the physical-Y convention regardless of writing mode, `border-collapse: collapse` resolved border
conflicts and painted segments as if the table were always horizontal, `vertical-align` inside a cell
offset content along the wrong physical axis, and a `rowspan` cell's own row-axis extent could come out
corrupted (including corrupting sibling cells' positions in the same row).

All five now correctly follow the table's own writing mode:

- `<caption>` (top or bottom) stacks along the row (block) axis and sizes across the column (inline) axis,
  matching the same axis convention body rows already used.
- `<thead>`/`<tfoot>` are positioned along the row axis on their own side of the body rows. A header/footer
  group with more than one row does not yet reverse its own internal row order under `vertical-rl` the way
  `<tbody>` rows do — tracked as [#784](https://github.com/jhaygood86/PeachPDF/issues/784); a single-row
  group (the common case) is unaffected.
- `border-collapse: collapse` resolves conflicting adjacent borders and paints the resulting segments on
  the correct physical edges for the table's writing mode, rather than always treating "row boundary" as a
  physically horizontal line.
- `vertical-align` (`top`/`middle`/`bottom`, and length/percentage values) offsets a cell's content along
  the row axis rather than always along physical Y.
- A `rowspan` cell sizes to the combined row-axis extent of the rows it spans, without corrupting the
  position of other cells in those rows. `colspan` needed no fix — column-axis sizing was already
  writing-mode-correct.

A vertical table using any of these features previously rendered with wrong caption/header/footer
placement, wrong border painting, wrong cell-content alignment, or corrupted rowspan geometry; it now
renders correctly. This is a visible rendering change for any such document, not a bug fix to output that
was already correct.

Real per-row pagination of a vertical table's own content remains out of scope — a vertical table that
doesn't fit on one page is still placed monolithically rather than split — tracked as
[#783](https://github.com/jhaygood86/PeachPDF/issues/783). See
[`.claude/accepted-gaps/no-vertical-writing-mode-layout.md`](../accepted-gaps/no-vertical-writing-mode-layout.md)
for the full current list of what's still unsupported under a vertical writing mode.
