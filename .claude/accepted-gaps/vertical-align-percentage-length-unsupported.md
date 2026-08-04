# `vertical-align` percentage/length values are silently rejected

Tracking issue: [#641](https://github.com/jhaygood86/PeachPDF/issues/641).

Per [CSS 2.1 §10.8.1](https://www.w3.org/TR/CSS21/visudet.html#propdef-vertical-align), `vertical-align`
accepts the 8 standard keywords (`baseline`, `sub`, `super`, `top`, `text-top`, `middle`, `bottom`,
`text-bottom`) **or** a `<percentage>`/`<length>` value, raising or lowering the box by that amount
relative to the baseline.

PeachPDF's cascade-time validation for `vertical-align` only recognizes the 8 keywords (`Map.VerticalAlignments`) -
a declaration like `vertical-align: 25%` or `vertical-align: 3px` is silently rejected at the cascade
layer and the property falls back to its initial value `baseline`, with no warning or error. Confirmed:
`<span style="vertical-align:25%">x</span>` lays out identically to no `vertical-align` declared at all.

This predates the typed-storage conversion (`CssProperty<VerticalAlignment>`) that surfaced it while
auditing the property's full spec grammar - the legacy CSS-OM parser (`VerticalAlignProperty`) already has
a `LengthOrPercentConverter.Or(VerticalAlignmentConverter)` grammar and its own tests asserting `3px`/`25%`
parse successfully at that layer, but the value never reaches the box's cascaded `vertical-align`, which is
where this issue actually bites.

**Deliberately out of scope.** Fixing this means wiring `vertical-align` through the
`CssKeywordOrValue<TEnum,TValue>` union mechanism (the same infrastructure `z-index` already uses for
`int | auto`) and adding the actual layout-time raise/lower-by-amount behavior to
`CssLayoutEngine.ApplyVerticalAlignment` - a real feature addition, not a storage-type change.
