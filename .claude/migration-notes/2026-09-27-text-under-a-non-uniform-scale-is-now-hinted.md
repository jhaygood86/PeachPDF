# Text under a non-uniform scale is now hinted

**Before:** the raster backend fitted text to the pixel grid only when the device transform scaled the horizontal and vertical
directions by the same factor; text drawn under a non-uniform scale (a CSS 3D transform whose projection stretches one axis
more than the other, or a page rendered at a non-square rasterization DPI) always fell back to the plain scaled design outline,
whatever `PdfGenerateConfig.TextHinting` asked for.

**Now:** a font's grid-fitting instructions are applied for a non-square pixel too — the engine fits the outline to a stretched
grid instead of a square one, exactly as FreeType does for a device whose horizontal and vertical `ppem` differ. Text reached
this way is a genuine pixel-level rendering change from before: stems, x-heights and baselines that used to sit at their scaled
design positions now snap to the (stretched) pixel grid, the same improvement uniformly-scaled hinted text already had. Vector
PDF text (the normal case) is unchanged; this affects only the raster backend, and only text under a rotation-free,
skew-free transform whose two axes scale differently — a rotation or a skew still refuses hinting, as before.

One narrower side effect of the same change: the old check that decided whether the two axes counted as "the same scale" was a
tolerance comparison, meant to treat a nominally-square scale whose axes only disagreed by floating-point noise as square. That
tolerance is gone along with the refusal it guarded, so a scale that is square in intent but rounds, per axis, to two different
whole pixels-per-em by the narrowest possible margin now takes the stretched (but still correct) hinting path instead of being
refused outright. This is strictly more text getting hinted, never less, and never a wrong outline for a genuinely square scale.
