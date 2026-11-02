# Text drawn through PeachDrawing.Win2D (and HtmlView) is now hinted and weight-corrected by default

**Before.** `Win2dRenderContext` with the default `PeachDrawing` text engine drew each run as a filled vector outline: not fitted to the pixel
grid, and blended as plain coverage. At body sizes it looked lighter and softer than DirectWrite, light-on-dark text the most.

**Now.** `Win2dRenderContext.TextRendering` defaults to `Win2dTextRendering.Auto`. Body text (up to 48 device pixels per em) is hinted, rasterized to
a coverage mask, corrected for the brightness of its colour and drawn on whole device pixels (`Grayscale`). Over a session a host owns (an
`HtmlView`, a `CanvasControl`) it is drawn with ClearType-style colour stripes (`Lcd`) when Windows ClearType is on and the surface is opaque. Large
text, and text that is rotated or skewed, stays a vector outline. A `Win2dComposer`'s bitmap never gets colour fringes by default.

**Why.** It is much closer to what DirectWrite draws, measured over a range of fonts, sizes and colours, without needing DirectWrite.

**If you need the old output.** Set `Win2dRenderContext.TextRendering = Win2dTextRendering.Outline` (or `HtmlView.TextRendering`). Pixels of exported
PNGs change slightly; layout, measurement, links and vector text under transforms do not. A masked run costs several times an outline run.

**Also.** `HtmlView` now sets its surface's clear colour from `PageBackground`, so an opaque page gives an opaque (alpha-less) surface.
