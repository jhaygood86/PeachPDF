# The wrapped-inline outline union omits atomic-inline descendants (issue #1207)

`OutlineDrawHandler.DrawRegionOutline` unions only the outlined inline's own per-line rectangles —
the ones `FragmentPainter.PaintBoxContent` already collects as outline paint entries.

Chromium feeds one more kind of rectangle into the same union: the border box of every
**atomic-inline descendant** (`<img>`, `inline-block`, inline-level flex/grid, replaced elements).
Where such a descendant is taller than the line box holding it, Chromium's outline bulges around it
and PeachPDF's follows the line box.

Tracked as #1207 rather than prose alone, since CSS Basic User Interface 4 §5.2's "around the border
edge of the element's fragments" is reasonably read to include them.

## Why it was out of scope

The geometry side is already done — `RectilinearRegion.Union` takes arbitrary rectangles and does not
care where they came from. The gap is purely about *what is fed in*, and that lives at the collection
site: including descendants means a fragment-tree descent from the outlined box at the point where
`PaintBoxContent` gathers outline entries, plus a decision about which descendant kinds qualify
(Chromium's rule is atomic inlines only, not every inline descendant), plus the interaction with
stacking-order hoisting for those descendants. That is a paint-walk change, not the geometry change
this work was scoped to.

## Trap for whoever picks it up

Do not reach for `CssBox` geometry to find the descendant rectangles — paint reads the fragment tree
only (see `docs/architecture.md` §6). The rectangles have to come from each descendant's
`BoxFragment`, in the same fragmentainer-local space the line rectangles already use, or the union
will mix two coordinate spaces and the failure will look like a geometry bug rather than a sourcing
one.

Reader-facing note lives in `docs/html-css-support.md` (without the issue number, per the docs rule).
