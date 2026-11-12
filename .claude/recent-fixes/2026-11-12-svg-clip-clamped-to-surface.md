# SvgCanvas clamps rectangle clips to its surface (2026-11-12)

**Symptom:** the harness's `--svg` export of a showcase had `viewBox="0 0 595.09 3965.552"` but a `<clipPath>` rect of height 99930.076.
`SvgShowcaseRenderer` lays the document out on a 100,000 pt "endless" sheet and then crops the `SvgComposer` to the end-of-content marker;
`PagePainter` builds the content clip from the layout band, so the clip kept the layout size.

**Fix:** `SvgCanvas.PushClip(Rect)` intersects the rect with the surface (the canvas's initial clip), mapped into current coordinates through the inverse
CTM. Only done when the CTM has no rotation/skew; a clip that misses the surface entirely, or already lies inside it, is written unchanged. The tracked
clip bound (`GetClip`, used for culling) still holds the original rect. `PushClip(GraphicsPath)`/`PushClipExclude` untouched.

**Not done:** cropping in the harness (it cannot reach the painter's clip; `PaintPageRegionAsync` adds a 48 pt bleed so the value would still be inexact).
