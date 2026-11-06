# Animations, transitions and animated images can play on a screen

**Before.** A document never moved: CSS animations were a still frame (`AnimationProgress`, PDF only), `transition` was parsed and never run, and the WinUI `HtmlView` showed the first frame
of an animated GIF or WebP.

**Now.** `HtmlDocumentOptions.AnimationsEnabled` (and `HtmlView.AnimationsEnabled`, off by default) plays them over time; `HtmlDocument.AdvanceAnimations` is the host's per-frame call. Nothing
changes for a document that does not turn it on, and PDF output is unchanged. `transition-delay`, `-duration`, `-property` and `-timing-function` are now read by the render layer
(`CssBox.TransitionDelay` and its siblings exist), so they appear in a box's style snapshot. `PeachDrawing.Core.Image` has `FrameCount`, `GetFrameDuration`, `LoopCount` and `CurrentFrame`; a
`RasterRenderContext` now decodes an animated GIF or WebP into all its frames (it drew the first), so a raster drawing of one still shows frame 0 unless `CurrentFrame` is set.
