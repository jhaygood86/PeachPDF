# `inset`/`outset` now bevel on a collapsed table and on a page/margin box

**Before:** every border segment that was not one of an ordinary box's four edges — a
`border-collapse: collapse` table's grid lines, and a page box's or page-margin box's border — was
shaded as if it were a top or left edge. Under `inset` all four sides came out darkened and under
`outset` all four came out lightened, so neither keyword was distinguishable from `solid` there. A
collapsed cell with `border: 20px inset` painted 100% of its frame in the darkened face where a
browser paints half of it in each. (`groove` and `ridge` were already drawn as two bands and are
unaffected — see the last paragraph.)

**Now:**

- **A collapsed table's grid lines show both faces**, one per half: the half at the smaller
  coordinate (the left half of a column line, the upper half of a row line) takes the face a
  bottom/right edge would, and the half at the larger coordinate takes the face a top/left edge
  would. A grid line is shared by the boxes on either side of it and so is not any one box's edge —
  on an outer line the box "before" it is the page. This makes a collapsed `inset` paint the same two
  bands as a `ridge`, and a collapsed `outset` the same as a `groove`, which is what Chrome paints
  for the same table (measured against Chrome 153 at 1, 2, 3, 5 and 20px, on outer and interior lines
  alike, and whether the winning declaration came from a cell or from the table).
- **A page box's and a page-margin box's border shades per side**, like any other box: `inset`
  darkens the top and left and lightens the bottom and right, `outset` mirrors it. An
  `@page { border: 20px inset }` used to paint `#9a9a9a` on all four edges and now paints `#9a9a9a`
  over `#eeeeee`.

`groove`/`ridge` are unchanged everywhere, and paint exactly the same bytes as before. On a collapsed
grid line they already went down the two-band path, and the old top/left convention happened to pick
the same two faces in the same order the new rule does. On a page/margin box's bottom or right edge
the face flip and the outer/inner band-order flip cancel, so they were already right there too. What
changes for them is only that a collapsed `inset` now looks like a `ridge` and a collapsed `outset`
like a `groove`, rather than like neither.

**What an author may need to change:** nothing is invalid that was valid before, but a collapsed
table or page-margin border declared `inset`/`outset` now reads as engraved rather than as a flat,
uniformly shaded frame. A frame that was *relying* on the flat look should declare `solid` and the
colour it wants.
