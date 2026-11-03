# Column flex items and `fr` grid tracks no longer shrink below their content

An item of an overflowing column flex container used to be squeezed below its content height (its text spilling out of its box); it now
keeps its content height (or its `height` when smaller) and the container overflows, as in browsers. A bare `1fr` grid track used to shrink
below its item's longest unbreakable run; it now stops at that width. `min-height: 0` / `minmax(0, 1fr)` / `overflow` other than `visible`
restore the old behaviour.
