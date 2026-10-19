# CSS animations on the elements of an inline `<svg>` are applied

With `PdfGenerateConfig.AnimationProgress` set, an `animation` on an element of an inline `<svg>` (`rect { animation: pulse 2s linear }`) used to be parsed and ignored: only HTML boxes and their pseudo-elements were
animated, so the element was drawn as its attributes and rules say (`git show v0.9.23:docs/html-css-support.md` listed it as not applied). It is now sampled like any other animation: the element takes the value its
animation has at that point for `fill`, `stroke`, `opacity`, `fill-opacity`, `stroke-opacity`, `stroke-width`, `stroke-dashoffset`, `stroke-dasharray`, `transform`, `stop-color`, `stop-opacity` and the geometry properties (`r`, `cx`, `x`, `width`, ...)
(see docs/supported-svg-features.md#css-animations). A document that animates an SVG and is rendered with `AnimationProgress` changes: the SVG shows the frame, not the resting state. A document rendered without
`AnimationProgress` is unchanged. The elements of a standalone SVG (`<img src="x.svg">`) are still not animated.
