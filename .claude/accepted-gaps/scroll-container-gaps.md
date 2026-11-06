# Scroll containers: what is not modelled

A box with `overflow: auto | scroll | hidden` of a document shown on a screen clips and scrolls (see [the recent fix](../recent-fixes/2026-11-06-scroll-containers.md)). A paged document has
none: print lets `auto` and `scroll` content run on past its box, which is the long-standing behaviour. These are left out.

- **Input in the WinUI view is the mouse wheel only.** Touch panning, the keyboard (arrow keys, Page Up/Down, Home/End, Space), dragging a scrollbar thumb, and focus scrolling a container
  to show the focused element are not routed to a container, and `IScrollProvider` is not exposed to UI Automation: a container scrolls by the wheel or by `SetScrollPosition`/`ScrollBy`/`ScrollIntoView`
  from the host. The scrollbars are drawn but not interactive.
- **`scroll-behavior: smooth`, `overscroll-behavior`, `scroll-snap-*`, `scrollbar-gutter`, `scrollbar-width` and `scrollbar-color` do nothing.** A scroll is instant, chains to the container around it
  (and then the page) when the inner one cannot go that way, the bars are overlays of a fixed width and colour that take no room from the content, and a scroll does not settle on a snap point.
- **Only `scroll-margin-top` and the root's `scroll-padding-top` exist as properties**, so `ScrollIntoView` keeps room at the top edge only and a scroll container's `scroll-padding` is not read.
  `ScrollIntoView` is `nearest` alignment; there is no `start`/`center`/`end`.
- **`position: sticky` inside a scroll container does not stick** (it keeps its laid-out place, as before); only a box that sticks to the page's own scroll does. A `position: fixed` box is held by the
  viewport and is not carried by a container, and neither clipped by it unless the container forms its containing block.
- **The scrollable overflow area is the border boxes, lines and words of what the container holds, plus the padding at its end.** It does not include the overflow of a nested scroll container (its own box
  counts), box shadows, outlines, or the part of a transformed box that a `transform` moves outside it (the untransformed box counts), and `overflow-clip-margin` is not read.
- **A container's own bounds are in the layout units of the page**; `writing-mode` and `direction` do not flip the axes, so a right-to-left container starts scrolled at its left edge, and a vertical writing mode
  scrolls along y as a horizontal one does.
- **Text selection and find do not scroll a container to show the match**, and a selection can include text that is scrolled out of view.
- **The offset is kept for a box while the document is laid out again, not while it is loaded again** (a reload makes new boxes): a class change through the mutation API that reloads the document puts every
  container back at its start.
