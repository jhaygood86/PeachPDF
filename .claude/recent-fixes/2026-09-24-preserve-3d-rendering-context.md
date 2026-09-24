# `transform-style: preserve-3d`: a 3D rendering context of depth-tested planes

Builds on [the raster backend's perspective warp](2026-09-24-raster-backend-svg-filters-backdrop-flatten-perspective-bitmap-glyphs.md).
What was load-bearing, and what was only found by running it.

**One accumulated matrix per plane, not one warp per element.** The old path warped each element by *its own* transform under its *parent's*
perspective, so a grandchild never saw a perspective and a child of a rotated parent did not rotate with it. A context instead accumulates
`Acc(C) = N(C) · Persp(parent) · Acc(parent)` (page coordinates, row vectors) down the tree and warps every plane through its own full matrix
(`FragmentPainter.Preserve3D.cs`). That single change fixes both halves of the issue.

**The depth of a plane is affine in screen coordinates.** The obvious depth test computes `z / w` per pixel from the plane point. It is
simpler than that: the inverse homography's raw output at a screen point is `(u, v, 1) / W'`, so `z' / W'` collapses to
`Za·u + Zb·v + Zc·w` of that raw output, affine in the screen position. Per row that is `z0 + zs·x`, with no division, and it lets the depth
test run four pixels per `Vector128<float>` (`PixelKernels.DepthTestRow`). This relies on `Homography.Invert` being the true inverse (scaled
by `1/det`), not the adjugate; a comment in `Warp.Composite` says so.

**Painting one plane without its siblings: an exclusion set, not `_ownOnly`.** A plane must paint its own content *and* its flat descendants
but not the other members. `_ownOnly` (used by the flatten repaint) skips *all* descendants, so it cannot be reused. Instead every member is
put in `FragmentPainter._contextMembers` and `PaintFragment` returns immediately for a fragment in it; `SubtreeExtent`/`SubtreeBleed` take the
same set so a plane's bitmap does not span its children, and `PaintSelectableText` hands it to the text overlay painter. Identity is enough
because *everything* is painted through `PaintFragment`, hoisted positioned descendants included. The set uses `ReferenceEqualityComparer`:
`BoxFragment` is a record and its value equality would walk the whole subtree.

**A context is entered more than once.** A stacking context is discovered by more than one ordering scope (the page root's hoist search and
the positioned stage above it), and `_painted` only guards `PaintTagged`. The first version therefore drew the context's bitmap twice (two
identical `Do` operators in the PDF; the second painted an empty plane because `PaintTagged` had already recorded it). `TryPaintContext3D`
returns early for a root already in `_painted`. Found by a PDF-level placement-count test, not by any pixel test.

**Fast path, so nothing regresses.** A context whose planes are all parallel to the view plane at one depth with no divisor
(`M13, M23, M14, M24, M43 ≈ 0`, `M44 ≈ 1`, none hidden) has nothing for a depth test to change, and is painted by the old per-element vector
path. So a page that sets `preserve-3d` for a browser but only uses 2D transforms stays vector, and a context with a single plane is left
to the single-element path.

**Vectorization and allocations (asked for explicitly).** `PixelKernels.Warp.cs`: the bilinear texel blend is one `Vector128<int>` per texel
(the four channels), and the depth test is a row kernel; both have scalar references and equality tests. The blend reuses the existing
`PixelKernels.BlendSpan`. `Warp.Apply` was refactored onto the same row sampler, which removed its per-call `int[4]`. `Homography` gained
`TryProjectRectangleBounds` (stackalloc, no lists), `SubtreeExtent`/`SubtreeBleed` lost their capturing closures, and the plane list, the
exclusion set and the depth buffer are reused or pooled. `Warp.Composite` is guarded by an allocation test (steady state, at both the
stackalloc and the pooled row-buffer widths).

Measured in Release, tiered compilation off, AVX2 machine:

| kernel | scalar | Vector128 |
|---|---|---|
| `BilinearTexels`, 4 M samples | 38.5 ms | 14.1 ms (2.7x) |
| `DepthTestRow`, 4 M pixels (two array fills included in both) | 3.8 ms | 1.8 ms (2.1x) |
| `Warp.Apply` 600x600 to 800x700, perspective | 57 ms before | 40 ms after (1.45x) |
| `Warp.Composite`, same map | | 28 ms |

No `Avx2` variant: the per-pixel coordinate mathematics, not the texel blend, dominates `Warp`, so a wider texel kernel would not clear the
1.3x bar the rest of the raster code uses.

**Deliberately not done.** Translucent pixels do not write depth, so planes are ordered back to front by their centres and a translucent
plane that itself intersects another can blend in the wrong order (documented as an approximation). Text overlays use the transform's
linearisation at each plane's centre, as for a single warped element. A warped context inside a transformed ancestor is warped in the
ancestor's untransformed space (a pre-existing limitation). `mask` and `isolation` are not implemented properties, so they are not among the
grouping properties that force `flat`.

**Evidence.** New tests: `Preserve3DTests` (painter and PDF-level tests over pixels: depth beats document order in either order, crossing
planes per pixel in both orders, a perspective reaching a grandchild, a cube, hidden back faces, a flip card, translucency, the grouping
properties, the vector fast path, a raster-less fallback, nested contexts, planes behind the eye), `WarpKernelsTests`, `WarpCompositeTests`.
The showcase `preserve_3d_rendering_context` was rasterized with PDFium and MuPDF and the two agree; the existing perspective showcase is
unchanged.
