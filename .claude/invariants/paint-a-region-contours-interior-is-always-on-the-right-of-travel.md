# A region contour's interior is always on the right of travel

`RectilinearRegion.Union` emits every boundary edge directed so that the **covered** side is on the
right of the direction of travel. Outer contours therefore come out clockwise and holes
counter-clockwise, and `IsClockwise` is a consequence of that rule rather than an independent step.

Everything downstream depends on it and none of it re-derives it:

- `RectilinearRegion.Shrink` insets a corner by the sum of its two adjacent edges' **inward** normals,
  and "inward" is computed purely from travel direction. Reverse a contour's winding and `Shrink`
  silently *expands* it — the region grows instead of shrinking, with no error anywhere.
- `OutlineRegionPainter.DescribeCorner` classifies a corner as convex or concave from the sign of the
  turn, which is only meaningful relative to a known winding. The convex/concave distinction is what
  keeps a band a constant thickness around a corner (a convex corner shrinks with inset, a concave
  one grows), so flipping winding turns a uniform band into one that pinches at every corner.
- `OutlineRegionPainter.FillBand` relies on a contour and its inset copy having **opposite** winding
  so the interior cancels under nonzero fill. Emit both the same way round and the band fills solid.

## The trap

The failure mode is not an exception — it is a shape that is wrong in a way that looks like a
different bug. A reversed hole renders as a filled blob; a reversed outer contour renders inside-out
or as nothing at all. Neither points back at winding.

If you need to walk a contour backwards (`EmitSegments` does, for the inset side of a band), **reverse
the emitted segments, not the points before classifying corners** — corner classification reads
direction of travel, so reversing the input points first inverts every convex/concave decision before
it is made.
