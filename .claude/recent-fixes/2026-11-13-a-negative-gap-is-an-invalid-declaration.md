# A negative `column-gap`, `row-gap` or `gap` is invalid

css-align-3 gives `column-gap`/`row-gap` the grammar `normal | <length-percentage [0,∞]>`; a negative value is dropped, as the negative padding and size values already are (see [negative padding](2026-11-12-a-negative-padding-is-an-invalid-declaration.md)). Found by `multicol-gap-negative-001`: the negative gap was accepted and the columns overlapped.

## How
- `KeywordOrValueGrammar` has a `nonnegative-length` value type (`TryParseLengthOrCalc` plus `IsValidNonNegativeLength`, the same pair the padding entries use); `column-gap` and `row-gap` declare it in `css-properties.json` and the schema lists it.
- The CSS-OM side: `Converters.NonNegativeLengthOrPercentOrNormalConverter` for both longhands and the `gap` shorthand (`GapProperty`), so a negative value in a shorthand also drops the whole declaration.

## Evidence
css-multicol +1, nothing lost in any module; `ANegativeGap_IsInvalid_SoTheEarlierGapApplies` (three declarations); engine, source-generator, PeachPDF and PeachBrackets suites green.

## Not done
Other `[0,∞]` properties (`flex-basis`, `grid-auto-*` track sizes, `outline-width`, `column-width`) were not audited.
