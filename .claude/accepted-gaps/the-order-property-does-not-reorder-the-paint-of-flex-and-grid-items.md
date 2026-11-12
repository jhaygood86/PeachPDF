# The `order` property does not reorder how flex and grid items paint

Flex and grid items are laid out in order-modified order but painted in document order (`StackingOrder.Flatten` yields a container's children as the
fragment tree lists them), so with overlapping items the wrong one is on top and the page's text is written in document order. CSS Flexbox 1 §4.3 paints
them in order-modified order; §5.4 keeps the DOM order for the logical (tagged) structure, which stays as it is. Found while making every item paint in one
pass (see [the paint-pass fix](../recent-fixes/2026-11-12-flex-and-grid-items-paint-in-one-pass-and-a-reflowed-inline-box-keeps-no-translation.md)).

Tracking issue: #2383.
