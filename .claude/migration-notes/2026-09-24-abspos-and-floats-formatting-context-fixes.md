# Two absolutely positioned boxes that landed in the wrong place now sit where they belong

**Before:** an absolutely positioned box was shortened by a `float: left` that *preceded it* as a sibling,
although a positioned box starts a new formatting context that outside floats never reach. Its inline
content was pushed right by the float's width, and when the box was then moved to its `right` offset
the content ended up beyond the page and was culled (a header's cart icon vanished while its badge stayed).

**Now:** the box's lines ignore floats outside it. A float *inside* the box still narrows its lines, as before.

**Before:** an absolutely positioned box nested in a `float`, or in an `inline-block` holding block-level
content, whose containing block was a `position: relative` *inline* (`<span>`) was placed at the sheet's
top-left corner, outside the page margin. In an `inline-block` holding only inline content it could be
placed from the inline-block's left edge rather than from the start of the `<span>`.

**Now:** it is placed against the `<span>`'s fragments like a box directly inside the `<span>`, including
`right`/`bottom` and percentage offsets. A `<span>` that a page break falls inside still resolves `right`/`bottom`
against the last fragment on the page the box is laid out on.

**Also:** an `inside`/`outside` float placed among inline content is now matched on the side it resolves to
for its page, so it shortens the line on that side instead of being ignored.
