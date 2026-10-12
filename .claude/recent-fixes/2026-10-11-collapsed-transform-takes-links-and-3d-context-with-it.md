# A collapsed or edge-on transformed element takes its links, widgets and 3D context with it (#1703)

[#1693's fix](2026-10-10-non-invertible-transform-paints-nothing.md) stopped painting an element whose 2D matrix has
no inverse, but four neighbouring paths still looked at something else. All four now agree on "not rendered".

- **Load-bearing idea: the matrix that decides is the full 4x4, and the 2D one decides only the element's own plane.**
  css-transforms-1 §"Transform Rendering" speaks of the transformation matrix. `scaleZ(0)`/`scale3d(1,1,0)`/a
  `matrix3d` with a zero z row are singular in 4x4 while their z=0 projection is the identity; `rotateX(90deg)` is the
  opposite (a rotation, invertible in 4x4, edge-on in 2D). So `FragmentPainter.IsInvertible4` (determinant in double, cut at 1e-18 because a 3-axis determinant is the cube of the scale,
  hand-expanded; a test checks it against `Matrix4x4.GetDeterminant`) is an early-out at the top of `PaintFragment`,
  *before* `TryPaintContext3D`, and the 2D `LeavesVisibleArea` stays for the box's own plane. **Do not cull a
  `preserve-3d` root on its 2D thickness**: `rotateX(90deg)` on a context root is the classic cube-face layout whose
  children stand up out of the edge-on plane.
- **Links and widgets are collected from the box tree, so the painter tells them what it culled.** A first version
  re-derived "no visible area" over the box (4x4, `IsInvertible`, `HasVisibleThickness` on the border box, with an
  `IsProjected` exemption for perspective/`preserve-3d`). Two rounds of review found it wrong in both directions: a
  `height:0` wrapper whose overflow is squashed to nothing kept its link, a 1pt box with thick overflowing content lost
  links over content that was drawn, and an edge-on plane the eye is level with kept a link over blank paper. The painter
  is the only place that knows the subtree extent and the projection, so `HtmlContainerInt.NoteNotRendered(box)` is
  called wherever the painter culls (`PaintFragment`'s 2D guard, `PaintProjective`'s sliver, `ComposeContext`'s sliver)
  and `DomUtils.LeavesNoVisibleArea` is that record plus the one thing answerable without painting - a singular 4x4.
  `GetAllLinkBoxes`, `GetAllFormFieldBoxes` and `GetAllSvgLinks` stop at such a box; `GetAllLinkAndBookmarkBoxes` only
  stops collecting *links* and keeps walking, so a collapsed heading keeps its bookmark (a bookmark names a place in the
  document, not something painted). A `display: contents` link inside a collapsed ancestor is dropped via
  `IsUnderNoVisibleArea`, since the shell has no box in the tree to carry the guard. **The collectors therefore only
  filter once the pages have been painted** - true for `PdfGenerator` (links are collected after the page loop); a host
  calling `HtmlContainer.GetLinks()` on an unpainted container gets only the singular-4x4 case filtered. Multi-page box:
  culled on any page it is painted on, culled for the document.
- **Which culls are recorded:** the 2D guard, the projective sliver, and every other "nothing drawn" return of
  `PaintProjective`/`ComposeContext` (backface hidden, wholly behind the eye). In a 3D context a culled plane is *not*
  recorded while a plane below it is still drawn (`NoteCulledPlanes`, which resolves it bottom-up so a culled child with
  a drawn grandchild counts as live): that child is composed on its own account (a `rotateY(-90deg)` inside a
  `rotateY(90deg)` faces the viewer again), so its links are live; only what is coplanar with the culled plane goes with
  it, and a culled plane whose child planes are all culled too is recorded. The record is made on the box's own `HtmlContainer`, not the painter's, because a running
  element's content is painted by another painter than the one that owns its boxes; `PdfGenerator.FindLinkFragments`
  (running-element links, walked over fragments) asks the same `LeavesNoVisibleArea`.
- **Known edges, accepted:** a culled `preserve-3d` plane that still has a drawn child plane keeps the links in its own coplanar content (the collectors skip a recorded box's whole subtree, which would take the live child's links with it); the record is made where the painter culls, so a collapsed 2D element whose fragment fails
  `PaintFragment`'s `visible` test (clipped away by an ancestor's `overflow`) is never recorded and keeps its link; a box
  split over pages is culled for the document if any page culls it; the entry is never cleared (one layout and paint per
  container). A plane with no extent cannot keep its culled parent alive in practice - a zero-size box has no fragment, so
  it is never a plane - and `NoteCulledPlanes` counts a plane live only if it was composed (`HasSource`).
- **Chrome agrees (measured, headless screenshot):** `scaleZ(0)`, `scale3d(1,1,0)`, a `matrix3d` with a zero z row and
  `scale(0)` paint nothing, as does a `preserve-3d` root with `scaleZ(0)` and a `translateZ(5px) scaleZ(0)` child of a
  `preserve-3d` parent; `scaleZ(0.5)` and `translateZ(5px)` are drawn. A review agent claimed browsers flatten and still
  draw a singular 4x4 - they do not.
- **Edge-on under perspective: the projective warp and the 3D compositor had no thickness cull.** What was emitted for
  `perspective:200pt` + `rotateY(90deg)`: a 0.24pt-wide image XObject and the plane's text again as `3 Tr` under a
  near-singular `cm`. `ProjectsToSliver` clips the plane's image to w > 0 (`Homography.ProjectRectangle`) and compares
  area / diameter with the same 0.017 CSS px as the flat case; `PaintProjective` and `ComposeContext` both ask it, and
  returning before the bitmap also removes the text layer. `rotateY(89.99deg)` of a 100pt box is within rounding of the
  cut, as in the flat case, so the tests use clear-cut angles.
- **Found by running it:** the first version of the edge-on tests rasterized with `RasterCanvas` and counted ink; they
  passed with the cull *disabled* (the software warp draws nothing for a degenerate map). The PDF is where the hairline
  lives, so the tests assert on the emitted image XObjects and `3 Tr`, and each of the three mechanisms was disabled in
  turn to confirm a test fails. In the 3D-context test the context root's own plane is always composed to a bitmap, so it
  compares image counts between the edge-on and a tilted plane rather than asserting zero images.
- **Found in review:** the 4x4 guard in `PaintFragment` only reached a context *root*; `CollectPlanes` also drops a child
  whose 4x4 is singular. The first member test passed without that because the collapsed plane sat behind the red one: put
  it in front (`translateZ(60pt) scaleZ(0)`, scale first) so the test can tell. The 4x4 cut-off is `1e-18`, not the 2D
  `1e-12`: a three-axis determinant is the cube of the scale, so `scale3d(0.00008, ...)` on a very wide element would
  otherwise be culled while `scale(0.00008)` is not; whether a matrix is merely tiny is left to the 2D checks.
- **Not done here:** a link/widget over a *drawn* transformed element still sat at the untransformed rectangle; that was
  [fixed next](2026-10-12-annotations-follow-the-transform-the-painter-drew.md).
- **Evidence:** `CollapsedTransformTests` (78 cases with the running-element file) - 21 of the first 53 failed with the guards disabled, and the sliver cull is covered by
  one test per call site.
