# `initial` on an SVG element is the property's initial value in an attribute, a `style` attribute and a rule

Written as a presentation attribute or in an inline `style` attribute, `initial` on `fill`, `stroke`, `stroke-width`, `stroke-miterlimit`, `fill-rule`, `fill-opacity`, `stroke-opacity`, `stroke-linecap`,
`stroke-linejoin`, `stroke-dasharray`, `stroke-dashoffset`, `writing-mode`, `text-orientation`, `letter-spacing` and `word-spacing` kept the value inherited from the parent (`v0.9.25-preview1`'s `SvgTreeBuilder`
passes the raw keyword to a parser that rejects it, and an invalid value means inherited). So `<g fill="red"><rect style="fill: initial"/></g>` painted red where a browser paints black. `initial` now resolves to
the initial value in both forms.

In a stylesheet rule, `initial` for `writing-mode`, `text-orientation`, `direction`, `letter-spacing` and `word-spacing` also kept the inherited value (the rule path has no initial value for them), and now resolves to
`horizontal-tb`, `mixed`, `ltr`, `0` and `0`. The other properties already did in a rule.

`direction` with a value that is not `ltr` or `rtl` (`direction="bogus"`, and `unset` or `revert` as an attribute or `style` value) was `ltr`; it is now the parent's direction, as for every other inherited property.
