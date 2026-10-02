# A nested multi-column container continuing in the next outer column was placed over the first (seed 222)

A multi-column container laid out beside a float is narrowed to clear it, and `ColumnsBesideFloats` keeps the
left edge and width it was narrowed to for the pages it continues onto, where the box still sits where that layout left
it. A container nested in an outer column and continuing into the next *outer* column is placed at that column's own x
instead, and the kept left edge, the first column's, was applied as an offset from there: `73 - 154`, so the container
went back to x=73, over the first outer column, and its words were drawn there a second time.

Fix: apply the kept extent only while the box's content left is still the kept left edge (to half a point). Otherwise the
box was placed afresh by another outer column, where the float is not beside it, and it is laid out afresh too.

Found by: reducing seed 222 (6 doubled words in the 300-document corpus) to a float and a nested `columns:3` in a
`columns:2`, with a trailing empty `<p>` that moves to the second outer column. First tried storing the kept value as an inset
from the content left instead; that broke 12 documents (lost words 68 to 290), because after the first layout the
box's `Location.X` already includes the inset, so a continuation's content left equals the kept left and the old
subtraction was zero by design.

Evidence: corpus (300 documents) differs from main in seed 222 alone, 6 doubled words to none; net8.0 suite green.