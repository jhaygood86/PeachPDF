# `font-weight`: inline-style parsing rejects non-hundred integer weights

Tracking issue: [#655](https://github.com/jhaygood86/PeachPDF/issues/655).

Per [CSS Fonts Level 4](https://www.w3.org/TR/css-fonts-4/#font-weight-prop), `font-weight`'s
`<font-weight-absolute>` grammar is `normal | bold | <number [1,1000]>` - any number in that range,
not just multiples of 100.

PeachPDF's Layer A CSS-OM parser (`FontWeightProperty`'s `WeightIntegerConverter`, backed by
`ValueExtensions.IsWeight`) only accepts exact multiples of 100 in `[100,900]` - the legacy CSS2.1 set.
An inline style like `font-weight: 550` is rejected at parse time and the whole declaration is dropped
before it ever reaches `CssBox`'s cascaded style.

This is inconsistent with the `CssBox`/generated-registry side, which deliberately keeps `font-weight`'s
integer grammar unconstrained (see `css-properties.json`'s `font-weight` entry) since
`FontWeightResolver`/`FontResolver.PickNearestWeight` already handle arbitrary integers via
`int.TryParse` - only the Layer A inline-style/stylesheet parsing path has this narrower restriction.

**Deliberately out of scope** of the #598 typed-storage conversion that found it (a
`CssKeywordOrValue<FontWeightKeyword, int>` storage-shape change, not a Layer A grammar change) -
fixing this means relaxing `ValueExtensions.IsWeight`, a change to the CSS-OM parsing layer unrelated to
how `CssBox` stores the resolved value.
