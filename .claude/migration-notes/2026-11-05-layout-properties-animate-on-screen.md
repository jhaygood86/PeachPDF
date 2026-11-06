# Layout properties play in a live animation

**Before.** With `HtmlDocumentOptions.AnimationsEnabled`, an animation of `width`, `height`, `margin`, `padding`, `top`, `font-size` or flex/grid sizing was accepted and did nothing; only paint-only properties
(`transform`, `opacity`, colours, shadows, filters) played.

**Now.** `HtmlDocument.AdvanceAnimations(now, measuringCanvas)` (and `AdvanceAnimationsAsync`) plays them too: a step that changes one lays the document out again, `AnimationFrame.LayoutChanged` says so, and `DirtyRegions`
are where something moved. `HtmlView` uses it, so `AnimationsEnabled` animates layout in the WinUI view. `AdvanceAnimations(now)` without a canvas is unchanged. `AnimationFrame` gained an optional `LayoutChanged`
member. Only a `MediaType.Screen` document without container queries plays layout properties, and each such frame costs a layout of the whole document.
