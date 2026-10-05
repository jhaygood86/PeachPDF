# `overflow: clip` and `overflow-x`/`overflow-y` clip content

Before: `overflow: clip` was ignored, and `overflow-x`/`overflow-y` were not read at all, so a box with
either let its text run past its edge (and a decorated one underlined the overflow).
After: `overflow: clip`, `overflow-x: hidden`/`overflow-y: hidden` and the two-value `overflow` clip to the
box as `overflow: hidden` does. `clip` makes no scroll container, so the box still breaks across pages.
