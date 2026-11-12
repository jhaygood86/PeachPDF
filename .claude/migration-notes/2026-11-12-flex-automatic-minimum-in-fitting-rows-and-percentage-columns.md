# Flex items keep their automatic minimum in a row that fits and under a percentage height

**Before.** In a flex row whose items fit and none of which grew, an item with a `flex-basis` of 0 (or a width shrunk to
nothing) stayed at that size with its content drawn past its box. In a column container with a definite height, an item with
a percentage `height` kept its whole content height as its minimum, so its siblings were squeezed to make room.

**Now.** The row item is raised to its `min-width: auto` size (its min-content width, or its `width` when smaller) and the other
items give the room back, as in browsers. In a column with a definite height the percentage `height` caps the item's `min-height: auto`
like a length does. `min-width: 0` and scroll containers still shrink freely.

Closes #2329 and #2332.
