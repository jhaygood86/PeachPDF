# `visibility: initial` on an element of an inline `<svg>` is `visible`

`visibility: initial` (as an attribute or in a declaration) on an SVG element inside a hidden ancestor kept the element hidden: `SvgTreeBuilder` treated every value other than `visible`, `hidden` and `collapse` as the
inherited one (`v0.9.25-preview1` has the same chain at `SvgTreeBuilder.cs` line 1560). It now resolves to `visible`, as in a browser, so `<g visibility="hidden"><rect style="visibility: initial"/></g>` draws the rect.
`inherit`, `unset` and `revert` still take the parent's value, which is what a browser does for an inherited property.
