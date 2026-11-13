# A negative `padding-*` was applied instead of dropped

Found by the WPT runner (the `padding-top-001`..`-bottom-`/`-left-`/`-right-` families, ~50 tests, all against `ref-no-vert-space-between`): `padding-top: -1px`
shrank the box by a pixel, so a 5px border drew 4px. CSS Box 4 padding is `<length-percentage [0,inf]>`: a negative value makes the declaration invalid, and the
earlier valid one stays in force.

## Fix
`Converters.NonNegativeLengthOrPercentConverter` (`ValueExtensions.ToNonNegativeDistance`, the converter `line-height` already used, plus `calc()`) now backs the
four padding longhands' CSS-OM converter and `padding`'s periodic converter, and their entries in `css-properties.json` use it as a `cssom-grammar` data type, so
the render layer (`CssUtils.SetPropertyValue` through the generated registry) rejects the same values the CSS-OM does. They had been `"cssDataType": "length"`, whose
grammar takes a negative. A negative `calc()` is still accepted (it clamps at computed-value time, which this engine does as it does for other lengths).

## Extended to the size and border-width properties
The same `[0,inf]` rule applies to `width`, `height`, `min-*`/`max-*`, the border, outline, column-rule and text-stroke widths; they too took a negative
(`max-height: -1px` capped the box at zero; a negative border width went through `Math.Abs`). Mechanics:
- New generator data type `nonnegative-length` (`DataTypeKind.NonNegativeLength`, schema enum, `PropertyModelParser`, `ValidatorExpressionBuilder`) calling
  `CssValueParser.IsValidNonNegativeLength`; the 17 entries that were `["length", "keyword"]` use it. The CSS-OM `AutoSizeConverter`/`OptionalSizeConverter`/`SizeConverter`
  are built on the non-negative length-percentage converter.
- `ValueExtensions.ToNonNegativeDistance` accepts a `calc()` that evaluates below zero (css-values-4 section 10.2: clamped when computed).
- **`CalcSerializer` folded a constant `calc(50pt - 200pt)` to the bare text `-66.67px`**, which the stricter check then rejected as a negative literal, so
  `width: calc(...)` that used to clamp to 0 became `auto` (caught by `CalcIntegrationTests.Width_CalcNegativeResult_ClampsToZero`). A negative fold now stays
  `calc(-66.67px)`. Found by bisecting the three edits one at a time: the validator, the CSS-OM converters and the JSON all accepted the calc by themselves.

## Not done
An invalid longhand after a valid shorthand (`border-top: 6pt solid; border-top-width: -4pt`) ends at `medium`, not the shorthand's 6pt as a dropped declaration should;
not traced. Other `[0,inf]` properties on a plain `length` type may still accept negatives (`column-gap`, flex-basis...).

## Evidence
css2-flow 1,673 -> 1,710 (+37) for padding, then 1,720 (+10) for sizes and border widths; none lost across the 16 modules; Engine.Tests 19,835 (one order-dependent table test fails in the full run and passes alone),
PeachPDF.Tests 1,978, PeachBrackets.Tests 1,215.
