# A collapsed table holds its outermost grid lines, and its joints have owners

Both changes affect what a `border-collapse: collapse` table looks like and how much room it takes.
Confirmed against `v0.9.19` (`git show v0.9.19:src/PeachPDF/Html/Core/Dom/CssLayoutEngineTable.cs`
still halves the outer line at `ApplyCollapsedUsedBorderWidths` and cancels the whole width in
`StartXSpacing`), so both are genuine changes relative to the last release.

## A collapsed table is now bigger, by half an outer line on each side

**Before:** the table's border box was centred on its outermost grid lines, taking half of each — CSS
2.1 §17.6.2's letter ("the width of the table includes half the table border"). The other half painted
*outside* the table: over whatever preceded it, or clipped away entirely at a page or container edge,
which made an outer line come out half the thickness of an interior one.

**Now:** the table's border box contains each outermost grid line whole, matching every browser. A
table whose cells carry a 20px collapsed border is 20px wider and 20px taller than before, and nothing
of a collapsed border is painted outside the table it belongs to.

**What an author sees:** a collapsed table occupies more room than it used to — a 2×2 table of 60×40px
cells with 20px borders is 180×140px, where it was 160×120px — and a following or preceding element is
no longer overlapped. A collapsed table that previously happened to fit a fixed-width container by
exactly the width of its border may now wrap or overflow; its `border-collapse: separate` twin was
always this size, so the two now agree.

## A collapsed table's corners are painted

**Before:** every grid line's run stopped at the perpendicular line's centre, so the square where two
lines cross was only ever three-quarters painted at the table's four corners — a visible notch,
clearest with a thick border, and unmissable with `inset`/`outset`.

**Now:** where two grid lines cross, exactly one of them paints the whole square. The wider line takes
it; at equal width, the higher §17.6.2 style priority; failing both, the row line, except on the
table's first row line, where the column line takes it unless it is the one at the table's inline
start.

**What an author sees:** corners are filled. A separate consequence of the same rule is that the
*interior* crossings change hands: they used to go to the column line (which was painted last) and now
go to the row line, which is visible whenever the two lines differ in colour or carry a bevel. Both
match what a browser paints for the same table.
