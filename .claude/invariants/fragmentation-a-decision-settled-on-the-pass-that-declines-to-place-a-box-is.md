# A decision settled on the pass that *declines* to place a box is retracted before the pass that places it

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

The placing pass takes the resumed-target branch, which asserts nothing, and an enclosing engine may re-open the box's prologue in between — whose retractions exist so a *re-decided* break can re-assert them, which a decision already travelling in a record is not. Two facts were lost this way in #397: `PlacedByForcedBreak`, which put a following sibling **ahead of the break**, and a directional break's reserved blank slot, which landed `break-before: right` on a left-hand page.
