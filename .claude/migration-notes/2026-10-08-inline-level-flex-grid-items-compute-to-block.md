# Inline-level flex/grid items compute to their block-level display

Before: an in-flow child of a flex or grid container declared `inline`, `inline-block`, `inline-flex`,
`inline-grid` or `inline-table` (or left at the default `inline`, as a `<span>` or `<img>` is) kept that
computed `display`; only table-internal displays were blockified.

Now: such an item computes to `block`, `block`, `flex`, `grid`, `table` respectively (CSS Display 3
§2.7).

**Rendered output changes for one shape: a nested `inline-flex`, `inline-grid` or `inline-table` that is
itself an item of a flex/grid container.** Its own children used to be laid out at the origin with no
size, so its `gap` and `align-items`, its grid tracks and its table cells were not applied (an
`inline-table`'s cells did not paint at all). They now lay out as a flex/grid/table container.
Two more shapes change: an `position: absolute`/`fixed` `<img>` that is a direct child of a flex/grid
container no longer takes part in the flex line (it used to push its sibling along and stretch the
container), and an `inline` item that contains a block (`<span>pre<div>b</div>post</span>`) now stacks
its rows instead of overlapping them.
Plain `display: inline` / `inline-block` items, in-flow replaced items (`<img>`, `<svg>`, `<iframe>`,
`<video>`, `<object>`), form controls, floated items, `<br>` between items and `display: contents` text
lay out as before.

A replaced grid item (`<img>`, inline `<svg>`) now keeps its natural size under the default
`justify-self`/`align-self: normal`, and under `start`/`center`/`end`, instead of being stretched across
its track (css-grid-2 §6.2). An explicit `stretch` still stretches it, and so does an item with no natural
size in that axis (an `<svg>` or SVG image with a `viewBox` but no `width`/`height`). This applies to
`<img style="display:block">` too.

Known leftover: an `inline-grid` item's auto width in a flex row is wider than its content (a 2×50pt
grid measured 290pt rather than 100pt, pushing its sibling away); it was also wrong before, differently.

Checked against the 213 TestHarness showcases, rendered in PDFium and MuPDF: every page identical.
