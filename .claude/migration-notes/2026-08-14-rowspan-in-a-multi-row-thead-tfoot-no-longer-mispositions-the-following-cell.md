# A rowspan in a multi-row `<thead>`/`<tfoot>` no longer mis-positions the following cell or under-sizes itself

Previously: in a multi-row `<thead>`/`<tfoot>`, a `rowspan` cell starting in one row and reaching into a
later row of the *same* group left every cell after it in that later row rendered at the wrong horizontal
position and width - the same position/width as the column the rowspan cell itself occupies, rather than
its own column. This also corrupted column width distribution for the whole table, since the mis-placed
cell's content was attributed to the wrong column when column widths were being worked out. Separately,
the rowspan cell itself never had its own height stretched to cover every row it spans - it kept only its
own natural (single-row) content height, even when the rows it spans were visibly taller.

Now: every cell in that row renders at its own correct column, and the rowspan cell's own box reaches all
the way down to the last row it spans - tracked as
[#740](https://github.com/jhaygood86/PeachPDF/issues/740) and
[#742](https://github.com/jhaygood86/PeachPDF/issues/742).
