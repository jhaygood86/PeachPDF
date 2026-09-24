# Absolutely positioned boxes are no longer clipped by a non-positioned `overflow: hidden` ancestor

**Before:** a `position: absolute` element was clipped by *any* `overflow: hidden` ancestor block, even one
that sits between it and its positioned containing block (e.g. `relative > overflow:hidden div > absolute`).

**Now:** as in browsers (CSS Overflow 3 §3), it is clipped only by `overflow: hidden` boxes on its containing
block chain: its nearest positioned ancestor and that box's own containing blocks. A `transform` other
than `none` (identity ones such as `translateZ(0)` or `scale(1)` included), `perspective`, `filter` or
`backdrop-filter` on the `overflow: hidden` box also puts it on that chain. To
keep the old clipping, make the `overflow: hidden` element `position: relative`.

The same goes for `position: fixed`, which was previously clipped by the nearest `overflow: hidden` block
ancestor. It now escapes every `overflow` box, unless an ancestor with `transform`/`perspective`/`filter`/
`backdrop-filter` forms its containing block.
