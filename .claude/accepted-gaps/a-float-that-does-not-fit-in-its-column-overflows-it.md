# A float among inline content that does not fit below the content in its column overflows it (#1341)

A block-level float child of a multi-column container (or of a block inside it) that does not fit moves to the top of the next column
([the fix](../recent-fixes/2026-11-03-a-float-that-does-not-fit-its-column-moves-to-the-next.md)). Two shapes are not covered:

- **A float written among inline content** (`<p>text<span style="float:right">...</span>text</p>`) is placed by the inline flow
  (`CssLayoutEngine.FlowFloatChild`), not by the block child loop that defers floats, so it stays at the foot of its column and overflows it.
  Deferring it means carrying the float over a break inside the inline flow, which is the substance of float fragmentation proper.
- **Column balancing does not count a float's space before it is moved.** Balancing grows the trial column height until the content fits, so a
  float that is deferred leaves its column shorter than the estimate expected rather than the estimate reserving room for it.
