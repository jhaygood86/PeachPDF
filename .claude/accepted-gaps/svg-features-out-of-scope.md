# SVG features that are out of scope entirely

SMIL animation, scripting, `<cursor>`/`<view>`, legacy SVG glyph-outline fonts, `<foreignObject>`, `icc-color` in SVG.

`<filter>`/`fe*` primitives are supported: a filter of only PDF-expressible primitives (`feFlood`, `feOffset`, `feMerge`,
`feTile`, the Porter-Duff `feComposite` operators, `feBlend`, a channel-independent `feColorMatrix`, a linear
`feComponentTransfer`) is evaluated over PDF tiles and stays vector; a filter with any other primitive
(`feGaussianBlur`, `feDropShadow`, `feMorphology`, `feConvolveMatrix`, `feTurbulence`, `feDisplacementMap`, the lighting
primitives, `feComposite type="arithmetic"`, cross-channel `feColorMatrix`, non-linear `feComponentTransfer`, or a primitive
subregion) is evaluated over pixels by the raster backend — see [Filters](../../docs/supported-svg-features.md#filters).
Still out of scope: `feImage`, and an `in`/`in2` of `BackgroundImage`/`BackgroundAlpha`/`FillPaint`/`StrokePaint`.
