# SVG `transform-origin`, `transform-box`, relative lengths in `translate()`

#1778. Last of the five SVG stylesheet issues; builds on [geometry properties](2026-10-19-svg-geometry-css-properties.md) and [the cascade tiers](2026-10-19-svg-style-cascade-importance-and-invalid-declarations.md).

## What was done

- `SvgElement.Transform` became a property over the parsed matrix plus an optional `SvgTransformOrigin` (`transform-origin` + `transform-box`). The origin is applied when the transform is read, not when it is parsed, because the
  `fill-box`/`stroke-box` of a group is only known once its children are built, and `ApplyCommon` runs before that. Without a `transform-origin` nothing changes: an SVG element's initial origin is the user-space origin, so
  `transform-box` alone does nothing (Chrome agrees).
- `SvgTransformParser.Parse` takes an `SvgTransformContext` (em, rem, reference box) so `translate*()` resolves `em`, `rem` and `%`. A `%` under `fill-box`/`stroke-box` is left unresolved (the box is not known at parse time): the function does nothing.
- `ResolveStyledAttr(node, "transform", SvgTransformParser.IsValidCssTransform)`: an inline `style="transform: rotate(10)"` is invalid CSS and is dropped, falling back to the rule/attribute. The attribute itself still takes bare numbers.
- `gradientTransform`/`patternTransform` pass `allowUnits: false`: a function with a unit does nothing there.

## Found by running it

The 5 existing tests that pinned `em`/`%` as "does nothing" were the old limitation, not a contract; `rem` in a standalone SVG is the `svg` element's own font size (it is the root), not 16px.

## Not done (tracked separately)

3D functions (`translate3d`, `matrix3d`, `rotate3d`, `perspective`), `calc()` reaching the parser from `style=`, the `q` unit and `scale(50%)` in the CSS-OM (shared with HTML, so a CSS-OM change, not an SVG one), `rotate()`/`scale()` independent properties.
