# CoreGraphics blur and drop shadow use each axis's own standard deviation

**Before (0.10.0):** `PeachDrawing.CoreGraphics` blurred a layer, and a drop shadow, by the mean of the horizontal and vertical standard deviations, so
`feGaussianBlur stdDeviation="8 1"` (or `filter: blur()` / `drop-shadow()` effects with unequal deviations) came out softer than specified along one axis and
sharper along the other.

**Now:** each axis is blurred by its own deviation, as `PeachDrawing` and `PeachDrawing.Win2D` already do. Core Image only blurs by one radius, so the axis with
the larger deviation is squeezed by the ratio of the two, blurred, and stretched back; an axis with deviation 0 is blurred by at most 1/64 of the other.
Output with equal deviations is unchanged.
