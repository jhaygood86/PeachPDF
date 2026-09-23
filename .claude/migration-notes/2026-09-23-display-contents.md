# `display: contents` now generates no box

**Before:** `display: contents` was not a recognised `display` keyword, so the declaration was dropped and the
element kept the box its tag defaults to (`@supports (display: contents)` reported false). A wrapper someone
had marked `display: contents` was a real block or inline box, with its own margin, padding, border and
background, and a flex or grid container treated it — not its children — as the item.

**Now:** the element generates no box and its children (and its `::before`/`::after`) take part in its
parent's formatting context. The element's own `margin`, `padding`, `border`, `background`, `opacity`,
`transform`, `position`, `float` and `overflow` no longer apply; a flex/grid container's items are the
wrapper's children; a `<tr>` with it hands its cells to the table; `@supports (display: contents)` is true.
`br`, `img`, form controls and the other elements CSS Display 3 Appendix B lists compute to `display: none`
instead, and `display: contents` on `<html>` computes to `block`. A document that used `display: contents`
on such an element in the hope it would be ignored will lay out differently.

`unicode-bidi` (so `<bdi>`, `<bdo>`, `[dir]`) on a `display: contents` element no longer isolates or
overrides its text, and a `counter-*` property on one has no effect — both follow from there being no box.
The element's `id`, links, bookmark, `string-set` and tagged-PDF structure element are kept.
