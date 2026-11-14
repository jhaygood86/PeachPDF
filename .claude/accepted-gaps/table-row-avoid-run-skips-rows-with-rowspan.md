# A table row avoid-run does not pull back rows involved in a rowspan

`break-after/before: avoid` between table body rows (and `break-inside: avoid` on a row group) is honored by
retracting the earlier rows of the run and re-placing them in the next band (see
`.claude/recent-fixes/2026-11-14-table-rows-honor-break-avoid.md`). A row holding a `rowspan > 1` cell (or a
spacing placeholder), or any run whose rows a pending rowspan ends in, is **not** pulled: the run is trimmed
past it, so the avoid is relaxed there. Reason: `TableRowCursor` records the writes a row makes to a spanning
cell of an earlier row for the current row only, so a multi-row retraction could not restore them.
Closing it means keeping the foreign-write record per row. Spec: css-break-3 §4.4 rules 1/2 are violated
in that case. Not tracked as a separate issue yet.
