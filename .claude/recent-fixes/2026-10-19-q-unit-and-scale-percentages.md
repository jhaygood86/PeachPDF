# `Q` unit and percentages in scale()

#1791 (part). `Length.Unit.Q` (1/40 cm) is parsed case-insensitively and converted like the other absolute units, in the CSS-OM, `calc()` and the SVG length/transform parsers. `scale()`, `scaleX/Y/Z()` and `scale3d()` take a `<percentage>` that is the same number (CSS Transforms 2), in the CSS-OM converter, `CssValueParser.ParseTransform` (`ScaleArg`) and `SvgTransformParser` (`Factor`).

# (continued) calc() and boxed percentages

- `SvgTransformParser.TryReadFunction` now reads each argument separately; a `calc()` is kept as written (`calc:` unit prefix) and evaluated by `Length()` through `SvgValueParsers.ParseLength` with the element's font basis (`SvgTransformContext.Basis`). It is read only when the transform came from `style=` or a rule (`allowCalc`), because the attribute grammar is numbers only. A calc() the function does not read (an extra argument) still makes it do nothing, so `calcRead == calcArgs` is checked at the end of `BuildMatrix`.
- A percentage under `transform-box: fill-box|stroke-box` cannot be folded when the builder reads the transform (the geometry is not built yet), so `SvgElement.DeferredTransform` keeps the text and `Transform` re-parses it against `SvgTransformOrigin.ReferenceBox` on each read, the same lazy shape the origin already uses.

## Not done
3D functions on an SVG element: an accepted gap, see the gap file for 3D transform functions on an SVG element.
