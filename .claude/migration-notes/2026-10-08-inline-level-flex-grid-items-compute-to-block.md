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
its track (css-grid-2 §6.2), including in a row with an explicit `grid-template-rows` height (where an
inline `<img>` with `justify-self: center` used to paint from the middle of its track). An explicit
`justify-self: stretch` still stretches it; an explicit `align-self: stretch` fills the row's height and
takes its width from that height through the aspect ratio, up to its track. An item with no natural width
(an `<svg>` or SVG image with a `viewBox` but no `width`/`height`) fills its track under any alignment.
This applies to `<img style="display:block">` too.

**One regression is knowingly left:** an absolutely or fixed positioned `<img>`/`<svg>` inside an
`inline-flex` container used to paint (the wrapper hid that an `inline-flex` box never lays out its
out-of-flow children) and no longer does; the same `<div>` was never laid out on any version. See #1665.

Known leftover: an `inline-grid` item's auto width in a flex row is wider than its content (a 2×50pt
grid measured 290pt rather than 100pt, pushing its sibling away); it was also wrong before, differently.

Checked against the 213 TestHarness showcases, rendered in PDFium and MuPDF: every page identical.
