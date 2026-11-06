# Scroll containers: what is not modelled

A box with `overflow: auto | scroll | hidden` of a document shown on a screen clips and scrolls (see [the recent fix](../recent-fixes/2026-11-06-scroll-containers.md)). A paged document has
none: print lets `auto` and `scroll` content run on past its box, which is the long-standing behaviour. These are left out.

- **Input in the WinUI view**: a touch in a view that does not scroll itself (`HtmlViewScrollMode.Disabled`, a view in a list) scrolls the list, not a container; a finger drag does not hand over to the
  page half way (a new gesture does); a key goes to a container only through the focused element, so a container with nothing focusable in it is reached by the wheel, the bars or a finger; and UI
  Automation sees a container only when it has a node in the accessibility tree (a list, a region, a table, a landmark), not a plain `div`. A fling is an exponential slowdown, not the system's own curve.
- **Scrolling properties**: `scrollbar-gutter` does nothing, which is what the specification asks of overlay scrollbars (they take no room, so there is nothing to reserve); `scroll-snap-type` on the root
  does not snap the document's own scroll; `scroll-snap-align` of a box taller than the scrollport aligns by its edge, with none of the spec's "free scrolling inside the box";
  `overscroll-behavior` is read for the routing of a wheel or a finger (`GetScrollContainerAt`) and has no overscroll glow or bounce; `scroll-behavior: smooth` is a fixed 0.3 s ease-out tween, stepped by
  `AdvanceAnimations` (a host that never calls it never sees one finish).
- **Writing modes**: the start side of the content is reachable where it is at the right (right-to-left text, `vertical-rl`) or the bottom, and the scroll offsets there run negative (`ScrollMetrics.MinX`,
  as `scrollLeft` does). A vertical writing mode scrolls its block axis along x, and the keys and the page step still follow the physical axes, not the logical ones.
- **A `position: fixed` box** is held by the viewport and is not carried by a container, and neither clipped by it unless the container forms its containing block.
- **The scrollable overflow area** is the border boxes, lines and words of what the container holds (moved by their transforms), plus the padding at its end. It does not include the overflow of a nested
  scroll container (its own box counts), and `overflow-clip-margin` is not read. Box shadows and outlines are ink overflow, not scrollable overflow, as the specification has it.
- **Selection**: a selection can include text that is scrolled out of view, and a drag that selects past the edge of a container does not scroll it. `ScrollTextIntoView` brings a found range into view, but
  no host control calls it by itself.
- **A reload keeps the offsets by the place of each container in the source**, so a change that adds or removes elements before a container moves its offset to whichever container has the old place.
