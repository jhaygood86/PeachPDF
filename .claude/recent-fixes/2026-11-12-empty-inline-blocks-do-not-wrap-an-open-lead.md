# Open lead: empty `inline-block` siblings never wrap (not fixed)

Found by the WPT runner (css/CSS2/margin-padding-clear `margin-collapse-0xx` with `display: inline-block` siblings, ~30 tests): two empty
`display:inline-block; width:100px` boxes in a 100px container stay on one line (x 0..205). With text inside, or with a block-level child, they wrap.

## Where it comes from
The ordinary (word-driven) inline-block path wraps through the box's first word; a box with no words has nothing to trigger it. Only
`LaysOutAsAnAtomicBox` boxes (block-level descendants, or content wider than the whole containing block) reach `FitAtomicInlineOnLine`, which does the
atomic line fit.

## What was tried and measured
1. Making `LaysOutAsAnAtomicBox` return true for a contentless box: css2-flow 1,710 -> 1,742 and +50/-1 across the run, but it changes the box's painted
   rectangle and vertical-align handling (the atomic path is a different layout), failing `InlineBlockDeclaredHeightGeometryTests.BorderBoxIsFlooredAtItsOwnBorderAndPadding`
   (painted 1 instead of 6) and `VerticalAlignOffsetTests.ARaisedInlineBlock_MakesItsLineTallerAndKeepsTheStrutsDescent`. Reverted.
2. Keeping the ordinary path and calling `FitAtomicInlineOnLine` just before its `FlowBox` call: no change in the WPT result, and one
   `EmptyInlineLineHeightTests` failure. Reverted.

Either needs the wrap decision made where the box's own rectangle is already being built (`RegisterAtomicInlineRectangles`), not before it.
