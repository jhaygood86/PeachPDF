# SVG features that are out of scope entirely

SMIL animation, scripting, `<cursor>`/`<view>`, legacy SVG glyph-outline fonts, `<foreignObject>`, `icc-color` in SVG.

`<filter>`/`fe*` primitives are no longer entirely out of scope — `feFlood`, `feOffset`, `feMerge`,
`feTile`, `feComposite` (except `type="arithmetic"`), `feBlend`, `feColorMatrix` (`type="matrix"`
restricted to a channel-independent/diagonal matrix, and `type="luminanceToAlpha"`), and
`feComponentTransfer` (`type="linear"` only) are supported, via a real primitive graph evaluated over
PDF tiles — see [Filters](../../docs/supported-svg-features.md#filters). Still out of scope, each for
its own reason: `feGaussianBlur`, `feMorphology`, `feTurbulence`, `feDisplacementMap`,
`feConvolveMatrix`, `feDiffuseLighting`/`feSpecularLighting` and their light-source elements, `feImage`
(all rasterization-only — no PDF operator equivalent); `feComposite type="arithmetic"` (needs true
per-pixel computation); `feColorMatrix type="saturate"`/`type="hueRotate"` and any `type="matrix"` with
a nonzero off-diagonal term, plus `feComponentTransfer type="gamma"`/`"table"`/`"discrete"` and a
non-identity `feFuncA` (see
[css-filter-cross-channel-functions-not-representable.md](css-filter-cross-channel-functions-not-representable.md)
for the cross-channel reasoning shared with CSS `filter`); an `in`/`in2` of `BackgroundImage`/
`BackgroundAlpha`/`FillPaint`/`StrokePaint`; and per-primitive `x`/`y`/`width`/`height` subregions.
