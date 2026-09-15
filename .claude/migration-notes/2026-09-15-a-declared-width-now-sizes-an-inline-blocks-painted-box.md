# A declared `width` now sizes an inline-block's painted box

An `inline-block` whose own content is inlines-only (or empty) is flowed into the surrounding block's
line boxes rather than laid out as a box of its own. An explicit `width` on such a box used to move
the following content along the line without sizing the box itself, so its background, border and
overflow clip were painted at whatever its content happened to measure.

**Before.** `<span style="display:inline-block;width:80px;padding:4px;border:2px solid;background:#fee">x</span>`
painted a box just wide enough for the `x`, while the text after it started 80px+padding+border
along the line — the decoration and the layout disagreed. An empty box of the same shape painted
nothing at all, having no content for its rectangle to be derived from, while still pushing what
followed it 80px along.

**Now.** The declared width is the box's used width. All three paint the same 80px content area plus
their padding and border, and the content after them starts at that same right edge. `box-sizing:
border-box` is honored (the declared width then covers the padding and border rather than sitting
inside them), and a border-box width narrower than the box's own border and padding is floored at
their sum. Content wider than the declared width still overflows rather than being cut back to it.

A **percentage** width is unchanged: such a box is still sized as though `width: auto` were declared.

## The second change, on `auto`-width boxes

An **empty** `auto`-width `inline-block` with padding or a border — `<span style="display:inline-block;
padding:4pt;border:2pt solid"></span>` — painted nothing at all, having no content for its rectangle
to be derived from. It now paints its own 12pt border box, so such a box's `background` and `border`
become visible where before only the room for them was reserved.

Neither change affects a box whose content is block-level (a `<div>` inside the `inline-block`, say):
that takes a different layout path, which already sized the box.
