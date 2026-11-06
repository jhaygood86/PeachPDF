# A box with `clear` goes below the floats it should, at any depth and in its own column

- A float two or four levels down an earlier sibling (`<div><div><div style="float:left">`) is now cleared. A box with `clear` after such a wrapper used to start beside the float, over the box it was meant to go below; it now goes below it, as in a browser.
- A float inside a box that is a formatting context of its own (an `overflow` other than `visible`, `display: flow-root`, an inline-block, a table cell, another float) is no longer cleared by a later box: it does not reach out of that box. A page that relied on a `clear` after a fixed-height `overflow: hidden` card to go below the card's floats now starts right after the card.
- In a multi-column container a box with `clear` only clears the floats of its own column. A float that starts the second column used to be pushed down by the first column's floats.
