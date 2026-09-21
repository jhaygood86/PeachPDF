# Auto margins centre in the containing block's content box; a `border-box` width floors at its own edges

Two block-width fixes that a plain `<div>` shows as well as an `<hr>`. Both are new relative to v0.9.19
(`CssLayoutEngine.ResolveAutoHorizontalMargin` read `ContainingBlock.Size.Width` and `GetBoxWidth` had no
floor there).

## Centring inside a `box-sizing: border-box` container

`margin: 0 auto` on a box with a definite width used to split the containing block's **border box** and place
the result from its **content** edge, so inside a `border-box` container with padding or a border the box sat
toward the end edge by half of that padding and border. It now centres in the container's content width, as
CSS 2.1 §10.3.3 states. Nothing changes inside a `content-box` container, where the two widths coincide.
A percentage margin (`margin-left: 10%`) and a table's `margin: auto` centring use the same basis and moved
with it.

## A width that cannot fit its own padding and border

An auto-width box in a container with no content width to give (a `width: 0` parent, or one narrower than the
box's own padding and border) used to resolve to a used width of 0 under `border-box`, and to a negative
content width under `content-box`, discarding its own padding and border either way. It now floors at them: a
`border-box` box is at least as wide as its padding and border, and a `content-box` box's content is at least 0.
A declared `border-box` width smaller than the box's own edges is floored the same way.
