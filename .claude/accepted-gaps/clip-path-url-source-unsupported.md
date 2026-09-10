# `clip-path: url(#id)` (SVG `<clipPath>` reference) is unsupported

[CSS Masking Module Level 1](https://www.w3.org/TR/css-masking-1/#the-clip-path) lets `clip-path`
reference an SVG `<clipPath>` element by `url(#id)`, using its child shapes as the clip geometry
(as inline `<svg>` content already does via `SvgClipPath`/`SvgRenderer`). The HTML `clip-path`
property only recognizes the CSS `<basic-shape>` functions (`polygon()`/`inset()`/`circle()`/
`ellipse()`/`path()`, see [docs/html-css-support.md](../../docs/html-css-support.md#opacity)) - a
`url(...)` value is not a valid `<basic-shape>` per `BasicShapeGrammar.TryParse`, so it is dropped
at parse time like any other invalid value, and an HTML element can't be clipped to an SVG
`<clipPath>` document fragment. Filed as [issue #217](https://github.com/jhaygood86/PeachPDF/issues/217).
