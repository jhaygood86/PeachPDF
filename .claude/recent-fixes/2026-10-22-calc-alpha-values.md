# `calc()` in opacity and the SVG `*-opacity` properties (#1817)

Three separate gates each rejected a `calc()` alpha: the CSS-OM `OpacityProperty` converter (`PercentOrNumberConverter` has no calc arm), the generated HTML validator for `opacity` (a hand-written number/percent check), and `SvgValueParsers.TryParseOpacity` (used by fill/stroke/stop/flood-opacity). A rejected declaration is dropped, so the box stayed opaque and a `calc()` keyframe never moved the animation.

Fix: one grammar, `CssValueParser.TryParseAlphaValue` (number, percentage, or a calc-family function that type-checks as Number or Percentage, with 100% = 1), used by the HTML validator, `DerivedStyle.ActualOpacity` and the SVG parser; the CSS-OM converter gains a `CalcValueConverter(Number | Percentage)` arm. Don't add a fourth parser for this grammar.

Trap: a mixed `calc(0.5 + 10%)` does not type-check (number plus percentage) and stays rejected, as in browsers.
