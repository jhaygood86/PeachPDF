# `font-variant-alternates`/`@font-feature-values` are HTML-only

`font-variant-alternates` and the `@font-feature-values` registry it resolves against apply to HTML
text only (`DerivedStyle`/`CssBox`) — an inline or standalone SVG `<text>`/`<tspan>` does not consult
either. This mirrors `font-palette`/`@font-palette-values`'s own existing scope: that feature is also
HTML-only today, with no `SvgTreeBuilder` wiring, so this isn't a new gap shape, just the same one for
a second registry-backed font feature.

`TextShapingFeatureResolver` — the resolver shared between HTML and SVG for the other `font-variant-*`
longhands (ligatures/caps/numeric/east-asian/position) — is deliberately registry-free: each of its
methods is a pure string-in/typed-out function. `font-variant-alternates` resolution needs the
per-document `@font-feature-values` registry plus the element's *used* font family to look a name up
against, the same two extra inputs `FontPaletteResolver` already needs — which is why it lives in its
own `FontVariantAlternatesResolver` class rather than as a `TextShapingFeatureResolver` method, and why
extending it to SVG means threading the registry into `SvgTreeBuilder` (the same way `HtmlContainerInt`
threads it into `CssBox.FontFeatureValuesRegistry` today) rather than a small addition to the shared
resolver.

Filed as [issue #1293](https://github.com/jhaygood86/PeachPDF/issues/1293).
