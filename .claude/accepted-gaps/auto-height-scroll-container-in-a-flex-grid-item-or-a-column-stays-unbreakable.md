# An auto-height scroll container in a multi-column container stays unbreakable

An auto-height `overflow: hidden`, `auto` or `scroll` box in ordinary block flow breaks between its lines across pages,
and so does one that is a **flex or grid item** (the classifier reads `HeightBeforeItemPin`, the height the author
wrote, not the one the commit pass pinned, and the box's height is never shrunk below its content, see
`CssLayoutEngine.ApplyHeight`). One kind is left:

- **A scroll container inside a multi-column container** is still monolithic, moved whole to the next fragmentainer
  when it would straddle the edge (`MonolithicContent.IsLaidOutByAnEngineThatCannotContinueIt`). Letting it break was
  tried and measured: an auto-height `overflow: hidden` box holding 12 paragraphs drew 8 of 96 words, and page 1 was
  blank (the box's clip rectangle is applied per fragment while the box spans columns). `overflow: auto` and `scroll`
  lost nothing there; only `hidden` did. A fix means resolving the overflow clip from the finished fragment for a box
  that continues.

A scroll container that holds an absolutely positioned box used to be a third kind. It is not any more: an absolute
box runs as passes of its own and no longer ends its container's, so such a container breaks like any other.

Tracking issue: #1528.
