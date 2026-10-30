# `initial` on an SVG element is the property's initial value in an attribute, a `style` attribute and a rule

Written as a presentation attribute or in an inline `style` attribute, `initial` on `fill`, `stroke`, `stroke-width`, `stroke-miterlimit`, `fill-rule`, `fill-opacity`, `stroke-opacity`, `stroke-linecap`,
`stroke-linejoin`, `stroke-dasharray`, `stroke-dashoffset`, `writing-mode`, `text-orientation`, `letter-spacing` and `word-spacing` kept the value inherited from the parent (`v0.9.25-preview1`'s `SvgTreeBuilder`
passes the raw keyword to a parser that rejects it, and an invalid value means inherited). So `<g fill="red"><rect style="fill: initial"/></g>` painted red where a browser paints black. `initial` now resolves to
the initial value in both forms.

In a stylesheet rule, `initial` for `writing-mode`, `text-orientation`, `direction`, `letter-spacing` and `word-spacing` also kept the inherited value (the rule path has no initial value for them), and now resolves to
`horizontal-tb`, `mixed`, `ltr`, `0` and `0`. The other properties already did in a rule.

`direction` with a value that is not `ltr` or `rtl` (`direction="bogus"`, and `unset` or `revert` as an attribute or `style` value) was `ltr`; it is now the parent's direction, as for every other inherited property.

`text-orientation="initial"` as a presentation attribute is now `mixed`. A browser ignores `text-orientation` as an attribute (SVG 2 does not list it as a presentation attribute) and keeps the parent's value; PeachPDF has always read it as an attribute, so `initial` follows the same reading as `upright`.

In an SVG animation, `letter-spacing: normal` and `word-spacing: normal` are the zero length, so a keyframe such as `from { letter-spacing: normal } to { letter-spacing: 20px }`, or a keyframe that leaves out its first or last step on an element whose rule says `initial`, blends smoothly. The first used to jump to the other value at the midpoint (`v0.9.25-preview1` reads `normal` as a keyword the interpolator cannot mix with a length).
