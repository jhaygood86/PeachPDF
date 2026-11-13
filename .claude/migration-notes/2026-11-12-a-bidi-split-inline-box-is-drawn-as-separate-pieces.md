# A bidi-split inline box is drawn as separate pieces, with its edges beside its own text

Before: when bidi reordering put one inline box's text between two pieces of another box's (an RLO opened inside a bordered or filled
`<span>` and closed outside it), the span's background and border were drawn as one rectangle covering the other text, and its right
border and padding were placed in the gap inside its own run.
Now: the span's right edge sits beside its last word and the span is drawn as two pieces around the other text (left border on the piece
that starts it, right border on the piece that ends it, top and bottom on both). Pages that mixed directions this way will see the
background and borders shrink to the box's own text.
