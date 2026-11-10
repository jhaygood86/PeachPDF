# A flex item raised to its minimum width no longer pushes the line past its container

In a row where the items' flex base sizes left free space, but one growing item (for example `flex: 2`, basis 0) held a word wider than its share, that item was widened to the word and the line ended up wider than its container, so the item and its word were drawn past the container's right edge, possibly off the page. The item now keeps the word's width (its automatic minimum size, CSS Flexbox 1 §4.5) and the other items of the line shrink to make room, as a browser does. A line whose items still fit once the minimum is applied is laid out as before.

`flex: none` now means `0 0 auto`, as specified: the item no longer shrinks (its `flex-shrink` was left at 1). A `flex: none` item on a line that overflows keeps its size and the other items give up the room.

A wrapping row now breaks its lines on each item's size clamped by its automatic minimum (a `flex: 1 1 0` item holding a long word takes that word's width), so an item that would not fit a line once raised to its minimum starts the next line instead of sharing a line it cannot fit.

A block with a definite `width` now contributes exactly that width to its container's shrink-to-fit size, however wide its text is (the text wraps inside it). Before, the width was only a minimum, so a box such as a `flex: none` item, a float or an inline-block holding `width: 230px` blocks with longer text could come out wider than 230px. Tables, table cells and replaced boxes are unchanged.
