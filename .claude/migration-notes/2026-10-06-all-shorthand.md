# `all` shorthand, and CSS-wide keywords on six more text properties

_Landed 2026-10-06._

Before: an `all: ...` declaration was dropped as an unknown property, so it reset nothing. Separately, `overflow-wrap`, `word-break`, `line-break`, `text-justify`,
`text-anchor` and `block-ellipsis` rejected the CSS-wide keywords (`overflow-wrap: inherit` was an invalid declaration and was ignored), contradicting the
"every property accepts all five keywords" statement in `docs/html-css-support.md`. Checked against the previous tag: no `all` shorthand and no `all` mention in the docs.

Now: `all: initial | inherit | unset | revert | revert-layer` resets every property except `direction`, `unicode-bidi` and custom properties, in HTML, MathML and SVG
styling, and those six properties accept every CSS-wide keyword. A stylesheet that already contained `all: ...` (previously inert) will start to apply it.
