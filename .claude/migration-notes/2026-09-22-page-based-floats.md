# `float: top/bottom/top-bottom/snap/inside/outside` now float to the page instead of falling back to `none`

Before this change, `float`'s grammar only recognized `left`/`right`/`none`/`footnote` — any other
value (including `top`, `bottom`, `top-bottom`, `snap`, `inside`, `outside`) failed to parse and fell
back to the property's initial value, `none`, per `css-properties.json`'s `enum-keyword` fallback. A
document declaring `float: top` (perhaps written against another renderer, such as Prince XML, which
already supports this keyword set) previously rendered that element as an ordinary in-flow block.

Now these six keywords parse as real CSS Page Floats values (issue #699):

- `top`/`bottom` remove the element from normal flow and reserve room at the top/bottom edge of
  whichever page its source position lands on, shrinking the usable content band for the flow content
  around it on that page.
- `top-bottom` tries `top`, falling back to `bottom` when the float doesn't fit at the top.
- `snap` floats to whichever edge (top or bottom) is nearer to the element's own natural position.
- `inside`/`outside` behave like `left`/`right`, but resolve which physical side to use from the
  landing page's parity (recto/verso) rather than a fixed side.

A document that happens to already declare one of these six values - whether intentionally targeting
another renderer's behavior or as an authoring mistake - now sees the element float instead of
rendering in flow. See [Page floats](../../docs/html-css-support.md#page-floats-float-topbottomtop-bottomsnapinsideoutside)
for the full behavior and its known limitations.
