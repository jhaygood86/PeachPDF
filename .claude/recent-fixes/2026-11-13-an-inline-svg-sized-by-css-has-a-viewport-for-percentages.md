# A percentage inside an inline `<svg>` sized only by CSS resolved against nothing

`<svg style="width:200px;height:200px"><rect width="100%" height="100%" fill="green"/></svg>` painted nothing: `SvgTreeBuilder` took the viewport a percentage resolves against from the root's `viewBox` or its `width`/`height` attributes, and an svg sized by CSS has neither, so the viewport was null and `100%` was 0. The WPT css-transforms references (`svg-scale-ref.html` and the dozens that use it) are written exactly this way.

`ISvgSourceNode.CssWidthPx`/`CssHeightPx` (null by default; `CssBoxSvgSourceNode` answers the box's plain-length CSS `width`/`height` in CSS px, not `auto` and not a percentage) are now the last fallback of the root viewport, after the `viewBox` and the attributes. They do **not** set `SvgDocument.Width`/`Height`, which also feed the intrinsic size and aspect ratio (`SvgIntrinsicSize`); a CSS size is not an intrinsic one.

css-transforms 502 -> 540 (+38, no losses); `InlineSvgViewportTests`.

## Not done
A CSS size that is itself a percentage or `auto` (a flex item, `width: 100%` of a sized parent) still leaves the viewport null: the size is only known after layout, and the graphic is built during measurement.
