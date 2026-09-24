# CSS `perspective` and 3D transforms under perspective render (they used to be ignored)

**Before:** `perspective()` in a `transform` list contributed nothing, and the `perspective`, `perspective-origin` and `backface-visibility`
properties were accepted and dropped, so a card with `transform: rotateY(50deg)` inside a `perspective` container was drawn as a plain
foreshortened parallelogram (or not foreshortened at all), and `backface-visibility: hidden` never hid anything.

**Now:** an element whose transform involves `perspective()`, or that is a direct child of a `perspective` container with a transform that moves
it out of its plane, is drawn in real perspective: painted into a bitmap and warped through the projective map, at `RasterizationDpi`.
`backface-visibility: hidden` hides an element turned away from the viewer (even when its map is affine, as `rotateY(180deg)` is). A plane
parallel to the view plane (`translateZ()` under perspective) is just scaled and stays vector. See
[3D transforms and perspective](../../docs/html-css-support.md#3d-transforms-and-perspective).

What a document author can notice: pages already written for browsers with a perspective stage now look like they do there instead of
flat; such elements are bitmaps (sharp to the raster resolution, larger in the file) with their text still selectable; a document
targeting PDF/A-1 or PDF/X-1a/X-3 that uses them is rejected unless it asks for transparency flattening. `transform-style: preserve-3d`
is still not modelled.

Verified at the previous release tag (v0.9.19): `docs/html-css-support.md` listed `perspective()`, `perspective`, `perspective-origin`
and `backface-visibility` under unsupported CSS features.
