# Floats no longer overlap an earlier float of the other side, or start above an earlier float

- A `float: left` box after a `float: right` box, when the two are together wider than their container, used to stay at the top and be drawn over the right one. It now goes below it, as in a browser. Floats that fit side by side are placed as before, and so is a left float followed by a right one.
- A float is no longer placed above the top of an earlier float: when an earlier float was pushed down, a later float that would fit in the gap above it now starts at that float's top.
