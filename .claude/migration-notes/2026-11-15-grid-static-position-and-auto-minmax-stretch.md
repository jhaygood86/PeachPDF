# Static position in a grid, `minmax(<length>, auto)` stretch, grid-lanes repeats

- An absolutely positioned child of a grid container that is **not** its containing block (the grid has `position: static`) used to be aligned inside the grid area its `grid-area`/`grid-column`/`grid-row` named. It is now aligned in the grid's content box, as css-grid-1 9.2 says; the named lines only matter when the grid is the containing block.
- A grid track `minmax(<length>, auto)` stretches to take free space like a bare `auto` track (it did not).
- In `display: grid-lanes`, `repeat(auto-fill|auto-fit, <intrinsic>)` repeats as often as the items' sizes allow (it was always once), `auto-fit` collapses every lane no item ended up in, intrinsic row lanes are as large as the largest auto-placed item, and a definite item moves up under `grid-lanes-pack: dense`.
