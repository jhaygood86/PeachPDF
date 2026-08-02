# SVG fill-rule/stroke-linecap/stroke-linejoin fall back to a hardcoded value on invalid input, not the inherited value

Tracking issue: [#599](https://github.com/jhaygood86/PeachPDF/issues/599).

Per [SVG 1.1 §11.4](https://www.w3.org/TR/SVG11/painting.html) and CSS Cascade & Inheritance 4 §3, an
invalid value for an inherited presentation property should compute to the property's *inherited*
value, same as any other CSS property. PeachPDF's `fill-rule`, `stroke-linecap`, and `stroke-linejoin`
instead fall back to a hardcoded keyword (`nonzero`/`butt`/`miter` respectively) on an invalid value,
regardless of what the element would otherwise have inherited — a pre-existing deviation, not introduced
by the CSS/SVG property registry generator migration (see CLAUDE.md's generator section) that encoded it
as data instead of scattered logic.

`SvgTreeBuilder.ApplyCommon`'s generated dispatch marks these three properties' `svg.invalidBehavior` as
`"leave-unset"` in `css-properties.json` (which reproduces the historical hardcoded fallback, since
`SvgElement`'s own field constructor defaults already equal each parser's hardcoded default) rather than
`"inherit"` — the value every other invalid-falls-back-to-inherited SVG property
(`stroke-width`/`stroke-miterlimit`/`stroke-dashoffset`/`stroke-dasharray`) correctly uses.

**Deliberately out of scope.** Fixing this means changing the three entries' `svg.invalidBehavior` to
`"inherit"`, updating `SvgTreeBuilder.ApplyCommon`'s corresponding blocks to check `TrySet`'s return
value and fall back to `inherited.X` on failure (the same shape the four inherited-fallback properties
already use), and re-rasterizing affected showcases per this repo's paint-verification convention since
it's a rendering-visible change.
