# SVG text: remaining CSS text/font properties

Tracked in [issue #1572](https://github.com/jhaygood86/PeachPDF/issues/1572).

SVG text now honours the font-variant/palette group (`font-palette`, `font-variant-alternates`,
`font-variant-emoji`, `font-variation-settings`, `font-optical-sizing`, numeric `font-weight`,
`oblique <angle>`). Still not wired in `SvgTreeBuilder.ComputeFontContext`/`BuildTextRunCore`:
`font-synthesis*`, `font-size-adjust`, `font-language-override`, the `font`/`font-variant` shorthands,
`font-family` fallbacks past the first family, and the layout-model/painting properties listed in the
issue (`white-space`, `tab-size`, `text-indent`, `text-shadow`, `paint-order`, `dominant-baseline`, ...).
They were left out because each needs SVG-specific layout or paint machinery rather than the
registry/resolver plumbing this change added.

Also: text painted as outlines (gradient/pattern fill, stroke) is drawn in the font's default palette,
since `Canvas.GetTextOutline` has no palette parameter.
