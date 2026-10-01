# An empty `<div style="clear: both">` now clears the float above it

**Before:** an empty block with `clear` (the classic clearfix div) was skipped when the next sibling was placed, so the
content after it stayed beside the float instead of dropping below it.

**Now:** that content starts below the float's margin edge, as in a browser. A document that relied on text wrapping
beside a float past an empty clearing div will now wrap below it.
