# A negative `padding-*` was applied instead of dropped

Found by the WPT runner (the `padding-top-001`..`-bottom-`/`-left-`/`-right-` families, ~50 tests, all against `ref-no-vert-space-between`): `padding-top: -1px`
shrank the box by a pixel, so a 5px border drew 4px. CSS Box 4 padding is `<length-percentage [0,inf]>`: a negative value makes the declaration invalid, and the
earlier valid one stays in force.

## Fix
`Converters.NonNegativeLengthOrPercentConverter` (`ValueExtensions.ToNonNegativeDistance`, the converter `line-height` already used, plus `calc()`) now backs the
four padding longhands' CSS-OM converter and `padding`'s periodic converter, and their entries in `css-properties.json` use it as a `cssom-grammar` data type, so
the render layer (`CssUtils.SetPropertyValue` through the generated registry) rejects the same values the CSS-OM does. They had been `"cssDataType": "length"`, whose
grammar takes a negative. A negative `calc()` is still accepted (it clamps at computed-value time, which this engine does as it does for other lengths).

## Not done
Other properties with a `[0,inf]` range and a plain `length` data type may accept negatives the same way (`border-width` goes through `Math.Abs` in
`CssValueParser`, which should reject instead of folding the sign). Each is its own entry in `css-properties.json`; this change covers padding only.

## Evidence
css2-flow 1,673 -> 1,710 (+37, none lost across the 16 modules); Engine.Tests 19,835 (one order-dependent table test fails in the full run and passes alone),
PeachPDF.Tests 1,978, PeachBrackets.Tests 1,215.
