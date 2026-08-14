# A rowspan in a multi-row `<thead>`/`<tfoot>` no longer mis-positions the following cell

Previously: in a multi-row `<thead>`/`<tfoot>`, a `rowspan` cell starting in one row and reaching into a
later row of the *same* group left every cell after it in that later row rendered at the wrong horizontal
position and width - the same position/width as the column the rowspan cell itself occupies, rather than
its own column. This also corrupted column width distribution for the whole table, since the mis-placed
cell's content was attributed to the wrong column when column widths were being worked out.

Now: every cell in that row renders at its own correct column - tracked as
[#740](https://github.com/jhaygood86/PeachPDF/issues/740).

The rowspan cell's own height still isn't stretched to cover every row it spans in this shape - a
separate, deeper defect - tracked as [#742](https://github.com/jhaygood86/PeachPDF/issues/742).
