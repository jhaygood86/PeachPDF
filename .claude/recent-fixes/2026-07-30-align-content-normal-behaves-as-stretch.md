# `align-content: normal` behaves as `stretch`, and a single flex line fills its container

Tracker: [#504](https://github.com/jhaygood86/PeachPDF/issues/504). Closes [#461](https://github.com/jhaygood86/PeachPDF/issues/461).

## The load-bearing idea

`CssLayoutEngineFlex.DistributeCrossSpace`'s `align-content` switch had a `default:` arm doing
cross-start packing that both `flex-start` (correct) and `normal` (not — `normal` computes to
`stretch` per [css-align-3 §5.1](https://www.w3.org/TR/css-align-3/#propdef-align-content)) fell
into. `AlignItems`/`AlignSelf`'s own `normal` was already aliased onto the `stretch` arm in this same
file's `ComputeCrossOffsets` (`case CssConstants.Stretch: case "normal":`) — `align-content` gets the
identical one-line treatment, moving `"normal"` out of `default:` and into the `Stretch` case.

The second half is CSS Flexbox 1 [§9.4 step 8](https://www.w3.org/TR/css-flexbox-1/#algo-cross-line):
a container with **exactly one flex line** sizes that line to the container's own definite cross
size. The existing guard read `!_isWrap`, i.e. only `nowrap`, when the real condition is
`lines.Count == 1` — a `wrap`/`wrap-reverse` container whose content simply doesn't wrap is one line
too, and step 8 does not carve out an exception for how a container arrived at having one line.

## What was found by running it, not by reading it

Fixing the guard exposed that the fix's own scope is bigger than "wrapping containers whose lines
never got tested" — it also changes any *already-tested* `wrap`/`wrap-reverse` fixture that happens
to end up with exactly one line and a definite cross size, which two pre-existing tests did:
`WrapReverse_Column_WithADefiniteWidth_KeepsIt` (two items sharing one column-direction line, cross
axis = definite width) and `WrapReverse_StretchableItem_StillFillsItsLine` (two items sharing one
row-direction line, cross axis = definite height). Both were asserting the line sized to its
*content* (matching the old, narrower reading of step 8) and needed their expected numbers
recalculated against the new stretch-to-container-size line. In both cases only the position of the
non-stretching, definite-size sibling item needed no change — its "flush against the far edge"
placement is invariant to the line's own cross size for a single-line container, since the line's
`CrossOffset + CrossSize` always sums to the container's cross extent regardless of how that sum
splits between the two — only the item that either can stretch, or sits alone with fixed size against
a *changed* line size (the three-line wrap-reverse `align-content` fixture below), actually moves.

A third fixture, `WrapReverse_Column_UnequalLineCrossSizes_StackRightToLeftWithoutOverlapping`'s
sibling case with `align-content` left unset instead of stated, was where the "flush against the far
edge is invariant" shortcut *stopped* applying (three lines, not one) and had to be worked by hand:
each line's sole item, non-stretching, flushes at `line.CrossSize - itemWidth` inside its own line —
so growing the line by the shared `extra` moves that local flush point by the same `extra`, and the
outermost line's item ends up exactly where it was before stretching (its far edge already sat on the
container's real edge), while the two inner lines' items visibly shift. Assuming the item's absolute
position tracked its line's own `CrossOffset` directly (ignoring this per-item local flush term) gave
a first-draft expected value 33.33pt short of the real one — caught by running the test, not by
re-reading the arithmetic.

## What was deliberately not done

Grid's own `align-content` (a separate code path, untouched) was not investigated — issue #461 scoped
this to flex only, and grid was not named as affected.

## Evidence

`FlexboxIntegrationTests` (89 tests, all passing) plus the full `Flex`/`Grid`/`Fragmentation` filter
(823 tests, all passing) after the fix, including the two pre-existing single-line `wrap-reverse`
fixtures above with their expected values corrected rather than left broken.
