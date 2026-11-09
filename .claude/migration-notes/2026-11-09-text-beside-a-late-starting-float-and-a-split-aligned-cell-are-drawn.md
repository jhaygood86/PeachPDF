# Text beside a late-starting float, and a split middle/bottom-aligned cell, are no longer lost

- Two left floats side by side where the first ends on an earlier page: the lines of the paragraph after them on the later page used to run the full
  width over the second float (and a word could be drawn on no page). They are now shortened to the float's right edge.
- A `rowspan` cell taller than a page with `vertical-align: middle` or `bottom` under a repeated `<thead>`/`<tfoot>` used to have its content pushed down
  by room it does not have, and the text after its page-tall block was drawn on no page. A cell that splits across pages is now left top-aligned, as
  browsers do; a cell that fits in one page band keeps its alignment.
