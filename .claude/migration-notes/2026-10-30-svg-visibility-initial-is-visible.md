# `visibility: initial` on an element of an inline `<svg>` is `visible`

`visibility: initial` on an SVG element inside a hidden ancestor kept the element hidden, whether it was written as a presentation attribute, in an inline `style` attribute or in a stylesheet rule. The attribute and
`style` forms were mapped to the inherited value by `SvgTreeBuilder` (`v0.9.25-preview1` has the same chain at `SvgTreeBuilder.cs` line 1560); the stylesheet form resolved through a registry that holds no SVG initial
value for `visibility`, and "no value" means inherited. It now resolves to `visible` in all three, as in a browser, so `<g visibility="hidden"><rect style="visibility: initial"/></g>` draws the rect.
`inherit`, `unset` and `revert` still take the parent's value, which is what a browser does for an inherited property.
