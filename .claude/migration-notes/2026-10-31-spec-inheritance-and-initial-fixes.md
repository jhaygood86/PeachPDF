# Inheritance and initial-value fixes found while generating the CSS-OM properties

Before: the CSS-OM `Property` class of a longhand and its `css-properties.json` entry each declared whether it is
inherited, and they disagreed. After: the CSS-OM classes of 170 longhands are generated from the JSON, and a drift test
(`CssomInheritanceDriftTests`) holds the 92 hand-written ones to it.

User-visible:

- `text-underline-offset` is now inherited (css-text-decor-4 section 2.8, Inherited: yes). Before, a child of an element
  with `text-underline-offset: 5pt` underlined at `auto`.
- `min-height` now has the computed initial value `auto` (css-sizing-3), where it was `0`. Layout is unchanged: `auto`
  resolves to 0 for a block and the flex automatic minimum is handled by the flex engine.
- In the CSS-OM (what `getPropertyValue`-style inspection and the `inherit` keyword of an invalid declaration see),
  these longhands are now inherited, as their specs say: `caption-side`, `image-orientation`, `image-rendering`,
  `block-ellipsis`, `line-break`, `word-break`, `text-align-last`, `text-anchor`, `text-justify` and the SVG paint
  properties `fill`, `fill-rule`, `fill-opacity`, `stroke`, `stroke-width`, `stroke-opacity`, `stroke-miterlimit`,
  `stroke-dasharray`, `stroke-dashoffset`, `stroke-linecap`, `stroke-linejoin`. The render cascade already inherited
  all of these; only the CSS-OM flag was wrong.
