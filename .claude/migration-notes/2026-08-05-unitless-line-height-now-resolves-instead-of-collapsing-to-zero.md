# A bare unitless `line-height` (e.g. `line-height: 1.5`) now resolves correctly instead of collapsing to 0

**Landed:** 2026-08-05 — Convert `line-height` to typed keyword-or-value storage (#598 follow-up)
**Doc section:** docs/html-css-support.md § [line-height row](../../docs/html-css-support.md)

`line-height`'s grammar is `normal | <number> | <length-percentage>` (CSS Inline Layout 3 §4). Before
this change, a bare unitless number (`line-height: 1.5`) validated as CSS-legal, but the string-based
resolver (`CssValueParser.ParseLength`) had no case for a number with no unit suffix — it fell through to
`Length.Unit.None`, which `Length.ToPixels` resolves to 0. Every real-world unitless `line-height`
declaration therefore silently collapsed the element's used line height to 0, rather than the spec's
"number × the element's own font size" (CSS2.1 §10.8.1).

Converting `line-height` to typed `CssProperty<CssKeywordOrValue<NormalKeyword, LengthOrUnitless>>`
storage required a real resolution path for the bare-number case as part of building the new
`LengthOrUnitless` union type — there was no way to store a validated-but-unhandled state the way the old
string field could. `LengthOrUnitless`'s `Unitless` side now resolves to `Unitless × box.GetEmHeight()`
(the box's own resolved font size), matching spec.

A document that declared a unitless `line-height` (a common authoring pattern, e.g. `line-height: 1.5`)
will now see visibly taller line boxes/leading than before, matching browsers, instead of lines packed at
their content height with no extra leading.
