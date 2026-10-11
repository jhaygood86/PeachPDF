# `transform` no longer applies to a plain inline box

**Before:** `<span style="transform: rotate(45deg)">` (any non-replaced `display: inline` box - `<span>`, `<a>`,
`<em>`, including one that wraps a block) was rotated, scaled or translated like a block, made a stacking context, and became the containing block for
`position: fixed` descendants.

**Now:** the property has no effect on it, as css-transforms-1 §2 specifies (only block-level, atomic inline-level
and table-internal boxes are transformable) and as browsers do: the span paints unrotated, is not a stacking context
because of it, and does not contain fixed descendants. Give the element `display: inline-block` (or `block`) to
transform it. A floated or absolutely positioned element is blockified, an `inline-block`/`inline-flex`/`inline-grid`
box and a replaced element (`<img>`, `<svg>`, `<video>`, `<iframe>`, a form control) are atomic, so none of those change.

`perspective`, `transform-style` and `backface-visibility` follow the same rule (they apply to transformable elements only), and a `<col>`/
`<colgroup>` is not transformable either.
