# A vertical-mode float's reach is relative to the box that placed it

`CssBox.VerticalFloatOccupancy` says how far a float reaches along the inline axis (physical Y), measured from the top
or bottom edge **of the box that placed it**, and carries that box's top and (when its height is definite) bottom edge.
`VerticalFloatReach.GrowInsets` is the only place that turns it into an inset for a reader, and it does so against the
reader's own top and bottom.

**Why.** A box that reads a float is often not the one that placed it: a paragraph beside a float in a wrapper has its own
edges (its padding, its own height). Using the placer's offset as the reader's read a float pinned to the bottom of a
200pt wrapper as 60pt of a 100pt paragraph that ended 40pt above the float, and narrowed the paragraph's columns to
40pt. It also misplaced a float nested inside a preceding float.

**What must hold.**

- The placer's inline extent comes from `CssLayoutEngine.DefiniteContentHeight` when its height is definite, never from
  `WritingModeFrame.LogicalContentWidth`/`ClientBottom`, which read 0 until the epilogue.
- An auto-height placer has no known bottom edge yet; the reach then falls back to the reader's own bottom, which is
  right when the reader stretches to the placer's edge and is the documented approximation otherwise.
- A column is beside a float when its leading edge is in the float's block range, **half-open at the float's block-end
  edge**; using a closed range gave a column starting exactly where a float ends an inset it had no reason to have.
