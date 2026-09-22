# A collapsed table's outermost grid lines overhang its box, and their corners go unpainted — issue #1257

**Tracked bug, not a limitation argued through and accepted.** Measured while fixing #1237 and left
out of it: #1237 is about which *face* a bevelled segment paints and changed no geometry at all, and
both symptoms here reproduce with `border-style: solid`.

A collapsed grid line is centered on its grid position, and the outermost ones' grid positions are
the table's own border-box edges — so each outer line is painted half outside the table's box, and
each line's run stops at that box rather than at the outer edge of the perpendicular line it meets.
Two consequences, measured against Chrome 153 at 1 image px per CSS px:

- **The outer half overhangs.** A 40px-tall block immediately followed by a collapsed table with
  `border: 20px solid red`: Chrome's first red row is y=40 and PeachPDF's is y=30, i.e. the border
  paints 10px *over* the preceding block. At a page or container edge that half is clipped away
  instead, so an outer line comes out half the thickness of an interior one.
- **The corners are never painted.** Over one 2×2 table's 180×140 slot Chrome paints 7800 px of each
  bevel face against 7600 here; the entire 400px difference is the four 10×10 corner squares.

Only the first is arguably correct: [CSS 2.1 §17.6.2](https://www.w3.org/TR/CSS21/tables.html#collapsing-borders)
really does say the table's own border width is *half* the maximum collapsed border and that "the
width of the table includes half the table border", which is what this implements. Chrome puts the
whole outermost line inside the table, so closing that half means choosing browser behaviour over
CSS 2.1's letter — a decision, not a bug fix. The unpainted corners are wrong under either model: the
corner square belongs to the grid, and clipping each line's run to the table's border box is what
leaves the hole.

The rectangles come from `CssLayoutEngineTable.EmitCollapsedBorderSegments`, not from
`BordersDrawHandler` — nothing in the paint path can fix this. Visible in the `border_style`
showcase's collapsed-table section, where each collapsed table reads slightly smaller than its
`border-collapse: separate` twin.

`docs/html-css-support.md` does not describe this to readers today; if it starts to, that note and
this file are deleted together when #1257 closes.
