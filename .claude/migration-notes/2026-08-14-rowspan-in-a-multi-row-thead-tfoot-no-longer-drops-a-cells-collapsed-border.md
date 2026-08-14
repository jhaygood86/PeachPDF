# A rowspan in a multi-row `<thead>`/`<tfoot>` no longer drops a cell's collapsed border

Previously: in a `border-collapse: collapse` table, a `rowspan` cell starting in one row of a multi-row
`<thead>`/`<tfoot>` and reaching into a later row of the *same* group left every cell after it in that
later row placed at the wrong column in the table's internal grid — off by however many columns the
rowspan cell occupies. Any declared border on that mis-placed cell was silently dropped as a candidate
during CSS 2.1 §17.6.2 border-conflict resolution, for both the group's own internal grid line and, if
the group repeats across pages, its boundary to the table body.

Now: the cell resolves at its correct column, and its own declared border participates in border-conflict
resolution normally — tracked as [#736](https://github.com/jhaygood86/PeachPDF/issues/736).

A cell in this shape still renders its own text content at the wrong horizontal position/width (a
separate, deeper defect using a different code path) — tracked as
[#740](https://github.com/jhaygood86/PeachPDF/issues/740).
