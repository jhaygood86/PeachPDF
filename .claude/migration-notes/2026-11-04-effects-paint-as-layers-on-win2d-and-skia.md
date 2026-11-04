# Shadows, filters and backdrop-filter paint as layer effects on Win2D and Skia

**What changed.** On a canvas that applies layer effects itself (`Canvas.HasNativeLayerEffects`: `Win2dCanvas` and `SkiaCanvas`), an outset
blurred `box-shadow`, a CSS `filter` (blur, colour functions, opacity, drop-shadow) and `backdrop-filter` are painted as a layer carrying the
effects instead of being rendered into a CPU bitmap, filtered there and uploaded again. PDF, SVG and the raster canvas are unchanged.

**What a document author would notice.** Nothing at normal sizes; at the edges of a shadow or a filtered box the pixels can differ by a level
or two from the bitmap path (the outset shadow's knock-out under the box is a clip rather than an erase; a backdrop blur holds its edge pixels
instead of mirroring them). Win2D's conic gradient is a pixel shader, so a translucent stop now has its exact alpha (the wedges doubled it at
every seam).

**Verified against** `git show v0.9.26-preview1:docs/peachdrawing-win2d.md`: none of this is documented there.
