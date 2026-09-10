# `clip-path`'s `<geometry-box>` reference box is always the border-box

[CSS Masking Module Level 1](https://www.w3.org/TR/css-masking-1/#the-clip-path) lets a basic shape's
reference box be selected by an optional `<geometry-box>` keyword (`margin-box`, `padding-box`,
`content-box`, `fill-box`, `stroke-box`, `view-box`) alongside the shape function, e.g.
`clip-path: circle(50%) padding-box`. PeachPDF's grammar and resolver (`BasicShapeGrammar`,
`CssClipPathResolver`) don't parse or honor a `<geometry-box>` keyword at all - every basic shape is
always resolved against the element's border-box, regardless of what the author specified. Filed as
[issue #217](https://github.com/jhaygood86/PeachPDF/issues/217).
