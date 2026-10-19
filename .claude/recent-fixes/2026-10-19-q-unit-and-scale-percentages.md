# `Q` unit and percentages in scale()

#1791 (part). `Length.Unit.Q` (1/40 cm) is parsed case-insensitively and converted like the other absolute units, in the CSS-OM, `calc()` and the SVG length/transform parsers. `scale()`, `scaleX/Y/Z()` and `scale3d()` take a `<percentage>` that is the same number (CSS Transforms 2), in the CSS-OM converter, `CssValueParser.ParseTransform` (`ScaleArg`) and `SvgTransformParser` (`Factor`).

## Not done (rest of #1791)
- `calc()` in a `style=` transform on an SVG element: `SvgTransformParser.TryReadFunction` skips a non-number argument and the function does nothing. The `transform` attribute must keep refusing `calc()` (its grammar is numbers only).
- `translate(<percentage>)` with `transform-box: fill-box|stroke-box`: the reference box is only known at paint time, so the percentage cannot be folded when the builder reads the transform.
- 3D functions on an SVG element do nothing; CSS Transforms 2 §11 asks for real 3D there (not a 2D projection), so this needs the HTML 3D/perspective machinery pointed at an SVG element.
- The legacy `CssLength` parser (page-box lengths) does not know `Q`.
