# A flex item's block-child text no longer spuriously wraps at certain font sizes

## What changed

A block box that is the sole content of a `display:flex` item (e.g. a plain `<div>` wrapping an
`<h1>`) is now consistently sized to its true max-content width. Before this fix, a rounding
artifact in how that width was computed (an IEEE-754 floating-point addition-order difference
between the intrinsic-width calculation and the real line-layout fit-check, with zero tolerance in
the latter) could size the item a hair narrower than its own text needed, at certain font sizes —
non-monotonically, so some sizes wrapped while others a point or two away did not, for the exact
same markup.

A heading like:
```html
<div style="display:flex"><div><h1 style="margin:0;font-size:32px">JOB SHEET</h1></div></div>
```
could wrap ("JOB" / "SHEET" on two lines) at the affected sizes; it now stays on one line, as
Chrome and other browsers already render it.

This only affected a flex item whose own children include a block box (not an item containing
inline text directly, which already had the equivalent protection) - a narrow, specific layout
shape. Flex items sized correctly before this fix continue to size identically.
