# `corner-shape` family (issue 2269)

**Load-bearing idea.** The shape is data on `BorderRadii` (one superellipse parameter K per corner, stored as K - 1 so
`default(BorderRadii)` and every constructor that is not told a shape mean round). `ComputeRadii`/`ComputeInnerRadii`
attach the shape, so every consumer that already took `BorderRadii` gets it. `RenderUtils.GetRoundRect(g, rect, BorderRadii)`
is the one path builder: all-round boxes keep the exact elliptical arcs, anything else goes through `CornerShapePath`.

**Parameter.** n = 2^K in `|u|^n + |v|^n = 1` (round 1, squircle 2, bevel 0, square +inf). A negative K is the mirror of its
positive twin across the corner's chord (scoop = concave ellipse centred on the box corner, notch = concave right angle).

**Found by running it.** Insetting a scoop or notch like a convex corner (shrink the radius by the border width) leaves the
border no thickness along the cut - the first rasterization showed the border vanishing at every concave corner. The inner
contour of a concave corner keeps the cut centred on the *border box's* corner and grows it (`CornerOverhang`,
`AddAnchoredConcaveCorner`). The mitred per-side clip sectors also have to run along the diagonals as deep as the corner
shapes reach, not only the border's own thickness, or the part of the border that follows a cut is clipped away.

**Traps.** `FragmentPaintHarness` boxes sit at the page margin, so assert on extents (max - min), not absolute coordinates.
A single-corner shorthand keeps one value for the radius (`3px`), the two-corner and `corner` forms serialise `h v`.
Logical corners resolve through `LogicalPropertyResolver.Corner` and take part in the same declaration-order tie-break as the
logical edges (`LogicalDeclarationOrder` knows the physical corner names).

**Not done.** See [the accepted gap](../accepted-gaps/corner-shape-follow-ups.md).

**Evidence.** `CornerShapeIntegrationTests`, `CornerShapePropertyTests`; the `corner_shape` showcase rasterized in MuPDF and
PDFium agree; Engine, PeachBrackets, generator suites pass.
