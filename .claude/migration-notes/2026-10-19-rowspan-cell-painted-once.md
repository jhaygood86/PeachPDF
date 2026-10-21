# A `rowspan` cell is painted once

Before (v0.9.23 included): a cell spanning N rows was drawn up to 2N-1 times at the same place, once through its own row and
again through each row it covers, so its text looked bold and a PDF reader extracted it that many times. Now a copy that would
draw only what an earlier copy at the same place already shows is left out, so the text is drawn and extracted once. Every copy
still drawn was drawn before, at the same place. Under a repeated `<thead>`/`<tfoot>`, where a row taller than a page is clipped
to its own band, a copy that shows a different part of the cell is still drawn, so the cell's background and borders still
reach the bottom of its last row on the continuation page.

One thing the extra copies used to hide: a background on a later row the cell spans is painted over that row's whole width,
and the cell's next copy then covered it again. Without that copy, the row's background covers the part of the cell in that
row (its fill, borders and text) until row backgrounds are painted only behind the cells of their own row, as CSS 2.1 §17.5.1
requires ([#1894](https://github.com/jhaygood86/PeachPDF/issues/1894), fixed by [#1895](https://github.com/jhaygood86/PeachPDF/pull/1895)).
