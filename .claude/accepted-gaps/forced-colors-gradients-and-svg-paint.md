# Forced colors does not drop gradient backgrounds or recolour SVG paint

Tracked in issue 2097. With `UserPreferences.ForcedColors` the cascade's `Actual*Color` getters return palette colours and shadows are skipped (`ForcedColorsAdjuster`), but CSS Color Adjust 1 section 3.1 also drops `background-image` gradients and recolours SVG `fill`/`stroke`/`stop-color`. Neither is done.

**Why not:** gradients and SVG paint are not read through `DerivedStyle.Actual*Color`, the single seam the first change hooks into; each needs its own override at its own painter, and neither is needed for text to be readable in the common case (a gradient behind text is rare in a page that a high contrast reader opens).
