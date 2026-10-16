# SVG elements styled from a stylesheet: stops, bare numbers, CSS transforms

First of three steps toward CSS animations on the elements of an inline `<svg>` (#1672: the item "CSS animations on the elements of an inline `<svg>` are not applied"). The animation needs the properties
it animates to survive a stylesheet and a `@keyframes` block at all, and measuring what survived (a probe through `CssParser.ParseStyleSheet` and through the built `SvgDocument`) found that most did not. This step
closes the ones the animation will use; the next steps are the builder-side sampling and the geometry properties.

## What was found by running it

- The CSS-OM drops a declaration it has no property for, silently, in a rule and in a keyframe alike. Dropped: `stop-color`, `stop-opacity`, `r`, `cx`, `cy`, `rx`, `x`, `y`, `d`, `flood-color`, `lighting-color`,
  `paint-order`, `mask`, `marker-*`, `transform-box`; kept: `fill`, `stroke`, `opacity`, `fill-opacity`, `stroke-opacity`, `stroke-width: 6px`, `stroke-dasharray`, `visibility`, `font-size`.
  Unitless `stroke-width: 6` and `stroke-dashoffset: 7` were refused by the length grammar, the form every line-drawing animation uses.
- Registering `stop-color` was not enough: `BuildStops` read `stop-color` from the attribute and the element's own `style=""` and never asked the matched-rules tier, so a rule on a `stop` did nothing. It now goes
  through `ResolveStyledAttr` (style > matched rule > attribute), which is also where the animation tier will sit.
- `SvgTransformParser` read every function through the attribute grammar: numbers only, a trailing unit skipped. `rotate(0.25turn)` was 0.25 degrees; `translateX()` produced no matrix. CSS keyframes and the
  interpolator's own output (`translateX(40px)`, `rotate(1.5rad)`) are in the CSS syntax, so it had to read it. It is one parser for the `transform` attribute, `gradientTransform` and `patternTransform` too, so the CSS forms
  are now accepted there (a superset of a browser's attribute grammar; the review's differential of about 240 strings found no valid attribute string whose result changed beyond float noise: a quarter turn is now exactly 0 and 1).
- A review found, and the commit then fixed: a function name could not carry a digit, so `translate3d(...)` ended the whole list (everything after it was lost, before this change too); an argument that is not a number
  (`calc()`, a nested function) is now skipped to its closing parenthesis and the function does nothing, the rest still applying; a unit on a `matrix()` entry or a rotation centre makes the function unresolvable;
  a quarter turn is exact; `stop-opacity` takes a percentage as Chrome does; `ParseStopColor` lost its `style` parameter, which `ResolveStyledAttr` now settles.

## What was done

`StopColorProperty`/`StopOpacityProperty` and `LengthOrPercentOrNumberConverter` (the non-negative form for `stroke-width`) in the CSS-OM; `BuildStops` through `ResolveStyledAttr`, with `currentColor` the host colour as
`flood-color` does; units on the arguments of `SvgTransformParser` and the single-axis, `skew()` and `rotateZ()` functions. Tests are Chrome's computed values (`SvgStylesheetPropertyTests`, one inline-SVG class,
`StopPropertyTests`); the `svg_stylesheet_properties` showcase renders the same in MuPDF, PDFium and Chrome.

## Not done

- `r`, `cx`, `cy`, `rx`, `ry`, `x`, `y`, `width`, `height`, `d` as CSS properties: the builder reads them from attributes in many places (the geometry step).
- `flood-color`, `flood-opacity`, `lighting-color`, `paint-order`, `mask`, `marker-*`, `clip-rule`, `transform-box` from a stylesheet; `transform-origin`; `em`/percentage lengths, `calc()` and 3D functions in an SVG
  `transform`. A transform read from a stylesheet is also more lenient than CSS (`rotate(10)` with no unit is accepted, Chrome drops that declaration): the CSS-OM already refuses it in a rule, but not in `style=""`.
- A gradient reference set from CSS (`.band { fill: url(#g) }`, `style="fill: url(#g)"`) is drawn in the wrong place or not at all on `main` too (#1776); the showcase uses the attribute.
