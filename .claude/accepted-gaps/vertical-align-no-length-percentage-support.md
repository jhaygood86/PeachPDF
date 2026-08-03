# `vertical-align` does not support length or percentage values

Tracking issue: [#603](https://github.com/jhaygood86/PeachPDF/issues/603).

Per [CSS 2.1 §10.8.1](https://www.w3.org/TR/CSS21/visudet.html#propdef-vertical-align) / CSS Inline
Layout 3, `vertical-align` accepts a `<length>` or `<percentage>` in addition to its keyword set,
offsetting the box from its own baseline by that amount. `CssLayoutEngine.ApplyVerticalAlignment`
(`Html/Core/Dom/CssLayoutEngine.cs`) only switches on the keyword constants (`sub`, `super`, `top`,
`bottom`, `middle`, `text-top`, `text-bottom`); any other value — including a declared length or
percentage — falls through to the `default` case and is treated identically to `baseline`.

`css-properties.json`'s `vertical-align` entry is already correct: `cssDataType: "keyword"` with only
the 8 supported keywords, no length in the union, so `@supports (vertical-align: 4px)` (and real
dispatch) both correctly reject it rather than silently accepting and then ignoring it.
`docs/html-css-support.md`'s `vertical-align` row previously claimed "length/percentage values — full
support"; corrected to describe the actual keyword-only behavior.

**Deliberately out of scope.** Fixing this means adding a length/percentage branch to
`ApplyVerticalAlignment` that computes the offset (a percentage resolves against the box's own
`line-height`) and applies it from the box's baseline, mirroring the existing `sub`/`super` offset
logic — a real layout feature, not a doc-accuracy fix.
