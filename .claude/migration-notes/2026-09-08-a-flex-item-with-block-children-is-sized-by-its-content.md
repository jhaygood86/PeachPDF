# A flex item whose children are blocks is now sized by its content, not by its container

Previously, a flex item whose children were block-level boxes measured as wide as the flex
container itself. An auto-width block child fills its containing block during layout, so reading
the item's laid-out width reported the container's width rather than the item's own content — every
item on the line measured the full container, `justify-content` was left with no free space to
distribute, and the line rendered as an even split however narrow the items' content actually was.
A `space-between` row of two short items landed each one on half the page instead of hugging its
own edge.

Such an item is now measured by its own intrinsic content, per css-flexbox-1 §9.2 — so
`justify-content` (`space-between`, `space-around`, `flex-end`, `center`) has real free space to
distribute and positions the items where the property asks for.

Two narrower changes ride along in the same measurement:

- White space at the end of a wrapped line no longer counts toward an item's measure
  (css-text-3 §4.1.2 — it hangs). A wrapped item previously measured one word space wider than it
  drew, which could over-subscribe the line and make `flex-shrink` narrow a sibling that had room.
- An item whose own min-content is wider than the line now keeps its min-content width and
  overflows, rather than being sized below what it can draw. This is CSS 2.1 §10.3.5's
  shrink-to-fit, `min(max(min-content, available), max-content)`, where min-content is the lower
  bound.

See [Flexbox](../../docs/html-css-support.md#flexbox) in `docs/html-css-support.md`.
