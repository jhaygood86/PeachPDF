# `display: block` images/SVGs no longer follow an ancestor's `text-align`

Previously, a `display: block` `<img>` or inline `<svg>` was horizontally centered (or flushed right)
whenever an ancestor set `text-align: center` (or `right`), even with no `margin-left`/`margin-right:
auto` of its own.

`text-align` now has no effect on a `display: block` replaced element's own position, matching
[css-text-3 §6.1](https://www.w3.org/TR/css-text-3/#text-align-property) (which scopes `text-align` to a
block's inline-level content, never to where a block-level box sits). Such an element now renders flush
to its containing block's start edge by default, exactly like any other block box. To center one
explicitly, use `margin-left: auto; margin-right: auto` — the CSS 2.1 §10.3.3 mechanism — which this
change also fixed for the definite-width case (it previously computed the wrong offset for a wrapped
`display: block` replaced element even when both margins were explicitly `auto`).
