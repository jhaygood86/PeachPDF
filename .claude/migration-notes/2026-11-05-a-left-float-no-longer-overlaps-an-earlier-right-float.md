# Floats no longer overlap an earlier float of the other side, or start above an earlier float

- A `float: left` box after a `float: right` box, when the two are together wider than their container, used to stay at the top and be drawn over the right one. It now goes below it, as in a browser. Floats that fit side by side are placed as before, and so is a left float followed by a right one.
- A float is no longer placed above the top of an earlier float: when an earlier float was pushed down, a later float that would fit in the gap above it now starts at that float's top.
- Floats whose widths add up to their container only to within rounding (`66.6667%` and `33.3333%`) stay side by side.
- A float with `clear` now goes below the floats it clears by its own `margin-top` as well: two cleared floats with `margin: 8px 0` are 16px apart, not 8px, as in a browser. A page with a column of cleared floats that have a top margin (chat bubbles, say) moves down by that margin for each float after the first one in the run. `clear` on a block that is not a float is unchanged.
- A right float that does not fit beside an `inline-block` (or `inline-flex`, `inline-grid`, `inline-table`) already on its line goes below the line instead of over the box.
- A float is placed against where an earlier float is in the flow, not where a `position: relative` offset draws it, so an offset on a box of floats no longer moves the floats after it.
