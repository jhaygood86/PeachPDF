# text-align aligns to the edge a right float leaves

Before: a line beside a `float: right` was wrapped at the float's left edge, but `text-align: justify` then spread it to the container's full right edge, so its last word was drawn under the float; `right` and `center` aligned against the same full edge.

Now: justify, center and end-aligned lines use the shortened edge (the lesser of the line's content edge and the nearest right float's margin edge beside it), so the words end at the float.
