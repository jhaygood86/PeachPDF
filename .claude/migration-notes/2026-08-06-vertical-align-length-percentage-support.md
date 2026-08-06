# `vertical-align` now supports length and percentage values

**Landed:** 2026-08-06 — Fix open alignment issues (#603)
**Doc section:** docs/html-css-support.md § [vertical-align row](../../docs/html-css-support.md#text)

`vertical-align`'s cascade storage (`css-properties.json`'s `vertical-align` entry) was keyword-only —
a `<length>`/`<percentage>` value (CSS 2.1 §10.8.1: "raises (or lowers, if the value is negative) the box
by this distance ... a percentage of the 'line-height' property value") was rejected at the cascade and
the property fell back to its initial `baseline`, silently ignoring the declaration.

`vertical-align` is now converted to a keyword-or-value union (`CssKeywordOrValue<VerticalAlignment,
LengthOrCalc>`, the same mechanism `line-height`/`word-spacing` already use for their own
`<keyword> | <length>` grammars — a percentage is already covered by the shared length resolver, no
separate percentage type is needed), and `CssLayoutEngine.ApplyVerticalAlignment` resolves a length/
percentage value into an offset from the box's own baseline, mirroring the existing `sub`/`super` offset
logic (a percentage resolves against the box's own `line-height`).

A document declaring `vertical-align: <length>` or `vertical-align: <percentage>` (e.g. `vertical-align:
5pt`, `vertical-align: 50%`) will now see the inline-level box actually raised (positive value) or lowered
(negative value) by that distance, instead of the declaration being silently dropped and the box staying
at its baseline position. Table cells are unaffected: `vertical-align` on a table cell uses a separate,
more limited keyword-only algorithm per CSS 2.1 §17.5.3, where a length/percentage has no defined meaning
and continues to be a no-op there, the same as it always was for any other value that algorithm doesn't
recognize.
